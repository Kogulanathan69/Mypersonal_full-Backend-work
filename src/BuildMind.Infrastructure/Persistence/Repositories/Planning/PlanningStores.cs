using System.Text.Json;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Domain.ValueObjects;
using BuildMind.Domain.ValueObjects.Land;
using BuildMind.Infrastructure.Persistence.Mapping;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Planning;

/// <summary>
/// Shared protected-write helper (ratified A1–A8): every write first locks the project row FOR UPDATE and re-checks
/// owner + authorization version inside the same transaction. Because all protected writers of a project serialize on
/// that row, check-then-insert sequences inside the transaction (idempotency keys, head numbers) are race-free.
/// </summary>
internal sealed class ProtectedPlanningWriter(BuildMindDbContext context, TransactionRunner transactions)
{
    public async Task<ProtectedWriteStatus> RunAsync(Guid projectId, Guid actor, long version,
        Func<CancellationToken, Task<ProtectedWriteStatus>> write, CancellationToken token)
    {
        try
        {
            return await transactions.ExecuteAsync(async inner =>
            {
                if (!await new ProjectMutationAuthorization(context).LockAndCheckAsync(projectId, actor, version, inner).ConfigureAwait(false))
                    throw new Rejected(ProtectedWriteStatus.Denied);
                var status = await write(inner).ConfigureAwait(false);
                if (status != ProtectedWriteStatus.Written)
                    throw new Rejected(status); // roll back anything staged
                return status;
            }, token).ConfigureAwait(false);
        }
        catch (Rejected rejected)
        {
            return rejected.Status;
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    private sealed class Rejected(ProtectedWriteStatus status) : Exception
    {
        public ProtectedWriteStatus Status { get; } = status;
    }
}

internal static class PlanningJson
{
    public static readonly JsonSerializerOptions Options = new(GenerationContractJson.StrictOptions);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidDataException("Stored planning JSON is empty.");
}

internal sealed class MySqlLandRevisionStore(BuildMindDbContext context, TransactionRunner transactions) : ILandRevisionStore
{
    public async Task<LandRevision?> GetCurrentAsync(Guid projectId, CancellationToken token)
    {
        var row = await context.Set<LandRevisionRecord>().AsNoTracking().Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.RevisionNumber).FirstOrDefaultAsync(token).ConfigureAwait(false);
        return row is null ? null : ToModel(row);
    }

    public async Task<LandRevision?> GetAsync(Guid projectId, Guid landRevisionId, CancellationToken token)
    {
        var row = await context.Set<LandRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == landRevisionId, token).ConfigureAwait(false);
        return row is null ? null : ToModel(row);
    }

    public Task<ProtectedWriteStatus> TryAppendAsync(LandRevision revision, Guid? expectedCurrentId, long authorizationVersion,
        CancellationToken token) =>
        new ProtectedPlanningWriter(context, transactions).RunAsync(revision.ProjectId, revision.CreatedBy, authorizationVersion, async inner =>
        {
            var head = await context.Set<LandRevisionRecord>().AsNoTracking().Where(r => r.ProjectId == revision.ProjectId)
                .OrderByDescending(r => r.RevisionNumber).Select(r => new { r.Id, r.RevisionNumber })
                .FirstOrDefaultAsync(inner).ConfigureAwait(false);
            if (head?.Id != expectedCurrentId || revision.RevisionNumber != (head?.RevisionNumber ?? 0) + 1)
                return ProtectedWriteStatus.Conflict;
            var loc = revision.Location is { State: LandLocationState.Provided } l ? l : null;
            var rep = revision.ReportedArea;
            context.Add(new LandRevisionRecord
            {
                Id = revision.Id, ProjectId = revision.ProjectId, RevisionNumber = revision.RevisionNumber,
                LandJson = PlanningJson.Write(revision.Land), AreaM2 = revision.AreaM2, PerimeterM = revision.PerimeterM,
                LocationLocalityId = loc?.LocalityId, LocationLocalityName = loc?.LocalityName,
                LocationLatitude = loc?.Latitude, LocationLongitude = loc?.Longitude,
                LocationSource = loc?.Source?.ToString(),
                LocationVerificationStatus = loc?.VerificationStatus.ToString(),
                LocationVerificationProvider = loc?.VerificationProvider,
                LocationVerifiedAtUtc = loc?.VerifiedAtUtc,
                ReportedAreaOriginalValue = rep?.OriginalValue,
                ReportedAreaUnit = rep?.Unit.ToString(),
                ReportedAreaM2 = rep?.AreaM2,
                ReportedAreaSourceFileId = rep?.Source.FileId,
                ReportedAreaSourcePageNumber = rep?.Source.PageNumber,
                ReportedAreaSourceReferenceText = rep?.Source.ReferenceText,
                ReportedAreaReviewStatus = rep?.ReviewStatus.ToString(),
                EdgeCount = revision.EdgeCount,
                SurveyId = revision.SurveyId, CreatedBy = revision.CreatedBy, CreatedAtUtc = revision.CreatedAtUtc,
                FrameJson = revision.FrameJson, GeometryDigest = revision.GeometryDigest,
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token);

    private static LandRevision ToModel(LandRevisionRecord row)
    {
        LandLocation? location = null;
        if (row.LocationLocalityName is not null)
        {
            if (!Enum.TryParse<LandLocationSource>(row.LocationSource, out var source)
                || !Enum.TryParse<LandLocationVerificationStatus>(row.LocationVerificationStatus, out var verificationStatus))
            {
                throw new InvalidDataException("Stored land location metadata is invalid.");
            }
            location = LandLocation.Create(row.LocationLocalityName, source, row.LocationLocalityId,
                row.LocationLatitude, row.LocationLongitude, verificationStatus,
                row.LocationVerificationProvider, row.LocationVerifiedAtUtc);
        }

        ReportedLandArea? reportedArea = null;
        if (row.ReportedAreaOriginalValue is { } origValue)
        {
            if (!Enum.TryParse<ReportedAreaUnit>(row.ReportedAreaUnit, out var unit)
                || !Enum.TryParse<ReportedAreaReviewStatus>(row.ReportedAreaReviewStatus, out var reviewStatus)
                || row.ReportedAreaSourceFileId is not { } sourceFileId)
            {
                throw new InvalidDataException("Stored reported land area metadata is invalid.");
            }
            reportedArea = new ReportedLandArea(origValue, unit,
                new LandAreaSourceReference(sourceFileId, row.ReportedAreaSourcePageNumber, row.ReportedAreaSourceReferenceText),
                reviewStatus);
        }

        return new(row.Id, row.ProjectId, row.RevisionNumber,
            PlanningJson.Read<MetricLandDto>(row.LandJson), row.AreaM2, row.EdgeCount, row.SurveyId, row.CreatedBy, row.CreatedAtUtc,
            row.FrameJson, row.GeometryDigest, row.PerimeterM, location, reportedArea);
    }
}

internal sealed class MySqlGenerationJobStore(BuildMindDbContext context, TransactionRunner transactions) : IGenerationJobStore
{
    public async Task<GenerationJob?> FindByKeyAsync(Guid projectId, Guid actorId, string idempotencyKey, CancellationToken token)
    {
        var row = await context.Set<GenerationJobRecord>().AsNoTracking()
            .SingleOrDefaultAsync(j => j.ProjectId == projectId && j.RequestedBy == actorId && j.IdempotencyKey == idempotencyKey, token)
            .ConfigureAwait(false);
        return row is null ? null : ToModel(row);
    }

    public async Task<GenerationJob?> GetAsync(Guid projectId, Guid jobId, CancellationToken token)
    {
        var row = await context.Set<GenerationJobRecord>().AsNoTracking()
            .SingleOrDefaultAsync(j => j.ProjectId == projectId && j.Id == jobId, token).ConfigureAwait(false);
        return row is null ? null : ToModel(row);
    }

    public async Task<IReadOnlyList<GenerationCandidate>> GetCandidatesAsync(Guid projectId, Guid jobId, CancellationToken token)
    {
        var rows = await context.Set<GenerationCandidateRecord>().AsNoTracking()
            .Where(c => c.ProjectId == projectId && c.JobId == jobId).OrderBy(c => c.Ordinal).ToListAsync(token).ConfigureAwait(false);
        return rows.Select(c =>
        {
            var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(c.GeometryJson, PlanEditorJson.StrictOptions)
                ?? throw new InvalidDataException("Stored candidate geometry is empty.");
            if (PlanRevisionService.ComputeGeometryHash(c.GeometryJson) != c.GeometryHash)
                throw new InvalidDataException("Stored candidate geometry does not match its hash.");
            return new GenerationCandidate(c.Id, c.JobId, c.ProjectId, c.Ordinal, geometry, c.GeometryJson, c.GeometryHash,
                PlanningJson.Read<AlternativeMetricsDto>(c.MetricsJson), PlanningJson.Read<ValidationResultDto>(c.ValidationJson),
                c.DiversitySignature, c.CreatedAtUtc);
        }).ToArray();
    }

    public Task<ProtectedWriteStatus> TryStartAsync(GenerationJob running, long authorizationVersion, CancellationToken token) =>
        new ProtectedPlanningWriter(context, transactions).RunAsync(running.ProjectId, running.RequestedBy, authorizationVersion, async inner =>
        {
            if (await context.Set<GenerationJobRecord>().AnyAsync(j => j.ProjectId == running.ProjectId
                    && j.RequestedBy == running.RequestedBy && j.IdempotencyKey == running.IdempotencyKey, inner).ConfigureAwait(false))
                return ProtectedWriteStatus.Conflict;
            context.Add(new GenerationJobRecord
            {
                Id = running.Id, ProjectId = running.ProjectId, RequestedBy = running.RequestedBy,
                IdempotencyKey = running.IdempotencyKey, LandRevisionId = running.LandRevisionId,
                RequirementsRevisionId = running.RequirementsRevisionId, Status = running.Status.ToString(),
                EvidenceMode = running.EvidenceMode, Seed = running.Seed, CreatedAtUtc = running.CreatedAtUtc,
                EvidencePackId = running.EvidencePackId,
                DiagnosticsJson = "[]",
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token);

    public Task<ProtectedWriteStatus> TryCompleteAsync(GenerationJob completed, IReadOnlyList<GenerationCandidate> candidates,
        long authorizationVersion, CancellationToken token) =>
        new ProtectedPlanningWriter(context, transactions).RunAsync(completed.ProjectId, completed.RequestedBy, authorizationVersion, async inner =>
        {
            var diagnostics = PlanningJson.Write(completed.Diagnostics);
            var status = completed.Status.ToString();
            var updated = await context.Set<GenerationJobRecord>()
                .Where(j => j.Id == completed.Id && j.ProjectId == completed.ProjectId && j.Status == nameof(GenerationJobStatus.Running))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, status)
                    .SetProperty(j => j.Outcome, completed.Outcome)
                    .SetProperty(j => j.ReasonCode, completed.ReasonCode)
                    .SetProperty(j => j.AlgorithmVersion, completed.AlgorithmVersion)
                    .SetProperty(j => j.CompletedAtUtc, completed.CompletedAtUtc)
                    .SetProperty(j => j.DiagnosticsJson, diagnostics), inner).ConfigureAwait(false);
            if (updated != 1)
                return ProtectedWriteStatus.Conflict;
            foreach (var candidate in candidates)
            {
                context.Add(new GenerationCandidateRecord
                {
                    Id = candidate.Id, JobId = candidate.JobId, ProjectId = candidate.ProjectId, Ordinal = candidate.Ordinal,
                    GeometryJson = candidate.GeometryJson, GeometryHash = candidate.GeometryHash,
                    MetricsJson = PlanningJson.Write(candidate.Metrics), ValidationJson = PlanningJson.Write(candidate.Validation),
                    DiversitySignature = candidate.DiversitySignature, CreatedAtUtc = candidate.CreatedAtUtc,
                });
            }
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token);

    private static GenerationJob ToModel(GenerationJobRecord row) => new(row.Id, row.ProjectId, row.RequestedBy, row.IdempotencyKey,
        row.LandRevisionId, row.RequirementsRevisionId, Enum.Parse<GenerationJobStatus>(row.Status), row.Outcome, row.ReasonCode,
        row.AlgorithmVersion, row.EvidenceMode, row.Seed, row.CreatedAtUtc, row.CompletedAtUtc,
        PlanningJson.Read<DiagnosticDto[]>(row.DiagnosticsJson), row.EvidencePackId);
}

internal sealed class MySqlPlanCreationStore(BuildMindDbContext context, TransactionRunner transactions) : IPlanCreationStore
{
    public Task<ProtectedWriteStatus> TryCreateAsync(PlanRevisionSnapshot firstRevision, IssuedValidationEvidence evidence,
        long authorizationVersion, CancellationToken token)
    {
        if (firstRevision.VersionNumber != 1 || firstRevision.ParentRevisionId is not null
            || evidence.Reference.ValidationId != firstRevision.Validation.ValidationId
            || evidence.Reference.GeometryHash != firstRevision.GeometryHash || evidence.Reference.HasBlockingIssues)
            throw new ArgumentException("A first revision must be version 1 citing matching nonblocking server evidence.");
        return new ProtectedPlanningWriter(context, transactions).RunAsync(firstRevision.ProjectId, firstRevision.CreatedBy,
            authorizationVersion, async inner =>
            {
                if (await context.Set<PlanRecord>().AnyAsync(p => p.ProjectId == firstRevision.ProjectId
                        && p.PlanId == firstRevision.PlanId, inner).ConfigureAwait(false))
                    return ProtectedWriteStatus.Conflict;
                context.Add(new PlanRecord { ProjectId = firstRevision.ProjectId, PlanId = firstRevision.PlanId });
                await context.SaveChangesAsync(inner).ConfigureAwait(false);
                context.Add(PlanValidationLedger.ToRecord(evidence));
                context.Add(PlanRevisionMapper.ToRecord(firstRevision));
                await context.SaveChangesAsync(inner).ConfigureAwait(false);
                await context.Set<PlanRecord>()
                    .Where(p => p.ProjectId == firstRevision.ProjectId && p.PlanId == firstRevision.PlanId && p.HeadRevisionId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.HeadRevisionId, firstRevision.RevisionId), inner).ConfigureAwait(false);
                return ProtectedWriteStatus.Written;
            }, token);
    }
}

internal static class PlanValidationLedger
{
    public static PlanValidationEvidenceRecord ToRecord(IssuedValidationEvidence evidence) => new()
    {
        ValidationId = evidence.Reference.ValidationId, ProjectId = evidence.ProjectId, PlanId = evidence.PlanId,
        ValidatorVersion = evidence.Reference.ValidatorVersion, GeometryHash = evidence.Reference.GeometryHash,
        GeometryValid = evidence.Reference.GeometryValid, RequirementsMatch = evidence.Reference.RequirementsMatch,
        RegulatoryEvidenceStatus = evidence.Reference.RegulatoryEvidenceStatus, HasBlockingIssues = evidence.Reference.HasBlockingIssues,
        ValidatedAtUtc = evidence.Reference.ValidatedAtUtc, DiagnosticsJson = PlanningJson.Write(evidence.Diagnostics),
    };
}

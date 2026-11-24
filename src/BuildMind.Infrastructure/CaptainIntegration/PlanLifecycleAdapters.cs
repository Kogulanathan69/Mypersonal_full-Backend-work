using System.Collections.Concurrent;
using System.Text.Json;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Mapping;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Repositories.Planning;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>MySQL datetime(6) keeps microseconds; M3 compares timestamps exactly, so time is truncated at the source.</summary>
public sealed class MicrosecondTimeProvider(TimeProvider inner) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        var now = inner.GetUtcNow().ToUniversalTime();
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }
}

/// <summary>Fresh owner-scoped authorization version for a protected write by <paramref name="actor"/>.</summary>
internal static class ProtectedVersion
{
    public static async Task<long?> ReadAsync(IProjectAuthorizationPort authorization, Guid projectId, Guid actor, CancellationToken token)
    {
        var result = await authorization.AuthorizeOwnerAsync(projectId, actor, token).ConfigureAwait(false);
        return result.Status == ProjectAuthorizationStatus.Authorized && result.Snapshot is { } snapshot
               && snapshot.ProjectId == projectId && snapshot.ActorId == actor
            ? snapshot.Version.Value
            : null;
    }
}

/// <summary>M3 <see cref="IPlanRevisionStore"/> over the existing plans/plan_revisions tables (immutable revisions, head CAS).</summary>
internal sealed class MySqlPlanRevisionStore(BuildMindDbContext context, TransactionRunner transactions, IProjectAuthorizationPort authorization)
    : IPlanRevisionStore
{
    public async Task<PlanRevisionSnapshot?> GetAsync(Guid projectId, Guid planId, Guid revisionId, CancellationToken token)
    {
        var row = await context.Set<PlanRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ProjectId == projectId && r.PlanId == planId && r.RevisionId == revisionId, token).ConfigureAwait(false);
        return row is null ? null : PlanRevisionMapper.ToSnapshot(row);
    }

    public async Task<PlanRevisionSnapshot?> GetCurrentAsync(Guid projectId, Guid planId, CancellationToken token)
    {
        var row = await (from plan in context.Set<PlanRecord>().AsNoTracking()
                         where plan.ProjectId == projectId && plan.PlanId == planId && plan.HeadRevisionId != null
                         join revision in context.Set<PlanRevisionRecord>().AsNoTracking()
                             on new { plan.ProjectId, plan.PlanId, RevisionId = plan.HeadRevisionId!.Value }
                             equals new { revision.ProjectId, revision.PlanId, revision.RevisionId }
                         select revision).SingleOrDefaultAsync(token).ConfigureAwait(false);
        return row is null ? null : PlanRevisionMapper.ToSnapshot(row);
    }

    public async Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(Guid projectId, Guid planId, CancellationToken token) =>
        (await context.Set<PlanRevisionRecord>().AsNoTracking().Where(r => r.ProjectId == projectId && r.PlanId == planId)
            .OrderByDescending(r => r.VersionNumber).Take(500).ToListAsync(token).ConfigureAwait(false))
        .Select(PlanRevisionMapper.ToSnapshot).ToArray();

    public async Task<AppendRevisionResult> TryAppendAtomicallyAsync(Guid expectedBaseRevisionId, PlanRevisionSnapshot proposed, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, proposed.ProjectId, proposed.CreatedBy, token).ConfigureAwait(false) is not { } version)
            return new(AppendRevisionStatus.Unauthorized);
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(proposed.ProjectId, proposed.CreatedBy, version, async inner =>
            await PlanHead.AppendAsync(context, expectedBaseRevisionId, proposed, inner).ConfigureAwait(false), token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => new(AppendRevisionStatus.Created, proposed),
            ProtectedWriteStatus.Conflict => new(AppendRevisionStatus.RevisionConflict) { ExpectedRevisionId = proposed.RevisionId },
            _ => new(AppendRevisionStatus.Unauthorized),
        };
    }
}

internal static class PlanHead
{
    /// <summary>Inside a protected transaction: CAS the head from the expected base and append the immutable revision.</summary>
    public static async Task<ProtectedWriteStatus> AppendAsync(BuildMindDbContext context, Guid expectedBase, PlanRevisionSnapshot proposed,
        CancellationToken token)
    {
        var head = await context.Set<PlanRecord>().AsNoTracking()
            .Where(p => p.ProjectId == proposed.ProjectId && p.PlanId == proposed.PlanId).Select(p => p.HeadRevisionId)
            .SingleOrDefaultAsync(token).ConfigureAwait(false);
        var baseVersion = await context.Set<PlanRevisionRecord>().AsNoTracking()
            .Where(r => r.ProjectId == proposed.ProjectId && r.PlanId == proposed.PlanId && r.RevisionId == expectedBase)
            .Select(r => (long?)r.VersionNumber).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (head != expectedBase || baseVersion is null || proposed.ParentRevisionId != expectedBase
            || proposed.VersionNumber != baseVersion + 1)
            return ProtectedWriteStatus.Conflict;
        context.Add(PlanRevisionMapper.ToRecord(proposed));
        await context.SaveChangesAsync(token).ConfigureAwait(false);
        var moved = await context.Set<PlanRecord>()
            .Where(p => p.ProjectId == proposed.ProjectId && p.PlanId == proposed.PlanId && p.HeadRevisionId == expectedBase)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.HeadRevisionId, proposed.RevisionId), token).ConfigureAwait(false);
        return moved == 1 ? ProtectedWriteStatus.Written : ProtectedWriteStatus.Conflict;
    }
}

internal sealed class CaptainApplicableInputs(ILandRevisionStore lands, IRequirementRevisionRepository requirements)
    : IApplicableInputRevisionSource
{
    public async Task<ApplicableInputRevisions> GetCurrentAsync(Guid projectId, CancellationToken token)
    {
        var land = await lands.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        var requirement = await requirements.GetCurrentAsync(projectId, token).ConfigureAwait(false);
        return new(land?.Id ?? Guid.Empty, requirement?.Id ?? Guid.Empty);
    }
}

internal sealed class CaptainConfirmedLandPort(ILandRevisionStore lands) : IConfirmedLandRevisionPort
{
    public async Task<ConfirmedLandRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken token)
    {
        var land = await lands.GetAsync(projectId, revisionId, token).ConfigureAwait(false);
        return land is null ? null : new ConfirmedLandRevision(land.ProjectId, land.Id, land.Land.SchemaVersion,
            JsonSerializer.SerializeToElement(land.Land.Boundary, PlanningJson.Options));
    }
}

internal sealed class CaptainAuthorizedRequirementPort(IProjectAuthorizationPort authorization, IRequirementRevisionRepository requirements)
    : IAuthorizedRequirementRevisionPort
{
    public async Task<RequirementRevisionAccessResult> GetAsync(Guid projectId, Guid revisionId, Guid actor, CancellationToken token)
    {
        var access = await authorization.AuthorizeOwnerAsync(projectId, actor, token).ConfigureAwait(false);
        if (access.Status != ProjectAuthorizationStatus.Authorized || access.Snapshot is null)
            return new(RequirementRevisionAccessStatus.Unauthorized);
        var revision = await requirements.GetAsync(projectId, revisionId, token).ConfigureAwait(false);
        return revision is null
            ? new(RequirementRevisionAccessStatus.NotFound, authorization: access.Snapshot)
            : new(RequirementRevisionAccessStatus.Found,
                new RequirementRevisionReference(revision.ProjectId, revision.Id, revision.SchemaVersion), access.Snapshot);
    }
}

/// <summary>
/// Request-scoped hand-off: the M3 evidence-source port receives only the server-computed geometry hash, so the
/// compute client records each accepted candidate geometry under that exact hash for the evidence source to validate.
/// </summary>
internal sealed class EditGeometryHandoff
{
    private readonly ConcurrentDictionary<string, PlanGeometryDto> geometries = new(StringComparer.Ordinal);
    public void Record(PlanGeometryDto geometry) =>
        geometries[PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions))] = geometry;
    public PlanGeometryDto? Find(string hash) => geometries.TryGetValue(hash, out var geometry) ? geometry : null;
}

internal sealed class PlanEditorComputeClient(PythonServiceClient python, EditGeometryHandoff handoff, IPlanRevisionStore revisions)
    : IPlanEditorComputeClient
{
    public async Task<PlanEditResultDto> PreviewAsync(PlanEditorComputeRequest request, CancellationToken token)
    {
        // The M3 compute request carries no base revision; CandidatePreviewService has already verified that the
        // current head equals the command's base, so the head identity is what the result must echo.
        var head = await revisions.GetCurrentAsync(request.SourceGeometry.ProjectId, request.SourceGeometry.PlanId, token).ConfigureAwait(false)
            ?? throw new HttpRequestException("The plan head is unavailable.");
        PlanEditResultDto result;
        try
        {
            result = await python.PostJsonAsync<object, PlanEditResultDto>("internal/v1/plans/edit-preview",
                new
                {
                    schemaVersion = "captain-plan-edit-preview/1", baseRevisionId = head.RevisionId,
                    sourceGeometry = request.SourceGeometry, command = request.Command,
                }, _ => true, token).ConfigureAwait(false);
        }
        catch (DependencyFailureException failure) when (failure.Kind == DependencyFailureKind.Timeout)
        {
            throw new TimeoutException("Plan editor compute timed out.");
        }
        catch (DependencyFailureException failure) when (failure.Kind is DependencyFailureKind.MalformedResponse
                                                             or DependencyFailureKind.UnexpectedResponse or DependencyFailureKind.ResponseTooLarge)
        {
            throw new JsonException("Plan editor compute returned an invalid response.");
        }
        catch (DependencyFailureException)
        {
            throw new HttpRequestException("Plan editor compute is unavailable.");
        }
        if (result.CandidateGeometry is { } geometry)
            handoff.Record(geometry);
        return result;
    }
}

/// <summary>Records server-issued evidence in the ledger (the only evidence revisions may cite).</summary>
internal sealed class ValidationLedgerWriter(BuildMindDbContext context, TransactionRunner transactions, IProjectAuthorizationPort authorization)
{
    public async Task<bool> RecordAsync(IssuedValidationEvidence evidence, Guid actor, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, evidence.ProjectId, actor, token).ConfigureAwait(false) is not { } version)
            return false;
        return await new ProtectedPlanningWriter(context, transactions).RunAsync(evidence.ProjectId, actor, version, async inner =>
        {
            context.Add(PlanValidationLedger.ToRecord(evidence));
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false) == ProtectedWriteStatus.Written;
    }
}

internal sealed class LedgerValidationEvidenceSource(
    EditGeometryHandoff handoff, ICurrentUser user, ILandRevisionStore lands, IRequirementRevisionRepository requirements,
    IGenerationPolicyProvider policies, IPlanningComputeClient compute, ValidationLedgerWriter ledger, TimeProvider time)
    : ICandidateValidationEvidenceSource
{
    public async Task<PlanValidationReferenceDto?> GetAsync(Guid projectId, Guid planId, string exactGeometryHash, CancellationToken token)
    {
        if (handoff.Find(exactGeometryHash) is not { } geometry || user.UserId is not { } actor
            || geometry.ProjectId != projectId || geometry.PlanId != planId)
            return null;
        var land = await lands.GetAsync(projectId, geometry.LandRevisionId, token).ConfigureAwait(false);
        var requirement = await requirements.GetAsync(projectId, geometry.RequirementsRevisionId, token).ConfigureAwait(false);
        if (land is null || requirement is null)
            return null;
        var policy = policies.Get();
        var synthetic = policy is { EvidenceMode: PlanningSchemas.EvidenceModeSynthetic, SyntheticSetbackM: not null };
        PlanCheckResult check;
        try
        {
            var counts = requirement.Content.GroundFloorRooms.Where(r => r.Count > 0).ToDictionary(r => r.RoomType, r => r.Count, StringComparer.Ordinal);
            check = await compute.ValidatePlanAsync(new PlanCheckRequest(projectId, land.Id, land.Land,
                synthetic ? Enumerable.Repeat(policy!.SyntheticSetbackM!.Value, land.EdgeCount).ToArray() : null, counts, geometry), token).ConfigureAwait(false);
        }
        catch (DependencyFailureException failure) when (failure.Kind == DependencyFailureKind.Timeout)
        {
            throw new TimeoutException("Plan validation timed out.");
        }
        catch (DependencyFailureException)
        {
            throw new HttpRequestException("Plan validation is unavailable.");
        }
        var evidence = PlanSaveService.IssueEvidence(projectId, planId, exactGeometryHash, check,
            synthetic ? PlanningSchemas.SyntheticEvidenceStatus : PlanningSchemas.UnverifiedEvidenceStatus,
            new MicrosecondTimeProvider(time).GetUtcNow());
        return await ledger.RecordAsync(evidence, actor, token).ConfigureAwait(false) ? evidence.Reference : null;
    }
}

internal sealed class LedgerValidationEvidenceVerifier(BuildMindDbContext context) : IPlanValidationEvidenceVerifier
{
    public async Task<ValidationEvidenceStatus> VerifyAsync(Guid projectId, Guid planId, PlanValidationReferenceDto evidence,
        string exactGeometryHash, CancellationToken token)
    {
        var row = await context.Set<PlanValidationEvidenceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(e => e.ValidationId == evidence.ValidationId, token).ConfigureAwait(false);
        if (row is null)
            return ValidationEvidenceStatus.Rejected;
        var exact = row.ProjectId == projectId && row.PlanId == planId && row.GeometryHash == exactGeometryHash
            && row.GeometryHash == evidence.GeometryHash && row.ValidatorVersion == evidence.ValidatorVersion
            && row.GeometryValid == evidence.GeometryValid && row.RequirementsMatch == evidence.RequirementsMatch
            && row.RegulatoryEvidenceStatus == evidence.RegulatoryEvidenceStatus && row.HasBlockingIssues == evidence.HasBlockingIssues
            && row.ValidatedAtUtc.EqualsExact(evidence.ValidatedAtUtc);
        return exact ? ValidationEvidenceStatus.Verified : ValidationEvidenceStatus.Rejected;
    }
}

/// <summary>M3 candidate store + atomic transactions (protected write; candidate state CAS; head CAS; immutable append).</summary>
internal sealed class MySqlCandidatePreviewStore(BuildMindDbContext context, TransactionRunner transactions, IProjectAuthorizationPort authorization)
    : ICandidatePreviewStore, ICandidatePreviewTransaction
{
    public async Task CreateAsync(CandidatePreviewSnapshot candidate, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, candidate.ProjectId, candidate.OwnerId, token).ConfigureAwait(false) is not { } version)
            throw new InvalidOperationException("The candidate owner is not authorized for the project.");
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(candidate.ProjectId, candidate.OwnerId, version, async inner =>
        {
            if (await context.Set<PlanEditCandidateRecord>().AnyAsync(c => c.ProjectId == candidate.ProjectId
                    && c.PlanId == candidate.PlanId && c.CommandId == candidate.CommandId, inner).ConfigureAwait(false))
                return ProtectedWriteStatus.Conflict;
            context.Add(new PlanEditCandidateRecord
            {
                CandidateId = candidate.CandidateId, ProjectId = candidate.ProjectId, PlanId = candidate.PlanId, OwnerId = candidate.OwnerId,
                BaseRevisionId = candidate.BaseRevisionId, LandRevisionId = candidate.LandRevisionId,
                RequirementsRevisionId = candidate.RequirementsRevisionId, CommandId = candidate.CommandId,
                GeometryJson = candidate.GeometryJson, GeometryHash = candidate.GeometryHash,
                GeometrySchemaVersion = candidate.GeometrySchemaVersion, ValidationId = candidate.Validation.ValidationId,
                CreatedAtUtc = candidate.CreatedAtUtc, ExpiresAtUtc = candidate.ExpiresAtUtc, Status = candidate.Status.ToString(),
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false);
        if (status != ProtectedWriteStatus.Written)
            throw new InvalidOperationException($"Candidate was not stored ({status}).");
    }

    public async Task<CandidatePreviewSnapshot?> GetAsync(Guid candidateId, CancellationToken token)
    {
        var row = await (from candidate in context.Set<PlanEditCandidateRecord>().AsNoTracking()
                         where candidate.CandidateId == candidateId
                         join evidence in context.Set<PlanValidationEvidenceRecord>().AsNoTracking()
                             on candidate.ValidationId equals evidence.ValidationId
                         select new { candidate, evidence }).SingleOrDefaultAsync(token).ConfigureAwait(false);
        if (row is null) return null;
        var c = row.candidate;
        var e = row.evidence;
        return new CandidatePreviewSnapshot(c.CandidateId, c.ProjectId, c.PlanId, c.OwnerId, c.BaseRevisionId, c.LandRevisionId,
            c.RequirementsRevisionId, c.CommandId, c.GeometryJson, c.GeometryHash, c.GeometrySchemaVersion,
            new PlanValidationReferenceDto
            {
                ValidationId = e.ValidationId, ValidatorVersion = e.ValidatorVersion, GeometryHash = e.GeometryHash,
                GeometryValid = e.GeometryValid, RequirementsMatch = e.RequirementsMatch,
                RegulatoryEvidenceStatus = e.RegulatoryEvidenceStatus, HasBlockingIssues = e.HasBlockingIssues, ValidatedAtUtc = e.ValidatedAtUtc,
            }, c.CreatedAtUtc, c.ExpiresAtUtc, Enum.Parse<CandidatePreviewStatus>(c.Status));
    }

    public Task<CandidateActionResult> TryApplyAtomicallyAsync(CandidatePreviewSnapshot expected, PlanRevisionSnapshot proposed,
        DateTimeOffset now, CancellationToken token) =>
        TransitionAsync(expected, now, CandidatePreviewStatus.Applied, token, async inner =>
            await PlanHead.AppendAsync(context, expected.BaseRevisionId, proposed, inner).ConfigureAwait(false) == ProtectedWriteStatus.Written
                ? null : CandidateActionStatus.StaleBaseRevision,
            applied => new(CandidateActionStatus.Applied, applied, proposed));

    public Task<CandidateActionResult> TryRejectAtomicallyAsync(CandidatePreviewSnapshot expected, DateTimeOffset now, CancellationToken token) =>
        TransitionAsync(expected, now, CandidatePreviewStatus.Rejected, token, _ => Task.FromResult<CandidateActionStatus?>(null),
            rejected => new(CandidateActionStatus.Rejected, rejected));

    public Task<CandidateActionResult> TryExpireAtomicallyAsync(CandidatePreviewSnapshot expected, DateTimeOffset now, CancellationToken token) =>
        TransitionAsync(expected, now, CandidatePreviewStatus.Expired, token, _ => Task.FromResult<CandidateActionStatus?>(null),
            expired => new(CandidateActionStatus.Expired, expired), allowExpired: true);

    private async Task<CandidateActionResult> TransitionAsync(CandidatePreviewSnapshot expected, DateTimeOffset now, CandidatePreviewStatus target,
        CancellationToken token, Func<CancellationToken, Task<CandidateActionStatus?>> effect,
        Func<CandidatePreviewSnapshot, CandidateActionResult> success, bool allowExpired = false)
    {
        if (await ProtectedVersion.ReadAsync(authorization, expected.ProjectId, expected.OwnerId, token).ConfigureAwait(false) is not { } version)
            return new(CandidateActionStatus.NotFound);
        CandidateActionResult? outcome = null;
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(expected.ProjectId, expected.OwnerId, version, async inner =>
        {
            var row = await context.Set<PlanEditCandidateRecord>().AsNoTracking()
                .SingleOrDefaultAsync(c => c.CandidateId == expected.CandidateId, inner).ConfigureAwait(false);
            if (row is null || row.ProjectId != expected.ProjectId || row.PlanId != expected.PlanId || row.OwnerId != expected.OwnerId)
            {
                outcome = new(CandidateActionStatus.NotFound);
                return ProtectedWriteStatus.Conflict;
            }
            var current = Enum.Parse<CandidatePreviewStatus>(row.Status);
            if (current != CandidatePreviewStatus.Pending)
            {
                outcome = new(current switch
                {
                    CandidatePreviewStatus.Applied => CandidateActionStatus.AlreadyApplied,
                    CandidatePreviewStatus.Rejected => CandidateActionStatus.AlreadyRejected,
                    _ => CandidateActionStatus.Expired,
                }, expected with { Status = current });
                return ProtectedWriteStatus.Conflict;
            }
            if (!allowExpired && now >= row.ExpiresAtUtc)
            {
                outcome = new(CandidateActionStatus.Expired, expected with { Status = CandidatePreviewStatus.Expired });
                return ProtectedWriteStatus.Conflict;
            }
            if (allowExpired && now < row.ExpiresAtUtc)
            {
                outcome = new(CandidateActionStatus.PersistenceFailure, expected);
                return ProtectedWriteStatus.Conflict;
            }
            if (await effect(inner).ConfigureAwait(false) is { } failure)
            {
                outcome = new(failure, expected);
                return ProtectedWriteStatus.Conflict;
            }
            var targetName = target.ToString();
            var moved = await context.Set<PlanEditCandidateRecord>()
                .Where(c => c.CandidateId == expected.CandidateId && c.Status == nameof(CandidatePreviewStatus.Pending))
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, targetName), inner).ConfigureAwait(false);
            if (moved != 1)
            {
                outcome = new(CandidateActionStatus.PersistenceFailure, expected);
                return ProtectedWriteStatus.Conflict;
            }
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => success(expected with { Status = target }),
            ProtectedWriteStatus.Denied => new(CandidateActionStatus.NotFound),
            _ => outcome ?? new(CandidateActionStatus.PersistenceFailure, expected),
        };
    }
}

/// <summary>Validation evidence older than the configured window cannot be confirmed (provisional product value).</summary>
internal sealed class ConfiguredValidationFreshnessPolicy(IConfiguration configuration) : IValidationFreshnessPolicy
{
    public const string HoursKey = "CaptainIntegration:ValidationFreshnessHours";

    public bool IsFresh(PlanRevisionSnapshot revision, DateTimeOffset now)
    {
        // No options-binder package in Infrastructure (G04 C8 note): parse invariantly; invalid or missing means 24 h.
        var hours = double.TryParse(configuration[HoursKey], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var configured) ? configured : 24;
        if (!double.IsFinite(hours) || hours is <= 0 or > 24 * 30) hours = 24;
        var validated = revision.Validation.ValidatedAtUtc;
        return validated <= now.AddMinutes(1) && now - validated <= TimeSpan.FromHours(hours);
    }
}

/// <summary>
/// Final confirmation: protected write; the exact revision must still be the head; one confirmation per plan. No
/// project lifecycle transition is applied (no approved M1 lifecycle writer exists).
/// </summary>
internal sealed class MySqlFinalConfirmationTransaction(BuildMindDbContext context, TransactionRunner transactions, IProjectAuthorizationPort authorization)
    : IFinalConfirmationTransaction
{
    public async Task<ConfirmationResult> ConfirmAtomicallyAsync(PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision,
        CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, confirmation.ProjectId, confirmation.ConfirmedBy, token).ConfigureAwait(false) is not { } version)
            return new(ConfirmationStatus.Unauthorized);
        ConfirmationStatus? failure = null;
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(confirmation.ProjectId, confirmation.ConfirmedBy, version, async inner =>
        {
            var head = await context.Set<PlanRecord>().AsNoTracking()
                .Where(p => p.ProjectId == confirmation.ProjectId && p.PlanId == confirmation.PlanId).Select(p => p.HeadRevisionId)
                .SingleOrDefaultAsync(inner).ConfigureAwait(false);
            if (head != exactRevision.RevisionId)
            {
                failure = ConfirmationStatus.NotCurrentRevision;
                return ProtectedWriteStatus.Conflict;
            }
            if (await context.Set<PlanConfirmationRecord>().AnyAsync(c => c.ProjectId == confirmation.ProjectId
                    && c.PlanId == confirmation.PlanId, inner).ConfigureAwait(false))
            {
                failure = ConfirmationStatus.AlreadyConfirmed;
                return ProtectedWriteStatus.Conflict;
            }
            context.Add(new PlanConfirmationRecord
            {
                ConfirmationId = confirmation.ConfirmationId, ProjectId = confirmation.ProjectId, PlanId = confirmation.PlanId,
                ConfirmedRevisionId = confirmation.ConfirmedRevisionId, GeometryHash = confirmation.GeometryHash,
                ValidationId = confirmation.ValidationId, ConfirmedBy = confirmation.ConfirmedBy,
                ConfirmedAtUtc = confirmation.ConfirmedAtUtc, WarningAcknowledgement = confirmation.WarningAcknowledgement,
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => new(ConfirmationStatus.Confirmed, confirmation),
            ProtectedWriteStatus.Denied => new(ConfirmationStatus.Unauthorized),
            _ => new(failure ?? ConfirmationStatus.InvalidDependencyResponse),
        };
    }
}

/// <summary>The confirmation row itself is the durable hand-off to reports (Phase 6 reads plan_confirmations).</summary>
internal sealed class DurableConfirmationSink : IConfirmedPlanSnapshotSink
{
    public Task PublishAsync(PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision, CancellationToken token) =>
        Task.CompletedTask;
}

internal sealed class MySqlPlanCatalog(BuildMindDbContext context) : IPlanCatalog
{
    public async Task<IReadOnlyList<PlanSummary>> ListAsync(Guid projectId, CancellationToken token)
    {
        var plans = await context.Set<PlanRecord>().AsNoTracking().Where(p => p.ProjectId == projectId).ToListAsync(token).ConfigureAwait(false);
        var heads = await context.Set<PlanRevisionRecord>().AsNoTracking()
            .Where(r => r.ProjectId == projectId && context.Set<PlanRecord>().Any(p => p.ProjectId == projectId && p.PlanId == r.PlanId && p.HeadRevisionId == r.RevisionId))
            .Select(r => new { r.PlanId, r.RevisionId, r.VersionNumber, r.CreatedAtUtc }).ToListAsync(token).ConfigureAwait(false);
        var confirmations = await context.Set<PlanConfirmationRecord>().AsNoTracking().Where(c => c.ProjectId == projectId)
            .ToListAsync(token).ConfigureAwait(false);
        return plans.OrderBy(p => p.PlanId).Select(p =>
        {
            var head = heads.SingleOrDefault(h => h.PlanId == p.PlanId);
            var confirmation = confirmations.SingleOrDefault(c => c.PlanId == p.PlanId);
            return new PlanSummary(p.PlanId, head?.RevisionId, head?.VersionNumber, head?.CreatedAtUtc,
                confirmation is null ? null : new PlanConfirmationSummary(confirmation.ConfirmationId, confirmation.ConfirmedRevisionId,
                    confirmation.GeometryHash, confirmation.ValidationId, confirmation.ConfirmedAtUtc, confirmation.WarningAcknowledgement));
        }).ToArray();
    }
}

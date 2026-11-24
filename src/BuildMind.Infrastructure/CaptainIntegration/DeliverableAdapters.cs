using System.Text.Json;
using BuildMind.Application.Features.ArchitectRequests.Services;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Application.Features.Reports.Services;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records.Deliverables;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using BuildMind.Infrastructure.Persistence.Records.Planning;
using BuildMind.Infrastructure.Persistence.Repositories.Planning;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Private report artifact storage: an operator-provisioned exclusive directory; files are named only by the opaque
/// server-generated artifact GUID (never user input, so no path traversal); writes are create-new + atomic rename;
/// reads go through the existing bounded <see cref="ReportPrivatePdfReadVerifier"/>.
/// </summary>
internal sealed class PrivateReportArtifactStore(IConfiguration configuration)
{
    public const string DirectoryKey = "CaptainIntegration:ReportStorageDirectory";

    private string Root()
    {
        var path = configuration[DirectoryKey];
        var root = string.IsNullOrWhiteSpace(path) ? null : new DirectoryInfo(path);
        if (root is null || !Path.IsPathFullyQualified(path!) || !root.Exists || (root.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"{DirectoryKey} must name an existing private directory.");
        return root.FullName;
    }

    private string PathFor(Guid artifactId) => Path.Combine(Root(), artifactId.ToString("N") + ".pdf");

    public async Task WriteAsync(Guid artifactId, ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        var final = PathFor(artifactId);
        var temporary = final + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        File.Move(temporary, final, overwrite: false);
    }

    public async Task<byte[]?> ReadVerifiedAsync(ReportArtifact artifact, int maximumBytes, CancellationToken token)
    {
        var path = PathFor(artifact.ArtifactId);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return await ReportPrivatePdfReadVerifier.ReadVerifiedAsync(stream, artifact, maximumBytes, token).ConfigureAwait(false);
    }

    public void TryDelete(Guid artifactId)
    {
        try { File.Delete(PathFor(artifactId)); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Captain binding for the M5 report generation and read boundary candidates (Development profile only).</summary>
internal sealed class CaptainReportBoundary(
    BuildMindDbContext context, TransactionRunner transactions, IProjectAuthorizationPort authorization,
    IPlanRevisionStore revisions, IRequirementRevisionRepository requirements, PrivateReportArtifactStore artifacts, TimeProvider time)
    : IReportGenerationBoundaryCandidate, IReportReadBoundaryCandidate
{
    public const string SnapshotSchema = "captain-report-snapshot/1";
    public const string DisclaimerVersion = "captain-preliminary-disclaimer/1";
    public const string DisclaimerText =
        "PRELIMINARY 2D LAYOUT FOR DISCUSSION ONLY. Generated from customer requirements, a customer-confirmed boundary in a local " +
        "metric frame and DEVELOPMENT-ONLY synthetic room dimensions and setbacks. Rooms are rectangles without doors, windows, " +
        "circulation, structural or services design. This is not a construction drawing, not an architectural or engineering " +
        "design and not a planning, building or regulatory approval. The regulatory evidence status shown is not a compliance " +
        "determination. Consult a qualified professional and the relevant local authority before any decision.";

    private DateTimeOffset Now() => new MicrosecondTimeProvider(time).GetUtcNow();

    public async Task<ReportAcceptance> AcceptAsync(Guid actor, ReportGenerationRequest request, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, request.ProjectId, actor, token).ConfigureAwait(false) is not { } version)
            return new(ReportAcceptanceStatus.Rejected);
        var existing = await context.Set<ReportRecord>().AsNoTracking().AnyAsync(r => r.Id == request.ReportId, token).ConfigureAwait(false);
        if (existing) return new(ReportAcceptanceStatus.Existing);

        var confirmation = await context.Set<PlanConfirmationRecord>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.ConfirmationId == request.ConfirmationId && c.ProjectId == request.ProjectId, token).ConfigureAwait(false);
        if (confirmation is null || confirmation.PlanId != request.PlanId || confirmation.ConfirmedRevisionId != request.PlanRevisionId
            || confirmation.GeometryHash != request.GeometryHash)
            return new(ReportAcceptanceStatus.Rejected);
        var revision = await revisions.GetAsync(request.ProjectId, request.PlanId, request.PlanRevisionId, token).ConfigureAwait(false);
        if (revision is null) return new(ReportAcceptanceStatus.Rejected);
        var requirement = await requirements.GetAsync(request.ProjectId, revision.RequirementsRevisionId, token).ConfigureAwait(false);
        if (requirement is null) return new(ReportAcceptanceStatus.Rejected);

        var now = Now();
        Report pending;
        try
        {
            pending = Report.CreatePending(request.ReportId, request.ProjectId, actor, now,
                BuildSnapshot(requirement, revision, confirmation, now, DisclaimerVersion, DisclaimerText));
        }
        catch (ArgumentException)
        {
            return new(ReportAcceptanceStatus.Rejected);
        }
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(request.ProjectId, actor, version, async inner =>
        {
            if (await context.Set<ReportRecord>().AnyAsync(r => r.Id == request.ReportId, inner).ConfigureAwait(false))
                return ProtectedWriteStatus.Conflict;
            context.Add(new ReportRecord
            {
                Id = pending.Id, ProjectId = pending.ProjectId, RequestedBy = actor, RequestedAtUtc = now, SnapshotSchemaVersion = SnapshotSchema,
                CapturedAtUtc = now, RequirementsRevisionId = requirement.Id, PlanId = revision.PlanId, PlanRevisionId = revision.RevisionId,
                ConfirmationId = confirmation.ConfirmationId, DisclaimerVersion = DisclaimerVersion, DisclaimerText = DisclaimerText,
                StateVersion = 1, Status = nameof(ReportStatus.Pending),
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => new(ReportAcceptanceStatus.Accepted, pending),
            ProtectedWriteStatus.Conflict => new(ReportAcceptanceStatus.Existing),
            _ => new(ReportAcceptanceStatus.Rejected),
        };
    }

    public async Task<ReportPublication> PublishAsync(Guid actor, Report pending, ReadOnlyMemory<byte> bytes, string expectedHash, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, pending.ProjectId, actor, token).ConfigureAwait(false) is not { } version)
            return new(ReportPublicationStatus.Rejected);
        var now = Now();
        var artifact = new ReportArtifact(Guid.NewGuid(), "application/pdf", bytes.Length, expectedHash, now);
        if (!ReportArtifactIntegrity.Matches(artifact, bytes.Span, ReportPrivatePdfReadVerifier.AbsoluteMaximumBytes))
            return new(ReportPublicationStatus.Rejected);
        await artifacts.WriteAsync(artifact.ArtifactId, bytes, token).ConfigureAwait(false);
        var readBack = await artifacts.ReadVerifiedAsync(artifact, ReportPrivatePdfReadVerifier.AbsoluteMaximumBytes, token).ConfigureAwait(false);
        if (readBack is null)
        {
            artifacts.TryDelete(artifact.ArtifactId);
            return new(ReportPublicationStatus.Rejected);
        }
        var ready = pending.Complete(1, artifact, now);
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(pending.ProjectId, actor, version, async inner =>
        {
            var moved = await context.Set<ReportRecord>()
                .Where(r => r.Id == pending.Id && r.ProjectId == pending.ProjectId && r.StateVersion == 1 && r.Status == nameof(ReportStatus.Pending))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.StateVersion, 2L).SetProperty(r => r.Status, nameof(ReportStatus.Ready))
                    .SetProperty(r => r.ArtifactId, artifact.ArtifactId).SetProperty(r => r.ArtifactMediaType, artifact.MediaType)
                    .SetProperty(r => r.ArtifactByteLength, artifact.ByteLength).SetProperty(r => r.ArtifactContentHash, artifact.ContentHash)
                    .SetProperty(r => r.ArtifactCreatedAtUtc, artifact.CreatedAtUtc).SetProperty(r => r.CompletedAtUtc, now), inner)
                .ConfigureAwait(false);
            return moved == 1 ? ProtectedWriteStatus.Written : ProtectedWriteStatus.Conflict;
        }, token).ConfigureAwait(false);
        if (status == ProtectedWriteStatus.Written)
            return new(ReportPublicationStatus.Published, ready.Artifact);
        artifacts.TryDelete(artifact.ArtifactId); // the CAS did not publish it: remove the unreferenced private file
        return new(ReportPublicationStatus.Rejected);
    }

    public async Task<bool> FailAsync(Guid actor, Report pending, string safeCode, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, pending.ProjectId, actor, token).ConfigureAwait(false) is not { } version)
            return false;
        var now = Now();
        _ = pending.Fail(1, safeCode, now); // domain validation of the code and transition
        return await new ProtectedPlanningWriter(context, transactions).RunAsync(pending.ProjectId, actor, version, async inner =>
            await context.Set<ReportRecord>()
                .Where(r => r.Id == pending.Id && r.StateVersion == 1 && r.Status == nameof(ReportStatus.Pending))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.StateVersion, 2L).SetProperty(r => r.Status, nameof(ReportStatus.Failed))
                    .SetProperty(r => r.FailureCode, safeCode).SetProperty(r => r.FailedAtUtc, now), inner).ConfigureAwait(false) == 1
                ? ProtectedWriteStatus.Written : ProtectedWriteStatus.Conflict, token).ConfigureAwait(false) == ProtectedWriteStatus.Written;
    }

    public async Task<ReportReadReceipt> ReadAsync(Guid actor, Guid projectId, Guid reportId, bool download, int maximumBytes, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, projectId, actor, token).ConfigureAwait(false) is null)
            return new(ReportReadStatus.Hidden, actor);
        var report = await LoadAsync(projectId, reportId, token).ConfigureAwait(false);
        if (report is null) return new(ReportReadStatus.Hidden, actor);
        byte[]? bytes = null;
        if (download && report.Status == ReportStatus.Ready && report.Artifact is { } artifact)
        {
            bytes = await artifacts.ReadVerifiedAsync(artifact, maximumBytes, token).ConfigureAwait(false);
            if (bytes is null) return new(ReportReadStatus.Unavailable, actor);
            // Re-check authorization after the read: a revocation during the read never releases bytes.
            if (await ProtectedVersion.ReadAsync(authorization, projectId, actor, token).ConfigureAwait(false) is null)
                return new(ReportReadStatus.Hidden, actor);
        }
        return new(ReportReadStatus.Found, actor, report, bytes);
    }

    public async Task<Report?> LoadAsync(Guid projectId, Guid reportId, CancellationToken token)
    {
        var row = await context.Set<ReportRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == reportId && r.ProjectId == projectId, token).ConfigureAwait(false);
        if (row is null) return null;
        var revision = await revisions.GetAsync(projectId, row.PlanId, row.PlanRevisionId, token).ConfigureAwait(false);
        var requirement = await requirements.GetAsync(projectId, row.RequirementsRevisionId, token).ConfigureAwait(false);
        var confirmation = await context.Set<PlanConfirmationRecord>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.ConfirmationId == row.ConfirmationId, token).ConfigureAwait(false);
        if (revision is null || requirement is null || confirmation is null)
            throw new InvalidDataException("Report sources are missing.");
        var snapshot = BuildSnapshot(requirement, revision, confirmation, row.CapturedAtUtc, row.DisclaimerVersion, row.DisclaimerText);
        var status = Enum.Parse<ReportStatus>(row.Status);
        var artifact = row.ArtifactId is { } id
            ? new ReportArtifact(id, row.ArtifactMediaType!, row.ArtifactByteLength!.Value, row.ArtifactContentHash!, row.ArtifactCreatedAtUtc!.Value)
            : null;
        return Report.Rehydrate(row.Id, row.ProjectId, row.RequestedBy, row.RequestedAtUtc, snapshot, row.StateVersion, status, artifact,
            row.CompletedAtUtc, row.FailureCode, row.FailedAtUtc);
    }

    private static ReportSnapshot BuildSnapshot(RequirementRevision requirement, PlanRevisionSnapshot revision,
        PlanConfirmationRecord confirmation, DateTimeOffset capturedAt, string disclaimerVersion, string disclaimerText)
    {
        var v = revision.Validation;
        var plan = new ReportPlanSnapshot(revision.ProjectId, revision.PlanId, revision.RevisionId, revision.VersionNumber,
            revision.GeometrySchemaVersion, revision.GeometryJson, revision.GeometryHash, revision.LandRevisionId,
            revision.RequirementsRevisionId, revision.CreatedBy, revision.CreatedAtUtc,
            new ReportValidationSnapshot(v.ValidationId, v.ValidatorVersion, v.GeometryHash, v.GeometryValid, v.RequirementsMatch,
                v.RegulatoryEvidenceStatus, v.HasBlockingIssues, v.ValidatedAtUtc));
        var confirmed = new ReportConfirmationSnapshot(confirmation.ConfirmationId, confirmation.ProjectId, confirmation.PlanId,
            confirmation.ConfirmedRevisionId, confirmation.GeometryHash, confirmation.ValidationId, confirmation.ConfirmedBy,
            confirmation.ConfirmedAtUtc, confirmation.WarningAcknowledgement);
        return new ReportSnapshot(SnapshotSchema, capturedAt, requirement, plan, confirmed, new ReportDisclaimer(disclaimerVersion, disclaimerText));
    }
}

/// <summary>
/// Captain binding for the M5 architect workflow candidate. Recipients must be listed in the operator-supplied
/// architect directory; the acknowledged policy version must equal the configured current sharing policy; only a Ready,
/// intact report for the plan's current confirmation can be shared; other document types are not eligible (scope
/// undefined, G12). Nothing is dispatched to any external party.
/// </summary>
internal sealed class CaptainArchitectBoundary(
    BuildMindDbContext context, TransactionRunner transactions, IProjectAuthorizationPort authorization,
    CaptainReportBoundary reports, PrivateReportArtifactStore artifacts, IConfiguration configuration)
    : IArchitectWorkflowBoundaryCandidate
{
    public const string RecipientsFileKey = "CaptainIntegration:ArchitectRecipientsFile";
    public const string PolicyVersionKey = "CaptainIntegration:SharingPolicyVersion";

    public async Task<ArchitectReceipt> CreateAsync(Guid actor, ArchitectRequest proposed, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, proposed.ProjectId, actor, token).ConfigureAwait(false) is not { } version
            || proposed.RequestedBy != actor)
            return new(ArchitectBoundaryStatus.Hidden, actor);
        var policy = configuration[PolicyVersionKey];
        if (string.IsNullOrWhiteSpace(policy) || !Recipients().Contains(proposed.RecipientId))
            return new(ArchitectBoundaryStatus.NotEligible, actor);
        if (!string.Equals(policy, proposed.Consent.PolicyVersion, StringComparison.Ordinal))
            return new(ArchitectBoundaryStatus.Conflict, actor);
        if (proposed.Consent.DocumentIds.Count > 0 || proposed.Consent.ReportId is not { } reportId)
            return new(ArchitectBoundaryStatus.NotEligible, actor);
        var report = await reports.LoadAsync(proposed.ProjectId, reportId, token).ConfigureAwait(false);
        if (report is null) return new(ArchitectBoundaryStatus.Hidden, actor);
        if (report.Status != ReportStatus.Ready || report.Artifact is not { } artifact
            || await artifacts.ReadVerifiedAsync(artifact, ReportPrivatePdfReadVerifier.AbsoluteMaximumBytes, token).ConfigureAwait(false) is null)
            return new(ArchitectBoundaryStatus.NotEligible, actor);

        var consent = proposed.Consent;
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(proposed.ProjectId, actor, version, async inner =>
        {
            // The report must still belong to the plan's current (only) confirmation inside the locked transaction.
            var confirmationCurrent = await context.Set<PlanConfirmationRecord>().AnyAsync(c => c.ProjectId == proposed.ProjectId
                && c.ConfirmationId == report.InputSnapshot.Confirmation.ConfirmationId, inner).ConfigureAwait(false);
            if (!confirmationCurrent) return ProtectedWriteStatus.Conflict;
            context.Add(new ArchitectRequestRecord
            {
                RequestId = consent.RequestId, ConsentId = consent.Id, ProjectId = consent.ProjectId, GrantedBy = consent.GrantedBy,
                RecipientId = consent.RecipientId, ReportId = consent.ReportId, DocumentIdsJson = JsonSerializer.Serialize(consent.DocumentIds),
                PolicyVersion = consent.PolicyVersion, GrantedAtUtc = consent.GrantedAtUtc, RequestedAtUtc = proposed.RequestedAtUtc,
                StateVersion = proposed.StateVersion, Status = proposed.Status.ToString(),
            });
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return ProtectedWriteStatus.Written;
        }, token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => new(ArchitectBoundaryStatus.Accepted, actor, proposed),
            ProtectedWriteStatus.Conflict => new(ArchitectBoundaryStatus.NotEligible, actor),
            _ => new(ArchitectBoundaryStatus.Hidden, actor),
        };
    }

    public async Task<ArchitectReceipt> ReadAsync(Guid actor, Guid project, Guid request, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, project, actor, token).ConfigureAwait(false) is null)
            return new(ArchitectBoundaryStatus.Hidden, actor);
        var row = await context.Set<ArchitectRequestRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.RequestId == request && r.ProjectId == project && r.GrantedBy == actor, token).ConfigureAwait(false);
        return row is null ? new(ArchitectBoundaryStatus.Hidden, actor) : new(ArchitectBoundaryStatus.Accepted, actor, ToDomain(row));
    }

    public async Task<ArchitectListReceipt> ListAsync(Guid actor, Guid project, int maximum, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, project, actor, token).ConfigureAwait(false) is null)
            return new(ArchitectBoundaryStatus.Hidden, actor, project);
        var rows = await context.Set<ArchitectRequestRecord>().AsNoTracking().Where(r => r.ProjectId == project && r.GrantedBy == actor)
            .OrderByDescending(r => r.RequestedAtUtc).Take(maximum + 1).ToListAsync(token).ConfigureAwait(false);
        if (rows.Count > maximum) return new(ArchitectBoundaryStatus.Unavailable, actor, project); // never truncate silently
        return new(ArchitectBoundaryStatus.Accepted, actor, project, rows.Select(ToDomain).ToArray());
    }

    public async Task<ArchitectReceipt> WithdrawAsync(Guid actor, Guid project, Guid request, long version, CancellationToken token)
    {
        if (await ProtectedVersion.ReadAsync(authorization, project, actor, token).ConfigureAwait(false) is not { } authVersion)
            return new(ArchitectBoundaryStatus.Hidden, actor);
        var row = await context.Set<ArchitectRequestRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.RequestId == request && r.ProjectId == project && r.GrantedBy == actor, token).ConfigureAwait(false);
        if (row is null) return new(ArchitectBoundaryStatus.Hidden, actor);
        var previous = ToDomain(row);
        if (previous.Status != ArchitectRequestStatus.Pending || previous.StateVersion != version)
            return new(ArchitectBoundaryStatus.Conflict, actor);
        var withdrawn = previous.ProposeWithdrawal(version, new MicrosecondTimeProvider(TimeProvider.System).GetUtcNow());
        var status = await new ProtectedPlanningWriter(context, transactions).RunAsync(project, actor, authVersion, async inner =>
            await context.Set<ArchitectRequestRecord>()
                .Where(r => r.RequestId == request && r.StateVersion == version && r.Status == nameof(ArchitectRequestStatus.Pending))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.StateVersion, withdrawn.StateVersion)
                    .SetProperty(r => r.Status, nameof(ArchitectRequestStatus.Withdrawn))
                    .SetProperty(r => r.WithdrawnAtUtc, withdrawn.WithdrawnAtUtc), inner).ConfigureAwait(false) == 1
                ? ProtectedWriteStatus.Written : ProtectedWriteStatus.Conflict, token).ConfigureAwait(false);
        return status switch
        {
            ProtectedWriteStatus.Written => new(ArchitectBoundaryStatus.Accepted, actor, withdrawn, previous),
            ProtectedWriteStatus.Conflict => new(ArchitectBoundaryStatus.Conflict, actor),
            _ => new(ArchitectBoundaryStatus.Hidden, actor),
        };
    }

    private HashSet<Guid> Recipients()
    {
        var path = configuration[RecipientsFileKey];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("architects").EnumerateArray()
                .Select(a => a.GetProperty("recipientId").GetGuid()).Where(id => id != Guid.Empty).ToHashSet();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    private static ArchitectRequest ToDomain(ArchitectRequestRecord row) => ArchitectRequest.Rehydrate(
        new ShareConsent(row.ConsentId, row.RequestId, row.ProjectId, row.GrantedBy, row.RecipientId, row.ReportId,
            JsonSerializer.Deserialize<Guid[]>(row.DocumentIdsJson) ?? [], row.PolicyVersion, row.GrantedAtUtc, explicitlyGranted: true),
        row.RequestedAtUtc, row.StateVersion, Enum.Parse<ArchitectRequestStatus>(row.Status), row.WithdrawnAtUtc);
}

internal sealed class MySqlReportCatalog(BuildMindDbContext context) : IReportCatalog
{
    public async Task<IReadOnlyList<ReportSummary>> ListAsync(Guid projectId, int maximum, CancellationToken token) =>
        (await context.Set<ReportRecord>().AsNoTracking().Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.RequestedAtUtc).Take(maximum).ToListAsync(token).ConfigureAwait(false))
        .Select(r => new ReportSummary(r.Id, r.Status, r.PlanId, r.PlanRevisionId, r.ConfirmationId, r.RequestedAtUtc, r.CompletedAtUtc,
            r.FailureCode)).ToArray();
}

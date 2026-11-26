using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Reports.Abstractions;

/// <summary>Immutable source identities to recheck during report acceptance; not a freshness/authorization token.</summary>
public sealed class ReportSourceExpectation
{
    public Guid ProjectId { get; }
    public Guid RequirementsRevisionId { get; }
    public string RequirementsSchemaVersion { get; }
    public string CatalogueVersion { get; }
    public Guid LandRevisionId { get; }
    public Guid PlanId { get; }
    public Guid PlanRevisionId { get; }
    public Guid ConfirmationId { get; }
    public Guid ValidationId { get; }
    public string GeometryHash { get; }

    public ReportSourceExpectation(ReportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProjectId = snapshot.ProjectId;
        RequirementsRevisionId = snapshot.Requirements.Id;
        RequirementsSchemaVersion = snapshot.Requirements.SchemaVersion;
        CatalogueVersion = snapshot.Requirements.CatalogueVersion;
        LandRevisionId = snapshot.Plan.LandRevisionId;
        PlanId = snapshot.Plan.PlanId;
        PlanRevisionId = snapshot.Plan.RevisionId;
        ConfirmationId = snapshot.Confirmation.ConfirmationId;
        ValidationId = snapshot.Plan.Validation.ValidationId;
        GeometryHash = snapshot.Plan.GeometryHash;
    }
}

public enum ReportWriteStatus
{
    Created, Transitioned, ReportAlreadyExists, ReportNotFound, SourceConflict, StateConflict, SourceUnavailable
}

public sealed class ReportWriteResult
{
    public ReportWriteStatus Status { get; }
    public Report? SavedReport { get; }

    public ReportWriteResult(ReportWriteStatus status, Report? savedReport = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status is ReportWriteStatus.Created or ReportWriteStatus.Transitioned)
        {
            ArgumentNullException.ThrowIfNull(savedReport);
            if ((status == ReportWriteStatus.Created && savedReport.Status != ReportStatus.Pending)
                || (status == ReportWriteStatus.Transitioned && savedReport.Status == ReportStatus.Pending))
                throw new ArgumentException("Saved report state conflicts with the successful operation.", nameof(savedReport));
        }
        else if (savedReport is not null)
            throw new ArgumentException("Failed report writes cannot contain a saved report.", nameof(savedReport));
        Status = status;
        SavedReport = savedReport;
    }
}

/// <summary>
/// Persistence port only; not an ownership guard. Application must authenticate and authorize every
/// operation using the agreed M1/M4 boundary. No adapter, database transaction or source-trust check is implemented here.
/// </summary>
public interface IReportRepository
{
    /// <summary>Filter by BOTH project and report identity. Never return another project's report.</summary>
    Task<Report?> GetAsync(Guid projectId, Guid reportId, CancellationToken cancellationToken);

    /// <summary>
    /// Require nonempty IDs, Pending state/version 1, and expectations matching the proposed snapshot.
    /// In one atomic acceptance operation, verify project existence and the exact persisted requirements,
    /// plan, confirmation, validation/hash and land references against the agreed M3/M4 eligibility policy;
    /// insert the immutable snapshot/report only if all checks pass. Never use a caller's confirmation flag.
    /// Source conflict/unavailability and duplicate report ID must leave all existing data unchanged.
    /// M4 must establish the atomic source-read boundary; sequential reads followed by insert are insufficient.
    /// Duplicate-request idempotency beyond report-ID uniqueness remains an integration decision.
    /// </summary>
    Task<ReportWriteResult> TryCreatePendingAtomicallyAsync(Report pendingReport,
        ReportSourceExpectation expectedSources, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically compare the persisted project's report StateVersion with expectedStateVersion (positive).
    /// Require Pending -> Ready/Failed and exactly one version increment. Reject changes to identity,
    /// requester, request time or any input snapshot value. Only status-specific terminal metadata may change.
    /// Validate the whole proposed state, never partially update or overwrite a terminal report.
    /// Storage/rendering must happen outside the transaction; a Ready proposal requires verified private artifact metadata.
    /// Conflict/not-found returns no saved payload. Publication recovery is an M4/storage integration responsibility.
    /// </summary>
    Task<ReportWriteResult> TryTransitionAtomicallyAsync(long expectedStateVersion,
        Report proposedState, CancellationToken cancellationToken);

    // Implementations must honor cancellation without partial writes. A cancelled client does not
    // prove a remote commit was rolled back; recovery/idempotency must be established with M4.
}

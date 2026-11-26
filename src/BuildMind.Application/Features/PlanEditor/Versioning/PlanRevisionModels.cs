using BuildMind.Application.Features.PlanEditor.Contracts;

namespace BuildMind.Application.Features.PlanEditor.Versioning;

public sealed record PlanRevisionSnapshot(
    Guid RevisionId,
    Guid PlanId,
    Guid ProjectId,
    Guid? ParentRevisionId,
    long VersionNumber,
    string GeometrySchemaVersion,
    string GeometryJson,
    string GeometryHash,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    PlanValidationReferenceDto Validation,
    Guid? EditCommandId,
    Guid CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? ChangeSummary);

public sealed record CreatePlanRevisionCommand(
    Guid ProjectId,
    Guid PlanId,
    Guid ExpectedBaseRevisionId,
    PlanGeometryDto Geometry,
    PlanValidationReferenceDto Validation,
    Guid? EditCommandId,
    string? ChangeSummary);

public enum AppendRevisionStatus
{
    Created,
    RevisionConflict,
    NotFound,
    Unauthorized,
    ValidationUnavailable,
    ValidationRejected,
    DuplicateCommand,
    TransactionOutcomeUnknown,
    InvalidDependencyResponse,
    DependencyTimeout,
    DependencyUnavailable,
    UnexpectedInternalFailure
}

// Provisional M3 result; production reconciliation requires M4 coordination.
public sealed record AppendRevisionResult(AppendRevisionStatus Status, PlanRevisionSnapshot? Revision = null)
{
    public Guid? ExpectedRevisionId { get; init; }
    public Guid? ExpectedEditCommandId { get; init; }
}

public enum ValidationEvidenceStatus { Verified, Unavailable, Rejected }

public interface IPlanValidationEvidenceVerifier
{
    Task<ValidationEvidenceStatus> VerifyAsync(
        Guid projectId,
        Guid planId,
        PlanValidationReferenceDto evidence,
        string exactGeometryHash,
        CancellationToken cancellationToken);
}

public interface IPlanRevisionStore
{
    Task<PlanRevisionSnapshot?> GetAsync(Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken);
    Task<PlanRevisionSnapshot?> GetCurrentAsync(Guid projectId, Guid planId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(Guid projectId, Guid planId, CancellationToken cancellationToken);
    Task<AppendRevisionResult> TryAppendAtomicallyAsync(
        Guid expectedBaseRevisionId,
        PlanRevisionSnapshot proposedRevision,
        CancellationToken cancellationToken);
}

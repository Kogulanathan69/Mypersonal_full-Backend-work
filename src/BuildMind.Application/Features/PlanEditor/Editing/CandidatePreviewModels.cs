using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.PlanEditor.Editing;

// Feature-local provisional states. These are not approved shared API values.
public enum CandidatePreviewStatus { Pending, Applied, Rejected, Expired }

public sealed record CandidatePreviewSnapshot(
    Guid CandidateId,
    Guid ProjectId,
    Guid PlanId,
    Guid OwnerId,
    Guid BaseRevisionId,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    Guid CommandId,
    string GeometryJson,
    string GeometryHash,
    string GeometrySchemaVersion,
    PlanValidationReferenceDto Validation,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    CandidatePreviewStatus Status);

public sealed record CreateCandidatePreviewCommand(
    Guid ProjectId,
    Guid PlanId,
    Guid BaseRevisionId,
    PlanEditCommandDto Command,
    string CorrelationId);

public enum CandidatePreviewResultStatus
{
    Created,
    Unauthorized,
    RevisionNotFound,
    StaleBaseRevision,
    StaleInputs,
    InvalidDependencyResponse,
    InvalidCommand,
    DependencyUnavailable,
    CandidateRejected,
    PersistenceOutcomeUnknown,
    ValidationUnavailable,
    ValidationRejected,
    DependencyTimeout,
    UnexpectedInternalFailure
}

public sealed record CandidatePreviewResult(
    CandidatePreviewResultStatus Status,
    CandidatePreviewSnapshot? Candidate = null,
    IReadOnlyList<PlanEditDiagnosticDto>? Diagnostics = null);

public sealed record CandidateActionCommand(Guid CandidateId, Guid ProjectId, Guid PlanId);

public sealed record ApplyCandidateCommand(
    Guid CandidateId,
    Guid ProjectId,
    Guid PlanId,
    bool AcknowledgedWarnings,
    string? ChangeSummary = null);

public enum CandidateActionStatus
{
    Applied,
    Rejected,
    Unauthorized,
    NotFound,
    WrongProjectOrPlan,
    AlreadyApplied,
    AlreadyRejected,
    Expired,
    StaleBaseRevision,
    StaleInputs,
    InvalidDependencyResponse,
    DependencyUnavailable,
    GeometryTampered,
    ValidationUnavailable,
    ValidationRejected,
    BlockingIssues,
    WarningAcknowledgementRequired,
    PersistenceFailure,
    TransactionOutcomeUnknown,
    DependencyTimeout,
    UnexpectedInternalFailure
}

public sealed record CandidateActionResult(
    CandidateActionStatus Status,
    CandidatePreviewSnapshot? Candidate = null,
    PlanRevisionSnapshot? Revision = null);

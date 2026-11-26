using BuildMind.Application.Features.PlanEditor.Diagnostics;

namespace BuildMind.Application.Features.PlanEditor.Editing;

public sealed record AiEditPreviewCommand(
    Guid ProjectId,
    Guid PlanId,
    Guid ExpectedBaseRevisionId,
    string CustomerInstruction,
    string CorrelationId);

public enum AiEditOrchestrationStatus
{
    CandidateCreated,
    InvalidRequest,
    Unauthorized,
    RevisionNotFound,
    StaleBaseRevision,
    StaleInputs,
    InvalidDependencyResponse,
    ProposalUnavailable,
    ProposalRejected,
    ProposalInvalid,
    DependencyUnavailable,
    DependencyTimeout,
    CandidateRejected,
    CandidatePersistenceOutcomeUnknown,
    ValidationUnavailable,
    ValidationRejected,
    UnexpectedInternalFailure
}

public sealed record AiEditOrchestrationResult(
    AiEditOrchestrationStatus Status,
    CandidatePreviewSnapshot? Candidate = null,
    PlanEditorDiagnostic? Diagnostic = null);

namespace BuildMind.Application.Features.PlanEditor.Diagnostics;

public enum PlanEditorDiagnosticSeverity { Information, Warning, Error, Critical }
public enum PlanEditorRetrySafety { Never, Safe, Conditional }

public sealed record PlanEditorDiagnostic(
    string Code,
    string SafeMessage,
    PlanEditorDiagnosticSeverity Severity,
    bool IsBlocking,
    string? CorrelationId,
    Guid? AffectedEntityId,
    PlanEditorRetrySafety RetrySafety);

public enum PlanEditorDependencyOperation
{
    Ownership,
    Lifecycle,
    LandRevision,
    RequirementRevision,
    Compute,
    Persistence,
    Transaction,
    CandidatePersistence,
    CandidateApplyTransaction,
    CandidateRejectTransaction,
    CandidateExpireTransaction,
    Validation,
    AiProposal,
    ReportPublication,
    RevisionRead,
    ApplicableInputsRead
}

namespace BuildMind.Application.Features.PlanEditor.Diagnostics;

/// <summary>Feature-local proposed identifiers. Shared HTTP/API approval remains pending.</summary>
public static class PlanEditorErrorCodes
{
    public const string InvalidEditCommand = "INVALID_EDIT_COMMAND";
    public const string InvalidEditPayload = "INVALID_EDIT_PAYLOAD";
    public const string UnsupportedEditOperation = "UNSUPPORTED_EDIT_OPERATION";
    public const string UnsupportedSchemaVersion = "UNSUPPORTED_SCHEMA_VERSION";
    public const string InvalidGeometryReference = "INVALID_GEOMETRY_REFERENCE";
    public const string DuplicateEntityId = "DUPLICATE_ENTITY_ID";
    public const string GeometryOverlap = "GEOMETRY_OVERLAP";
    public const string OutsideLandBoundary = "OUTSIDE_LAND_BOUNDARY";
    public const string InvalidOpeningPosition = "INVALID_OPENING_POSITION";
    public const string UnsupportedTopology = "UNSUPPORTED_TOPOLOGY";

    public const string UnauthorizedProjectAccess = "UNAUTHORIZED_PROJECT_ACCESS";
    public const string ProjectNotFound = "PROJECT_NOT_FOUND";
    public const string PlanNotFound = "PLAN_NOT_FOUND";
    public const string FloorNotFound = "FLOOR_NOT_FOUND";
    public const string RoomNotFound = "ROOM_NOT_FOUND";
    public const string WallNotFound = "WALL_NOT_FOUND";
    public const string OpeningNotFound = "OPENING_NOT_FOUND";

    public const string CandidateNotFound = "CANDIDATE_NOT_FOUND";
    public const string CandidateExpired = "CANDIDATE_EXPIRED";
    public const string CandidateAlreadyApplied = "CANDIDATE_ALREADY_APPLIED";
    public const string CandidateAlreadyRejected = "CANDIDATE_ALREADY_REJECTED";
    public const string CandidateBindingMismatch = "CANDIDATE_BINDING_MISMATCH";
    public const string CandidateTampered = "CANDIDATE_TAMPERED";

    public const string RevisionConflict = "REVISION_CONFLICT";
    public const string RevisionNotFound = "REVISION_NOT_FOUND";
    public const string DuplicateCommand = "DUPLICATE_COMMAND";
    public const string LandRevisionChanged = "LAND_REVISION_CHANGED";
    public const string RequirementRevisionChanged = "REQUIREMENT_REVISION_CHANGED";
    public const string GeometryHashMismatch = "GEOMETRY_HASH_MISMATCH";

    public const string ValidationUnavailable = "VALIDATION_UNAVAILABLE";
    public const string ValidationEvidenceInvalid = "VALIDATION_EVIDENCE_INVALID";
    public const string ValidationEvidenceExpired = "VALIDATION_EVIDENCE_EXPIRED";
    public const string BlockingValidationIssues = "BLOCKING_VALIDATION_ISSUES";
    public const string WarningAcknowledgementRequired = "WARNING_ACKNOWLEDGEMENT_REQUIRED";

    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string DependencyTimeout = "DEPENDENCY_TIMEOUT";
    public const string InvalidDependencyResponse = "INVALID_DEPENDENCY_RESPONSE";
    public const string PersistenceFailure = "PERSISTENCE_FAILURE";
    public const string TransactionFailure = "TRANSACTION_FAILURE";
    public const string PersistenceOutcomeUnknown = "PERSISTENCE_OUTCOME_UNKNOWN";
    public const string TransactionOutcomeUnknown = "TRANSACTION_OUTCOME_UNKNOWN";
    public const string AiProposalInvalid = "AI_PROPOSAL_INVALID";
    public const string ReportPublicationFailure = "REPORT_PUBLICATION_FAILURE";
    public const string UnexpectedInternalFailure = "UNEXPECTED_INTERNAL_FAILURE";

    public const string ConfirmationBlocked = "CONFIRMATION_BLOCKED";
    public const string ConfirmationAlreadyExists = "CONFIRMATION_ALREADY_EXISTS";

    public static IReadOnlyList<string> All { get; } =
    [
        InvalidEditCommand, InvalidEditPayload, UnsupportedEditOperation, UnsupportedSchemaVersion,
        InvalidGeometryReference, DuplicateEntityId, GeometryOverlap, OutsideLandBoundary,
        InvalidOpeningPosition, UnsupportedTopology, UnauthorizedProjectAccess, ProjectNotFound,
        PlanNotFound, FloorNotFound, RoomNotFound, WallNotFound, OpeningNotFound, CandidateNotFound,
        CandidateExpired, CandidateAlreadyApplied, CandidateAlreadyRejected, CandidateBindingMismatch,
        CandidateTampered, RevisionConflict, RevisionNotFound, DuplicateCommand, LandRevisionChanged,
        RequirementRevisionChanged, GeometryHashMismatch, ValidationUnavailable, ValidationEvidenceInvalid,
        ValidationEvidenceExpired, BlockingValidationIssues, WarningAcknowledgementRequired,
        DependencyUnavailable, DependencyTimeout, InvalidDependencyResponse, PersistenceFailure,
        TransactionFailure, PersistenceOutcomeUnknown, TransactionOutcomeUnknown, AiProposalInvalid,
        ReportPublicationFailure, UnexpectedInternalFailure,
        ConfirmationBlocked, ConfirmationAlreadyExists
    ];
}

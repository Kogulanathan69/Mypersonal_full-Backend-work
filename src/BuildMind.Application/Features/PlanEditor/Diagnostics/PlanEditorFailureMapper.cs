using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.Features.PlanEditor.Diagnostics;

public static class PlanEditorFailureMapper
{
    public static PlanEditorDiagnostic FromCandidatePreview(
        CandidatePreviewResultStatus status, string? correlationId = null) => status switch
    {
        CandidatePreviewResultStatus.Unauthorized => Failure(PlanEditorErrorCodes.UnauthorizedProjectAccess,
            "You are not authorized to access this project.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidatePreviewResultStatus.RevisionNotFound => Failure(PlanEditorErrorCodes.RevisionNotFound,
            "The requested plan revision was not found.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidatePreviewResultStatus.StaleBaseRevision => Failure(PlanEditorErrorCodes.RevisionConflict,
            "The plan changed before this request was completed.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidatePreviewResultStatus.StaleInputs => Failure(PlanEditorErrorCodes.CandidateBindingMismatch,
            "The candidate inputs are no longer current.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidatePreviewResultStatus.InvalidDependencyResponse => Failure(PlanEditorErrorCodes.InvalidDependencyResponse,
            "A required service returned an invalid response.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidatePreviewResultStatus.InvalidCommand => Failure(PlanEditorErrorCodes.InvalidEditCommand,
            "The edit command is invalid.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidatePreviewResultStatus.DependencyUnavailable => Retryable(PlanEditorErrorCodes.DependencyUnavailable,
            "A required service is temporarily unavailable.", correlationId),
        CandidatePreviewResultStatus.CandidateRejected => Failure(PlanEditorErrorCodes.InvalidDependencyResponse,
            "The edit candidate could not be accepted.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidatePreviewResultStatus.PersistenceOutcomeUnknown => Conditional(
            PlanEditorErrorCodes.PersistenceOutcomeUnknown,
            "Candidate persistence has an unknown outcome and must be reconciled before retry.", correlationId),
        CandidatePreviewResultStatus.ValidationUnavailable => Retryable(PlanEditorErrorCodes.ValidationUnavailable,
            "Authoritative validation is temporarily unavailable.", correlationId),
        CandidatePreviewResultStatus.ValidationRejected => Failure(PlanEditorErrorCodes.ValidationEvidenceInvalid,
            "The validation evidence could not be accepted.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidatePreviewResultStatus.DependencyTimeout => Retryable(PlanEditorErrorCodes.DependencyTimeout,
            "A required service timed out.", correlationId),
        CandidatePreviewResultStatus.UnexpectedInternalFailure => Failure(PlanEditorErrorCodes.UnexpectedInternalFailure,
            "The operation could not be completed.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A successful result has no failure diagnostic.")
    };

    public static PlanEditorDiagnostic FromCandidateAction(
        CandidateActionStatus status, string? correlationId = null) => status switch
    {
        CandidateActionStatus.Unauthorized => Failure(PlanEditorErrorCodes.UnauthorizedProjectAccess,
            "You are not authorized to access this project.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidateActionStatus.NotFound => Failure(PlanEditorErrorCodes.CandidateNotFound,
            "The requested candidate was not found.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidateActionStatus.WrongProjectOrPlan => Failure(PlanEditorErrorCodes.CandidateBindingMismatch,
            "The candidate does not belong to this plan.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidateActionStatus.AlreadyApplied => Failure(PlanEditorErrorCodes.CandidateAlreadyApplied,
            "The candidate has already been applied.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidateActionStatus.AlreadyRejected => Failure(PlanEditorErrorCodes.CandidateAlreadyRejected,
            "The candidate has already been rejected.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidateActionStatus.Expired => Failure(PlanEditorErrorCodes.CandidateExpired,
            "The candidate has expired.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidateActionStatus.StaleBaseRevision => Failure(PlanEditorErrorCodes.RevisionConflict,
            "The plan changed before this candidate was applied.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidateActionStatus.StaleInputs => Failure(PlanEditorErrorCodes.CandidateBindingMismatch,
            "The candidate inputs are no longer current.", PlanEditorDiagnosticSeverity.Error, correlationId),
        CandidateActionStatus.InvalidDependencyResponse => Failure(PlanEditorErrorCodes.InvalidDependencyResponse,
            "A required service returned an invalid response.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidateActionStatus.DependencyUnavailable => Retryable(PlanEditorErrorCodes.DependencyUnavailable,
            "A required service is temporarily unavailable.", correlationId),
        CandidateActionStatus.GeometryTampered => Failure(PlanEditorErrorCodes.CandidateTampered,
            "The candidate snapshot failed its integrity check.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidateActionStatus.ValidationUnavailable => Retryable(PlanEditorErrorCodes.ValidationUnavailable,
            "Authoritative validation is temporarily unavailable.", correlationId),
        CandidateActionStatus.ValidationRejected => Failure(PlanEditorErrorCodes.ValidationEvidenceInvalid,
            "The validation evidence could not be accepted.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidateActionStatus.BlockingIssues => Failure(PlanEditorErrorCodes.BlockingValidationIssues,
            "The candidate has blocking validation issues.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        CandidateActionStatus.WarningAcknowledgementRequired => new(
            PlanEditorErrorCodes.WarningAcknowledgementRequired,
            "Review and acknowledge the nonblocking warnings before continuing.",
            PlanEditorDiagnosticSeverity.Warning, true, correlationId, null, PlanEditorRetrySafety.Never),
        CandidateActionStatus.PersistenceFailure => new(
            PlanEditorErrorCodes.TransactionFailure, "The operation could not be saved.",
            PlanEditorDiagnosticSeverity.Critical, true, correlationId, null, PlanEditorRetrySafety.Conditional),
        CandidateActionStatus.TransactionOutcomeUnknown => Conditional(
            PlanEditorErrorCodes.TransactionOutcomeUnknown,
            "The transaction outcome is unknown and must be reconciled before retry.", correlationId),
        CandidateActionStatus.DependencyTimeout => Retryable(PlanEditorErrorCodes.DependencyTimeout,
            "A required service timed out.", correlationId),
        CandidateActionStatus.UnexpectedInternalFailure => Failure(PlanEditorErrorCodes.UnexpectedInternalFailure,
            "The operation could not be completed.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A successful result has no failure diagnostic.")
    };

    public static PlanEditorDiagnostic FromRevision(
        AppendRevisionStatus status, string? correlationId = null) => status switch
    {
        AppendRevisionStatus.RevisionConflict => Failure(PlanEditorErrorCodes.RevisionConflict,
            "The plan changed before the revision was saved.", PlanEditorDiagnosticSeverity.Error, correlationId),
        AppendRevisionStatus.NotFound => Failure(PlanEditorErrorCodes.RevisionNotFound,
            "The requested plan revision was not found.", PlanEditorDiagnosticSeverity.Error, correlationId),
        AppendRevisionStatus.Unauthorized => Failure(PlanEditorErrorCodes.UnauthorizedProjectAccess,
            "You are not authorized to access this project.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        AppendRevisionStatus.ValidationUnavailable => Retryable(PlanEditorErrorCodes.ValidationUnavailable,
            "Authoritative validation is temporarily unavailable.", correlationId),
        AppendRevisionStatus.ValidationRejected => Failure(PlanEditorErrorCodes.ValidationEvidenceInvalid,
            "The validation evidence could not be accepted.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        AppendRevisionStatus.DuplicateCommand => Failure(PlanEditorErrorCodes.DuplicateCommand,
            "This edit command has already been submitted.", PlanEditorDiagnosticSeverity.Error, correlationId),
        AppendRevisionStatus.TransactionOutcomeUnknown => Conditional(
            PlanEditorErrorCodes.TransactionOutcomeUnknown,
            "The revision transaction outcome is unknown and must be reconciled before retry.",
            correlationId),
        AppendRevisionStatus.InvalidDependencyResponse => Failure(
            PlanEditorErrorCodes.InvalidDependencyResponse,
            "A required service returned an invalid revision snapshot.",
            PlanEditorDiagnosticSeverity.Critical, correlationId),
        AppendRevisionStatus.DependencyTimeout => Retryable(PlanEditorErrorCodes.DependencyTimeout,
            "A required service timed out.", correlationId),
        AppendRevisionStatus.DependencyUnavailable => Retryable(PlanEditorErrorCodes.DependencyUnavailable,
            "A required service is temporarily unavailable.", correlationId),
        AppendRevisionStatus.UnexpectedInternalFailure => Failure(PlanEditorErrorCodes.UnexpectedInternalFailure,
            "The operation could not be completed.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A successful result has no failure diagnostic.")
    };

    public static PlanEditorDiagnostic FromConfirmation(
        ConfirmationStatus status, string? correlationId = null) => status switch
    {
        ConfirmationStatus.Unauthorized => Failure(PlanEditorErrorCodes.UnauthorizedProjectAccess,
            "You are not authorized to access this project.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        ConfirmationStatus.RevisionNotFound => Failure(PlanEditorErrorCodes.RevisionNotFound,
            "The requested plan revision was not found.", PlanEditorDiagnosticSeverity.Error, correlationId),
        ConfirmationStatus.GeometryHashMismatch => Failure(PlanEditorErrorCodes.GeometryHashMismatch,
            "The plan snapshot failed its integrity check.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        ConfirmationStatus.StaleInputs => Failure(PlanEditorErrorCodes.CandidateBindingMismatch,
            "The plan inputs are no longer current.", PlanEditorDiagnosticSeverity.Error, correlationId),
        ConfirmationStatus.ValidationStale => Failure(PlanEditorErrorCodes.ValidationEvidenceExpired,
            "The validation evidence is no longer current.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        ConfirmationStatus.NotCurrentRevision => Failure(PlanEditorErrorCodes.RevisionConflict,
            "Only the current plan revision can be confirmed.", PlanEditorDiagnosticSeverity.Error, correlationId),
        ConfirmationStatus.BlockingIssues => Failure(PlanEditorErrorCodes.ConfirmationBlocked,
            "The plan cannot be confirmed while blocking issues remain.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        ConfirmationStatus.WarningAcknowledgementRequired => new(
            PlanEditorErrorCodes.WarningAcknowledgementRequired,
            "Review and acknowledge the nonblocking warnings before continuing.",
            PlanEditorDiagnosticSeverity.Warning, true, correlationId, null, PlanEditorRetrySafety.Never),
        ConfirmationStatus.AlreadyConfirmed => Failure(PlanEditorErrorCodes.ConfirmationAlreadyExists,
            "This exact plan revision is already confirmed.", PlanEditorDiagnosticSeverity.Error, correlationId),
        ConfirmationStatus.TransactionOutcomeUnknown => Conditional(
            PlanEditorErrorCodes.TransactionOutcomeUnknown,
            "The confirmation transaction outcome is unknown and must be reconciled before retry.",
            correlationId),
        ConfirmationStatus.InvalidDependencyResponse => Failure(
            PlanEditorErrorCodes.InvalidDependencyResponse,
            "A required service returned an invalid confirmation dependency response.",
            PlanEditorDiagnosticSeverity.Critical, correlationId),
        ConfirmationStatus.DependencyTimeout => Retryable(PlanEditorErrorCodes.DependencyTimeout,
            "A required service timed out.", correlationId),
        ConfirmationStatus.DependencyUnavailable => Retryable(PlanEditorErrorCodes.DependencyUnavailable,
            "A required service is temporarily unavailable.", correlationId),
        ConfirmationStatus.UnexpectedInternalFailure => Failure(PlanEditorErrorCodes.UnexpectedInternalFailure,
            "The operation could not be completed.", PlanEditorDiagnosticSeverity.Critical, correlationId),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A successful result has no failure diagnostic.")
    };

    public static PlanEditorDiagnostic FromConfirmationPublication(
        ConfirmationPublicationStatus status,
        string? correlationId = null) => status switch
    {
        ConfirmationPublicationStatus.OutcomeUnknown => Conditional(
            PlanEditorErrorCodes.ReportPublicationFailure,
            "The plan is confirmed, but report publication must be reconciled before delivery is retried.",
            correlationId),
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "A successful or unattempted publication has no failure diagnostic.")
    };

    public static PlanEditorDiagnostic FromDependencyException(
        PlanEditorDependencyOperation operation,
        Exception exception,
        string? correlationId,
        CancellationToken callerCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException && callerCancellationToken.IsCancellationRequested)
            throw exception;
        if (operation is PlanEditorDependencyOperation.Persistence or
            PlanEditorDependencyOperation.Transaction or
            PlanEditorDependencyOperation.CandidatePersistence or
            PlanEditorDependencyOperation.CandidateApplyTransaction or
            PlanEditorDependencyOperation.CandidateRejectTransaction or
            PlanEditorDependencyOperation.CandidateExpireTransaction or
            PlanEditorDependencyOperation.ReportPublication)
            return Conditional(
                operation is PlanEditorDependencyOperation.ReportPublication
                    ? PlanEditorErrorCodes.ReportPublicationFailure
                    : operation is PlanEditorDependencyOperation.Persistence or
                    PlanEditorDependencyOperation.CandidatePersistence
                    ? PlanEditorErrorCodes.PersistenceOutcomeUnknown
                    : PlanEditorErrorCodes.TransactionOutcomeUnknown,
                operation is PlanEditorDependencyOperation.ReportPublication
                    ? "Report publication has an unknown outcome and requires reconciliation before retry."
                    : operation is PlanEditorDependencyOperation.Persistence or
                    PlanEditorDependencyOperation.CandidatePersistence
                    ? "Persistence has an unknown outcome and must be reconciled before retry."
                    : "The transaction outcome is unknown and must be reconciled before retry.",
                correlationId);
        if (exception is TimeoutException or OperationCanceledException)
            return new(PlanEditorErrorCodes.DependencyTimeout, "A required service timed out.",
                PlanEditorDiagnosticSeverity.Error, true, correlationId, null, PlanEditorRetrySafety.Safe);
        if (exception is HttpRequestException)
            return Retryable(PlanEditorErrorCodes.DependencyUnavailable,
                "A required service is temporarily unavailable.", correlationId);
        if (exception is JsonException or InvalidDependencyResponseException or FormatException)
            return Failure(PlanEditorErrorCodes.InvalidDependencyResponse,
                "A required service returned an invalid response.", PlanEditorDiagnosticSeverity.Critical, correlationId);

        return Failure(PlanEditorErrorCodes.UnexpectedInternalFailure,
            "The operation could not be completed.", PlanEditorDiagnosticSeverity.Critical, correlationId);
    }

    public static int ProposedHttpStatus(PlanEditorDiagnostic diagnostic) => diagnostic.Code switch
    {
        PlanEditorErrorCodes.UnauthorizedProjectAccess => 403,
        PlanEditorErrorCodes.ProjectNotFound or PlanEditorErrorCodes.PlanNotFound or
            PlanEditorErrorCodes.RevisionNotFound or PlanEditorErrorCodes.CandidateNotFound => 404,
        PlanEditorErrorCodes.RevisionConflict or PlanEditorErrorCodes.DuplicateCommand or
            PlanEditorErrorCodes.CandidateAlreadyApplied or PlanEditorErrorCodes.CandidateAlreadyRejected or
            PlanEditorErrorCodes.ConfirmationAlreadyExists => 409,
        PlanEditorErrorCodes.InvalidEditCommand or PlanEditorErrorCodes.InvalidEditPayload or
            PlanEditorErrorCodes.UnsupportedSchemaVersion => 400,
        PlanEditorErrorCodes.DependencyUnavailable or PlanEditorErrorCodes.ValidationUnavailable => 503,
        PlanEditorErrorCodes.DependencyTimeout => 504,
        PlanEditorErrorCodes.PersistenceFailure or PlanEditorErrorCodes.TransactionFailure or
            PlanEditorErrorCodes.PersistenceOutcomeUnknown or PlanEditorErrorCodes.TransactionOutcomeUnknown or
            PlanEditorErrorCodes.ReportPublicationFailure or PlanEditorErrorCodes.UnexpectedInternalFailure => 500,
        _ => 422
    };

    private static PlanEditorDiagnostic Failure(
        string code, string message, PlanEditorDiagnosticSeverity severity, string? correlationId) =>
        new(code, message, severity, true, correlationId, null, PlanEditorRetrySafety.Never);

    private static PlanEditorDiagnostic Retryable(string code, string message, string? correlationId) =>
        new(code, message, PlanEditorDiagnosticSeverity.Error, true, correlationId, null, PlanEditorRetrySafety.Safe);

    private static PlanEditorDiagnostic Conditional(string code, string message, string? correlationId) =>
        new(code, message, PlanEditorDiagnosticSeverity.Critical, true, correlationId, null,
            PlanEditorRetrySafety.Conditional);
}

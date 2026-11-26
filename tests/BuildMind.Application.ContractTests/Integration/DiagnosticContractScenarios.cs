using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

internal static class DiagnosticContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("diagnostic identifiers are stable unique uppercase values", () =>
        {
            Check(PlanEditorErrorCodes.All.Count == PlanEditorErrorCodes.All.Distinct(StringComparer.Ordinal).Count(),
                "Diagnostic catalog contains duplicate identifiers.");
            Check(PlanEditorErrorCodes.All.All(code => code.Length > 0 && code == code.ToUpperInvariant()),
                "Diagnostic identifiers must be nonempty uppercase values.");
            Check(PlanEditorErrorCodes.FloorNotFound == "FLOOR_NOT_FOUND" &&
                PlanEditorErrorCodes.InvalidEditPayload == "INVALID_EDIT_PAYLOAD" &&
                PlanEditorErrorCodes.UnsupportedTopology == "UNSUPPORTED_TOPOLOGY",
                "Existing Python editor identifiers changed.");
        });

        run("diagnostic severity and blocking rules are consistent", () =>
        {
            var authorization = PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.Unauthorized);
            var warning = PlanEditorFailureMapper.FromCandidateAction(
                CandidateActionStatus.WarningAcknowledgementRequired);
            Check(authorization is { Severity: PlanEditorDiagnosticSeverity.Critical, IsBlocking: true },
                "Authorization failure was not critical and blocking.");
            Check(warning is { Severity: PlanEditorDiagnosticSeverity.Warning, IsBlocking: true },
                "Acknowledgement warning must block only until acknowledged.");
        });

        run("invalid edit command maps to stable safe diagnostic", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromCandidatePreview(
                CandidatePreviewResultStatus.InvalidCommand, "corr-edit");
            Check(diagnostic.Code == PlanEditorErrorCodes.InvalidEditCommand, "Invalid command code changed.");
            Check(diagnostic.SafeMessage == "The edit command is invalid.", "Invalid command message was unsafe.");
        });

        run("authorization failure maps to forbidden proposal", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromConfirmation(ConfirmationStatus.Unauthorized);
            Check(diagnostic.Code == PlanEditorErrorCodes.UnauthorizedProjectAccess,
                "Authorization code was inconsistent.");
            Check(PlanEditorFailureMapper.ProposedHttpStatus(diagnostic) == 403,
                "Authorization HTTP proposal was not forbidden.");
        });

        run("candidate expiry replay and binding mismatch have distinct codes", () =>
        {
            Check(PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.Expired).Code
                == PlanEditorErrorCodes.CandidateExpired, "Expiry code changed.");
            Check(PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.AlreadyApplied).Code
                == PlanEditorErrorCodes.CandidateAlreadyApplied, "Apply replay code changed.");
            Check(PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.AlreadyRejected).Code
                == PlanEditorErrorCodes.CandidateAlreadyRejected, "Reject replay code changed.");
            Check(PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.WrongProjectOrPlan).Code
                == PlanEditorErrorCodes.CandidateBindingMismatch, "Binding mismatch code changed.");
        });

        run("geometry hash mismatch and tampering remain critical", () =>
        {
            var confirmation = PlanEditorFailureMapper.FromConfirmation(ConfirmationStatus.GeometryHashMismatch);
            var candidate = PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.GeometryTampered);
            Check(confirmation.Code == PlanEditorErrorCodes.GeometryHashMismatch && confirmation.IsBlocking,
                "Confirmation hash mismatch was weakened.");
            Check(candidate.Code == PlanEditorErrorCodes.CandidateTampered &&
                candidate.Severity == PlanEditorDiagnosticSeverity.Critical,
                "Candidate tampering was weakened.");
        });

        run("stale revisions map to conflict without retry claim", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromRevision(AppendRevisionStatus.RevisionConflict);
            Check(diagnostic.Code == PlanEditorErrorCodes.RevisionConflict, "Revision conflict code changed.");
            Check(diagnostic.RetrySafety == PlanEditorRetrySafety.Never,
                "Blind retry was allowed for a stale revision.");
            Check(PlanEditorFailureMapper.ProposedHttpStatus(diagnostic) == 409,
                "Conflict HTTP proposal changed.");
        });

        run("validation unavailable is safely retryable", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.ValidationUnavailable);
            Check(diagnostic.Code == PlanEditorErrorCodes.ValidationUnavailable, "Validation unavailable code changed.");
            Check(diagnostic.RetrySafety == PlanEditorRetrySafety.Safe, "Validation unavailable retry was unsafe.");
            Check(PlanEditorFailureMapper.ProposedHttpStatus(diagnostic) == 503,
                "Validation unavailable HTTP proposal changed.");
        });

        run("invalid validation evidence remains critical and nonretryable", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.ValidationRejected);
            Check(diagnostic.Code == PlanEditorErrorCodes.ValidationEvidenceInvalid,
                "Invalid validation evidence code changed.");
            Check(diagnostic is { Severity: PlanEditorDiagnosticSeverity.Critical,
                RetrySafety: PlanEditorRetrySafety.Never }, "Invalid evidence behavior was weakened.");
        });

        run("dependency timeout is distinct from unavailability", () =>
        {
            var timeout = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Compute, new TimeoutException("private endpoint"), "corr-timeout");
            var unavailable = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Compute, new HttpRequestException("private endpoint"), "corr-down");
            Check(timeout.Code == PlanEditorErrorCodes.DependencyTimeout, "Timeout was not distinguished.");
            Check(unavailable.Code == PlanEditorErrorCodes.DependencyUnavailable,
                "Unavailability was not distinguished.");
            Check(PlanEditorFailureMapper.ProposedHttpStatus(timeout) == 504, "Timeout HTTP proposal changed.");
        });

        run("caller cancellation semantics are preserved", () =>
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            CheckThrows<OperationCanceledException>(() => PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Compute, new OperationCanceledException("caller cancelled"),
                "corr-cancel", source.Token));
        });

        run("dependency cancellation without caller cancellation maps to timeout", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Compute,
                new OperationCanceledException("dependency deadline"), "corr-dependency-cancel");
            Check(diagnostic.Code == PlanEditorErrorCodes.DependencyTimeout,
                "Dependency cancellation was not treated as a timeout.");
            Check(diagnostic.RetrySafety == PlanEditorRetrySafety.Safe,
                "Read-only dependency timeout retry classification changed.");
        });

        run("unexpected dependency response maps without raw details", () =>
        {
            const string sensitive = "server password=do-not-expose";
            var diagnostic = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.LandRevision, new JsonException(sensitive), "corr-invalid");
            Check(diagnostic.Code == PlanEditorErrorCodes.InvalidDependencyResponse,
                "Invalid dependency response code changed.");
            Check(!diagnostic.SafeMessage.Contains(sensitive, StringComparison.Ordinal),
                "Sensitive exception detail escaped into the diagnostic.");
        });

        run("unexpected internal failure suppresses sensitive exception details", () =>
        {
            const string sensitive = "SQL connection user=admin secret=private";
            var diagnostic = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Validation, new InvalidOperationException(sensitive), "corr-internal");
            var invalidData = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.RequirementRevision,
                new InvalidDataException(sensitive), "corr-invalid-data");
            Check(diagnostic.Code == PlanEditorErrorCodes.UnexpectedInternalFailure,
                "Unexpected failure code changed.");
            Check(invalidData.Code == PlanEditorErrorCodes.UnexpectedInternalFailure,
                "A general InvalidDataException was treated as a proven dependency response violation.");
            Check(!diagnostic.SafeMessage.Contains("SQL", StringComparison.OrdinalIgnoreCase) &&
                !diagnostic.SafeMessage.Contains("secret", StringComparison.OrdinalIgnoreCase) &&
                !invalidData.SafeMessage.Contains("secret", StringComparison.OrdinalIgnoreCase),
                "Internal exception details escaped into the diagnostic.");
        });

        run("persistence and transaction exceptions require reconciliation", () =>
        {
            var persistence = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Persistence, new InvalidOperationException("provider detail"), "p");
            var transaction = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Transaction, new InvalidOperationException("provider detail"), "t");
            Check(persistence.Code == PlanEditorErrorCodes.PersistenceOutcomeUnknown &&
                persistence.RetrySafety == PlanEditorRetrySafety.Conditional, "Persistence retry classification changed.");
            Check(transaction.Code == PlanEditorErrorCodes.TransactionOutcomeUnknown &&
                transaction.RetrySafety == PlanEditorRetrySafety.Conditional, "Transaction retry classification changed.");
        });

        run("read timeout remains safe while mutation timeout requires reconciliation", () =>
        {
            var read = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.Compute, new TimeoutException("private read timeout"), "read-corr");
            var persistence = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.CandidatePersistence,
                new TimeoutException("private persistence timeout"), "persist-corr");
            var transaction = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.CandidateApplyTransaction,
                new HttpRequestException("private acknowledgement lost"), "apply-corr");
            Check(read is { Code: PlanEditorErrorCodes.DependencyTimeout,
                RetrySafety: PlanEditorRetrySafety.Safe }, "Read-only timeout retry semantics changed.");
            Check(persistence is { Code: PlanEditorErrorCodes.PersistenceOutcomeUnknown,
                RetrySafety: PlanEditorRetrySafety.Conditional, CorrelationId: "persist-corr" },
                "Persistence timeout was declared safely retryable.");
            Check(transaction is { Code: PlanEditorErrorCodes.TransactionOutcomeUnknown,
                RetrySafety: PlanEditorRetrySafety.Conditional, CorrelationId: "apply-corr" },
                "Transaction transport failure was declared safely retryable.");
            Check(!persistence.SafeMessage.Contains("private", StringComparison.OrdinalIgnoreCase) &&
                !transaction.SafeMessage.Contains("private", StringComparison.OrdinalIgnoreCase),
                "Mutation diagnostic exposed dependency details.");
        });

        run("candidate uncertain outcomes have explicit reconciliation diagnostics", () =>
        {
            var creation = PlanEditorFailureMapper.FromCandidatePreview(
                CandidatePreviewResultStatus.PersistenceOutcomeUnknown, "candidate-corr");
            var action = PlanEditorFailureMapper.FromCandidateAction(
                CandidateActionStatus.TransactionOutcomeUnknown, "transaction-corr");
            Check(creation.Code == PlanEditorErrorCodes.PersistenceOutcomeUnknown &&
                creation.RetrySafety == PlanEditorRetrySafety.Conditional &&
                creation.CorrelationId == "candidate-corr", "Candidate reconciliation diagnostic changed.");
            Check(action.Code == PlanEditorErrorCodes.TransactionOutcomeUnknown &&
                action.RetrySafety == PlanEditorRetrySafety.Conditional &&
                action.CorrelationId == "transaction-corr", "Transaction reconciliation diagnostic changed.");
        });

        run("revision uncertainty and invalid dependency diagnostics remain safe", () =>
        {
            const string correlation = "revision-correlation";
            var unknown = PlanEditorFailureMapper.FromRevision(
                AppendRevisionStatus.TransactionOutcomeUnknown, correlation);
            Check(unknown.Code == PlanEditorErrorCodes.TransactionOutcomeUnknown &&
                unknown.RetrySafety == PlanEditorRetrySafety.Conditional &&
                unknown.CorrelationId == correlation && unknown.IsBlocking &&
                unknown.SafeMessage.Contains("reconciled before retry", StringComparison.Ordinal) &&
                PlanEditorFailureMapper.ProposedHttpStatus(unknown) == 500,
                "Revision uncertainty diagnostic lost reconciliation semantics.");

            var invalid = PlanEditorFailureMapper.FromRevision(
                AppendRevisionStatus.InvalidDependencyResponse, correlation);
            Check(invalid.Code == PlanEditorErrorCodes.InvalidDependencyResponse &&
                invalid.Severity == PlanEditorDiagnosticSeverity.Critical && invalid.IsBlocking &&
                invalid.RetrySafety == PlanEditorRetrySafety.Never &&
                !invalid.SafeMessage.Contains("provider", StringComparison.OrdinalIgnoreCase),
                "Invalid revision dependency diagnostic was retryable or exposed provider detail.");
        });

        run("confirmation transaction and publication uncertainty remain distinct", () =>
        {
            const string correlation = "confirmation-correlation";
            var transaction = PlanEditorFailureMapper.FromConfirmation(
                ConfirmationStatus.TransactionOutcomeUnknown, correlation);
            Check(transaction is
                {
                    Code: PlanEditorErrorCodes.TransactionOutcomeUnknown,
                    Severity: PlanEditorDiagnosticSeverity.Critical,
                    IsBlocking: true,
                    RetrySafety: PlanEditorRetrySafety.Conditional,
                    CorrelationId: correlation
                } && transaction.SafeMessage.Contains("reconciled before retry", StringComparison.Ordinal) &&
                PlanEditorFailureMapper.ProposedHttpStatus(transaction) == 500,
                "Confirmation transaction uncertainty lost reconciliation semantics.");

            var invalid = PlanEditorFailureMapper.FromConfirmation(
                ConfirmationStatus.InvalidDependencyResponse, correlation);
            Check(invalid is
                {
                    Code: PlanEditorErrorCodes.InvalidDependencyResponse,
                    Severity: PlanEditorDiagnosticSeverity.Critical,
                    IsBlocking: true,
                    RetrySafety: PlanEditorRetrySafety.Never
                }, "Invalid confirmation dependency response was weakened.");

            var publication = PlanEditorFailureMapper.FromConfirmationPublication(
                ConfirmationPublicationStatus.OutcomeUnknown, correlation);
            Check(publication.Code == PlanEditorErrorCodes.ReportPublicationFailure &&
                publication.RetrySafety == PlanEditorRetrySafety.Conditional &&
                publication.CorrelationId == correlation &&
                publication.SafeMessage.Contains("plan is confirmed", StringComparison.OrdinalIgnoreCase) &&
                !publication.SafeMessage.Contains("provider", StringComparison.OrdinalIgnoreCase),
                "Publication uncertainty implied confirmation rollback or exposed provider detail.");
            CheckThrows<ArgumentOutOfRangeException>(() =>
                PlanEditorFailureMapper.FromConfirmationPublication(
                    ConfirmationPublicationStatus.Acknowledged));
        });

        run("expiration transaction failure requires reconciliation with safe diagnostics", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.CandidateExpireTransaction,
                new TimeoutException("private expiration transaction detail"), "expire-correlation");
            Check(diagnostic is
                {
                    Code: PlanEditorErrorCodes.TransactionOutcomeUnknown,
                    RetrySafety: PlanEditorRetrySafety.Conditional,
                    CorrelationId: "expire-correlation"
                }, "Expiration transaction timeout was declared safely retryable.");
            Check(!diagnostic.SafeMessage.Contains("private", StringComparison.OrdinalIgnoreCase),
                "Expiration diagnostic exposed dependency details.");
        });

        run("warning acknowledgement never weakens critical blocking diagnostics", () =>
        {
            var warning = PlanEditorFailureMapper.FromCandidateAction(
                CandidateActionStatus.WarningAcknowledgementRequired);
            var blocking = PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.BlockingIssues);
            Check(warning.Severity == PlanEditorDiagnosticSeverity.Warning, "Warning severity changed.");
            Check(blocking.Code == PlanEditorErrorCodes.BlockingValidationIssues &&
                blocking.Severity == PlanEditorDiagnosticSeverity.Critical && blocking.IsBlocking,
                "Critical validation issue was made acknowledgeable.");
        });

        run("trusted correlation ID is propagated without fabrication", () =>
        {
            const string correlation = "trusted-correlation-123";
            var diagnostic = PlanEditorFailureMapper.FromRevision(
                AppendRevisionStatus.DuplicateCommand, correlation);
            var absent = PlanEditorFailureMapper.FromRevision(AppendRevisionStatus.DuplicateCommand);
            Check(diagnostic.CorrelationId == correlation, "Trusted correlation ID changed.");
            Check(absent.CorrelationId is null, "A missing correlation ID was fabricated.");
        });

        run("report publication failure after commit remains explicit", () =>
        {
            var confirmationCommitted = true;
            var diagnostic = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.ReportPublication,
                new InvalidOperationException("private report provider detail"), "corr-report");
            Check(confirmationCommitted, "Scenario must represent a committed confirmation.");
            Check(diagnostic.Code == PlanEditorErrorCodes.ReportPublicationFailure,
                "Report publication failure was hidden as confirmation rollback.");
            Check(diagnostic.RetrySafety == PlanEditorRetrySafety.Conditional,
                "Report retry must require idempotent outbox semantics.");
            var timeout = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.ReportPublication,
                new TimeoutException("private delivery acknowledgement"), "corr-report-timeout");
            Check(timeout.Code == PlanEditorErrorCodes.ReportPublicationFailure &&
                timeout.RetrySafety == PlanEditorRetrySafety.Conditional &&
                !timeout.SafeMessage.Contains("private", StringComparison.OrdinalIgnoreCase),
                "Publication timeout was declared safely repeatable or exposed provider detail.");
        });

        run("AI proposal failure has safe feature-local mapping", () =>
        {
            var diagnostic = PlanEditorFailureMapper.FromDependencyException(
                PlanEditorDependencyOperation.AiProposal, new InvalidOperationException("prompt and key"), "corr-ai");
            Check(diagnostic.Code == PlanEditorErrorCodes.UnexpectedInternalFailure &&
                diagnostic.RetrySafety == PlanEditorRetrySafety.Never, "Unexpected AI provider defect was mislabeled.");
            Check(!diagnostic.SafeMessage.Contains("key", StringComparison.OrdinalIgnoreCase),
                "AI provider detail escaped into the diagnostic.");
        });

        run("successful statuses cannot be misrepresented as failures", () =>
        {
            CheckThrows<ArgumentOutOfRangeException>(() =>
                PlanEditorFailureMapper.FromCandidateAction(CandidateActionStatus.Applied));
            CheckThrows<ArgumentOutOfRangeException>(() =>
                PlanEditorFailureMapper.FromRevision(AppendRevisionStatus.Created));
            CheckThrows<ArgumentOutOfRangeException>(() =>
                PlanEditorFailureMapper.FromConfirmation(ConfirmationStatus.Confirmed));
        });

        run("GVR21 read operations and GVR24 mutation mappings remain distinct", () =>
        {
            foreach (var operation in new[] { PlanEditorDependencyOperation.RevisionRead,
                PlanEditorDependencyOperation.ApplicableInputsRead })
            {
                Check(PlanEditorFailureMapper.FromDependencyException(operation, new TimeoutException(), "read")
                    is { Code: PlanEditorErrorCodes.DependencyTimeout, RetrySafety: PlanEditorRetrySafety.Safe },
                    "Read timeout mapping changed.");
                Check(PlanEditorFailureMapper.FromDependencyException(operation, new JsonException(), "read")
                    is { Code: PlanEditorErrorCodes.InvalidDependencyResponse, RetrySafety: PlanEditorRetrySafety.Never },
                    "Invalid read mapping changed.");
            }
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

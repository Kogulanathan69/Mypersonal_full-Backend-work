using System.Text;
using BuildMind.Application.Features.PlanEditor.Integration;

namespace BuildMind.Application.ContractTests.Integration;

internal static class F02SecurityContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("F02 authorization binding is immutable and rejects invalid identities", () =>
        {
            var project = Guid.NewGuid();
            var actor = Guid.NewGuid();
            var binding = Binding(project, actor, 1);

            Check(binding.ProjectId == project && binding.AuthenticatedActorId == actor && binding.Version.Value == 1,
                "Authorization binding did not preserve its exact immutable values.");
            CheckThrows<ArgumentException>(() => new AuthorizationBinding(Guid.Empty, actor, new AuthorizationVersion(1)));
            CheckThrows<ArgumentException>(() => new AuthorizationBinding(project, Guid.Empty, new AuthorizationVersion(1)));
            CheckThrows<ArgumentException>(() => new AuthorizationBinding(project, actor, default));
            Check(typeof(AuthorizationBinding).GetProperties().All(property => property.SetMethod is null),
                "Authorization binding exposed a mutable property.");
        });

        run("F02 authorization validator fails closed for missing and contradictory evidence", () =>
        {
            var expected = Binding();
            Check(AuthorizationBindingValidator.Validate(null, expected.ProjectId,
                    expected.AuthenticatedActorId, expected.Version) == AuthorizationBindingValidationStatus.MissingEvidence,
                "Null authorization evidence was accepted.");
            Check(AuthorizationBindingValidator.Validate(expected, Guid.Empty,
                    expected.AuthenticatedActorId, expected.Version) == AuthorizationBindingValidationStatus.InvalidExpectedIdentity,
                "A malformed expected project was accepted.");
            Check(AuthorizationBindingValidator.Validate(expected, expected.ProjectId,
                    Guid.Empty, expected.Version) == AuthorizationBindingValidationStatus.InvalidExpectedIdentity,
                "A malformed expected actor was accepted.");
            Check(AuthorizationBindingValidator.Validate(expected, expected.ProjectId,
                    expected.AuthenticatedActorId, default) == AuthorizationBindingValidationStatus.InvalidExpectedIdentity,
                "A malformed expected authorization version was accepted.");
            Check(AuthorizationBindingValidator.Validate(expected, Guid.NewGuid(),
                    expected.AuthenticatedActorId, expected.Version) == AuthorizationBindingValidationStatus.ProjectMismatch,
                "Cross-project evidence was accepted.");
            Check(AuthorizationBindingValidator.Validate(expected, expected.ProjectId,
                    Guid.NewGuid(), expected.Version) == AuthorizationBindingValidationStatus.ActorMismatch,
                "Cross-actor evidence was accepted.");
            Check(AuthorizationBindingValidator.Validate(expected, expected.ProjectId,
                    expected.AuthenticatedActorId, new AuthorizationVersion(expected.Version.Value + 1)) ==
                AuthorizationBindingValidationStatus.VersionMismatch,
                "An authorization-version mismatch was accepted.");
            Check(AuthorizationBindingValidator.Validate(expected, expected.ProjectId,
                    expected.AuthenticatedActorId, expected.Version) == AuthorizationBindingValidationStatus.Valid,
                "Exact authorization evidence was rejected.");
        });

        run("F02 mutation identity rejects malformed operation and fingerprint evidence", () =>
        {
            var fingerprint = Fingerprint("valid-operation");
            CheckThrows<ArgumentException>(() => new MutationIdentity(
                Guid.Empty, ProvisionalMutationOperation.CandidateCreate, 1, fingerprint));
            CheckThrows<ArgumentOutOfRangeException>(() => new MutationIdentity(
                Guid.NewGuid(), (ProvisionalMutationOperation)999, 1, fingerprint));
            CheckThrows<ArgumentOutOfRangeException>(() => new MutationIdentity(
                Guid.NewGuid(), ProvisionalMutationOperation.CandidateCreate, 0, fingerprint));
            CheckThrows<ArgumentOutOfRangeException>(() => new MutationIdentity(
                Guid.NewGuid(), ProvisionalMutationOperation.CandidateCreate, -1, fingerprint));
            CheckThrows<ArgumentException>(() => new MutationIdentity(
                Guid.NewGuid(), ProvisionalMutationOperation.CandidateCreate, 1, default));
        });

        run("F02 mutation identity detects every exact identity conflict", () =>
        {
            var operationId = Guid.NewGuid();
            var first = Mutation(operationId, "same-command");

            Check(MutationIdentityValidator.Compare(first, Mutation(operationId, "same-command")) ==
                    MutationIdentityComparisonStatus.ExactMatch,
                "Exact mutation identity and fingerprint did not match.");
            Check(MutationIdentityValidator.Compare(first, Mutation(Guid.NewGuid(), "same-command")) ==
                    MutationIdentityComparisonStatus.OperationIdMismatch,
                "An operation-ID mismatch was accepted.");
            Check(MutationIdentityValidator.Compare(first,
                    Mutation(operationId, "same-command", ProvisionalMutationOperation.CandidateReject)) ==
                    MutationIdentityComparisonStatus.OperationTypeMismatch,
                "An operation-type mismatch was accepted.");
            Check(MutationIdentityValidator.Compare(first,
                    Mutation(operationId, "same-command", fingerprintProfileVersion: 2)) ==
                    MutationIdentityComparisonStatus.FingerprintProfileMismatch,
                "A fingerprint-profile mismatch was accepted.");
            Check(MutationIdentityValidator.Compare(first, Mutation(operationId, "changed-command")) ==
                    MutationIdentityComparisonStatus.FingerprintMismatch,
                "The same operation identity with a different fingerprint was accepted.");
            Check(MutationIdentityValidator.Compare(null, first) == MutationIdentityComparisonStatus.MissingIdentity,
                "Missing mutation identity was accepted.");
        });

        run("F02 atomic successes require exact plan-scoped evidence", () =>
        {
            var planId = Guid.NewGuid();
            var authorization = Binding();
            var mutation = Mutation();
            var evidence = Evidence(planId, authorization, mutation);
            var success = new AtomicMutationResult(AtomicMutationStatus.Succeeded, evidence);
            var duplicate = new AtomicMutationResult(AtomicMutationStatus.MatchingDuplicate, evidence);

            Check(success.Evidence == evidence && duplicate.Evidence == evidence &&
                    !success.AllowsAutomaticRetry && !duplicate.AllowsAutomaticRetry,
                "A valid success result lost evidence or enabled automatic retry.");
            Check(AtomicMutationResultValidator.Validate(success, planId, authorization, mutation) ==
                AtomicMutationResultValidationStatus.Valid, "Exact success evidence failed validation.");
            Check(AtomicMutationResultValidator.Validate(success, Guid.Empty, authorization, mutation) ==
                AtomicMutationResultValidationStatus.InvalidExpectedEvidence, "An empty expected plan was accepted.");
            Check(AtomicMutationResultValidator.Validate(success, Guid.NewGuid(), authorization, mutation) ==
                AtomicMutationResultValidationStatus.PlanScopeMismatch, "Incorrect success plan evidence was accepted.");
            Check(AtomicMutationResultValidator.Validate(duplicate, Guid.NewGuid(), authorization, mutation) ==
                AtomicMutationResultValidationStatus.PlanScopeMismatch, "Incorrect duplicate plan evidence was accepted.");
            Check(AtomicMutationResultValidator.Validate(success, planId,
                    Binding(Guid.NewGuid(), authorization.AuthenticatedActorId, authorization.Version.Value), mutation) ==
                AtomicMutationResultValidationStatus.AuthorizationMismatch, "Cross-project success evidence was accepted.");
            Check(AtomicMutationResultValidator.Validate(success, planId,
                    Binding(authorization.ProjectId, Guid.NewGuid(), authorization.Version.Value), mutation) ==
                AtomicMutationResultValidationStatus.AuthorizationMismatch, "Cross-actor success evidence was accepted.");
            foreach (var result in new[] { success, duplicate })
            {
                Check(AtomicMutationResultValidator.Validate(result, planId, authorization, mutation) ==
                    AtomicMutationResultValidationStatus.Valid,
                    "Exact atomic evidence was rejected.");
                Check(AtomicMutationResultValidator.Validate(result, planId,
                        Binding(authorization.ProjectId, authorization.AuthenticatedActorId,
                            authorization.Version.Value + 1), mutation) ==
                    AtomicMutationResultValidationStatus.AuthorizationMismatch,
                    "An incorrect authorization version was accepted.");
                Check(AtomicMutationResultValidator.Validate(result, planId, authorization,
                        Mutation(Guid.NewGuid())) ==
                    AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                    "An incorrect operation ID was accepted.");
                Check(AtomicMutationResultValidator.Validate(result, planId, authorization,
                        Mutation(mutation.OperationId, operation: ProvisionalMutationOperation.CandidateReject)) ==
                    AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                    "An incorrect operation type was accepted.");
                Check(AtomicMutationResultValidator.Validate(result, planId, authorization,
                        Mutation(mutation.OperationId, fingerprintProfileVersion: 2)) ==
                    AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                    "An incorrect fingerprint profile was accepted.");
                Check(AtomicMutationResultValidator.Validate(result, planId, authorization,
                        Mutation(mutation.OperationId, "changed")) ==
                    AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                    "An incorrect fingerprint was accepted.");
            }

            Check(AtomicMutationResultValidator.Validate(null, planId, authorization, mutation) ==
                AtomicMutationResultValidationStatus.MissingResult,
                "A missing atomic result was accepted.");

            CheckThrows<ArgumentNullException>(() => new AtomicMutationResult(AtomicMutationStatus.Succeeded));
            CheckThrows<ArgumentException>(() => new AtomicMutationEvidence(
                Guid.NewGuid(), Guid.Empty, authorization, mutation));
            CheckThrows<ArgumentException>(() => new AtomicMutationResult(
                AtomicMutationStatus.Succeeded, evidence, Reference(planId, authorization, mutation)));
        });

        run("F02 atomic failures distinguish dependency unavailability and preserve uncertainty", () =>
        {
            var planId = Guid.NewGuid();
            var authorization = Binding();
            var mutation = Mutation();
            var evidence = Evidence(planId, authorization, mutation);
            var reference = Reference(planId, authorization, mutation);

            CheckThrows<ArgumentException>(() => new AtomicMutationResult(AtomicMutationStatus.DependencyUnavailable, evidence));
            CheckThrows<ArgumentException>(() => new AtomicMutationResult(
                AtomicMutationStatus.DependencyUnavailable, reconciliation: reference));
            CheckThrows<ArgumentException>(() => new AtomicMutationResult(AtomicMutationStatus.AuthorizationDenied, evidence));
            CheckThrows<ArgumentException>(() => new AtomicMutationResult(AtomicMutationStatus.TransactionOutcomeUnknown, evidence));
            CheckThrows<ArgumentOutOfRangeException>(() => new AtomicMutationResult((AtomicMutationStatus)999));

            foreach (var status in new[]
            {
                AtomicMutationStatus.AuthorizationDenied,
                AtomicMutationStatus.AuthorizationVersionMismatch,
                AtomicMutationStatus.StaleBaseRevision,
                AtomicMutationStatus.StaleApplicableInputs,
                AtomicMutationStatus.CandidateStateConflict,
                AtomicMutationStatus.FingerprintConflict,
                AtomicMutationStatus.InvalidDependencyResponse,
                AtomicMutationStatus.DependencyUnavailable,
                AtomicMutationStatus.PersistenceFailure,
            })
            {
                var result = new AtomicMutationResult(status);
                Check(result.Evidence is null && result.Reconciliation is null && !result.AllowsAutomaticRetry,
                    $"Failure {status} exposed protected evidence or permitted replay.");
                Check(AtomicMutationResultValidator.Validate(result, planId, authorization, mutation) ==
                    AtomicMutationResultValidationStatus.NoBindableEvidence,
                    $"Failure {status} incorrectly claimed exact evidence binding.");
                Check(AtomicMutationResultValidator.Validate(result, Guid.Empty, authorization, mutation) ==
                    AtomicMutationResultValidationStatus.InvalidExpectedEvidence,
                    $"Failure {status} accepted an invalid expected plan.");
                Check(AtomicMutationResultValidator.Validate(result, planId, null!, mutation) ==
                    AtomicMutationResultValidationStatus.InvalidExpectedEvidence,
                    $"Failure {status} accepted missing expected authorization.");
                Check(AtomicMutationResultValidator.Validate(result, planId, authorization, null!) ==
                    AtomicMutationResultValidationStatus.InvalidExpectedEvidence,
                    $"Failure {status} accepted missing expected mutation identity.");
                Check(result.Status == status && result.Evidence is null && result.Reconciliation is null &&
                        !result.AllowsAutomaticRetry,
                    $"Validation mutated failure state {status}.");
            }

            Check(AtomicMutationStatus.DependencyUnavailable != AtomicMutationStatus.AuthorizationDenied &&
                    AtomicMutationStatus.DependencyUnavailable != AtomicMutationStatus.InvalidDependencyResponse &&
                    AtomicMutationStatus.DependencyUnavailable != AtomicMutationStatus.PersistenceFailure &&
                    AtomicMutationStatus.DependencyUnavailable != AtomicMutationStatus.TransactionOutcomeUnknown,
                "Confirmed dependency unavailability was collapsed into another atomic outcome.");
        });

        run("F02 unknown mutation outcomes preserve exact plan and operation scope", () =>
        {
            var planId = Guid.NewGuid();
            var authorization = Binding();
            var mutation = Mutation();
            var reference = Reference(planId, authorization, mutation);
            var unknown = new AtomicMutationResult(
                AtomicMutationStatus.TransactionOutcomeUnknown, reconciliation: reference);

            Check(unknown.Evidence is null && unknown.Reconciliation == reference && !unknown.AllowsAutomaticRetry,
                "Unknown transaction outcome was treated as success or retryable.");
            Check(AtomicMutationResultValidator.Validate(unknown, planId, authorization, mutation) ==
                AtomicMutationResultValidationStatus.Valid, "Exact reconciliation scope failed validation.");
            Check(AtomicMutationResultValidator.Validate(unknown, Guid.NewGuid(), authorization, mutation) ==
                AtomicMutationResultValidationStatus.PlanScopeMismatch, "Incorrect unknown-outcome plan was accepted.");
            Check(AtomicMutationResultValidator.Validate(unknown, planId, authorization,
                    Mutation(Guid.NewGuid(), "mutation")) == AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                "Incorrect unknown-outcome operation identity was accepted.");
            Check(AtomicMutationResultValidator.Validate(unknown, planId, authorization,
                    Mutation(mutation.OperationId, "changed")) == AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                "Incorrect unknown-outcome fingerprint was accepted.");
            Check(AtomicMutationResultValidator.Validate(unknown, planId, Binding(), mutation) ==
                AtomicMutationResultValidationStatus.AuthorizationMismatch,
                "Contradictory reconciliation authorization was accepted.");
            Check(AtomicMutationResultValidator.Validate(unknown, planId, authorization,
                    Mutation(mutation.OperationId, operation: ProvisionalMutationOperation.CandidateReject)) ==
                AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                "An unknown outcome accepted an incorrect operation type.");
            Check(AtomicMutationResultValidator.Validate(unknown, planId, authorization,
                    Mutation(mutation.OperationId, fingerprintProfileVersion: 2)) ==
                AtomicMutationResultValidationStatus.MutationIdentityMismatch,
                "An unknown outcome accepted an incorrect fingerprint profile.");
            Check(AtomicMutationResultValidator.Validate(unknown, planId,
                    Binding(authorization.ProjectId, authorization.AuthenticatedActorId,
                        authorization.Version.Value + 1), mutation) ==
                AtomicMutationResultValidationStatus.AuthorizationMismatch,
                "An unknown outcome accepted an incorrect authorization version.");
            Check(unknown.Status == AtomicMutationStatus.TransactionOutcomeUnknown &&
                    unknown.Evidence is null && unknown.Reconciliation == reference &&
                    !unknown.AllowsAutomaticRetry,
                "Validation mutated transaction uncertainty, evidence, reference or retry policy.");

            CheckThrows<ArgumentNullException>(() => new AtomicMutationResult(AtomicMutationStatus.TransactionOutcomeUnknown));
            CheckThrows<ArgumentException>(() => new AtomicMutationResult(
                AtomicMutationStatus.PersistenceFailure, reconciliation: reference));
        });

        run("F02 reconciliation accepts only immutable evidence bound to the exact reference", () =>
        {
            var currentAuthorization = Binding();
            var historicalAuthorization = Binding(
                currentAuthorization.ProjectId, Guid.NewGuid(), currentAuthorization.Version.Value - 1);
            var planId = Guid.NewGuid();
            var mutation = Mutation();
            var reference = Reference(planId, currentAuthorization, mutation);
            var evidence = Committed(planId, historicalAuthorization, mutation);
            var committed = new ReconciliationResult(ReconciliationStatus.CommittedMatching, reference, evidence);

            Check(committed.CommittedEvidence == evidence && !committed.AllowsAutomaticRetry,
                "Matching commit lost immutable evidence or enabled automatic retry.");
            Check(ReconciliationResultValidator.Validate(committed, reference, historicalAuthorization) ==
                ReconciliationResultValidationStatus.Valid,
                "Exact committed reconciliation evidence failed validation.");
            Check(ReconciliationResultValidator.Validate(committed, reference) ==
                ReconciliationResultValidationStatus.MissingExpectedHistoricalAuthorization,
                "Historical mutation authority was silently inferred from fresh authorization.");
            Check(ReconciliationResultValidator.Validate(committed, reference, Binding(
                    historicalAuthorization.ProjectId, Guid.NewGuid(), historicalAuthorization.Version.Value)) ==
                ReconciliationResultValidationStatus.HistoricalAuthorizationMismatch,
                "Incorrect historical authorization scope was accepted.");

            Check(ReconciliationResultValidator.Validate(null, reference, historicalAuthorization) ==
                ReconciliationResultValidationStatus.MissingResult,
                "Missing reconciliation result was accepted.");

            Check(ReconciliationResultValidator.Validate(committed, null, historicalAuthorization) ==
                ReconciliationResultValidationStatus.InvalidExpectedReference,
                "Missing expected reconciliation reference was accepted.");

            Check(ReconciliationResultValidator.Validate(committed, reference,
                    Binding(historicalAuthorization.ProjectId, historicalAuthorization.AuthenticatedActorId,
                        historicalAuthorization.Version.Value + 1)) ==
                ReconciliationResultValidationStatus.HistoricalAuthorizationMismatch,
                "Incorrect historical authorization version was accepted.");

            Check(ReconciliationResultValidator.Validate(committed, reference,
                    Binding(Guid.NewGuid(), historicalAuthorization.AuthenticatedActorId,
                        historicalAuthorization.Version.Value)) ==
                ReconciliationResultValidationStatus.HistoricalAuthorizationMismatch,
                "Incorrect historical project was accepted.");

            Check(ReconciliationResultValidator.Validate(committed,
                    Reference(planId, historicalAuthorization, mutation), historicalAuthorization) ==
                ReconciliationResultValidationStatus.CurrentAuthorizationMismatch,
                "Historical authorization substituted for current authorization.");

            Check(ReconciliationResultValidator.Validate(committed,
                    Reference(planId, Binding(currentAuthorization.ProjectId,
                        currentAuthorization.AuthenticatedActorId,
                        currentAuthorization.Version.Value + 1), mutation), historicalAuthorization) ==
                ReconciliationResultValidationStatus.CurrentAuthorizationMismatch,
                "Incorrect current authorization version was accepted.");

            Check(ReconciliationResultValidator.Validate(committed,
                    Reference(Guid.NewGuid(), currentAuthorization, mutation), historicalAuthorization) ==
                ReconciliationResultValidationStatus.PlanMismatch,
                "Committed reconciliation accepted a different plan.");

            Check(ReconciliationResultValidator.Validate(committed,
                    Reference(planId, Binding(Guid.NewGuid(),
                        currentAuthorization.AuthenticatedActorId,
                        currentAuthorization.Version.Value), mutation), historicalAuthorization) ==
                ReconciliationResultValidationStatus.ProjectMismatch,
                "Committed reconciliation accepted a different project.");

            foreach (var wrongMutation in new[]
            {
                Mutation(Guid.NewGuid()),
                Mutation(mutation.OperationId, operation: ProvisionalMutationOperation.CandidateReject),
                Mutation(mutation.OperationId, fingerprintProfileVersion: 2),
                Mutation(mutation.OperationId, "changed"),
            })
            {
                Check(ReconciliationResultValidator.Validate(committed,
                        Reference(planId, currentAuthorization, wrongMutation), historicalAuthorization) ==
                    ReconciliationResultValidationStatus.MutationIdentityMismatch,
                    "Committed reconciliation accepted a contradictory mutation identity.");
            }
            CheckThrows<ArgumentNullException>(() => new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference));
            CheckThrows<ArgumentNullException>(() => new ReconciliationResult(
                ReconciliationStatus.ConfirmedAbsent, null!));
            CheckThrows<ArgumentOutOfRangeException>(() => new ReconciliationResult(
                (ReconciliationStatus)999, reference));
            CheckThrows<ArgumentException>(() => new CommittedMutationEvidence(
                Guid.Empty, planId, historicalAuthorization, mutation));
            CheckThrows<ArgumentException>(() => new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference,
                Committed(Guid.NewGuid(), historicalAuthorization, mutation)));
            CheckThrows<ArgumentException>(() => new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference,
                Committed(planId, Binding(Guid.NewGuid(), Guid.NewGuid(), 1), mutation)));
            CheckThrows<ArgumentException>(() => new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference,
                Committed(planId, historicalAuthorization, Mutation(Guid.NewGuid(), "mutation"))));
            CheckThrows<ArgumentException>(() => new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference,
                Committed(planId, historicalAuthorization,
                    Mutation(mutation.OperationId, "mutation", ProvisionalMutationOperation.CandidateReject))));
            CheckThrows<ArgumentException>(() => new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference,
                Committed(planId, historicalAuthorization,
                    Mutation(mutation.OperationId, "mutation", fingerprintProfileVersion: 2))));
            CheckThrows<ArgumentException>(() => new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference,
                Committed(planId, historicalAuthorization, Mutation(mutation.OperationId, "changed"))));
        });

        run("F02 reconciliation noncommitted outcomes carry no evidence and require exact fresh scope", () =>
        {
            var reference = Reference();
            var evidence = Committed(reference.PlanId, reference.Authorization, reference.Mutation);
            foreach (var status in new[]
            {
                ReconciliationStatus.ConfirmedAbsent,
                ReconciliationStatus.ConflictingIdentity,
                ReconciliationStatus.UnauthorizedOrNotFound,
                ReconciliationStatus.DependencyUnavailable,
                ReconciliationStatus.Indeterminate,
            })
            {
                var result = new ReconciliationResult(status, reference);
                Check(result.CommittedEvidence is null && !result.AllowsAutomaticRetry,
                    $"Reconciliation status {status} exposed evidence or enabled retry.");
                Check(ReconciliationResultValidator.Validate(result, reference) ==
                    ReconciliationResultValidationStatus.Valid,
                    $"Exact payload-free reconciliation status {status} failed validation.");
                CheckThrows<ArgumentException>(() => new ReconciliationResult(status, reference, evidence));

                Check(ReconciliationResultValidator.Validate(result, null) ==
                    ReconciliationResultValidationStatus.InvalidExpectedReference,
                    $"Status {status} accepted a missing expected reference.");

                Check(ReconciliationResultValidator.Validate(result,
                        Reference(reference.PlanId, Binding(reference.Authorization.ProjectId,
                            reference.Authorization.AuthenticatedActorId,
                            reference.Authorization.Version.Value + 1), reference.Mutation)) ==
                    ReconciliationResultValidationStatus.CurrentAuthorizationMismatch,
                    $"Status {status} accepted an incorrect authorization version.");

                Check(ReconciliationResultValidator.Validate(result,
                        Reference(Guid.NewGuid(), reference.Authorization, reference.Mutation)) ==
                    ReconciliationResultValidationStatus.PlanMismatch,
                    $"Status {status} accepted a different plan.");

                Check(ReconciliationResultValidator.Validate(result,
                        Reference(reference.PlanId, Binding(Guid.NewGuid(),
                            reference.Authorization.AuthenticatedActorId,
                            reference.Authorization.Version.Value), reference.Mutation)) ==
                    ReconciliationResultValidationStatus.ProjectMismatch,
                    $"Status {status} accepted a different project.");

                Check(ReconciliationResultValidator.Validate(result,
                        Reference(reference.PlanId, Binding(reference.Authorization.ProjectId,
                            Guid.NewGuid(), reference.Authorization.Version.Value), reference.Mutation)) ==
                    ReconciliationResultValidationStatus.CurrentAuthorizationMismatch,
                    $"Status {status} accepted a different actor.");

                foreach (var wrongMutation in new[]
                {
                    Mutation(Guid.NewGuid()),
                    Mutation(reference.Mutation.OperationId,
                        operation: ProvisionalMutationOperation.CandidateReject),
                    Mutation(reference.Mutation.OperationId, fingerprintProfileVersion: 2),
                    Mutation(reference.Mutation.OperationId, "changed"),
                })
                {
                    Check(ReconciliationResultValidator.Validate(result,
                            Reference(reference.PlanId, reference.Authorization, wrongMutation)) ==
                        ReconciliationResultValidationStatus.MutationIdentityMismatch,
                        $"Status {status} accepted contradictory mutation identity.");
                }
            }

            var absent = new ReconciliationResult(ReconciliationStatus.ConfirmedAbsent, reference);
            Check(ReconciliationResultValidator.Validate(absent,
                    Reference(reference.PlanId, Binding(reference.Authorization.ProjectId, Guid.NewGuid(),
                        reference.Authorization.Version.Value), reference.Mutation)) ==
                ReconciliationResultValidationStatus.CurrentAuthorizationMismatch,
                "A payload-free result accepted a different current actor.");
            Check(ReconciliationResultValidator.Validate(absent,
                    Reference(Guid.NewGuid(), reference.Authorization, reference.Mutation)) ==
                ReconciliationResultValidationStatus.PlanMismatch,
                "A payload-free result accepted a different plan.");
            Check(ReconciliationResultValidator.Validate(absent,
                    Reference(reference.PlanId, Binding(Guid.NewGuid(), Guid.NewGuid(), 1), reference.Mutation)) ==
                ReconciliationResultValidationStatus.ProjectMismatch,
                "A payload-free result accepted a different project.");
            Check(ReconciliationResultValidator.Validate(absent,
                    Reference(reference.PlanId, reference.Authorization, Mutation(Guid.NewGuid(), "mutation"))) ==
                ReconciliationResultValidationStatus.MutationIdentityMismatch,
                "A payload-free result accepted a different operation identity.");
            Check(new ReconciliationResult(ReconciliationStatus.DependencyUnavailable, reference).Status !=
                    ReconciliationStatus.ConfirmedAbsent,
                "Dependency unavailability was represented as confirmed absence.");
        });

        run("F02 provisional contracts remain immutable and isolated from production", () =>
        {
            var accessMethod = typeof(IAuthorizedRequirementRevisionPort).GetMethod(nameof(IAuthorizedRequirementRevisionPort.GetAsync))!;
            var parameters = accessMethod.GetParameters();
            Check(parameters.Length == 4 && parameters[2].ParameterType == typeof(Guid) &&
                    parameters[2].Name == "authenticatedActorId",
                "The authorization-aware requirement port gained an actorless fallback.");
            Check(!typeof(ReconciliationResult).IsGenericType &&
                    typeof(ReconciliationResult).GetConstructors().Single().GetParameters()[2].ParameterType ==
                    typeof(CommittedMutationEvidence),
                "Reconciliation retained an arbitrary generic payload boundary.");
            Check(typeof(CommittedMutationEvidence).GetProperties().All(property => property.SetMethod is null) &&
                    typeof(AtomicMutationEvidence).GetProperties().All(property => property.SetMethod is null),
                "Protected F02 evidence exposed mutable properties.");

            var authorization = Binding();
            var mutation = Mutation();
            var planId = Guid.NewGuid();
            var reference = Reference(planId, authorization, mutation);
            var committedEvidence = Committed(planId, authorization, mutation);
            var reconciliation = new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference, committedEvidence);
            var atomicEvidence = Evidence(planId, authorization, mutation);
            var atomicResult = new AtomicMutationResult(AtomicMutationStatus.Succeeded, atomicEvidence);

            var authorizationCopy = authorization with { };
            var mutationCopy = mutation with { };
            var referenceCopy = reference with { };
            var committedEvidenceCopy = committedEvidence with { };
            var reconciliationCopy = reconciliation with { };
            var atomicEvidenceCopy = atomicEvidence with { };
            var atomicResultCopy = atomicResult with { };

            Check(!ReferenceEquals(authorization, authorizationCopy) && authorizationCopy == authorization,
                "Authorization record copying changed its security binding.");
            Check(!ReferenceEquals(mutation, mutationCopy) && mutationCopy == mutation,
                "Mutation record copying changed its identity or fingerprint.");
            Check(!ReferenceEquals(reference, referenceCopy) && referenceCopy == reference &&
                    ReferenceEquals(reference.Authorization, referenceCopy.Authorization) &&
                    ReferenceEquals(reference.Mutation, referenceCopy.Mutation),
                "Reconciliation-reference copying changed its nested security binding.");
            Check(!ReferenceEquals(committedEvidence, committedEvidenceCopy) &&
                    committedEvidenceCopy == committedEvidence &&
                    ReferenceEquals(committedEvidence.HistoricalAuthorization,
                        committedEvidenceCopy.HistoricalAuthorization) &&
                    ReferenceEquals(committedEvidence.Mutation, committedEvidenceCopy.Mutation),
                "Committed-evidence copying changed its historical security binding.");
            Check(!ReferenceEquals(reconciliation, reconciliationCopy) &&
                    reconciliationCopy == reconciliation &&
                    ReferenceEquals(reconciliation.Reference, reconciliationCopy.Reference) &&
                    ReferenceEquals(reconciliation.CommittedEvidence, reconciliationCopy.CommittedEvidence) &&
                    !reconciliationCopy.AllowsAutomaticRetry,
                "Reconciliation-result copying changed protected evidence or retry policy.");
            Check(!ReferenceEquals(atomicEvidence, atomicEvidenceCopy) && atomicEvidenceCopy == atomicEvidence &&
                    ReferenceEquals(atomicEvidence.Authorization, atomicEvidenceCopy.Authorization) &&
                    ReferenceEquals(atomicEvidence.Mutation, atomicEvidenceCopy.Mutation),
                "Atomic-evidence copying changed its nested security binding.");
            Check(!ReferenceEquals(atomicResult, atomicResultCopy) && atomicResultCopy == atomicResult &&
                    ReferenceEquals(atomicResult.Evidence, atomicResultCopy.Evidence) &&
                    atomicResultCopy.Reconciliation is null && !atomicResultCopy.AllowsAutomaticRetry,
                "Atomic-result copying changed protected evidence, reference or retry policy.");

            var immutableTypes = new[]
            {
                typeof(AuthorizationBinding), typeof(MutationIdentity), typeof(ReconciliationReference),
                typeof(CommittedMutationEvidence), typeof(ReconciliationResult),
                typeof(AtomicMutationEvidence), typeof(AtomicMutationResult)
            };
            Check(immutableTypes.All(type =>
                    type.IsSealed && type.GetProperties().All(property => property.SetMethod is null)),
                "An F02 evidence record exposed a supported mutable property.");

            var repositoryRoot = Path.GetDirectoryName(FixtureLocator.ResolveRepositoryFile("BuildMind.sln"))!;
            var contractPath = Path.GetFullPath(Path.Combine(repositoryRoot,
                "src/BuildMind.Application/Features/PlanEditor/Integration/F02SecurityContracts.cs"));
            var protectedNames = new[]
            {
                nameof(AuthorizationBinding),
                nameof(AuthorizationBindingValidationStatus),
                nameof(AuthorizationBindingValidator),
                nameof(ProvisionalMutationOperation),
                nameof(MutationIdentity),
                nameof(MutationIdentityComparisonStatus),
                nameof(MutationIdentityValidator),
                nameof(ReconciliationReference),
                nameof(ReconciliationStatus),
                nameof(CommittedMutationEvidence),
                nameof(ReconciliationResult),
                nameof(ReconciliationResultValidationStatus),
                nameof(ReconciliationResultValidator),
                nameof(AtomicMutationStatus),
                nameof(AtomicMutationEvidence),
                nameof(AtomicMutationResult),
                nameof(AtomicMutationResultValidationStatus),
                nameof(AtomicMutationResultValidator)
            };

            var declaredNames = System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(contractPath),
                    @"(?m)^\s*public\s+(?:(?:sealed|static|abstract|readonly|partial)\s+)*(?:record(?:\s+(?:class|struct))?|class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)")
                .Select(match => match.Groups[1].Value)
                .ToArray();

            Check(declaredNames.Length == protectedNames.Length &&
                    new HashSet<string>(protectedNames, StringComparer.Ordinal).SetEquals(declaredNames),
                "The isolation inventory does not match all public F02 contract declarations.");
            var authorizedAtomicContractPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                "src",
                "BuildMind.Application",
                "Features",
                "PlanEditor",
                "Integration",
                "AtomicPlanMutationContracts.cs"));
            Check(File.Exists(authorizedAtomicContractPath),
                "The approved atomic mutation contract consumer is missing.");
            var authorizedAtomicApplicationConsumerPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                "src",
                "BuildMind.Application",
                "Features",
                "PlanEditor",
                "Mutations",
                "AtomicPlanMutationConsumer.cs"));
            Check(File.Exists(authorizedAtomicApplicationConsumerPath),
                "The approved atomic mutation application consumer is missing.");

            var authorizedConsumers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                authorizedAtomicContractPath,
                authorizedAtomicApplicationConsumerPath,
            };
            var consumerPaths = Directory
                .EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(path => !string.Equals(path, contractPath, StringComparison.OrdinalIgnoreCase))
                .Where(path => protectedNames.Any(name => File.ReadAllText(path).Contains(name, StringComparison.Ordinal)))
                .ToArray();
            Check(consumerPaths.Contains(authorizedAtomicContractPath, StringComparer.OrdinalIgnoreCase),
                "The approved atomic mutation contract no longer consumes the protected F02 foundation.");
            Check(consumerPaths.Contains(authorizedAtomicApplicationConsumerPath, StringComparer.OrdinalIgnoreCase),
                "The approved atomic mutation application consumer no longer consumes the protected F02 foundation.");

            var unexpectedConsumers = consumerPaths
                .Where(path => !authorizedConsumers.Contains(path))
                .ToArray();

            Check(unexpectedConsumers.Length == 0,
                $"Provisional F02 contracts were consumed or registered by production source: {string.Join(", ", unexpectedConsumers)}");
            var unauthorizedProbePath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                "src",
                "BuildMind.Application",
                "Features",
                "PlanEditor",
                "Integration",
                "UnauthorizedF02Consumer.cs"));
            Check(!authorizedConsumers.Contains(unauthorizedProbePath),
                "The exact consumer allowlist admitted an unauthorized additional source file.");

            var forbiddenActivationMarkers = new[]
            {
                "IServiceCollection",
                "AddScoped",
                "AddSingleton",
                "AddTransient",
                "DbContext",
                "Microsoft.EntityFrameworkCore",
                "IApiEndpointModule",
                "MapGet",
                "MapPost",
                "MapPut",
                "MapDelete",
                "BackgroundService",
            };
            var authorizedConsumerTexts = authorizedConsumers.Select(File.ReadAllText).ToArray();
            Check(authorizedConsumerTexts.All(text => forbiddenActivationMarkers.All(marker =>
                    !text.Contains(marker, StringComparison.Ordinal))),
                "The approved contract-only consumer contains production activation, endpoint or persistence markers.");
            // This textual guard cannot prove isolation from reflection activation, generated code, aliases or
            // indirect consumption, convention-based registration, compiled architectural dependencies, runtime
            // dependency injection or endpoint activation. Compiled architecture, DI enumeration and endpoint
            // data-source tests remain a separate approved test slice.
        });
    }

    private static AuthorizationBinding Binding(Guid? projectId = null, Guid? actorId = null, long version = 7) =>
        new(projectId ?? Guid.NewGuid(), actorId ?? Guid.NewGuid(), new AuthorizationVersion(version));

    private static CommandFingerprint Fingerprint(string value) =>
        CommandFingerprint.Compute(Encoding.UTF8.GetBytes(value));

    private static MutationIdentity Mutation(
        Guid? operationId = null,
        string fingerprint = "mutation",
        ProvisionalMutationOperation operation = ProvisionalMutationOperation.CandidateApply,
        int fingerprintProfileVersion = 1) =>
        new(operationId ?? Guid.NewGuid(), operation, fingerprintProfileVersion, Fingerprint(fingerprint));

    private static ReconciliationReference Reference(
        Guid? planId = null,
        AuthorizationBinding? authorization = null,
        MutationIdentity? mutation = null) =>
        new(planId ?? Guid.NewGuid(), authorization ?? Binding(), mutation ?? Mutation());

    private static AtomicMutationEvidence Evidence(
        Guid planId,
        AuthorizationBinding authorization,
        MutationIdentity mutation) =>
        new(Guid.NewGuid(), planId, authorization, mutation);

    private static CommittedMutationEvidence Committed(
        Guid planId,
        AuthorizationBinding historicalAuthorization,
        MutationIdentity mutation) =>
        new(Guid.NewGuid(), planId, historicalAuthorization, mutation);

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

using System.Reflection;
using System.Text;
using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

/// <summary>
/// Database-free STEP 08 contract evidence. These checks do not establish live authorization,
/// persistence atomicity, durable idempotency, row locking or MySQL behavior.
/// </summary>
internal static class AtomicPlanMutationContractScenarios
{
    private const string Schema = "0.1-draft";

    public static void Register(Action<string, Action> run)
    {
        run("AM-A01 valid trusted actor and current authorization version", () =>
        {
            var request = CreateRequest();
            Check(request.Scope.TrustedActorId == Actor && request.CurrentAuthorization.Version.Value == 7,
                "Trusted actor or authorization version was not retained.");
        });

        run("AM-A02 missing authenticated actor is rejected", () =>
            CheckThrows<ArgumentException>(() => AtomicMutationScope.Create(
                Project, Plan, Operation, ProvisionalMutationOperation.CandidateCreate,
                Guid.Empty, new AuthorizationVersion(1))));

        run("AM-A04 empty actor project plan or operation identity is rejected", () =>
        {
            CheckThrows<ArgumentException>(() => Scope(ProvisionalMutationOperation.CandidateCreate, projectId: Guid.Empty));
            CheckThrows<ArgumentException>(() => Scope(ProvisionalMutationOperation.CandidateCreate, planId: Guid.Empty));
            CheckThrows<ArgumentException>(() => Scope(ProvisionalMutationOperation.CandidateCreate, operationId: Guid.Empty));
            CheckThrows<ArgumentOutOfRangeException>(() => Scope((ProvisionalMutationOperation)999));
        });

        run("AM-A05 authorization denial carries no protected payload", () =>
        {
            var response = CandidateCreateMutationResponse.Failed(
                new AtomicMutationResult(AtomicMutationStatus.AuthorizationDenied));
            Check(response.Candidate is null && response.Result.Evidence is null &&
                  response.Result.Reconciliation is null && !response.Result.AllowsAutomaticRetry,
                "Authorization denial disclosed protected evidence or payload.");
        });

        run("AM-A06 authorization actor mismatch fails closed", () =>
        {
            var scope = Scope(ProvisionalMutationOperation.CandidateCreate);
            CheckThrows<ArgumentException>(() => CreateRequest(
                scope, new AuthorizationBinding(Project, Id(99), scope.AuthorizationVersion)));
        });

        run("AM-A07 authorization project mismatch fails closed", () =>
        {
            var scope = Scope(ProvisionalMutationOperation.CandidateCreate);
            CheckThrows<ArgumentException>(() => CreateRequest(
                scope, new AuthorizationBinding(Id(99), Actor, scope.AuthorizationVersion)));
        });

        run("AM-A08 zero or negative authorization versions are rejected", () =>
        {
            CheckThrows<ArgumentOutOfRangeException>(() => _ = new AuthorizationVersion(0));
            CheckThrows<ArgumentOutOfRangeException>(() => _ = new AuthorizationVersion(-1));
            CheckThrows<ArgumentException>(() => AtomicMutationScope.Create(
                Project, Plan, Operation, ProvisionalMutationOperation.CandidateCreate,
                Actor, default));
        });

        run("AM-F01 all six operations recompute exact typed V1 fingerprints", () =>
        {
            var requests = AllRequests();
            Check(requests.Length == 6, "Not all six operations were constructed.");
            foreach (var item in requests)
            {
                Check(item.Mutation.Fingerprint.IsValid &&
                      item.Mutation.FingerprintProfileVersion == MutationFingerprintProfileV1.ProfileNumber &&
                      MutationFingerprintProfileV1.IsCanonicalEncoding(item.CanonicalBytes),
                    $"{item.Operation} did not retain an exact canonical V1 identity.");
            }
        });

        run("AM-F02 operation-specific requests reject the wrong category", () =>
            CheckAllWrongOperationsRejected());

        run("AM-F03 non-V1 fingerprint profile is rejected", () =>
        {
            var scope = Scope(ProvisionalMutationOperation.CandidateReject);
            var exact = RejectIdentity(scope);
            var wrong = new MutationIdentity(
                exact.OperationId, exact.Operation, 2, exact.Fingerprint);
            CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
                scope, Authorization(scope), wrong, Candidate));
        });

        run("AM-F04 modified actor or authorization version changes authority and fingerprint", () =>
        {
            var scope = Scope(ProvisionalMutationOperation.CandidateReject);
            var identity = RejectIdentity(scope);
            CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
                scope, new AuthorizationBinding(Project, Id(98), scope.AuthorizationVersion), identity, Candidate));
            CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
                scope, new AuthorizationBinding(Project, Actor, new AuthorizationVersion(8)), identity, Candidate));

            var changed = Scope(ProvisionalMutationOperation.CandidateReject, actorId: Id(98));
            Check(!RejectIdentity(changed).Fingerprint.Matches(identity.Fingerprint),
                "Actor mutation did not change the typed fingerprint.");
        });

        run("AM-F05 modified project or plan is rejected and changes fingerprint", () =>
        {
            var baseline = Scope(ProvisionalMutationOperation.CandidateReject);
            var projectChanged = Scope(ProvisionalMutationOperation.CandidateReject, projectId: Id(90));
            var planChanged = Scope(ProvisionalMutationOperation.CandidateReject, planId: Id(91));
            Check(!RejectIdentity(projectChanged).Fingerprint.Matches(RejectIdentity(baseline).Fingerprint) &&
                  !RejectIdentity(planChanged).Fingerprint.Matches(RejectIdentity(baseline).Fingerprint),
                "Project or plan identity was not fingerprint-bound.");
            CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
                baseline, Authorization(baseline), RejectIdentity(projectChanged), Candidate));
        });

        run("AM-F06 modified operation identity is rejected", () =>
        {
            var scope = Scope(ProvisionalMutationOperation.CandidateReject);
            var exact = RejectIdentity(scope);
            var changed = new MutationIdentity(
                Id(99), exact.Operation, exact.FingerprintProfileVersion, exact.Fingerprint);
            CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
                scope, Authorization(scope), changed, Candidate));
        });

        run("AM-F07 modified mandatory operation field is rejected", () =>
        {
            var scope = Scope(ProvisionalMutationOperation.CandidateReject);
            var exact = RejectIdentity(scope, Candidate);
            CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
                scope, Authorization(scope), exact, Id(99)));
        });

        run("AM-F08 exact matching duplicate returns the bound original outcome", () =>
        {
            var request = CreateRequest();
            var payload = PreparedCandidate();
            var response = CandidateCreateMutationResponse.MatchingDuplicate(
                request, Result(request.Scope, request.Mutation, payload.CandidateId,
                    AtomicMutationStatus.MatchingDuplicate), payload);
            Check(response.Candidate is not null &&
                  response.Candidate.CandidateId == payload.CandidateId &&
                  response.Candidate.Geometry.ExactJson == payload.Geometry.ExactJson &&
                  response.Result.Status == AtomicMutationStatus.MatchingDuplicate &&
                  !response.Result.AllowsAutomaticRetry,
                "Exact duplicate did not retain the original bound outcome.");
        });

        run("AM-F09 conflicting fingerprint returns no protected identity", () =>
        {
            var response = CandidateCreateMutationResponse.Failed(
                new AtomicMutationResult(AtomicMutationStatus.FingerprintConflict));
            Check(response.Candidate is null && response.Result.Evidence is null &&
                  response.Result.Reconciliation is null,
                "Fingerprint conflict disclosed stored identity or outcome.");
        });

        run("AM-F11 raw canonical bytes are not accepted as mutation requests", () =>
        {
            var requestTypes = RequestTypes();
            Check(requestTypes.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .SelectMany(method => method.GetParameters())
                    .All(parameter => parameter.ParameterType != typeof(byte[]) &&
                                      parameter.ParameterType != typeof(ReadOnlyMemory<byte>)),
                "A public request factory accepts raw canonical bytes.");
        });

        run("AM-F12 BM-JCS or unrelated SHA256 digest cannot substitute for V1", () =>
        {
            var scope = Scope(ProvisionalMutationOperation.CandidateReject);
            var unrelated = CommandFingerprint.Compute(Encoding.UTF8.GetBytes("{\"a\":1}"));
            var identity = new MutationIdentity(
                scope.OperationId, scope.Operation, MutationFingerprintProfileV1.ProfileNumber, unrelated);
            CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
                scope, Authorization(scope), identity, Candidate));
        });

        run("AM-C01 candidate creation yields an exactly bound Pending payload", () =>
        {
            var request = CreateRequest();
            var payload = PreparedCandidate();
            var response = CandidateCreateMutationResponse.Succeeded(
                request, Result(request.Scope, request.Mutation, payload.CandidateId), payload);
            Check(response.Candidate?.CandidateId == payload.CandidateId &&
                  response.Candidate.OwnerId == request.Scope.TrustedActorId &&
                  response.Candidate.Geometry.ProjectId == request.Scope.ProjectId &&
                  response.Candidate.Geometry.PlanId == request.Scope.PlanId &&
                  response.Candidate.Geometry.ExactJson == payload.Geometry.ExactJson,
                "Candidate create did not produce the exact Pending payload.");
        });

        run("AM-C06 candidate apply rejects modified geometry hash", () =>
        {
            var prepared = PreparedRevision();
            var scope = Scope(ProvisionalMutationOperation.CandidateApply);
            var changedHash = "sha256:" + new string('f', 64);
            var identity = ApplyIdentity(scope, changedHash);
            CheckThrows<ArgumentException>(() => CandidateApplyMutationRequest.Create(
                scope, Authorization(scope), identity, Candidate, Base, Land, Requirements,
                changedHash, ValidationId, prepared));
        });

        run("AM-C07 candidate apply rejects wrong validation evidence", () =>
        {
            var prepared = PreparedRevision();
            var scope = Scope(ProvisionalMutationOperation.CandidateApply);
            var wrongValidation = Id(99);
            var identity = ApplyIdentity(scope, Geometry().GeometryHash, wrongValidation);
            CheckThrows<ArgumentException>(() => CandidateApplyMutationRequest.Create(
                scope, Authorization(scope), identity, Candidate, Base, Land, Requirements,
                Geometry().GeometryHash, wrongValidation, prepared));
        });

        run("AM-C12 system expiration cannot fabricate a user actor", () =>
        {
            Check(typeof(IAtomicPlanMutationPort).GetMethods().All(method =>
                    !method.Name.Contains("System", StringComparison.OrdinalIgnoreCase) &&
                    !method.Name.Contains("Worker", StringComparison.OrdinalIgnoreCase)),
                "The port exposes a fabricated system-expiration path.");
            CheckThrows<ArgumentException>(() => AtomicMutationScope.Create(
                Project, Plan, Operation, ProvisionalMutationOperation.CandidateExpire,
                Guid.Empty, new AuthorizationVersion(7)));
        });

        run("AM-R01 append request freezes exact validated revision material", () =>
        {
            var request = AppendRequest();
            Check(request.PreparedRevision.ExpectedBaseRevisionId == Base &&
                  request.PreparedRevision.Validation.ValidationId == ValidationId &&
                  request.PreparedRevision.Geometry.GeometryHash == Geometry().GeometryHash,
                "Append request lost exact revision material.");
        });

        run("AM-R04 append rejects invalid geometry and evidence binding", () =>
        {
            var geometry = Geometry();
            var wrongValidation = Validation(geometry, landRevisionId: Id(99));
            CheckThrows<ArgumentException>(() => PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, geometry, wrongValidation));
        });

        run("AM-R05 final confirmation binds the exact current revision", () =>
        {
            var request = ConfirmRequest();
            var payload = ConfirmationPayload(request);
            var response = FinalConfirmationMutationResponse.Succeeded(
                request, Result(request.Scope, request.Mutation, payload.ConfirmationId), payload);
            Check(response.Confirmation?.ExactRevisionId == Revision &&
                  response.Confirmation.GeometryHash == request.Confirmation.GeometryHash,
                "Confirmation payload does not bind the exact revision.");
        });

        run("AM-R10 matching confirmation duplicate returns original confirmation", () =>
        {
            var request = ConfirmRequest();
            var payload = ConfirmationPayload(request);
            var response = FinalConfirmationMutationResponse.MatchingDuplicate(
                request, Result(request.Scope, request.Mutation, payload.ConfirmationId,
                    AtomicMutationStatus.MatchingDuplicate), payload);
            Check(ReferenceEquals(response.Confirmation, payload),
                "Matching duplicate replaced the original confirmation outcome.");
        });

        run("AM-T07 unknown outcome contains the exact reconciliation reference", () =>
        {
            var request = CreateRequest();
            var result = UnknownResult(request.Scope, request.Mutation);
            var response = CandidateCreateMutationResponse.OutcomeUnknown(request, result);
            Check(response.Candidate is null && response.Result.Reconciliation?.Mutation == request.Mutation &&
                  !response.Result.AllowsAutomaticRetry,
                "Unknown outcome lost its exact reconciliation reference.");
        });

        run("AM-T08 reconciliation validates committed matching outcome", () =>
        {
            var request = CreateRequest();
            var reference = Reference(request.Scope, request.Mutation);
            var historical = request.CurrentAuthorization;
            var evidence = new CommittedMutationEvidence(Candidate, Plan, historical, request.Mutation);
            var result = new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference, evidence);
            Check(ReconciliationResultValidator.Validate(result, reference, historical) ==
                  ReconciliationResultValidationStatus.Valid,
                "Committed reconciliation did not validate exact historical evidence.");
        });

        run("AM-T09 reconciliation conflict exposes no committed evidence", () =>
        {
            var request = CreateRequest();
            var result = new ReconciliationResult(
                ReconciliationStatus.ConflictingIdentity, Reference(request.Scope, request.Mutation));
            Check(result.CommittedEvidence is null && !result.AllowsAutomaticRetry,
                "Conflicting reconciliation disclosed evidence or enabled retry.");
        });

        run("AM-T11 reconciliation rejects historical actor or version mismatch", () =>
        {
            var request = CreateRequest();
            var reference = Reference(request.Scope, request.Mutation);
            var evidence = new CommittedMutationEvidence(
                Candidate, Plan, request.CurrentAuthorization, request.Mutation);
            var result = new ReconciliationResult(
                ReconciliationStatus.CommittedMatching, reference, evidence);
            var wrong = new AuthorizationBinding(Project, Id(99), new AuthorizationVersion(8));
            Check(ReconciliationResultValidator.Validate(result, reference, wrong) ==
                  ReconciliationResultValidationStatus.HistoricalAuthorizationMismatch,
                "Historical authorization mismatch was accepted.");
        });

        run("AM-T13 no result permits automatic replay", () =>
        {
            var request = CreateRequest();
            var reference = Reference(request.Scope, request.Mutation);
            Check(!new AtomicMutationResult(AtomicMutationStatus.AuthorizationDenied).AllowsAutomaticRetry &&
                  !UnknownResult(request.Scope, request.Mutation).AllowsAutomaticRetry &&
                  !new ReconciliationResult(ReconciliationStatus.Indeterminate, reference).AllowsAutomaticRetry,
                "A result enabled automatic mutation replay.");
        });

        run("AM-D02 fingerprint conflict does not disclose stored mutation identity", () =>
        {
            var result = new AtomicMutationResult(AtomicMutationStatus.FingerprintConflict);
            var response = CandidateCreateMutationResponse.Failed(result);
            Check(response.Candidate is null && result.Evidence is null && result.Reconciliation is null,
                "Fingerprint conflict disclosed a stored identity.");
        });

        run("AM-D03 definite failure cannot contain protected payload", () =>
        {
            var request = CreateRequest();
            var payload = PreparedCandidate();
            var evidence = new AtomicMutationEvidence(
                payload.CandidateId, Plan, request.CurrentAuthorization, request.Mutation);
            CheckThrows<ArgumentException>(() => _ = new AtomicMutationResult(
                AtomicMutationStatus.AuthorizationDenied, evidence));
            Check(ResponseTypes().All(type => type.GetConstructors().Length == 0 &&
                                      type.GetProperties().All(property => property.SetMethod is null)),
                "A response exposes public construction or mutation bypasses.");
        });

        run("AM-D04 unknown outcome cannot contain success payload", () =>
        {
            var request = CreateRequest();
            var response = CandidateCreateMutationResponse.OutcomeUnknown(
                request, UnknownResult(request.Scope, request.Mutation));
            Check(response.Candidate is null && response.Result.Evidence is null &&
                  response.Result.Reconciliation is not null,
                "Unknown outcome exposed a success payload.");
        });

        run("AM-X01 all requests exactly match scope authorization mutation and fingerprint", () =>
        {
            foreach (var item in AllRequests())
            {
                Check(item.Scope.ProjectId == item.Authorization.ProjectId &&
                      item.Scope.TrustedActorId == item.Authorization.AuthenticatedActorId &&
                      item.Scope.AuthorizationVersion == item.Authorization.Version &&
                      item.Scope.OperationId == item.Mutation.OperationId &&
                      item.Scope.Operation == item.Mutation.Operation,
                    $"{item.Operation} common binding is inconsistent.");
            }
        });

        run("AM-X02 each request rejects a different operation category", () =>
            CheckAllWrongOperationsRejected());

        run("AM-X03 candidate create excludes caller output and duplicate retains provider outcome", () =>
        {
            var request = CreateRequest();
            var secondRequest = CreateRequest();
            var payload = PreparedCandidate();
            var competingPayload = PreparedCandidate(
                candidateId: Id(89), createdAtUtc: Timestamp(3), expiresAtUtc: Timestamp(4));
            var response = CandidateCreateMutationResponse.MatchingDuplicate(
                request, Result(request.Scope, request.Mutation, payload.CandidateId,
                    AtomicMutationStatus.MatchingDuplicate), payload);
            Check(request.Mutation.Fingerprint.Matches(secondRequest.Mutation.Fingerprint) &&
                  request.GetType().GetProperty("PreparedCandidate") is null &&
                  request.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
                      .SelectMany(method => method.GetParameters())
                      .All(parameter => parameter.ParameterType != typeof(PreparedCandidateSnapshot)) &&
                  response.Candidate?.CandidateId == payload.CandidateId &&
                  response.Candidate.CandidateId != competingPayload.CandidateId,
                "Caller-controlled candidate output entered creation intent or replaced the committed outcome.");
        });

        run("AM-X04 warning-bearing Candidate Apply is blocked under V1", () =>
        {
            var geometry = Geometry();
            var warningEvidence = Validation(
                geometry, requirementsMatch: false, regulatoryEvidenceStatus: "UNVERIFIED");
            var prepared = PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, geometry, warningEvidence);
            var scope = Scope(ProvisionalMutationOperation.CandidateApply);
            CheckThrows<ArgumentException>(() => CandidateApplyMutationRequest.Create(
                scope, Authorization(scope), ApplyIdentity(scope), Candidate, Base, Land,
                Requirements, geometry.GeometryHash, ValidationId, prepared));
        });

        run("AM-X05 append excludes provider-derived revision fields", () =>
        {
            var type = typeof(PlanRevisionAppendMutationRequest);
            var forbidden = new[] { "RevisionId", "VersionNumber", "ParentRevisionId", "CreatedBy", "CreatedAtUtc", "ChangeSummary" };
            Check(type.IsSealed && forbidden.All(name => type.GetProperty(name) is null) &&
                  type.GetProperties().All(property => property.SetMethod is null),
                "Append request exposes provider-derived outcome authority.");
        });

        run("AM-X06 authorization version advance requires reconciliation not duplicate", () =>
        {
            var oldRequest = CreateRequest();
            var payload = PreparedCandidate();
            var oldResult = Result(oldRequest.Scope, oldRequest.Mutation, payload.CandidateId,
                AtomicMutationStatus.MatchingDuplicate);
            var newScope = Scope(ProvisionalMutationOperation.CandidateCreate, version: 8);
            var newRequest = CreateRequest(newScope, Authorization(newScope));
            CheckThrows<ArgumentException>(() => CandidateCreateMutationResponse.MatchingDuplicate(
                newRequest, oldResult, payload));

            var reconcile = AtomicMutationReconciliationRequest.Create(
                Reference(newScope, oldRequest.Mutation), oldRequest.CurrentAuthorization);
            Check(reconcile.Reference.Authorization.Version.Value == 8 &&
                  reconcile.HistoricalAuthorization.Version.Value == 7,
                "Current and historical authorization were not separated.");
        });

        run("AM-X09 success payload identity scope hash and state match evidence", () =>
        {
            var request = ApplyRequest();
            var candidate = AtomicCandidatePayload.Create(
                Candidate, Project, Plan, CandidatePreviewStatus.Applied,
                request.CandidateGeometryHash, request.ValidationEvidenceId);
            var revision = RevisionPayload();
            var result = Result(request.Scope, request.Mutation, revision.RevisionId);
            var response = CandidateApplyMutationResponse.Succeeded(
                request, result, candidate, revision);
            Check(response.Revision is { } persistedRevision &&
                  response.Candidate is { } persistedCandidate &&
                  persistedRevision.RevisionId == result.Evidence?.DurableOutcomeId &&
                  persistedCandidate.GeometryHash == persistedRevision.Geometry.GeometryHash &&
                  persistedCandidate.State == CandidatePreviewStatus.Applied,
                "Success response accepted contradictory protected payload.");

            var wrongCandidate = AtomicCandidatePayload.Create(
                Id(90), Project, Plan, CandidatePreviewStatus.Applied,
                request.CandidateGeometryHash, request.ValidationEvidenceId);
            CheckThrows<ArgumentException>(() => CandidateApplyMutationResponse.Succeeded(
                request, result, wrongCandidate, revision));

            var create = CreateRequest();
            var wrongCreated = PreparedCandidate(ownerId: Id(91));
            CheckThrows<ArgumentException>(() => CandidateCreateMutationResponse.Succeeded(
                create, Result(create.Scope, create.Mutation, wrongCreated.CandidateId), wrongCreated));

            var reject = RejectRequest();
            var wrongRejected = CandidatePayload(CandidatePreviewStatus.Rejected, Id(92));
            CheckThrows<ArgumentException>(() => CandidateStateMutationResponse.Succeeded(
                reject, Result(reject.Scope, reject.Mutation, wrongRejected.CandidateId), wrongRejected));

            var append = AppendRequest();
            var otherJson = ValidGeometryJson().Replace("Bedroom", "Changed Bedroom", StringComparison.Ordinal);
            var otherGeometry = FrozenGeometrySnapshot.Create(
                Schema, otherJson, PlanRevisionService.ComputeGeometryHash(otherJson));
            var wrongRevision = AtomicPlanRevisionPayload.Create(
                Id(93), Project, Plan, otherGeometry, ValidationId, Land, Requirements);
            CheckThrows<ArgumentException>(() => PlanRevisionMutationResponse.Succeeded(
                append, Result(append.Scope, append.Mutation, wrongRevision.RevisionId), wrongRevision));

            var confirmationRequest = ConfirmRequest();
            var wrongConfirmation = AtomicFinalConfirmationPayload.Create(
                Id(94), Project, Plan, Id(95), confirmationRequest.Confirmation.GeometryHash,
                ValidationId, Actor, true);
            CheckThrows<ArgumentException>(() => FinalConfirmationMutationResponse.Succeeded(
                confirmationRequest,
                Result(confirmationRequest.Scope, confirmationRequest.Mutation, wrongConfirmation.ConfirmationId),
                wrongConfirmation));
        });

        run("AM-X10 source collections and returned bytes cannot mutate frozen request", () =>
        {
            var issues = new List<string> { "WARN-1" };
            var geometry = Geometry();
            var evidence = Validation(geometry, issueCodes: issues);
            issues[0] = "CHANGED";
            var prepared = PreparedCandidateSnapshot.Create(
                Candidate, Actor, Base, Land, Requirements, geometry, evidence,
                Timestamp(1), Timestamp(2));
            var request = CreateRequest();
            var before = request.Mutation.Fingerprint.Value;
            var bytes = request.GetCanonicalFingerprintBytes();
            bytes[0] ^= 0xff;
            Check(prepared.Validation.IssueCodes.Single() == "WARN-1" &&
                  request.Mutation.Fingerprint.Value == before &&
                  MutationFingerprintProfileV1.IsCanonicalEncoding(request.GetCanonicalFingerprintBytes()),
                "External collection or byte mutation changed the frozen request.");
        });

        run("AM-X11 disposed source JsonDocument cannot alter frozen command", () =>
        {
            CandidateCreateMutationRequest request;
            using (var document = JsonDocument.Parse("{\"deltaX\":1.25,\"deltaY\":-0.5}"))
            {
                request = CreateRequest(command: Command(document.RootElement));
            }
            Check(request.Command.Payload.GetProperty("deltaX").GetDouble() == 1.25 &&
                  MutationFingerprintProfileV1.IsCanonicalEncoding(request.GetCanonicalFingerprintBytes()),
                "Disposed source JSON invalidated the frozen command.");
        });

        run("AM09B-H01-01 strict Plan Geometry contract rejects malformed structures", () =>
        {
            const string missingMandatory = "{\"schemaVersion\":\"0.1-draft\",\"floors\":[]}";
            CheckThrows<ArgumentException>(() => FrozenGeometrySnapshot.Create(
                Schema, missingMandatory, PlanRevisionService.ComputeGeometryHash(missingMandatory)));

            var invalidConvention = ValidGeometryJson().Replace(
                "\"unit\": \"m\"", "\"unit\": \"ft\"", StringComparison.Ordinal);
            CheckThrows<ArgumentException>(() => FrozenGeometrySnapshot.Create(
                Schema, invalidConvention, PlanRevisionService.ComputeGeometryHash(invalidConvention)));

            var duplicate = "{\"schemaVersion\":\"0.1-draft\"," + ValidGeometryJson()[1..];
            CheckThrows<ArgumentException>(() => FrozenGeometrySnapshot.Create(
                Schema, duplicate, PlanRevisionService.ComputeGeometryHash(duplicate)));

            var exact = ValidGeometryJson();
            CheckThrows<ArgumentException>(() => FrozenGeometrySnapshot.Create(
                "different-schema", exact, PlanRevisionService.ComputeGeometryHash(exact)));
        });

        run("AM09B-H01-02 embedded geometry identities must match mutation scope", () =>
        {
            var wrongProjectGeometry = Geometry(ValidGeometryJson(projectId: Id(90)));
            var wrongProjectEvidence = Validation(wrongProjectGeometry);
            var wrongProjectRevision = PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, wrongProjectGeometry, wrongProjectEvidence);
            var applyScope = Scope(ProvisionalMutationOperation.CandidateApply);
            CheckThrows<ArgumentException>(() => CandidateApplyMutationRequest.Create(
                applyScope, Authorization(applyScope),
                ApplyIdentity(applyScope, wrongProjectGeometry.GeometryHash),
                Candidate, Base, Land, Requirements,
                wrongProjectGeometry.GeometryHash, ValidationId, wrongProjectRevision));

            var wrongPlanGeometry = Geometry(ValidGeometryJson(planId: Id(91)));
            var wrongPlanRevision = PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, wrongPlanGeometry, Validation(wrongPlanGeometry));
            CheckThrows<ArgumentException>(() => CandidateApplyMutationRequest.Create(
                applyScope, Authorization(applyScope),
                ApplyIdentity(applyScope, wrongPlanGeometry.GeometryHash),
                Candidate, Base, Land, Requirements,
                wrongPlanGeometry.GeometryHash, ValidationId, wrongPlanRevision));

            var wrongLandGeometry = Geometry(ValidGeometryJson(landRevisionId: Id(92)));
            CheckThrows<ArgumentException>(() => PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, wrongLandGeometry,
                Validation(wrongLandGeometry, landRevisionId: Id(92))));

            var wrongRequirementsGeometry = Geometry(
                ValidGeometryJson(requirementsRevisionId: Id(93)));
            var wrongRequirementsEvidence = FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", wrongRequirementsGeometry.GeometryHash,
                Land, Id(93), true, true, "VERIFIED", false, Timestamp(0));
            CheckThrows<ArgumentException>(() => PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, wrongRequirementsGeometry, wrongRequirementsEvidence));
        });

        run("AM09B-H01-03 protected revision payload rejects cross-scope geometry", () =>
        {
            var wrongProjectGeometry = Geometry(ValidGeometryJson(projectId: Id(94)));
            CheckThrows<ArgumentException>(() => AtomicPlanRevisionPayload.Create(
                Revision, Project, Plan, wrongProjectGeometry,
                ValidationId, Land, Requirements));
        });

        run("AM09B-H02-01 invalid or blocking evidence cannot authorize mutations", () =>
        {
            var geometry = Geometry();
            var invalid = Validation(geometry, geometryValid: false);
            CheckThrows<ArgumentException>(() => PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, geometry, invalid));
            CheckThrows<ArgumentException>(() => PreparedCandidateSnapshot.Create(
                Candidate, Actor, Base, Land, Requirements, geometry, invalid,
                Timestamp(1), Timestamp(2)));

            var blocking = Validation(
                geometry, hasBlockingIssues: true, issueCodes: ["GEOMETRY_BLOCKED"]);
            CheckThrows<ArgumentException>(() => PreparedRevisionSnapshot.Create(
                Base, Land, Requirements, geometry, blocking));
        });

        run("AM09B-H02-02 warning acknowledgement is derived from evidence semantics", () =>
        {
            var geometry = Geometry();
            var requirementsWarning = Validation(geometry, requirementsMatch: false);
            var regulatoryWarning = Validation(
                geometry, regulatoryEvidenceStatus: "UNVERIFIED");
            var verified = Validation(geometry);
            Check(requirementsWarning.RequiresWarningAcknowledgement &&
                  regulatoryWarning.RequiresWarningAcknowledgement &&
                  !verified.RequiresWarningAcknowledgement,
                "Warning acknowledgement was not derived from the approved evidence fields.");

            foreach (var warning in new[] { requirementsWarning, regulatoryWarning })
            {
                var prepared = PreparedRevisionSnapshot.Create(
                    Base, Land, Requirements, geometry, warning);
                var scope = Scope(ProvisionalMutationOperation.CandidateApply);
                CheckThrows<ArgumentException>(() => CandidateApplyMutationRequest.Create(
                    scope, Authorization(scope), ApplyIdentity(scope), Candidate,
                    Base, Land, Requirements, geometry.GeometryHash, ValidationId, prepared));
            }
        });

        run("AM09B-H02-03 unknown regulatory evidence classifications are rejected", () =>
        {
            var geometry = Geometry();
            CheckThrows<ArgumentException>(() => FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash,
                Land, Requirements, true, true, "FABRICATED_STATUS", false, Timestamp(0)));
        });

        run("AM09B-M01-01 candidate creation request contains fingerprint-bound intent only", () =>
        {
            var request = CreateRequest();
            var factories = typeof(CandidateCreateMutationRequest)
                .GetMethods(BindingFlags.Public | BindingFlags.Static);
            Check(request.GetType().GetProperty("PreparedCandidate") is null &&
                  factories.SelectMany(method => method.GetParameters())
                      .All(parameter => parameter.ParameterType != typeof(PreparedCandidateSnapshot)),
                "Provider-derived candidate output remains caller-controlled creation intent.");
        });

        run("AM09B-M01-02 duplicate response preserves the first provider outcome", () =>
        {
            var request = CreateRequest();
            var first = PreparedCandidate();
            var competing = PreparedCandidate(
                candidateId: Id(95), createdAtUtc: Timestamp(3), expiresAtUtc: Timestamp(4));
            var committed = Result(
                request.Scope, request.Mutation, first.CandidateId,
                AtomicMutationStatus.MatchingDuplicate);
            var response = CandidateCreateMutationResponse.MatchingDuplicate(
                request, committed, first);
            Check(response.Candidate?.CandidateId == first.CandidateId &&
                  response.Candidate.CandidateId != competing.CandidateId,
                "Matching duplicate did not retain the first provider-derived outcome.");
            CheckThrows<ArgumentException>(() => CandidateCreateMutationResponse.MatchingDuplicate(
                request, committed, competing));
        });

        run("AM09B1-L01-01 geometry enforces strict UTF-8 byte boundaries", () =>
        {
            const int maximumBytes = 4 * 1024 * 1024;
            var belowJson = GeometryJsonAtUtf8ByteCount(maximumBytes - 1);
            var exactJson = GeometryJsonAtUtf8ByteCount(maximumBytes);
            var below = FrozenGeometrySnapshot.Create(
                Schema, belowJson, PlanRevisionService.ComputeGeometryHash(belowJson));
            var exact = FrozenGeometrySnapshot.Create(
                Schema, exactJson, PlanRevisionService.ComputeGeometryHash(exactJson));
            Check(Encoding.UTF8.GetByteCount(below.ExactJson) == maximumBytes - 1 &&
                  Encoding.UTF8.GetByteCount(exact.ExactJson) == maximumBytes,
                "Valid geometry did not preserve the inclusive UTF-8 byte boundary.");

            var aboveJson = GeometryJsonAtUtf8ByteCount(maximumBytes + 1);
            var aboveHash = PlanRevisionService.ComputeGeometryHash(aboveJson);
            var aboveError = CaptureThrows<ArgumentException>(() =>
                FrozenGeometrySnapshot.Create(Schema, aboveJson, aboveHash));
            Check(aboveError.Message.Contains(
                    "Geometry JSON cannot exceed", StringComparison.Ordinal),
                "Oversized geometry was not rejected by the size guard.");

            var unicodeBase = ValidGeometryJson().Replace(
                "Bedroom", "\u0BA4\u0BAE\u0BBF\u0BB4\u0BCD", StringComparison.Ordinal);
            var unicodeBaseBytes = Encoding.UTF8.GetByteCount(unicodeBase);
            Check(unicodeBaseBytes > unicodeBase.Length,
                "Expected a genuinely multibyte geometry fixture.");

            var unicodeAbove = unicodeBase +
                new string(' ', maximumBytes + 1 - unicodeBaseBytes);

            Check(unicodeAbove.Length <= maximumBytes &&
                  Encoding.UTF8.GetByteCount(unicodeAbove) == maximumBytes + 1,
                "Invalid multibyte boundary test fixture.");

            var unicodeAboveHash =
                PlanRevisionService.ComputeGeometryHash(unicodeAbove);

            var unicodeAboveError = CaptureThrows<ArgumentException>(() =>
                FrozenGeometrySnapshot.Create(
                    Schema, unicodeAbove, unicodeAboveHash));

            Check(unicodeAboveError.Message.Contains(
                    "Geometry JSON cannot exceed", StringComparison.Ordinal),
                "Multibyte geometry bypassed the UTF-8 size guard.");

            var multibyteJson = ValidGeometryJson().Replace(
                "Bedroom", "படுக்கையறை", StringComparison.Ordinal);
            var multibyte = FrozenGeometrySnapshot.Create(
                Schema, multibyteJson, PlanRevisionService.ComputeGeometryHash(multibyteJson));
            Check(Encoding.UTF8.GetByteCount(multibyte.ExactJson) > multibyte.ExactJson.Length,
                "Multibyte geometry was not measured as UTF-8 bytes.");

            var malformedUnicode = ValidGeometryJson().Replace(
                "Bedroom", "\ud800", StringComparison.Ordinal);
            var malformedError = CaptureThrows<ArgumentException>(() =>
                FrozenGeometrySnapshot.Create(
                    Schema, malformedUnicode, exact.GeometryHash));

            Check(malformedError.Message.Contains(
                    "well-formed Unicode", StringComparison.Ordinal) &&
                  malformedError.InnerException is EncoderFallbackException,
                "Malformed Unicode was not rejected by strict UTF-8 validation.");
        });

        run("AM09B1-L01-02 validator version uses Unicode scalar boundaries", () =>
        {
            var geometry = Geometry();
            var below = new string('v', 63);
            var exact = new string('v', 63) + "😀";
            Check(FrozenValidationEvidence.Create(
                    ValidationId, below, geometry.GeometryHash, Land, Requirements,
                    true, true, "VERIFIED", false, Timestamp(0)).ValidatorVersion == below,
                "Below-limit validator version was rejected.");
            Check(FrozenValidationEvidence.Create(
                    ValidationId, exact, geometry.GeometryHash, Land, Requirements,
                    true, true, "VERIFIED", false, Timestamp(0)).ValidatorVersion == exact,
                "Exact-limit Unicode-scalar validator version was rejected.");
            CheckThrows<ArgumentException>(() => FrozenValidationEvidence.Create(
                ValidationId, new string('v', 65), geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0)));
            CheckThrows<ArgumentException>(() => FrozenValidationEvidence.Create(
                ValidationId, "\ud800", geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0)));
        });

        run("AM09B1-L01-03 validation issue collection stops at the approved boundary", () =>
        {
            var geometry = Geometry();
            var below = FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0),
                Enumerable.Repeat("ISSUE", 999));
            var exact = FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0),
                Enumerable.Repeat("ISSUE", 1_000));
            Check(below.IssueCodes.Count == 999 && exact.IssueCodes.Count == 1_000,
                "Valid issue-code collection boundary was not preserved.");
            CheckThrows<ArgumentException>(() => FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0), OversizedIssueCodes()));
        });

        run("AM09B1-L01-04 individual issue codes use Unicode scalar boundaries", () =>
        {
            var geometry = Geometry();
            var below = new string('I', 63);
            var exact = new string('I', 63) + "😀";
            var evidence = FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0), [below, exact]);
            Check(evidence.IssueCodes.SequenceEqual([below, exact]),
                "Valid individual issue-code boundaries were not preserved.");
            CheckThrows<ArgumentException>(() => FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0), [new string('I', 65)]));
        });

        run("AM09B1-L01-05 regulatory status bound preserves the semantic allowlist", () =>
        {
            var geometry = Geometry();
            var valid = FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0));
            Check(valid.RegulatoryEvidenceStatus == "VERIFIED",
                "Approved below-limit regulatory status was rejected.");

            var exactLimitError = CaptureThrows<ArgumentException>(() => FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, new string('R', 32), false, Timestamp(0)));
            var aboveLimitError = CaptureThrows<ArgumentException>(() => FrozenValidationEvidence.Create(
                ValidationId, "validator-v1", geometry.GeometryHash, Land, Requirements,
                true, true, new string('R', 33), false, Timestamp(0)));
            Check(exactLimitError.Message.Contains("approved regulatory evidence status", StringComparison.Ordinal) &&
                  aboveLimitError.Message.Contains("cannot exceed 32 Unicode scalar values", StringComparison.Ordinal),
                "Regulatory status size and semantic boundaries were not applied in the correct order.");
        });

        run("AM09B1-L01-06 bounded snapshots cannot be bypassed through responses", () =>
        {
            var geometry = Geometry();
            var exactCode = new string('I', 64);
            var validation = FrozenValidationEvidence.Create(
                ValidationId, new string('v', 64), geometry.GeometryHash, Land, Requirements,
                true, true, "VERIFIED", false, Timestamp(0), [exactCode]);
            var candidate = PreparedCandidateSnapshot.Create(
                Candidate, Actor, Base, Land, Requirements, geometry, validation,
                Timestamp(1), Timestamp(2));
            var request = CreateRequest();
            var response = CandidateCreateMutationResponse.Succeeded(
                request, Result(request.Scope, request.Mutation, Candidate), candidate);
            Check(response.Candidate?.Validation.IssueCodes.Single() == exactCode,
                "Exact-limit evidence did not survive the protected response boundary.");
            Check(typeof(FrozenGeometrySnapshot).GetConstructors().Length == 0 &&
                  typeof(FrozenValidationEvidence).GetConstructors().Length == 0,
                "A public constructor can bypass bounded snapshot factories.");
        });
    }

    private static CandidateCreateMutationRequest CreateRequest(
        AtomicMutationScope? scope = null,
        AuthorizationBinding? authorization = null,
        MutationIdentity? mutation = null,
        PlanEditCommandDto? command = null)
    {
        scope ??= Scope(ProvisionalMutationOperation.CandidateCreate);
        authorization ??= Authorization(scope);
        command ??= Command();
        mutation ??= CreateIdentity(scope, command);
        return CandidateCreateMutationRequest.Create(
            scope, authorization, mutation, Base, Land, Requirements,
            Schema, command);
    }

    private static CandidateApplyMutationRequest ApplyRequest()
    {
        var scope = Scope(ProvisionalMutationOperation.CandidateApply);
        var prepared = PreparedRevision();
        return CandidateApplyMutationRequest.Create(
            scope, Authorization(scope), ApplyIdentity(scope), Candidate, Base, Land,
            Requirements, prepared.Geometry.GeometryHash, ValidationId, prepared);
    }

    private static CandidateRejectMutationRequest RejectRequest()
    {
        var scope = Scope(ProvisionalMutationOperation.CandidateReject);
        return CandidateRejectMutationRequest.Create(
            scope, Authorization(scope), RejectIdentity(scope), Candidate, "USER_REJECTED");
    }

    private static CandidateExpireMutationRequest ExpireRequest()
    {
        var scope = Scope(ProvisionalMutationOperation.CandidateExpire);
        return CandidateExpireMutationRequest.Create(
            scope, Authorization(scope), ExpireIdentity(scope), Candidate, Timestamp(2));
    }

    private static PlanRevisionAppendMutationRequest AppendRequest()
    {
        var scope = Scope(ProvisionalMutationOperation.PlanRevisionAppend);
        var prepared = PreparedRevision();
        return PlanRevisionAppendMutationRequest.Create(
            scope, Authorization(scope), AppendIdentity(scope), prepared);
    }

    private static FinalPlanConfirmationMutationRequest ConfirmRequest()
    {
        var scope = Scope(ProvisionalMutationOperation.FinalPlanConfirmation);
        var prepared = PreparedConfirmationSnapshot.Create(
            Revision, Geometry().GeometryHash, ValidationId, Land, Requirements, true);
        return FinalPlanConfirmationMutationRequest.Create(
            scope, Authorization(scope), ConfirmIdentity(scope), prepared);
    }

    private static RequestEvidence[] AllRequests()
    {
        var create = CreateRequest();
        var apply = ApplyRequest();
        var reject = RejectRequest();
        var expire = ExpireRequest();
        var append = AppendRequest();
        var confirm = ConfirmRequest();
        return
        [
            new(create.Scope, create.CurrentAuthorization, create.Mutation, create.GetCanonicalFingerprintBytes()),
            new(apply.Scope, apply.CurrentAuthorization, apply.Mutation, apply.GetCanonicalFingerprintBytes()),
            new(reject.Scope, reject.CurrentAuthorization, reject.Mutation, reject.GetCanonicalFingerprintBytes()),
            new(expire.Scope, expire.CurrentAuthorization, expire.Mutation, expire.GetCanonicalFingerprintBytes()),
            new(append.Scope, append.CurrentAuthorization, append.Mutation, append.GetCanonicalFingerprintBytes()),
            new(confirm.Scope, confirm.CurrentAuthorization, confirm.Mutation, confirm.GetCanonicalFingerprintBytes()),
        ];
    }

    private static void CheckAllWrongOperationsRejected()
    {
        var createScope = Scope(ProvisionalMutationOperation.CandidateApply);
        CheckThrows<ArgumentException>(() => CreateRequest(createScope, Authorization(createScope)));

        var applyScope = Scope(ProvisionalMutationOperation.CandidateReject);
        CheckThrows<ArgumentException>(() => CandidateApplyMutationRequest.Create(
            applyScope, Authorization(applyScope), ApplyIdentity(applyScope), Candidate,
            Base, Land, Requirements, Geometry().GeometryHash, ValidationId, PreparedRevision()));

        var rejectScope = Scope(ProvisionalMutationOperation.CandidateExpire);
        CheckThrows<ArgumentException>(() => CandidateRejectMutationRequest.Create(
            rejectScope, Authorization(rejectScope), RejectIdentity(rejectScope), Candidate));

        var expireScope = Scope(ProvisionalMutationOperation.PlanRevisionAppend);
        CheckThrows<ArgumentException>(() => CandidateExpireMutationRequest.Create(
            expireScope, Authorization(expireScope), ExpireIdentity(expireScope), Candidate, Timestamp(2)));

        var appendScope = Scope(ProvisionalMutationOperation.FinalPlanConfirmation);
        CheckThrows<ArgumentException>(() => PlanRevisionAppendMutationRequest.Create(
            appendScope, Authorization(appendScope), AppendIdentity(appendScope), PreparedRevision()));

        var confirmScope = Scope(ProvisionalMutationOperation.CandidateCreate);
        CheckThrows<ArgumentException>(() => FinalPlanConfirmationMutationRequest.Create(
            confirmScope, Authorization(confirmScope), ConfirmIdentity(confirmScope),
            PreparedConfirmationSnapshot.Create(
                Revision, Geometry().GeometryHash, ValidationId, Land, Requirements, true)));
    }

    private static PreparedCandidateSnapshot PreparedCandidate(
        Guid? candidateId = null,
        Guid? ownerId = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? expiresAtUtc = null)
    {
        var geometry = Geometry();
        return PreparedCandidateSnapshot.Create(
            candidateId ?? Candidate, ownerId ?? Actor, Base, Land, Requirements,
            geometry, Validation(geometry),
            createdAtUtc ?? Timestamp(1), expiresAtUtc ?? Timestamp(2));
    }

    private static PreparedRevisionSnapshot PreparedRevision()
    {
        var geometry = Geometry();
        return PreparedRevisionSnapshot.Create(
            Base, Land, Requirements, geometry, Validation(geometry));
    }

    private static FrozenGeometrySnapshot Geometry(string? json = null)
    {
        json ??= ValidGeometryJson();
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        return FrozenGeometrySnapshot.Create(Schema, json, hash);
    }

    private static string ValidGeometryJson(
        Guid? projectId = null,
        Guid? planId = null,
        Guid? landRevisionId = null,
        Guid? requirementsRevisionId = null)
    {
        var json = File.ReadAllText(FixtureLocator.ResolveRepositoryFile(
            "docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json"));
        return json
            .Replace("00000000-0000-4000-8000-000000000001", (projectId ?? Project).ToString(), StringComparison.Ordinal)
            .Replace("00000000-0000-4000-8000-000000000002", (planId ?? Plan).ToString(), StringComparison.Ordinal)
            .Replace("00000000-0000-4000-8000-000000000003", (landRevisionId ?? Land).ToString(), StringComparison.Ordinal)
            .Replace("00000000-0000-4000-8000-000000000004", (requirementsRevisionId ?? Requirements).ToString(), StringComparison.Ordinal);
    }

    private static string GeometryJsonAtUtf8ByteCount(int targetBytes)
    {
        var json = ValidGeometryJson();
        var currentBytes = Encoding.UTF8.GetByteCount(json);
        if (targetBytes < currentBytes)
            throw new ArgumentOutOfRangeException(nameof(targetBytes));
        return json + new string(' ', targetBytes - currentBytes);
    }

    private static IEnumerable<string> OversizedIssueCodes()
    {
        for (var index = 0; index <= 1_000; index++) yield return "ISSUE";
        throw new InvalidOperationException("The bounded factory enumerated beyond the first excess item.");
    }

    private static FrozenValidationEvidence Validation(
        FrozenGeometrySnapshot geometry,
        Guid? landRevisionId = null,
        bool geometryValid = true,
        bool? requirementsMatch = true,
        string regulatoryEvidenceStatus = "VERIFIED",
        bool hasBlockingIssues = false,
        IEnumerable<string>? issueCodes = null) =>
        FrozenValidationEvidence.Create(
            ValidationId, "validator-v1", geometry.GeometryHash,
            landRevisionId ?? Land, Requirements, geometryValid, requirementsMatch,
            regulatoryEvidenceStatus, hasBlockingIssues, Timestamp(0), issueCodes);

    private static PlanEditCommandDto Command(JsonElement? payload = null)
    {
        JsonElement exact;
        if (payload is { } supplied)
        {
            exact = supplied.Clone();
        }
        else
        {
            using var document = JsonDocument.Parse("{\"deltaX\":1.25,\"deltaY\":-0.5}");
            exact = document.RootElement.Clone();
        }

        return new PlanEditCommandDto
        {
            CommandId = CommandId,
            CommandType = PlanEditCommandTypes.MoveRoom,
            FloorId = Floor,
            TargetId = Target,
            Payload = exact,
        };
    }

    private static MutationIdentity CreateIdentity(AtomicMutationScope scope, PlanEditCommandDto command)
    {
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateCreateFingerprintInput(
            scope.ToPublicFingerprintScope(), Base, Land, Requirements, Schema, command));
        return Identity(scope, fingerprint);
    }

    private static MutationIdentity ApplyIdentity(
        AtomicMutationScope scope, string? geometryHash = null, Guid? validationId = null) =>
        Identity(scope, MutationFingerprintProfileV1.Compute(new CandidateApplyFingerprintInput(
            scope.ToPublicFingerprintScope(), Candidate, Base, Land, Requirements,
            geometryHash ?? Geometry().GeometryHash, validationId ?? ValidationId)));

    private static MutationIdentity RejectIdentity(AtomicMutationScope scope, Guid? candidateId = null) =>
        Identity(scope, MutationFingerprintProfileV1.Compute(new CandidateRejectFingerprintInput(
            scope.ToPublicFingerprintScope(), candidateId ?? Candidate, "USER_REJECTED")));

    private static MutationIdentity ExpireIdentity(AtomicMutationScope scope) =>
        Identity(scope, MutationFingerprintProfileV1.Compute(new CandidateExpireFingerprintInput(
            scope.ToPublicFingerprintScope(), Candidate, Timestamp(2))));

    private static MutationIdentity AppendIdentity(AtomicMutationScope scope) =>
        Identity(scope, MutationFingerprintProfileV1.Compute(new PlanRevisionAppendFingerprintInput(
            scope.ToPublicFingerprintScope(), Base, Geometry().GeometryHash,
            Land, Requirements, ValidationId, Schema)));

    private static MutationIdentity ConfirmIdentity(AtomicMutationScope scope) =>
        Identity(scope, MutationFingerprintProfileV1.Compute(new FinalPlanConfirmationFingerprintInput(
            scope.ToPublicFingerprintScope(), Revision, Geometry().GeometryHash,
            ValidationId, Land, Requirements, true)));

    private static MutationIdentity Identity(AtomicMutationScope scope, CommandFingerprint fingerprint) =>
        new(scope.OperationId, scope.Operation, MutationFingerprintProfileV1.ProfileNumber, fingerprint);

    private static AtomicMutationScope Scope(
        ProvisionalMutationOperation operation,
        Guid? projectId = null,
        Guid? planId = null,
        Guid? operationId = null,
        Guid? actorId = null,
        long version = 7) =>
        AtomicMutationScope.Create(
            projectId ?? Project, planId ?? Plan, operationId ?? Operation,
            operation, actorId ?? Actor, new AuthorizationVersion(version));

    private static AuthorizationBinding Authorization(AtomicMutationScope scope) =>
        new(scope.ProjectId, scope.TrustedActorId, scope.AuthorizationVersion);

    private static AtomicMutationResult Result(
        AtomicMutationScope scope, MutationIdentity mutation, Guid outcomeId,
        AtomicMutationStatus status = AtomicMutationStatus.Succeeded) =>
        new(status, new AtomicMutationEvidence(outcomeId, scope.PlanId, Authorization(scope), mutation));

    private static AtomicMutationResult UnknownResult(
        AtomicMutationScope scope, MutationIdentity mutation) =>
        new(AtomicMutationStatus.TransactionOutcomeUnknown,
            reconciliation: Reference(scope, mutation));

    private static ReconciliationReference Reference(
        AtomicMutationScope scope, MutationIdentity mutation) =>
        new(scope.PlanId, Authorization(scope), mutation);

    private static AtomicCandidatePayload CandidatePayload(
        CandidatePreviewStatus state,
        Guid? candidateId = null) =>
        AtomicCandidatePayload.Create(
            candidateId ?? Candidate, Project, Plan, state,
            Geometry().GeometryHash, ValidationId);

    private static AtomicPlanRevisionPayload RevisionPayload() =>
        AtomicPlanRevisionPayload.Create(
            Revision, Project, Plan, Geometry(), ValidationId, Land, Requirements);

    private static AtomicFinalConfirmationPayload ConfirmationPayload(
        FinalPlanConfirmationMutationRequest request) =>
        AtomicFinalConfirmationPayload.Create(
            Confirmation, Project, Plan, request.Confirmation.ExactRevisionId,
            request.Confirmation.GeometryHash, request.Confirmation.ValidationEvidenceId,
            Actor, request.Confirmation.WarningAcknowledged);

    private static Type[] RequestTypes() =>
    [
        typeof(CandidateCreateMutationRequest), typeof(CandidateApplyMutationRequest),
        typeof(CandidateRejectMutationRequest), typeof(CandidateExpireMutationRequest),
        typeof(PlanRevisionAppendMutationRequest), typeof(FinalPlanConfirmationMutationRequest),
    ];

    private static Type[] ResponseTypes() =>
    [
        typeof(CandidateCreateMutationResponse), typeof(CandidateApplyMutationResponse),
        typeof(CandidateStateMutationResponse), typeof(PlanRevisionMutationResponse),
        typeof(FinalConfirmationMutationResponse),
    ];

    private static DateTimeOffset Timestamp(int seconds) =>
        DateTimeOffset.UnixEpoch.AddSeconds(seconds).AddTicks(10);

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:x12}");

    private static readonly Guid Project = Id(1);
    private static readonly Guid Plan = Id(2);
    private static readonly Guid Operation = Id(3);
    private static readonly Guid Actor = Id(4);
    private static readonly Guid Base = Id(5);
    private static readonly Guid Land = Id(6);
    private static readonly Guid Requirements = Id(7);
    private static readonly Guid CommandId = Id(8);
    private static readonly Guid Floor = Id(9);
    private static readonly Guid Target = Id(10);
    private static readonly Guid Candidate = Id(11);
    private static readonly Guid ValidationId = Id(12);
    private static readonly Guid Revision = Id(13);
    private static readonly Guid Confirmation = Id(14);

    private sealed record RequestEvidence(
        AtomicMutationScope Scope,
        AuthorizationBinding Authorization,
        MutationIdentity Mutation,
        byte[] CanonicalBytes)
    {
        public ProvisionalMutationOperation Operation => Scope.Operation;
    }

    private static MutationFingerprintScope ToPublicFingerprintScope(this AtomicMutationScope scope) =>
        new(scope.ProjectId, scope.PlanId, scope.OperationId, scope.TrustedActorId, scope.AuthorizationVersion);

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

    private static T CaptureThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

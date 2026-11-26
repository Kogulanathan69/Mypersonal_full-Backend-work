using System.Text.Json;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Mutations;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

/// <summary>
/// Test-only mock-orchestration evidence for STEP 07-C3-A. These scenarios do not establish
/// database rollback, durable authorization, transaction locking or production authentication.
/// </summary>
internal static class AtomicPlanMutationConsumerContractScenarios
{
    private const string Schema = "0.1-draft";

    public static void Register(Action<string, Action> run)
    {
        run("AM-A03 atomic consumer rejects client actor injection for all six operations", () =>
        {
            foreach (var item in Cases(OtherActor))
            {
                var currentUser = new CurrentUserDouble(Actor);
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble();
                var consumer = new AtomicPlanMutationConsumer(currentUser, authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckFailure(result, AtomicMutationStatus.AuthorizationDenied, item.Name);
                Check(currentUser.RequiredUserCalls == 1, $"{item.Name} did not acquire the trusted actor exactly once.");
                Check(authorization.Calls == 0, $"{item.Name} actor injection reached authorization.");
                Check(port.TotalMutationCalls == 0 && port.ReconcileCalls == 0,
                    $"{item.Name} actor injection reached mutation or reconciliation.");
            }

            foreach (var item in Cases(Actor))
            {
                var currentUser = new CurrentUserDouble(Actor);
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble
                {
                    Status = AtomicMutationStatus.PersistenceFailure,
                };
                var consumer = new AtomicPlanMutationConsumer(currentUser, authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckFailure(result, AtomicMutationStatus.PersistenceFailure, item.Name);
                Check(authorization.Calls == 1 && authorization.LastActorId == Actor,
                    $"{item.Name} did not authorize the trusted actor.");
                Check(port.TotalMutationCalls == 1 && port.Calls(item.Operation) == 1,
                    $"{item.Name} did not make exactly one matching atomic-port call.");
                Check(port.ReconcileCalls == 0, $"{item.Name} automatically reconciled.");
                Check(port.WriteAttempts == 1, $"{item.Name} did not reach the simulated write boundary.");
            }

            foreach (var item in Cases(Actor))
            {
                var authorization = new ProjectAuthorizationPortDouble(
                    new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied));
                var port = new AtomicMutationPortDouble();
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckFailure(result, AtomicMutationStatus.AuthorizationDenied, item.Name);
                Check(port.TotalMutationCalls == 0, $"{item.Name} denial reached mutation.");
            }
        });

        run("AM-A09 atomic consumer preserves preflight and transaction-time authorization version mismatch", () =>
        {
            foreach (var item in Cases(Actor, version: 7))
            {
                var authorization = Authorized(Project, Actor, version: 8);
                var port = new AtomicMutationPortDouble();
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckFailure(result, AtomicMutationStatus.AuthorizationVersionMismatch, item.Name);
                Check(authorization.Calls == 1, $"{item.Name} did not perform preflight authorization.");
                Check(port.TotalMutationCalls == 0, $"{item.Name} version mismatch reached mutation.");
            }

            foreach (var item in Cases(Actor, version: 7))
            {
                var authorization = Authorized(Project, Actor, version: 7);
                var port = new AtomicMutationPortDouble
                {
                    Status = AtomicMutationStatus.AuthorizationVersionMismatch,
                };
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckFailure(result, AtomicMutationStatus.AuthorizationVersionMismatch, item.Name);
                Check(port.TotalMutationCalls == 1 && port.Calls(item.Operation) == 1,
                    $"{item.Name} transaction-time mismatch call count changed.");
                Check(port.ReconcileCalls == 0 && port.RetryCalls == 0,
                    $"{item.Name} transaction-time mismatch was retried or reconciled.");
            }

            foreach (var item in Cases(Actor))
            {
                var nullAuthorization = new ProjectAuthorizationPortDouble(null);
                var nullPort = new AtomicMutationPortDouble();
                var nullConsumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), nullAuthorization, nullPort);
                var nullResult = item.Invoke(nullConsumer, CancellationToken.None).GetAwaiter().GetResult();
                CheckFailure(nullResult, AtomicMutationStatus.InvalidDependencyResponse, item.Name);
                Check(nullPort.TotalMutationCalls == 0,
                    $"{item.Name} null authorization response reached mutation.");

                var contradictoryAuthorization = Authorized(OtherProject, Actor, 7);
                var contradictoryPort = new AtomicMutationPortDouble();
                var contradictoryConsumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), contradictoryAuthorization, contradictoryPort);
                var contradictoryResult = item.Invoke(
                    contradictoryConsumer, CancellationToken.None).GetAwaiter().GetResult();
                CheckFailure(contradictoryResult, AtomicMutationStatus.InvalidDependencyResponse, item.Name);
                Check(contradictoryPort.TotalMutationCalls == 0,
                    $"{item.Name} contradictory authorization response reached mutation.");
            }

        });

        run("AM-T01 atomic consumer propagates cancellation before mutation", () =>
        {
            foreach (var item in Cases(Actor))
            {
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                var currentUser = new CurrentUserDouble(Actor);
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble();
                var consumer = new AtomicPlanMutationConsumer(currentUser, authorization, port);

                CaptureThrowsAsync<OperationCanceledException>(() =>
                    item.Invoke(consumer, cancelled.Token));

                Check(currentUser.RequiredUserCalls == 0,
                    $"{item.Name} acquired identity after caller cancellation.");
                Check(authorization.Calls == 0 && port.TotalMutationCalls == 0,
                    $"{item.Name} caller cancellation reached a dependency.");
            }

            foreach (var item in Cases(Actor))
            {
                using var cancelled = new CancellationTokenSource();
                var authorization = Authorized(Project, Actor, 7);
                authorization.CancelSource = cancelled;
                authorization.CancelDuringCall = true;
                var port = new AtomicMutationPortDouble();
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                CaptureThrowsAsync<OperationCanceledException>(() =>
                    item.Invoke(consumer, cancelled.Token));

                Check(authorization.Calls == 1 && authorization.LastToken == cancelled.Token,
                    $"{item.Name} did not preserve the exact preflight cancellation token.");
                Check(port.TotalMutationCalls == 0,
                    $"{item.Name} mutation ran after cancelled preflight.");
            }
        });

        run("AM-T02 atomic consumer preserves cancellation before the test-only first write", () =>
        {
            foreach (var item in Cases(Actor))
            {
                using var cancelled = new CancellationTokenSource();
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble
                {
                    CancelBeforeFirstWrite = true,
                    CancelSource = cancelled,
                };
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                CaptureThrowsAsync<OperationCanceledException>(() =>
                    item.Invoke(consumer, cancelled.Token));

                Check(authorization.Calls == 1 && authorization.LastToken == cancelled.Token,
                    $"{item.Name} authorization received a different token.");
                Check(port.TotalMutationCalls == 1 && port.Calls(item.Operation) == 1,
                    $"{item.Name} did not make exactly one matching mutation call.");
                Check(port.LastToken == cancelled.Token,
                    $"{item.Name} mutation received a different caller token.");
                Check(port.WriteAttempts == 0,
                    $"{item.Name} cancellation occurred after a simulated write.");
                Check(port.RetryCalls == 0 && port.ReconcileCalls == 0,
                    $"{item.Name} cancellation triggered retry or reconciliation.");
            }
        });

        run("RPL-MO-14 atomic consumer rejects null responses for all six operations", () =>
        {
            foreach (var item in Cases(Actor))
            {
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble { ReturnNullResponse = true };
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckFailure(result, AtomicMutationStatus.InvalidDependencyResponse, item.Name);
                CheckExactInvocation(item, authorization, port);
            }
        });

        run("RPL-MO-15 atomic consumer rejects contradictory success responses for all six operations", () =>
        {
            foreach (var item in Cases(Actor))
            {
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble
                {
                    ResponseMode = AtomicResponseMode.Succeeded,
                    ReturnForeignBinding = true,
                };
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckForeignPayloadDiffers(item, port);
                CheckFailure(result, AtomicMutationStatus.InvalidDependencyResponse, item.Name);
                CheckExactInvocation(item, authorization, port);
            }
        });

        run("RPL-MO-17 atomic consumer preserves exact unknown outcomes and rejects contradictory references", () =>
        {
            foreach (var item in Cases(Actor))
            {
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble
                {
                    ResponseMode = AtomicResponseMode.OutcomeUnknown,
                };
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var exact = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckUnknown(exact, item);
                CheckExactInvocation(item, authorization, port);

                var contradictoryAuthorization = Authorized(Project, Actor, 7);
                var contradictoryPort = new AtomicMutationPortDouble
                {
                    ResponseMode = AtomicResponseMode.OutcomeUnknown,
                    ReturnForeignBinding = true,
                };
                var contradictoryConsumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), contradictoryAuthorization, contradictoryPort);

                var contradictory = item.Invoke(
                    contradictoryConsumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckFailure(contradictory, AtomicMutationStatus.InvalidDependencyResponse, item.Name);
                CheckExactInvocation(item, contradictoryAuthorization, contradictoryPort);
            }
        });

        run("RPL-MO-18 atomic consumer preserves exact matching duplicates for all six operations", () =>
        {
            foreach (var item in Cases(Actor))
            {
                var authorization = Authorized(Project, Actor, 7);
                var port = new AtomicMutationPortDouble
                {
                    ResponseMode = AtomicResponseMode.MatchingDuplicate,
                };
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var result = item.Invoke(consumer, CancellationToken.None).GetAwaiter().GetResult();

                CheckMatchingDuplicate(result, item, port);
                CheckExactInvocation(item, authorization, port);
            }
        });

        run("FC-RI-01 final confirmation rejects each mismatched protected response binding", () =>
        {
            foreach (var variant in Enum.GetValues<ConfirmationResponseVariant>())
            {
                var request = ConfirmRequest(Actor, version: 7);
                var authorization = Authorized(Project, Actor, version: 7);
                var port = new AtomicMutationPortDouble
                {
                    ResponseMode = AtomicResponseMode.Succeeded,
                    ConfirmationResponseVariant = variant,
                };
                var consumer = new AtomicPlanMutationConsumer(
                    new CurrentUserDouble(Actor), authorization, port);

                var response = consumer
                    .ConfirmFinalPlanAsync(request, CancellationToken.None)
                    .GetAwaiter().GetResult();

                CheckConfirmationVariantDiffers(
                    variant,
                    RequirePayload<AtomicFinalConfirmationPayload>(
                        port.LastProducedPrimaryPayload,
                        $"{variant} provider confirmation"),
                    request,
                    port.LastProducedConfirmationScope);
                CheckFailure(
                    new Observation(response.Result, response.Confirmation),
                    AtomicMutationStatus.InvalidDependencyResponse,
                    $"Final Confirmation {variant}");
                Check(authorization.Calls == 1 &&
                      port.Calls(ProvisionalMutationOperation.FinalPlanConfirmation) == 1 &&
                      port.TotalMutationCalls == 1 &&
                      port.WriteAttempts == 1 &&
                      port.RetryCalls == 0 &&
                      port.ReconcileCalls == 0,
                    $"Final Confirmation {variant} changed the single-call/no-retry boundary.");
            }
        });

        run("FC-RI-02 final confirmation fingerprint conflict exposes no protected payload", () =>
        {
            var request = ConfirmRequest(Actor, version: 7);
            var authorization = Authorized(Project, Actor, version: 7);
            var port = new AtomicMutationPortDouble
            {
                Status = AtomicMutationStatus.FingerprintConflict,
            };
            var consumer = new AtomicPlanMutationConsumer(
                new CurrentUserDouble(Actor), authorization, port);

            var response = consumer
                .ConfirmFinalPlanAsync(request, CancellationToken.None)
                .GetAwaiter().GetResult();

            CheckFailure(
                new Observation(response.Result, response.Confirmation),
                AtomicMutationStatus.FingerprintConflict,
                "Final Confirmation fingerprint conflict");
            Check(authorization.Calls == 1 &&
                  port.Calls(ProvisionalMutationOperation.FinalPlanConfirmation) == 1 &&
                  port.TotalMutationCalls == 1 &&
                  port.WriteAttempts == 1 &&
                  port.RetryCalls == 0 &&
                  port.ReconcileCalls == 0,
                "Final Confirmation fingerprint conflict was retried or reconciled.");
        });
    }

    private static OperationCase[] Cases(Guid actorId, long version = 7)
    {
        var create = CreateRequest(actorId, version);
        var apply = ApplyRequest(actorId, version);
        var reject = RejectRequest(actorId, version);
        var expire = ExpireRequest(actorId, version);
        var append = AppendRequest(actorId, version);
        var confirm = ConfirmRequest(actorId, version);

        return
        [
            new("Candidate Create", ProvisionalMutationOperation.CandidateCreate,
                create, create.Mutation, Candidate, observation =>
            {
                var candidate = RequirePayload<PreparedCandidateSnapshot>(observation.PrimaryPayload, "candidate");
                Check(candidate.CandidateId == Candidate && candidate.OwnerId == Actor &&
                      candidate.BaseRevisionId == create.ExpectedBaseRevisionId &&
                      candidate.LandRevisionId == create.LandRevisionId &&
                      candidate.RequirementsRevisionId == create.RequirementsRevisionId,
                    "Candidate Create duplicate payload lost its exact request binding.");
            }, async (consumer, token) =>
            {
                var response = await consumer.CreateCandidateAsync(create, token);
                return new(response.Result, response.Candidate);
            }),
            new("Candidate Apply", ProvisionalMutationOperation.CandidateApply,
                apply, apply.Mutation, Revision, observation =>
            {
                var candidate = RequirePayload<AtomicCandidatePayload>(observation.PrimaryPayload, "candidate");
                var revision = RequirePayload<AtomicPlanRevisionPayload>(observation.SecondaryPayload, "revision");
                Check(candidate.CandidateId == apply.CandidateId &&
                      candidate.State == CandidatePreviewStatus.Applied &&
                      candidate.ProjectId == Project && candidate.PlanId == Plan &&
                      candidate.GeometryHash == apply.CandidateGeometryHash &&
                      candidate.ValidationEvidenceId == apply.ValidationEvidenceId &&
                      revision.RevisionId == Revision && revision.ProjectId == Project &&
                      revision.PlanId == Plan &&
                      revision.Geometry.ExactJson == apply.PreparedRevision.Geometry.ExactJson &&
                      revision.ValidationEvidenceId == apply.PreparedRevision.Validation.ValidationId,
                    "Candidate Apply duplicate payload lost its exact candidate or revision binding.");
            }, async (consumer, token) =>
            {
                var response = await consumer.ApplyCandidateAsync(apply, token);
                return new(response.Result, response.Candidate, response.Revision);
            }),
            new("Candidate Reject", ProvisionalMutationOperation.CandidateReject,
                reject, reject.Mutation, Candidate, observation =>
            {
                var candidate = RequirePayload<AtomicCandidatePayload>(observation.PrimaryPayload, "candidate");
                Check(candidate.CandidateId == reject.CandidateId &&
                      candidate.State == CandidatePreviewStatus.Rejected &&
                      candidate.ProjectId == Project && candidate.PlanId == Plan,
                    "Candidate Reject duplicate payload lost its exact candidate binding.");
            }, async (consumer, token) =>
            {
                var response = await consumer.RejectCandidateAsync(reject, token);
                return new(response.Result, response.Candidate);
            }),
            new("Candidate Expire", ProvisionalMutationOperation.CandidateExpire,
                expire, expire.Mutation, Candidate, observation =>
            {
                var candidate = RequirePayload<AtomicCandidatePayload>(observation.PrimaryPayload, "candidate");
                Check(candidate.CandidateId == expire.CandidateId &&
                      candidate.State == CandidatePreviewStatus.Expired &&
                      candidate.ProjectId == Project && candidate.PlanId == Plan,
                    "Candidate Expire duplicate payload lost its exact candidate binding.");
            }, async (consumer, token) =>
            {
                var response = await consumer.ExpireCandidateAsync(expire, token);
                return new(response.Result, response.Candidate);
            }),
            new("Revision Append", ProvisionalMutationOperation.PlanRevisionAppend,
                append, append.Mutation, Revision, observation =>
            {
                var revision = RequirePayload<AtomicPlanRevisionPayload>(observation.PrimaryPayload, "revision");
                Check(revision.RevisionId == Revision && revision.ProjectId == Project &&
                      revision.PlanId == Plan &&
                      revision.Geometry.ExactJson == append.PreparedRevision.Geometry.ExactJson &&
                      revision.ValidationEvidenceId == append.PreparedRevision.Validation.ValidationId,
                    "Revision Append duplicate payload lost its exact revision binding.");
            }, async (consumer, token) =>
            {
                var response = await consumer.AppendRevisionAsync(append, token);
                return new(response.Result, response.Revision);
            }),
            new("Final Confirmation", ProvisionalMutationOperation.FinalPlanConfirmation,
                confirm, confirm.Mutation, Confirmation, observation =>
            {
                var confirmation = RequirePayload<AtomicFinalConfirmationPayload>(
                    observation.PrimaryPayload, "confirmation");
                Check(confirmation.ConfirmationId == Confirmation &&
                      confirmation.ProjectId == Project && confirmation.PlanId == Plan &&
                      confirmation.ExactRevisionId == confirm.Confirmation.ExactRevisionId &&
                      confirmation.GeometryHash == confirm.Confirmation.GeometryHash &&
                      confirmation.ValidationEvidenceId == confirm.Confirmation.ValidationEvidenceId &&
                      confirmation.ConfirmedBy == Actor &&
                      confirmation.WarningAcknowledged == confirm.Confirmation.WarningAcknowledged,
                    "Final Confirmation duplicate payload lost its exact confirmation binding.");
            }, async (consumer, token) =>
            {
                var response = await consumer.ConfirmFinalPlanAsync(confirm, token);
                return new(response.Result, response.Confirmation);
            }),
        ];
    }

    private static CandidateCreateMutationRequest CreateRequest(Guid actorId, long version)
    {
        var scope = Scope(ProvisionalMutationOperation.CandidateCreate, actorId, version);
        var command = Command();
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateCreateFingerprintInput(
            FingerprintScope(scope), Base, Land, Requirements, Schema, command));
        return CandidateCreateMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), Base, Land, Requirements,
            Schema, command);
    }

    private static CandidateApplyMutationRequest ApplyRequest(Guid actorId, long version)
    {
        var scope = Scope(ProvisionalMutationOperation.CandidateApply, actorId, version);
        var prepared = PreparedRevision();
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateApplyFingerprintInput(
            FingerprintScope(scope), Candidate, Base, Land, Requirements,
            prepared.Geometry.GeometryHash, ValidationId));
        return CandidateApplyMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), Candidate, Base, Land,
            Requirements, prepared.Geometry.GeometryHash, ValidationId, prepared);
    }

    private static CandidateApplyMutationRequest ForeignApplyRequest(long version)
    {
        var scope = ForeignScope(ProvisionalMutationOperation.CandidateApply, version);
        var prepared = PreparedRevision(
            OtherProject, OtherPlan, OtherBase, OtherLand, OtherRequirements, OtherValidationId);
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateApplyFingerprintInput(
            FingerprintScope(scope), OtherCandidate, OtherBase, OtherLand, OtherRequirements,
            prepared.Geometry.GeometryHash, OtherValidationId));
        return CandidateApplyMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), OtherCandidate, OtherBase,
            OtherLand, OtherRequirements, prepared.Geometry.GeometryHash, OtherValidationId, prepared);
    }

    private static CandidateRejectMutationRequest RejectRequest(Guid actorId, long version)
    {
        var scope = Scope(ProvisionalMutationOperation.CandidateReject, actorId, version);
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateRejectFingerprintInput(
            FingerprintScope(scope), Candidate, "USER_REJECTED"));
        return CandidateRejectMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), Candidate, "USER_REJECTED");
    }

    private static CandidateRejectMutationRequest ForeignRejectRequest(long version)
    {
        var scope = ForeignScope(ProvisionalMutationOperation.CandidateReject, version);
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateRejectFingerprintInput(
            FingerprintScope(scope), OtherCandidate, "USER_REJECTED"));
        return CandidateRejectMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), OtherCandidate, "USER_REJECTED");
    }

    private static CandidateExpireMutationRequest ExpireRequest(Guid actorId, long version)
    {
        var scope = Scope(ProvisionalMutationOperation.CandidateExpire, actorId, version);
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateExpireFingerprintInput(
            FingerprintScope(scope), Candidate, Timestamp(2)));
        return CandidateExpireMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), Candidate, Timestamp(2));
    }

    private static CandidateExpireMutationRequest ForeignExpireRequest(long version)
    {
        var scope = ForeignScope(ProvisionalMutationOperation.CandidateExpire, version);
        var fingerprint = MutationFingerprintProfileV1.Compute(new CandidateExpireFingerprintInput(
            FingerprintScope(scope), OtherCandidate, Timestamp(4)));
        return CandidateExpireMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), OtherCandidate, Timestamp(4));
    }

    private static PlanRevisionAppendMutationRequest AppendRequest(Guid actorId, long version)
    {
        var scope = Scope(ProvisionalMutationOperation.PlanRevisionAppend, actorId, version);
        var prepared = PreparedRevision();
        var fingerprint = MutationFingerprintProfileV1.Compute(new PlanRevisionAppendFingerprintInput(
            FingerprintScope(scope), Base, prepared.Geometry.GeometryHash, Land,
            Requirements, ValidationId, Schema));
        return PlanRevisionAppendMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), prepared);
    }

    private static PlanRevisionAppendMutationRequest ForeignAppendRequest(long version)
    {
        var scope = ForeignScope(ProvisionalMutationOperation.PlanRevisionAppend, version);
        var prepared = PreparedRevision(
            OtherProject, OtherPlan, OtherBase, OtherLand, OtherRequirements, OtherValidationId);
        var fingerprint = MutationFingerprintProfileV1.Compute(new PlanRevisionAppendFingerprintInput(
            FingerprintScope(scope), OtherBase, prepared.Geometry.GeometryHash, OtherLand,
            OtherRequirements, OtherValidationId, Schema));
        return PlanRevisionAppendMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), prepared);
    }

    private static FinalPlanConfirmationMutationRequest ConfirmRequest(Guid actorId, long version)
    {
        var geometry = Geometry();
        return ConfirmRequest(
            Project, Plan, Operation, actorId, version, Revision,
            geometry.GeometryHash, ValidationId, Land, Requirements,
            warningAcknowledged: true);
    }

    private static FinalPlanConfirmationMutationRequest ConfirmRequest(
        Guid projectId,
        Guid planId,
        Guid operationId,
        Guid actorId,
        long version,
        Guid revisionId,
        string geometryHash,
        Guid validationEvidenceId,
        Guid landRevisionId,
        Guid requirementsRevisionId,
        bool warningAcknowledged)
    {
        var scope = AtomicMutationScope.Create(
            projectId, planId, operationId,
            ProvisionalMutationOperation.FinalPlanConfirmation,
            actorId, new AuthorizationVersion(version));
        var prepared = PreparedConfirmationSnapshot.Create(
            revisionId, geometryHash, validationEvidenceId,
            landRevisionId, requirementsRevisionId, warningAcknowledged);
        var fingerprint = MutationFingerprintProfileV1.Compute(new FinalPlanConfirmationFingerprintInput(
            FingerprintScope(scope), revisionId, geometryHash,
            validationEvidenceId, landRevisionId, requirementsRevisionId,
            warningAcknowledged));
        return FinalPlanConfirmationMutationRequest.Create(
            scope, Authorization(scope), Identity(scope, fingerprint), prepared);
    }

    private static FinalPlanConfirmationMutationRequest VariantConfirmationRequest(
        ConfirmationResponseVariant variant,
        long version)
    {
        var geometryHash = Geometry().GeometryHash;
        return variant switch
        {
            ConfirmationResponseVariant.Project => ConfirmRequest(
                OtherProject, Plan, Operation, Actor, version, Revision,
                geometryHash, ValidationId, Land, Requirements, true),
            ConfirmationResponseVariant.Plan => ConfirmRequest(
                Project, OtherPlan, Operation, Actor, version, Revision,
                geometryHash, ValidationId, Land, Requirements, true),
            ConfirmationResponseVariant.ExactRevision => ConfirmRequest(
                Project, Plan, Operation, Actor, version, OtherRevision,
                geometryHash, ValidationId, Land, Requirements, true),
            ConfirmationResponseVariant.GeometryHash => ConfirmRequest(
                Project, Plan, Operation, Actor, version, Revision,
                Geometry(OtherProject, OtherPlan, OtherLand, OtherRequirements).GeometryHash,
                ValidationId, Land, Requirements, true),
            ConfirmationResponseVariant.ValidationEvidence => ConfirmRequest(
                Project, Plan, Operation, Actor, version, Revision,
                geometryHash, OtherValidationId, Land, Requirements, true),
            ConfirmationResponseVariant.WarningAcknowledgement => ConfirmRequest(
                Project, Plan, Operation, Actor, version, Revision,
                geometryHash, ValidationId, Land, Requirements, false),
            ConfirmationResponseVariant.AuthorizationVersion => ConfirmRequest(
                Project, Plan, Operation, Actor, checked(version + 1), Revision,
                geometryHash, ValidationId, Land, Requirements, true),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
    }

    private static PreparedRevisionSnapshot PreparedRevision(
        Guid? projectId = null,
        Guid? planId = null,
        Guid? baseRevisionId = null,
        Guid? landRevisionId = null,
        Guid? requirementsRevisionId = null,
        Guid? validationId = null)
    {
        var actualBase = baseRevisionId ?? Base;
        var actualLand = landRevisionId ?? Land;
        var actualRequirements = requirementsRevisionId ?? Requirements;
        var actualValidation = validationId ?? ValidationId;
        var geometry = Geometry(projectId, planId, actualLand, actualRequirements);
        return PreparedRevisionSnapshot.Create(
            actualBase, actualLand, actualRequirements, geometry,
            FrozenValidationEvidence.Create(
                actualValidation, "validator-v1", geometry.GeometryHash,
                actualLand, actualRequirements,
                true, true, "VERIFIED", false, Timestamp(0)));
    }

    private static FrozenGeometrySnapshot Geometry(
        Guid? projectId = null,
        Guid? planId = null,
        Guid? landRevisionId = null,
        Guid? requirementsRevisionId = null)
    {
        var actualProject = projectId ?? Project;
        var actualPlan = planId ?? Plan;
        var actualLand = landRevisionId ?? Land;
        var actualRequirements = requirementsRevisionId ?? Requirements;
        var json = File.ReadAllText(FixtureLocator.ResolveRepositoryFile(
            "docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json"))
            .Replace("00000000-0000-4000-8000-000000000001", actualProject.ToString(), StringComparison.Ordinal)
            .Replace("00000000-0000-4000-8000-000000000002", actualPlan.ToString(), StringComparison.Ordinal)
            .Replace("00000000-0000-4000-8000-000000000003", actualLand.ToString(), StringComparison.Ordinal)
            .Replace("00000000-0000-4000-8000-000000000004", actualRequirements.ToString(), StringComparison.Ordinal);
        return FrozenGeometrySnapshot.Create(
            Schema, json, PlanRevisionService.ComputeGeometryHash(json));
    }

    private static PlanEditCommandDto Command()
    {
        using var document = JsonDocument.Parse("{\"deltaX\":1.25,\"deltaY\":-0.5}");
        return new PlanEditCommandDto
        {
            CommandId = CommandId,
            CommandType = PlanEditCommandTypes.MoveRoom,
            FloorId = Floor,
            TargetId = Target,
            Payload = document.RootElement.Clone(),
        };
    }

    private static AtomicMutationScope Scope(
        ProvisionalMutationOperation operation, Guid actorId, long version) =>
        AtomicMutationScope.Create(
            Project, Plan, Operation, operation, actorId, new AuthorizationVersion(version));

    private static AtomicMutationScope ForeignScope(
        ProvisionalMutationOperation operation, long version) =>
        AtomicMutationScope.Create(
            OtherProject, OtherPlan, OtherOperation, operation, OtherActor,
            new AuthorizationVersion(version));

    private static AuthorizationBinding Authorization(AtomicMutationScope scope) =>
        new(scope.ProjectId, scope.TrustedActorId, scope.AuthorizationVersion);

    private static MutationIdentity Identity(
        AtomicMutationScope scope, CommandFingerprint fingerprint) =>
        new(scope.OperationId, scope.Operation, MutationFingerprintProfileV1.ProfileNumber, fingerprint);

    private static MutationFingerprintScope FingerprintScope(AtomicMutationScope scope) =>
        new(scope.ProjectId, scope.PlanId, scope.OperationId,
            scope.TrustedActorId, scope.AuthorizationVersion);

    private static ProjectAuthorizationPortDouble Authorized(
        Guid projectId, Guid actorId, long version) =>
        new(new ProjectAuthorizationResult(
            ProjectAuthorizationStatus.Authorized,
            new ProjectAuthorizationSnapshot(projectId, actorId, new AuthorizationVersion(version))));

    private static PreparedCandidateSnapshot PreparedCandidate(
        CandidateCreateMutationRequest request)
    {
        var geometry = Geometry();
        var validation = FrozenValidationEvidence.Create(
            ValidationId, "validator-v1", geometry.GeometryHash,
            request.LandRevisionId, request.RequirementsRevisionId,
            true, true, "VERIFIED", false, Timestamp(0));
        return PreparedCandidateSnapshot.Create(
            Candidate, request.Scope.TrustedActorId, request.ExpectedBaseRevisionId,
            request.LandRevisionId, request.RequirementsRevisionId,
            geometry, validation, Timestamp(1), Timestamp(2));
    }

    private static AtomicCandidatePayload CandidatePayload(
        AtomicMutationScope scope,
        Guid candidateId,
        CandidatePreviewStatus state,
        string geometryHash,
        Guid validationEvidenceId) =>
        AtomicCandidatePayload.Create(
            candidateId, scope.ProjectId, scope.PlanId, state,
            geometryHash, validationEvidenceId);

    private static AtomicPlanRevisionPayload RevisionPayload(
        AtomicMutationScope scope,
        PreparedRevisionSnapshot prepared,
        Guid? revisionId = null) =>
        AtomicPlanRevisionPayload.Create(
            revisionId ?? Revision, scope.ProjectId, scope.PlanId, prepared.Geometry,
            prepared.Validation.ValidationId, prepared.LandRevisionId,
            prepared.RequirementsRevisionId);

    private static AtomicFinalConfirmationPayload ConfirmationPayload(
        FinalPlanConfirmationMutationRequest request) =>
        AtomicFinalConfirmationPayload.Create(
            Confirmation, request.Scope.ProjectId, request.Scope.PlanId,
            request.Confirmation.ExactRevisionId, request.Confirmation.GeometryHash,
            request.Confirmation.ValidationEvidenceId, request.Scope.TrustedActorId,
            request.Confirmation.WarningAcknowledged);

    private static AtomicMutationResult SuccessfulResult(
        AtomicMutationScope scope,
        MutationIdentity mutation,
        Guid durableOutcomeId,
        AtomicMutationStatus status) =>
        new(status, new AtomicMutationEvidence(
            durableOutcomeId, scope.PlanId, Authorization(scope), mutation));

    private static AtomicMutationResult UnknownResult(
        AtomicMutationScope scope,
        MutationIdentity mutation) =>
        new(AtomicMutationStatus.TransactionOutcomeUnknown,
            reconciliation: new ReconciliationReference(scope.PlanId, Authorization(scope), mutation));

    private static void CheckFailure(
        Observation observation, AtomicMutationStatus expected, string operation)
    {
        Check(observation.Result.Status == expected, $"{operation} returned {observation.Result.Status}, expected {expected}.");
        Check(!observation.HasProtectedPayload && observation.Result.Evidence is null &&
              observation.Result.Reconciliation is null && !observation.Result.AllowsAutomaticRetry,
            $"{operation} failure exposed protected state or retry authority.");
    }

    private static void CheckExactInvocation(
        OperationCase item,
        ProjectAuthorizationPortDouble authorization,
        AtomicMutationPortDouble port)
    {
        Check(authorization.Calls == 1 && authorization.LastProjectId == Project &&
              authorization.LastActorId == Actor,
            $"{item.Name} did not authorize the exact project and trusted actor.");
        Check(port.TotalMutationCalls == 1 && port.Calls(item.Operation) == 1,
            $"{item.Name} did not make exactly one matching atomic-port call.");
        Check(AllOperations.Where(operation => operation != item.Operation)
                .All(operation => port.Calls(operation) == 0),
            $"{item.Name} invoked a sibling mutation operation.");
        Check(ReferenceEquals(port.LastRequest, item.Request),
            $"{item.Name} did not forward the exact immutable request instance.");
        Check(port.LastScope is not null &&
              port.LastScope.ProjectId == Project && port.LastScope.PlanId == Plan &&
              port.LastScope.OperationId == Operation && port.LastScope.Operation == item.Operation &&
              port.LastScope.TrustedActorId == Actor &&
              port.LastScope.AuthorizationVersion == new AuthorizationVersion(7),
            $"{item.Name} mutation call lost its exact trusted scope or authorization version.");
        Check(port.LastMutation is not null && MutationMatches(port.LastMutation, item.Mutation),
            $"{item.Name} mutation call changed its identity or fingerprint.");
        Check(port.RetryCalls == 0 && port.ReconcileCalls == 0,
            $"{item.Name} triggered automatic retry or reconciliation.");
    }

    private static void CheckUnknown(Observation observation, OperationCase item)
    {
        var result = observation.Result;
        var reference = result.Reconciliation;
        Check(result.Status == AtomicMutationStatus.TransactionOutcomeUnknown,
            $"{item.Name} changed an unknown transaction outcome to {result.Status}.");
        Check(!observation.HasProtectedPayload && result.Evidence is null &&
              reference is not null && !result.AllowsAutomaticRetry,
            $"{item.Name} unknown outcome exposed protected state or retry authority.");
        Check(reference!.PlanId == Plan &&
              reference.Authorization.ProjectId == Project &&
              reference.Authorization.AuthenticatedActorId == Actor &&
              reference.Authorization.Version == new AuthorizationVersion(7) &&
              MutationMatches(reference.Mutation, item.Mutation),
            $"{item.Name} did not preserve the exact reconciliation reference.");
    }

    private static void CheckForeignPayloadDiffers(
        OperationCase item,
        AtomicMutationPortDouble port)
    {
        switch (item.Operation)
        {
            case ProvisionalMutationOperation.CandidateCreate:
            {
                var candidate = RequirePayload<PreparedCandidateSnapshot>(
                    port.LastProducedPrimaryPayload, "foreign candidate");
                Check(candidate.OwnerId == OtherActor && candidate.OwnerId != Actor,
                    "Candidate Create foreign payload did not differ from the trusted actor.");
                break;
            }
            case ProvisionalMutationOperation.CandidateApply:
            {
                var candidate = RequirePayload<AtomicCandidatePayload>(
                    port.LastProducedPrimaryPayload, "foreign candidate");
                var revision = RequirePayload<AtomicPlanRevisionPayload>(
                    port.LastProducedSecondaryPayload, "foreign revision");
                Check(candidate.ProjectId == OtherProject && candidate.ProjectId != Project &&
                      candidate.PlanId == OtherPlan && candidate.PlanId != Plan &&
                      candidate.CandidateId == OtherCandidate && candidate.CandidateId != Candidate &&
                      candidate.ValidationEvidenceId == OtherValidationId &&
                      revision.RevisionId == OtherRevision && revision.RevisionId != Revision &&
                      revision.ProjectId == OtherProject && revision.PlanId == OtherPlan &&
                      revision.LandRevisionId == OtherLand &&
                      revision.RequirementsRevisionId == OtherRequirements &&
                      revision.Geometry.GeometryHash != Geometry().GeometryHash,
                    "Candidate Apply foreign response did not carry a distinct protected payload.");
                break;
            }
            case ProvisionalMutationOperation.CandidateReject:
            case ProvisionalMutationOperation.CandidateExpire:
            {
                var candidate = RequirePayload<AtomicCandidatePayload>(
                    port.LastProducedPrimaryPayload, "foreign candidate");
                Check(candidate.ProjectId == OtherProject && candidate.ProjectId != Project &&
                      candidate.PlanId == OtherPlan && candidate.PlanId != Plan &&
                      candidate.CandidateId == OtherCandidate && candidate.CandidateId != Candidate &&
                      candidate.ValidationEvidenceId == OtherValidationId &&
                      candidate.GeometryHash != Geometry().GeometryHash,
                    $"{item.Name} foreign response did not carry a distinct protected payload.");
                break;
            }
            case ProvisionalMutationOperation.PlanRevisionAppend:
            {
                var revision = RequirePayload<AtomicPlanRevisionPayload>(
                    port.LastProducedPrimaryPayload, "foreign revision");
                Check(revision.RevisionId == OtherRevision && revision.RevisionId != Revision &&
                      revision.ProjectId == OtherProject && revision.ProjectId != Project &&
                      revision.PlanId == OtherPlan && revision.PlanId != Plan &&
                      revision.LandRevisionId == OtherLand &&
                      revision.RequirementsRevisionId == OtherRequirements &&
                      revision.ValidationEvidenceId == OtherValidationId &&
                      revision.Geometry.GeometryHash != Geometry().GeometryHash,
                    "Revision Append foreign response did not carry a distinct protected payload.");
                break;
            }
            case ProvisionalMutationOperation.FinalPlanConfirmation:
            {
                var confirmation = RequirePayload<AtomicFinalConfirmationPayload>(
                    port.LastProducedPrimaryPayload, "foreign confirmation");
                Check(confirmation.ConfirmedBy == OtherActor && confirmation.ConfirmedBy != Actor,
                    "Final Confirmation foreign payload did not differ from the trusted actor.");
                break;
            }
            default:
                throw new Exception($"Unsupported operation {item.Operation}.");
        }
    }

    private static void CheckConfirmationVariantDiffers(
        ConfirmationResponseVariant variant,
        AtomicFinalConfirmationPayload actual,
        FinalPlanConfirmationMutationRequest expected,
        AtomicMutationScope? actualScope)
    {
        var differs = variant switch
        {
            ConfirmationResponseVariant.Project =>
                actual.ProjectId != expected.Scope.ProjectId,
            ConfirmationResponseVariant.Plan =>
                actual.PlanId != expected.Scope.PlanId,
            ConfirmationResponseVariant.ExactRevision =>
                actual.ExactRevisionId != expected.Confirmation.ExactRevisionId,
            ConfirmationResponseVariant.GeometryHash =>
                !string.Equals(
                    actual.GeometryHash,
                    expected.Confirmation.GeometryHash,
                    StringComparison.Ordinal),
            ConfirmationResponseVariant.ValidationEvidence =>
                actual.ValidationEvidenceId != expected.Confirmation.ValidationEvidenceId,
            ConfirmationResponseVariant.WarningAcknowledgement =>
                actual.WarningAcknowledged != expected.Confirmation.WarningAcknowledged,
            ConfirmationResponseVariant.AuthorizationVersion =>
                actualScope is not null &&
                actualScope.AuthorizationVersion != expected.Scope.AuthorizationVersion,
            _ => false,
        };

        Check(differs, $"Final Confirmation {variant} did not produce a distinct provider response.");
    }

    private static void CheckMatchingDuplicate(
        Observation observation,
        OperationCase item,
        AtomicMutationPortDouble port)
    {
        var result = observation.Result;
        var evidence = result.Evidence;
        Check(result.Status == AtomicMutationStatus.MatchingDuplicate &&
              observation.HasProtectedPayload && evidence is not null &&
              result.Reconciliation is null && !result.AllowsAutomaticRetry,
            $"{item.Name} did not preserve a safe matching-duplicate outcome.");
        Check(evidence!.DurableOutcomeId == item.DurableOutcomeId &&
              evidence.PlanId == Plan &&
              evidence.Authorization.ProjectId == Project &&
              evidence.Authorization.AuthenticatedActorId == Actor &&
              evidence.Authorization.Version == new AuthorizationVersion(7) &&
              MutationMatches(evidence.Mutation, item.Mutation),
            $"{item.Name} matching duplicate lost its durable request binding.");
        item.CheckProtectedPayload(observation);
        CheckExactProducedPayload(observation, item, port);
    }

    private static void CheckExactProducedPayload(
        Observation observation,
        OperationCase item,
        AtomicMutationPortDouble port)
    {
        switch (item.Operation)
        {
            case ProvisionalMutationOperation.CandidateCreate:
                CheckPreparedCandidateEqual(
                    RequirePayload<PreparedCandidateSnapshot>(observation.PrimaryPayload, "candidate"),
                    RequirePayload<PreparedCandidateSnapshot>(port.LastProducedPrimaryPayload, "source candidate"));
                break;
            case ProvisionalMutationOperation.CandidateApply:
                CheckAtomicCandidateEqual(
                    RequirePayload<AtomicCandidatePayload>(observation.PrimaryPayload, "candidate"),
                    RequirePayload<AtomicCandidatePayload>(port.LastProducedPrimaryPayload, "source candidate"));
                CheckRevisionEqual(
                    RequirePayload<AtomicPlanRevisionPayload>(observation.SecondaryPayload, "revision"),
                    RequirePayload<AtomicPlanRevisionPayload>(port.LastProducedSecondaryPayload, "source revision"));
                break;
            case ProvisionalMutationOperation.CandidateReject:
            case ProvisionalMutationOperation.CandidateExpire:
                CheckAtomicCandidateEqual(
                    RequirePayload<AtomicCandidatePayload>(observation.PrimaryPayload, "candidate"),
                    RequirePayload<AtomicCandidatePayload>(port.LastProducedPrimaryPayload, "source candidate"));
                break;
            case ProvisionalMutationOperation.PlanRevisionAppend:
                CheckRevisionEqual(
                    RequirePayload<AtomicPlanRevisionPayload>(observation.PrimaryPayload, "revision"),
                    RequirePayload<AtomicPlanRevisionPayload>(port.LastProducedPrimaryPayload, "source revision"));
                break;
            case ProvisionalMutationOperation.FinalPlanConfirmation:
                CheckConfirmationEqual(
                    RequirePayload<AtomicFinalConfirmationPayload>(observation.PrimaryPayload, "confirmation"),
                    RequirePayload<AtomicFinalConfirmationPayload>(port.LastProducedPrimaryPayload, "source confirmation"));
                break;
            default:
                throw new Exception($"Unsupported operation {item.Operation}.");
        }
    }

    private static void CheckPreparedCandidateEqual(
        PreparedCandidateSnapshot actual,
        PreparedCandidateSnapshot expected)
    {
        Check(actual.CandidateId == expected.CandidateId &&
              actual.OwnerId == expected.OwnerId &&
              actual.BaseRevisionId == expected.BaseRevisionId &&
              actual.LandRevisionId == expected.LandRevisionId &&
              actual.RequirementsRevisionId == expected.RequirementsRevisionId &&
              actual.CreatedAtUtc == expected.CreatedAtUtc &&
              actual.ExpiresAtUtc == expected.ExpiresAtUtc,
            "Candidate Create did not preserve all immutable candidate fields.");
        CheckGeometryEqual(actual.Geometry, expected.Geometry, "Candidate Create");
        CheckValidationEqual(actual.Validation, expected.Validation, "Candidate Create");
    }

    private static void CheckAtomicCandidateEqual(
        AtomicCandidatePayload actual,
        AtomicCandidatePayload expected) =>
        Check(actual.CandidateId == expected.CandidateId &&
              actual.ProjectId == expected.ProjectId &&
              actual.PlanId == expected.PlanId &&
              actual.State == expected.State &&
              string.Equals(actual.GeometryHash, expected.GeometryHash, StringComparison.Ordinal) &&
              actual.ValidationEvidenceId == expected.ValidationEvidenceId,
            "Matching duplicate did not preserve every candidate payload field.");

    private static void CheckRevisionEqual(
        AtomicPlanRevisionPayload actual,
        AtomicPlanRevisionPayload expected)
    {
        Check(actual.RevisionId == expected.RevisionId &&
              actual.ProjectId == expected.ProjectId &&
              actual.PlanId == expected.PlanId &&
              actual.ValidationEvidenceId == expected.ValidationEvidenceId &&
              actual.LandRevisionId == expected.LandRevisionId &&
              actual.RequirementsRevisionId == expected.RequirementsRevisionId,
            "Matching duplicate did not preserve every revision payload field.");
        CheckGeometryEqual(actual.Geometry, expected.Geometry, "Matching duplicate revision");
    }

    private static void CheckConfirmationEqual(
        AtomicFinalConfirmationPayload actual,
        AtomicFinalConfirmationPayload expected) =>
        Check(actual.ConfirmationId == expected.ConfirmationId &&
              actual.ProjectId == expected.ProjectId &&
              actual.PlanId == expected.PlanId &&
              actual.ExactRevisionId == expected.ExactRevisionId &&
              string.Equals(actual.GeometryHash, expected.GeometryHash, StringComparison.Ordinal) &&
              actual.ValidationEvidenceId == expected.ValidationEvidenceId &&
              actual.ConfirmedBy == expected.ConfirmedBy &&
              actual.WarningAcknowledged == expected.WarningAcknowledged,
            "Matching duplicate did not preserve every final-confirmation payload field.");

    private static void CheckGeometryEqual(
        FrozenGeometrySnapshot actual,
        FrozenGeometrySnapshot expected,
        string operation) =>
        Check(string.Equals(actual.SchemaVersion, expected.SchemaVersion, StringComparison.Ordinal) &&
              string.Equals(actual.ExactJson, expected.ExactJson, StringComparison.Ordinal) &&
              string.Equals(actual.GeometryHash, expected.GeometryHash, StringComparison.Ordinal) &&
              actual.ProjectId == expected.ProjectId &&
              actual.PlanId == expected.PlanId &&
              actual.LandRevisionId == expected.LandRevisionId &&
              actual.RequirementsRevisionId == expected.RequirementsRevisionId,
            $"{operation} did not preserve every frozen geometry field.");

    private static void CheckValidationEqual(
        FrozenValidationEvidence actual,
        FrozenValidationEvidence expected,
        string operation) =>
        Check(actual.ValidationId == expected.ValidationId &&
              string.Equals(actual.ValidatorVersion, expected.ValidatorVersion, StringComparison.Ordinal) &&
              string.Equals(actual.GeometryHash, expected.GeometryHash, StringComparison.Ordinal) &&
              actual.LandRevisionId == expected.LandRevisionId &&
              actual.RequirementsRevisionId == expected.RequirementsRevisionId &&
              actual.GeometryValid == expected.GeometryValid &&
              actual.RequirementsMatch == expected.RequirementsMatch &&
              string.Equals(actual.RegulatoryEvidenceStatus, expected.RegulatoryEvidenceStatus, StringComparison.Ordinal) &&
              actual.HasBlockingIssues == expected.HasBlockingIssues &&
              actual.RequiresWarningAcknowledgement == expected.RequiresWarningAcknowledgement &&
              actual.ValidatedAtUtc == expected.ValidatedAtUtc &&
              actual.IssueCodes.SequenceEqual(expected.IssueCodes, StringComparer.Ordinal),
            $"{operation} did not preserve every frozen validation field.");

    private static bool MutationMatches(MutationIdentity actual, MutationIdentity expected) =>
        actual.OperationId == expected.OperationId &&
        actual.Operation == expected.Operation &&
        actual.FingerprintProfileVersion == expected.FingerprintProfileVersion &&
        actual.Fingerprint.Matches(expected.Fingerprint);

    private static T RequirePayload<T>(object? payload, string name) where T : class =>
        payload as T ?? throw new Exception($"Expected protected {name} payload of type {typeof(T).Name}.");

    private static T CaptureThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { action().GetAwaiter().GetResult(); }
        catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static DateTimeOffset Timestamp(int seconds) =>
        DateTimeOffset.UnixEpoch.AddSeconds(seconds).AddTicks(10);

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:x12}");

    private static readonly Guid Project = Id(1);
    private static readonly Guid OtherProject = Id(2);
    private static readonly Guid Plan = Id(3);
    private static readonly Guid Operation = Id(4);
    private static readonly Guid Actor = Id(5);
    private static readonly Guid OtherActor = Id(6);
    private static readonly Guid Base = Id(7);
    private static readonly Guid Land = Id(8);
    private static readonly Guid Requirements = Id(9);
    private static readonly Guid CommandId = Id(10);
    private static readonly Guid Floor = Id(11);
    private static readonly Guid Target = Id(12);
    private static readonly Guid Candidate = Id(13);
    private static readonly Guid ValidationId = Id(14);
    private static readonly Guid Revision = Id(15);
    private static readonly Guid Confirmation = Id(16);
    private static readonly Guid OtherPlan = Id(17);
    private static readonly Guid OtherOperation = Id(18);
    private static readonly Guid OtherBase = Id(19);
    private static readonly Guid OtherLand = Id(20);
    private static readonly Guid OtherRequirements = Id(21);
    private static readonly Guid OtherCandidate = Id(22);
    private static readonly Guid OtherValidationId = Id(23);
    private static readonly Guid OtherRevision = Id(24);

    private static readonly ProvisionalMutationOperation[] AllOperations =
    [
        ProvisionalMutationOperation.CandidateCreate,
        ProvisionalMutationOperation.CandidateApply,
        ProvisionalMutationOperation.CandidateReject,
        ProvisionalMutationOperation.CandidateExpire,
        ProvisionalMutationOperation.PlanRevisionAppend,
        ProvisionalMutationOperation.FinalPlanConfirmation,
    ];

    private sealed record Observation(
        AtomicMutationResult Result,
        object? PrimaryPayload,
        object? SecondaryPayload = null)
    {
        public bool HasProtectedPayload => PrimaryPayload is not null || SecondaryPayload is not null;
    }

    private sealed record OperationCase(
        string Name,
        ProvisionalMutationOperation Operation,
        object Request,
        MutationIdentity Mutation,
        Guid DurableOutcomeId,
        Action<Observation> CheckProtectedPayload,
        Func<AtomicPlanMutationConsumer, CancellationToken, Task<Observation>> Invoke);

    private enum AtomicResponseMode
    {
        Failure,
        Succeeded,
        MatchingDuplicate,
        OutcomeUnknown,
    }

    private enum ConfirmationResponseVariant
    {
        Project,
        Plan,
        ExactRevision,
        GeometryHash,
        ValidationEvidence,
        WarningAcknowledgement,
        AuthorizationVersion,
    }

    private sealed class CurrentUserDouble(Guid? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId.HasValue;
        public Guid? UserId => userId;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public int RequiredUserCalls { get; private set; }

        public Guid GetRequiredUserId()
        {
            RequiredUserCalls++;
            return userId ?? throw new UnauthenticatedAccessException();
        }
    }

    private sealed class ProjectAuthorizationPortDouble(ProjectAuthorizationResult? result)
        : IProjectAuthorizationPort
    {
        public int Calls { get; private set; }
        public Guid LastProjectId { get; private set; }
        public Guid LastActorId { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public bool CancelDuringCall { get; set; }
        public CancellationTokenSource? CancelSource { get; set; }

        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
            Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken)
        {
            Calls++;
            LastProjectId = projectId;
            LastActorId = authenticatedActorId;
            LastToken = cancellationToken;
            if (CancelDuringCall)
            {
                CancelSource!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(result!);
        }
    }

    private sealed class AtomicMutationPortDouble : IAtomicPlanMutationPort
    {
        private readonly Dictionary<ProvisionalMutationOperation, int> _calls = [];

        public AtomicMutationStatus Status { get; set; } = AtomicMutationStatus.PersistenceFailure;
        public AtomicResponseMode ResponseMode { get; set; }
        public bool ReturnForeignBinding { get; set; }
        public ConfirmationResponseVariant? ConfirmationResponseVariant { get; set; }
        public bool ReturnNullResponse { get; set; }
        public bool CancelBeforeFirstWrite { get; set; }
        public CancellationTokenSource? CancelSource { get; set; }
        public int TotalMutationCalls { get; private set; }
        public int ReconcileCalls { get; private set; }
        public int RetryCalls { get; private set; }
        public int WriteAttempts { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public object? LastRequest { get; private set; }
        public AtomicMutationScope? LastScope { get; private set; }
        public MutationIdentity? LastMutation { get; private set; }
        public AtomicMutationScope? LastProducedConfirmationScope { get; private set; }
        public object? LastProducedPrimaryPayload { get; private set; }
        public object? LastProducedSecondaryPayload { get; private set; }

        public int Calls(ProvisionalMutationOperation operation) =>
            _calls.TryGetValue(operation, out var count) ? count : 0;

        public Task<CandidateCreateMutationResponse> CreateCandidateAsync(
            CandidateCreateMutationRequest request, CancellationToken cancellationToken) =>
            Execute(request, request.Scope, request.Mutation, cancellationToken, () =>
            {
                var bound = ReturnForeignBinding
                    ? CreateRequest(OtherActor, request.Scope.AuthorizationVersion.Value)
                    : request;
                var candidate = PreparedCandidate(bound);
                LastProducedPrimaryPayload = candidate;
                LastProducedSecondaryPayload = null;
                return ResponseMode switch
                {
                    AtomicResponseMode.Succeeded => CandidateCreateMutationResponse.Succeeded(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, candidate.CandidateId,
                            AtomicMutationStatus.Succeeded), candidate),
                    AtomicResponseMode.MatchingDuplicate => CandidateCreateMutationResponse.MatchingDuplicate(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, candidate.CandidateId,
                            AtomicMutationStatus.MatchingDuplicate), candidate),
                    AtomicResponseMode.OutcomeUnknown => CandidateCreateMutationResponse.OutcomeUnknown(
                        bound, UnknownResult(bound.Scope, bound.Mutation)),
                    _ => CandidateCreateMutationResponse.Failed(new AtomicMutationResult(Status)),
                };
            });

        public Task<CandidateApplyMutationResponse> ApplyCandidateAsync(
            CandidateApplyMutationRequest request, CancellationToken cancellationToken) =>
            Execute(request, request.Scope, request.Mutation, cancellationToken, () =>
            {
                var bound = ReturnForeignBinding
                    ? ForeignApplyRequest(request.Scope.AuthorizationVersion.Value)
                    : request;
                var candidate = CandidatePayload(
                    bound.Scope, bound.CandidateId, CandidatePreviewStatus.Applied,
                    bound.CandidateGeometryHash, bound.ValidationEvidenceId);
                var revision = RevisionPayload(
                    bound.Scope, bound.PreparedRevision,
                    ReturnForeignBinding ? OtherRevision : Revision);
                LastProducedPrimaryPayload = candidate;
                LastProducedSecondaryPayload = revision;
                return ResponseMode switch
                {
                    AtomicResponseMode.Succeeded => CandidateApplyMutationResponse.Succeeded(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, revision.RevisionId,
                            AtomicMutationStatus.Succeeded), candidate, revision),
                    AtomicResponseMode.MatchingDuplicate => CandidateApplyMutationResponse.MatchingDuplicate(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, revision.RevisionId,
                            AtomicMutationStatus.MatchingDuplicate), candidate, revision),
                    AtomicResponseMode.OutcomeUnknown => CandidateApplyMutationResponse.OutcomeUnknown(
                        bound, UnknownResult(bound.Scope, bound.Mutation)),
                    _ => CandidateApplyMutationResponse.Failed(new AtomicMutationResult(Status)),
                };
            });

        public Task<CandidateStateMutationResponse> RejectCandidateAsync(
            CandidateRejectMutationRequest request, CancellationToken cancellationToken) =>
            Execute(request, request.Scope, request.Mutation, cancellationToken, () =>
            {
                var bound = ReturnForeignBinding
                    ? ForeignRejectRequest(request.Scope.AuthorizationVersion.Value)
                    : request;
                var candidate = CandidatePayload(
                    bound.Scope, bound.CandidateId, CandidatePreviewStatus.Rejected,
                    ReturnForeignBinding
                        ? Geometry(OtherProject, OtherPlan, OtherLand, OtherRequirements).GeometryHash
                        : Geometry().GeometryHash,
                    ReturnForeignBinding ? OtherValidationId : ValidationId);
                LastProducedPrimaryPayload = candidate;
                LastProducedSecondaryPayload = null;
                return ResponseMode switch
                {
                    AtomicResponseMode.Succeeded => CandidateStateMutationResponse.Succeeded(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, candidate.CandidateId,
                            AtomicMutationStatus.Succeeded), candidate),
                    AtomicResponseMode.MatchingDuplicate => CandidateStateMutationResponse.MatchingDuplicate(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, candidate.CandidateId,
                            AtomicMutationStatus.MatchingDuplicate), candidate),
                    AtomicResponseMode.OutcomeUnknown => CandidateStateMutationResponse.OutcomeUnknown(
                        bound, UnknownResult(bound.Scope, bound.Mutation)),
                    _ => CandidateStateMutationResponse.Failed(new AtomicMutationResult(Status)),
                };
            });

        public Task<CandidateStateMutationResponse> ExpireCandidateAsUserAsync(
            CandidateExpireMutationRequest request, CancellationToken cancellationToken) =>
            Execute(request, request.Scope, request.Mutation, cancellationToken, () =>
            {
                var bound = ReturnForeignBinding
                    ? ForeignExpireRequest(request.Scope.AuthorizationVersion.Value)
                    : request;
                var candidate = CandidatePayload(
                    bound.Scope, bound.CandidateId, CandidatePreviewStatus.Expired,
                    ReturnForeignBinding
                        ? Geometry(OtherProject, OtherPlan, OtherLand, OtherRequirements).GeometryHash
                        : Geometry().GeometryHash,
                    ReturnForeignBinding ? OtherValidationId : ValidationId);
                LastProducedPrimaryPayload = candidate;
                LastProducedSecondaryPayload = null;
                return ResponseMode switch
                {
                    AtomicResponseMode.Succeeded => CandidateStateMutationResponse.Succeeded(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, candidate.CandidateId,
                            AtomicMutationStatus.Succeeded), candidate),
                    AtomicResponseMode.MatchingDuplicate => CandidateStateMutationResponse.MatchingDuplicate(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, candidate.CandidateId,
                            AtomicMutationStatus.MatchingDuplicate), candidate),
                    AtomicResponseMode.OutcomeUnknown => CandidateStateMutationResponse.OutcomeUnknown(
                        bound, UnknownResult(bound.Scope, bound.Mutation)),
                    _ => CandidateStateMutationResponse.Failed(new AtomicMutationResult(Status)),
                };
            });

        public Task<PlanRevisionMutationResponse> AppendPlanRevisionAsync(
            PlanRevisionAppendMutationRequest request, CancellationToken cancellationToken) =>
            Execute(request, request.Scope, request.Mutation, cancellationToken, () =>
            {
                var bound = ReturnForeignBinding
                    ? ForeignAppendRequest(request.Scope.AuthorizationVersion.Value)
                    : request;
                var revision = RevisionPayload(
                    bound.Scope, bound.PreparedRevision,
                    ReturnForeignBinding ? OtherRevision : Revision);
                LastProducedPrimaryPayload = revision;
                LastProducedSecondaryPayload = null;
                return ResponseMode switch
                {
                    AtomicResponseMode.Succeeded => PlanRevisionMutationResponse.Succeeded(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, revision.RevisionId,
                            AtomicMutationStatus.Succeeded), revision),
                    AtomicResponseMode.MatchingDuplicate => PlanRevisionMutationResponse.MatchingDuplicate(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, revision.RevisionId,
                            AtomicMutationStatus.MatchingDuplicate), revision),
                    AtomicResponseMode.OutcomeUnknown => PlanRevisionMutationResponse.OutcomeUnknown(
                        bound, UnknownResult(bound.Scope, bound.Mutation)),
                    _ => PlanRevisionMutationResponse.Failed(new AtomicMutationResult(Status)),
                };
            });

        public Task<FinalConfirmationMutationResponse> ConfirmFinalPlanAsync(
            FinalPlanConfirmationMutationRequest request, CancellationToken cancellationToken) =>
            Execute(request, request.Scope, request.Mutation, cancellationToken, () =>
            {
                var bound = ReturnForeignBinding
                    ? ConfirmRequest(OtherActor, request.Scope.AuthorizationVersion.Value)
                    : ConfirmationResponseVariant is { } variant
                        ? VariantConfirmationRequest(
                            variant, request.Scope.AuthorizationVersion.Value)
                        : request;
                var confirmation = ConfirmationPayload(bound);
                LastProducedConfirmationScope = bound.Scope;
                LastProducedPrimaryPayload = confirmation;
                LastProducedSecondaryPayload = null;
                return ResponseMode switch
                {
                    AtomicResponseMode.Succeeded => FinalConfirmationMutationResponse.Succeeded(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, confirmation.ConfirmationId,
                            AtomicMutationStatus.Succeeded), confirmation),
                    AtomicResponseMode.MatchingDuplicate => FinalConfirmationMutationResponse.MatchingDuplicate(
                        bound, SuccessfulResult(
                            bound.Scope, bound.Mutation, confirmation.ConfirmationId,
                            AtomicMutationStatus.MatchingDuplicate), confirmation),
                    AtomicResponseMode.OutcomeUnknown => FinalConfirmationMutationResponse.OutcomeUnknown(
                        bound, UnknownResult(bound.Scope, bound.Mutation)),
                    _ => FinalConfirmationMutationResponse.Failed(new AtomicMutationResult(Status)),
                };
            });

        public Task<ReconciliationResult> ReconcileAsync(
            AtomicMutationReconciliationRequest request, CancellationToken cancellationToken)
        {
            ReconcileCalls++;
            throw new InvalidOperationException("Automatic reconciliation is forbidden in this test double.");
        }

        private Task<T> Execute<T>(
            object request,
            AtomicMutationScope scope,
            MutationIdentity mutation,
            CancellationToken cancellationToken,
            Func<T> response)
            where T : class
        {
            TotalMutationCalls++;
            LastToken = cancellationToken;
            LastRequest = request;
            LastScope = scope;
            LastMutation = mutation;
            _calls[scope.Operation] = Calls(scope.Operation) + 1;

            if (CancelBeforeFirstWrite)
            {
                CancelSource!.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            WriteAttempts++;

            if (ReturnNullResponse)
                return Task.FromResult<T>(null!);

            return Task.FromResult(response());
        }
    }
}

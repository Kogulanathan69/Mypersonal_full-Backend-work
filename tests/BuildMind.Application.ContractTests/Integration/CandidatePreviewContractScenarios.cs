using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

internal static class CandidatePreviewContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("candidate preview creates bound immutable pending candidate", () =>
        {
            var scenario = new Scenario();
            var result = scenario.Preview();
            Check(result.Status == CandidatePreviewResultStatus.Created, "Candidate was not created.");
            Check(result.Candidate is { Status: CandidatePreviewStatus.Pending }, "Candidate was not pending.");
            var candidate = result.Candidate ?? throw new Exception("Candidate payload was missing.");
            Check(candidate.OwnerId == scenario.OwnerId, "Trusted owner binding was not preserved.");
            Check(candidate.GeometryHash == PlanRevisionService.ComputeGeometryHash(candidate.GeometryJson),
                "Candidate hash was not bound to its snapshot.");
        });

        run("candidate preview leaves original revision unchanged", () =>
        {
            var scenario = new Scenario();
            var originalJson = scenario.Persistence.CurrentRevision.GeometryJson;
            scenario.Preview();
            Check(scenario.Persistence.RevisionCount == 1, "Preview created a saved revision.");
            Check(scenario.Persistence.CurrentRevision.GeometryJson == originalJson, "Preview mutated saved geometry.");
        });

        run("candidate apply creates exactly one immutable revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            var result = scenario.Apply(candidate);
            Check(result.Status == CandidateActionStatus.Applied, "Candidate was not applied.");
            Check(scenario.Persistence.RevisionCount == 2, "Apply did not create exactly one revision.");
            Check(result.Revision?.ParentRevisionId == candidate.BaseRevisionId, "Revision parent was not bound.");
            Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Applied,
                "Candidate was not consumed.");
        });

        run("candidate reject is terminal and creates no revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            var rejected = scenario.Reject(candidate);
            var apply = scenario.Apply(candidate);
            Check(rejected.Status == CandidateActionStatus.Rejected, "Candidate was not rejected.");
            Check(apply.Status == CandidateActionStatus.AlreadyRejected, "Rejected candidate was reusable.");
            Check(scenario.Persistence.RevisionCount == 1, "Reject created a revision.");
        });

        run("candidate preview and action reject unauthorized access", () =>
        {
            var scenario = new Scenario();
            Check(scenario.Preview(Guid.NewGuid()).Status == CandidatePreviewResultStatus.Unauthorized,
                "Unauthorized preview was allowed.");
            var candidate = scenario.Preview().Candidate!;
            Check(scenario.Apply(candidate, Guid.NewGuid()).Status == CandidateActionStatus.Unauthorized,
                "Unauthorized candidate apply was allowed.");
        });

        run("candidate rejects cross-project and cross-plan use", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            Check(scenario.Service.ApplyAsync(
                new(candidate.CandidateId, Guid.NewGuid(), candidate.PlanId, true), scenario.OwnerId).Result.Status
                == CandidateActionStatus.Unauthorized, "Cross-project candidate was accepted.");
            Check(scenario.Service.ApplyAsync(
                new(candidate.CandidateId, candidate.ProjectId, Guid.NewGuid(), true), scenario.OwnerId).Result.Status
                == CandidateActionStatus.NotFound, "Cross-plan candidate existence was disclosed.");
        });

        run("candidate missing lookup fails closed", () =>
        {
            var scenario = new Scenario();
            var result = scenario.Service.ApplyAsync(
                new(Guid.NewGuid(), scenario.ProjectId, scenario.PlanId, true), scenario.OwnerId).Result;
            Check(result.Status == CandidateActionStatus.NotFound, "Missing candidate was fabricated.");
        });

        run("candidate expiry boundary is terminal", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Clock.Advance(scenario.Lifetime);
            var result = scenario.Apply(candidate);
            Check(result.Status == CandidateActionStatus.Expired, "Expiry boundary remained usable.");
            Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Expired,
                "Expired status was not persisted by the transaction double.");
            Check(scenario.Persistence.ExpireCalls == 1 && scenario.Persistence.ApplyCalls == 0 &&
                scenario.Persistence.RevisionCount == 1, "Expiration boundary produced incorrect side effects.");
        });

        run("trusted facade authenticated apply creates one revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            var actor = new TrustedActionCurrentUserDouble(scenario.OwnerId);
            var facade = new TrustedCandidateActionFacade(actor, scenario.Service);

            var result = facade.ApplyAsync(new ApplyCandidateCommand(
                candidate.CandidateId, scenario.ProjectId, scenario.PlanId, true,
                "trusted facade apply")).GetAwaiter().GetResult();

            Check(result.Status == CandidateActionStatus.Applied,
                "Trusted facade apply did not succeed.");
            Check(result.Revision is not null,
                "Trusted facade apply returned no revision.");
            Check(scenario.Persistence.RevisionCount == 2,
                "Trusted facade apply did not create exactly one revision.");
            Check(scenario.Persistence.ApplyCalls == 1,
                "Trusted facade apply did not use the atomic transaction.");
            Check(actor.RequiredUserCalls == 1,
                "Trusted facade apply did not resolve actor exactly once.");
        });

        run("trusted facade authenticated reject creates no revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            var actor = new TrustedActionCurrentUserDouble(scenario.OwnerId);
            var facade = new TrustedCandidateActionFacade(actor, scenario.Service);

            var result = facade.RejectAsync(new CandidateActionCommand(
                candidate.CandidateId, scenario.ProjectId, scenario.PlanId))
                .GetAwaiter().GetResult();

            Check(result.Status == CandidateActionStatus.Rejected,
                "Trusted facade reject did not succeed.");
            Check(scenario.Persistence.RevisionCount == 1,
                "Trusted facade reject created an unexpected revision.");
            Check(scenario.Persistence.RejectCalls == 1,
                "Trusted facade reject did not use the atomic transaction.");
            Check(actor.RequiredUserCalls == 1,
                "Trusted facade reject did not resolve actor exactly once.");
        });

        run("trusted facade wrong actor cannot apply or reject", () =>
        {
            var applyScenario = new Scenario();
            var applyCandidate = applyScenario.Preview().Candidate!;
            var wrongApplyActor = new TrustedActionCurrentUserDouble(Guid.NewGuid());
            var applyFacade = new TrustedCandidateActionFacade(
                wrongApplyActor, applyScenario.Service);

            var apply = applyFacade.ApplyAsync(new ApplyCandidateCommand(
                applyCandidate.CandidateId, applyScenario.ProjectId,
                applyScenario.PlanId, true)).GetAwaiter().GetResult();

            Check(apply.Status == CandidateActionStatus.Unauthorized,
                "Wrong actor was allowed to apply.");
            Check(applyScenario.Persistence.RevisionCount == 1 &&
                  applyScenario.Persistence.ApplyCalls == 0,
                "Wrong actor reached apply mutation.");

            var rejectScenario = new Scenario();
            var rejectCandidate = rejectScenario.Preview().Candidate!;
            var wrongRejectActor = new TrustedActionCurrentUserDouble(Guid.NewGuid());
            var rejectFacade = new TrustedCandidateActionFacade(
                wrongRejectActor, rejectScenario.Service);

            var reject = rejectFacade.RejectAsync(new CandidateActionCommand(
                rejectCandidate.CandidateId, rejectScenario.ProjectId,
                rejectScenario.PlanId)).GetAwaiter().GetResult();

            Check(reject.Status == CandidateActionStatus.Unauthorized,
                "Wrong actor was allowed to reject.");
            Check(rejectScenario.Persistence.RevisionCount == 1 &&
                  rejectScenario.Persistence.RejectCalls == 0,
                "Wrong actor reached reject mutation.");
        });
        run("candidate duplicate apply is idempotently rejected", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.Applied, "First apply failed.");
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.AlreadyApplied, "Duplicate apply was accepted.");
            Check(scenario.Persistence.RevisionCount == 2, "Duplicate apply created another revision.");
        });

        run("candidate repeated reject reports terminal status", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            Check(scenario.Reject(candidate).Status == CandidateActionStatus.Rejected, "First reject failed.");
            Check(scenario.Reject(candidate).Status == CandidateActionStatus.AlreadyRejected,
                "Repeated reject did not preserve terminal status.");
        });

        run("candidate apply rejects stale base revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.AdvanceCurrentRevision();
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.StaleBaseRevision,
                "Stale candidate overwrote a newer revision.");
        });

        run("candidate apply rejects changed land revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.CurrentLandRevisionId = Guid.NewGuid();
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.StaleInputs,
                "Changed land revision was ignored.");
        });

        run("candidate apply rejects changed requirements revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.CurrentRequirementsRevisionId = Guid.NewGuid();
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.StaleInputs,
                "Changed requirement revision was ignored.");
        });

        run("candidate apply detects geometry hash mismatch", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.Replace(candidate with { GeometryHash = "sha256:tampered" });
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.GeometryTampered,
                "Tampered geometry hash was accepted.");
        });

        run("candidate apply detects tampered geometry snapshot", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.Replace(candidate with { GeometryJson = "{\"schemaVersion\":\"tampered\"}" });
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.GeometryTampered,
                "Tampered geometry snapshot was accepted.");
        });

        run("candidate preview fails closed when validation is unavailable", () =>
        {
            var scenario = new Scenario { ValidationStatus = ValidationEvidenceStatus.Unavailable };
            Check(scenario.Preview().Status == CandidatePreviewResultStatus.ValidationUnavailable,
                "Unavailable validation produced a candidate.");
            Check(scenario.Persistence.CandidateCount == 0, "Unavailable validation persisted a candidate.");
        });

        run("candidate preview rejects invalid validation evidence", () =>
        {
            var scenario = new Scenario { ReturnMismatchedEvidenceHash = true };
            Check(scenario.Preview().Status == CandidatePreviewResultStatus.ValidationRejected,
                "Mismatched evidence produced a candidate.");
        });

        run("candidate preview rejects blocking validation issues", () =>
        {
            var scenario = new Scenario { HasBlockingIssues = true };
            Check(scenario.Preview().Status == CandidatePreviewResultStatus.ValidationRejected,
                "Blocking evidence produced a candidate.");
        });

        run("candidate apply requires warning acknowledgement", () =>
        {
            var scenario = new Scenario { HasWarnings = true };
            var candidate = scenario.Preview().Candidate!;
            Check(scenario.Apply(candidate, acknowledgeWarnings: false).Status
                == CandidateActionStatus.WarningAcknowledgementRequired, "Warnings were silently accepted.");
            Check(scenario.Apply(candidate, acknowledgeWarnings: true).Status == CandidateActionStatus.Applied,
                "Acknowledged warning candidate was not applied.");
        });

        run("candidate concurrent apply creates one revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            var results = Task.WhenAll(
                scenario.Service.ApplyAsync(new(candidate.CandidateId, candidate.ProjectId, candidate.PlanId, true), scenario.OwnerId),
                scenario.Service.ApplyAsync(new(candidate.CandidateId, candidate.ProjectId, candidate.PlanId, true), scenario.OwnerId)).Result;
            Check(results.Count(result => result.Status == CandidateActionStatus.Applied) == 1,
                "Concurrent apply did not have exactly one winner.");
            Check(results.Count(result => result.Status == CandidateActionStatus.AlreadyApplied) == 1,
                "Concurrent replay did not observe consumed state.");
            Check(scenario.Persistence.RevisionCount == 2, "Concurrent apply created multiple revisions.");
        });

        run("candidate apply versus reject race has one terminal winner", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            var apply = Task.Run(() => scenario.Apply(candidate));
            var reject = Task.Run(() => scenario.Reject(candidate));
            Task.WaitAll(apply, reject);
            var statuses = new[] { apply.Result.Status, reject.Result.Status };
            Check(statuses.Count(status => status is CandidateActionStatus.Applied or CandidateActionStatus.Rejected) == 1,
                "Apply/reject race did not have exactly one winner.");
            Check(scenario.Persistence.RevisionCount is 1 or 2, "Race produced an impossible revision count.");
            Check(scenario.Persistence.RevisionCount == (statuses.Contains(CandidateActionStatus.Applied) ? 2 : 1),
                "Revision count does not match the race winner.");
        });

        run("candidate transaction failure rolls back candidate and revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.ReturnConfirmedRollback = true;
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.PersistenceFailure,
                "Confirmed rollback was not preserved.");
            Check(scenario.Persistence.RevisionCount == 1, "Failed transaction appended a revision.");
            Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Pending,
                "Failed transaction consumed the candidate.");
        });

        run("candidate preview rejects stale requested base", () =>
        {
            var scenario = new Scenario();
            Check(scenario.Preview(baseRevisionId: Guid.NewGuid()).Status
                == CandidatePreviewResultStatus.StaleBaseRevision, "Stale preview base was accepted.");
        });

        run("candidate preview rejects missing land or requirement dependency", () =>
        {
            var missingLand = new Scenario { LandAvailable = false };
            var missingRequirements = new Scenario { RequirementsAvailable = false };
            Check(missingLand.Preview().Status == CandidatePreviewResultStatus.DependencyUnavailable,
                "Missing land dependency produced a candidate.");
            Check(missingRequirements.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                "Missing referenced requirement revision was not treated as an integrity failure.");
        });

        run("candidate preview uses exact authorization-aware requirement identities", () =>
        {
            var scenario = new Scenario();
            var result = scenario.Preview();

            Check(result.Status == CandidatePreviewResultStatus.Created,
                "Authorized exact requirement access did not create a candidate.");
            Check(scenario.RequirementCalls == 1 &&
                  scenario.LastRequirementProjectId == scenario.ProjectId &&
                  scenario.LastRequirementRevisionId == scenario.CurrentRequirementsRevisionId &&
                  scenario.LastRequirementActorId == scenario.OwnerId,
                "Requirement access did not receive the exact trusted identities.");
        });

        run("candidate preview distinguishes requirement denial and authorized absence", () =>
        {
            var denied = new Scenario { RequirementAccessStatus = RequirementRevisionAccessStatus.Unauthorized };
            var absent = new Scenario { RequirementsAvailable = false };
            var deniedResult = denied.Preview();
            var absentResult = absent.Preview();
            var deniedDiagnostic = PlanEditorFailureMapper.FromCandidatePreview(deniedResult.Status);
            var absentDiagnostic = PlanEditorFailureMapper.FromCandidatePreview(absentResult.Status);

            Check(deniedResult.Status == CandidatePreviewResultStatus.Unauthorized,
                "Requirement authorization denial was not preserved.");
            Check(absentResult.Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                "Authorized requirement absence was not treated as an integrity failure.");
            Check(absentDiagnostic is
                    {
                        Code: PlanEditorErrorCodes.InvalidDependencyResponse,
                        RetrySafety: PlanEditorRetrySafety.Never
                    },
                "Authorized requirement absence was incorrectly classified as retryable.");
            Check(!deniedDiagnostic.SafeMessage.Contains(denied.ProjectId.ToString(), StringComparison.Ordinal) &&
                  !absentDiagnostic.SafeMessage.Contains(absent.ProjectId.ToString(), StringComparison.Ordinal) &&
                  !absentDiagnostic.SafeMessage.Contains(
                      absent.CurrentRequirementsRevisionId.ToString(), StringComparison.Ordinal),
                "Requirement access diagnostics disclosed protected identifiers.");
            Check(NoRequirementDownstreamEffects(denied) && NoRequirementDownstreamEffects(absent),
                "Denied or absent requirement access reached a downstream operation.");
        });

        run("candidate preview rejects a null authorization-aware dependency result", () =>
        {
            var scenario = new Scenario { ReturnNullRequirementAccessResult = true };

            Check(scenario.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                "A null authorization-aware result was not rejected.");
            Check(NoRequirementDownstreamEffects(scenario),
                "A null authorization-aware result reached a downstream operation.");
        });

        run("candidate preview rejects mismatched authorization on authorized absence", () =>
        {
            foreach (var mode in new[] { "actor", "project" })
            {
                var scenario = new Scenario
                {
                    RequirementAccessStatus = RequirementRevisionAccessStatus.NotFound
                };
                if (mode == "actor") scenario.RequirementAuthorizationActorId = Guid.NewGuid();
                if (mode == "project") scenario.RequirementAuthorizationProjectId = Guid.NewGuid();

                Check(scenario.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                    $"Authorized absence with a contradictory {mode} binding was accepted.");
                Check(NoRequirementDownstreamEffects(scenario),
                    "Contradictory authorized absence reached a downstream operation.");
            }
        });

        run("candidate preview contains representable malformed authorization evidence", () =>
        {
            var scenario = new Scenario
            {
                RequirementAccessStatus = RequirementRevisionAccessStatus.InvalidDependencyResponse,
                IncludePriorAuthorizationOnInvalidResponse = true,
                RequirementAuthorizationActorId = Guid.NewGuid()
            };

            Check(scenario.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                "Explicitly invalid dependency evidence was accepted.");
            Check(NoRequirementDownstreamEffects(scenario),
                "Explicitly invalid dependency evidence reached a downstream operation.");
        });

        run("candidate preview rejects contradictory authorization and requirement bindings", () =>
        {
            foreach (var mode in new[] { "actor", "project", "revision", "dependency" })
            {
                var scenario = new Scenario();
                if (mode == "actor") scenario.RequirementAuthorizationActorId = Guid.NewGuid();
                if (mode == "project") scenario.RequirementAuthorizationProjectId = Guid.NewGuid();
                if (mode == "revision") scenario.RequirementRevisionId = Guid.NewGuid();
                if (mode == "dependency")
                    scenario.RequirementAccessStatus = RequirementRevisionAccessStatus.InvalidDependencyResponse;

                Check(scenario.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                    $"Contradictory requirement {mode} binding was accepted.");
                Check(NoRequirementDownstreamEffects(scenario),
                    "A contradictory authorization-aware response reached a downstream operation.");
            }
        });

        run("candidate preview preserves requirement dependency failure distinctions", () =>
        {
            foreach (var item in new (Exception Error, CandidatePreviewResultStatus Status)[]
                     {
                         (new TimeoutException("private timeout"), CandidatePreviewResultStatus.DependencyTimeout),
                         (new HttpRequestException("private transport"), CandidatePreviewResultStatus.DependencyUnavailable),
                         (new InvalidOperationException("private defect"),
                             CandidatePreviewResultStatus.UnexpectedInternalFailure)
                     })
            {
                var scenario = new Scenario { RequirementException = item.Error };
                Check(scenario.Preview().Status == item.Status,
                    $"Requirement {item.Error.GetType().Name} was misclassified.");
                Check(NoRequirementDownstreamEffects(scenario),
                    "Requirement failure reached a downstream operation.");
            }
        });

        run("candidate preview requirement failures preserve approved retry classifications", () =>
        {
            foreach (var item in new[]
                     {
                         (Status: CandidatePreviewResultStatus.DependencyUnavailable,
                             Code: PlanEditorErrorCodes.DependencyUnavailable,
                             Retry: PlanEditorRetrySafety.Safe),
                         (Status: CandidatePreviewResultStatus.DependencyTimeout,
                             Code: PlanEditorErrorCodes.DependencyTimeout,
                             Retry: PlanEditorRetrySafety.Safe),
                         (Status: CandidatePreviewResultStatus.InvalidDependencyResponse,
                             Code: PlanEditorErrorCodes.InvalidDependencyResponse,
                             Retry: PlanEditorRetrySafety.Never)
                     })
            {
                var diagnostic = PlanEditorFailureMapper.FromCandidatePreview(item.Status);
                Check(diagnostic.Code == item.Code && diagnostic.RetrySafety == item.Retry,
                    $"{item.Status} did not preserve its approved retry classification.");
                Check(!diagnostic.SafeMessage.Contains("requirement", StringComparison.OrdinalIgnoreCase) &&
                      !diagnostic.SafeMessage.Contains("revision", StringComparison.OrdinalIgnoreCase),
                    $"{item.Status} disclosed protected requirement details.");
            }
        });

        run("candidate preview propagates cancellation during authorized requirement access", () =>
        {
            using var source = new CancellationTokenSource();
            var scenario = new Scenario { DuringRequirementAccess = source.Cancel };

            CheckThrows<OperationCanceledException>(() => scenario.PreviewWithCancellation(source.Token));
            Check(scenario.RequirementCalls == 1 && NoRequirementDownstreamEffects(scenario),
                "Requirement cancellation did not stop the preview at the secure boundary.");
        });

        run("candidate preview has no legacy actorless requirement fallback", () =>
        {
            var parameterTypes = typeof(CandidatePreviewService).GetConstructors().Single()
                .GetParameters().Select(parameter => parameter.ParameterType).ToArray();

            Check(parameterTypes.Contains(typeof(IAuthorizedRequirementRevisionPort)),
                "Candidate preview lost its authorization-aware requirement dependency.");
            Check(!parameterTypes.Contains(typeof(IRequirementRevisionPort)),
                "Candidate preview retained a legacy actorless requirement fallback.");
        });

        run("candidate preview rejects invalid command", () =>
        {
            var scenario = new Scenario();
            using var payload = JsonDocument.Parse("{}");
            var invalid = new PlanEditCommandDto
            {
                CommandId = Guid.NewGuid(), CommandType = PlanEditCommandTypes.MoveRoom,
                FloorId = Guid.NewGuid(), TargetId = Guid.NewGuid(), Payload = payload.RootElement.Clone()
            };
            Check(scenario.Preview(command: invalid).Status == CandidatePreviewResultStatus.InvalidCommand,
                "Invalid command reached compute dependency.");
        });

        run("candidate preview rejects wrongly scoped and wrong revision repository responses before compute", () =>
        {
            foreach (var mode in new[] { "project", "plan", "revision" })
            {
                var scenario = new Scenario();
                scenario.Persistence.CurrentResponseTransform = (_, current) => mode switch
                {
                    "project" => current with { ProjectId = Guid.NewGuid() },
                    "plan" => current with { PlanId = Guid.NewGuid() },
                    _ => current with { RevisionId = Guid.NewGuid() }
                };
                var result = scenario.Preview();
                Check(result.Status == (mode == "revision"
                    ? CandidatePreviewResultStatus.StaleBaseRevision
                    : CandidatePreviewResultStatus.InvalidDependencyResponse),
                    $"Wrong {mode} repository response was accepted.");
                Check(scenario.ComputeCalls == 0 && scenario.Persistence.CandidateCount == 0,
                    "Unrelated revision geometry reached compute or persistence.");
            }
        });

        run("candidate preview rejects source geometry inconsistent with its revision", () =>
        {
            var scenario = new Scenario();
            var current = scenario.Persistence.CurrentRevision;
            var wrongJson = current.GeometryJson.Replace(
                scenario.ProjectId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
            scenario.Persistence.ReplaceCurrent(current with
            {
                GeometryJson = wrongJson,
                GeometryHash = PlanRevisionService.ComputeGeometryHash(wrongJson),
                Validation = scenario.EvidenceFor(PlanRevisionService.ComputeGeometryHash(wrongJson))
            });
            Check(scenario.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                "Incorrectly scoped source geometry reached compute.");
            Check(scenario.ComputeCalls == 0 && scenario.Persistence.CandidateCount == 0,
                "Invalid source geometry caused side effects.");
        });

        run("candidate preview revalidates ownership base and inputs after compute", () =>
        {
            foreach (var mode in new[] { "ownership", "base", "land", "requirements" })
            {
                var scenario = new Scenario();
                scenario.DuringCompute = () =>
                {
                    if (mode == "ownership") scenario.Owned = false;
                    if (mode == "base") scenario.Persistence.AdvanceCurrentRevision();
                    if (mode == "land") scenario.CurrentLandRevisionId = Guid.NewGuid();
                    if (mode == "requirements") scenario.CurrentRequirementsRevisionId = Guid.NewGuid();
                };
                var result = scenario.Preview();
                var expected = mode == "ownership" ? CandidatePreviewResultStatus.Unauthorized :
                    mode == "base" ? CandidatePreviewResultStatus.StaleBaseRevision :
                    CandidatePreviewResultStatus.StaleInputs;
                Check(result.Status == expected, $"{mode} change during compute was accepted.");
                Check(scenario.Persistence.CandidateCount == 0, "Stale context persisted a candidate.");
            }
        });

        run("candidate preview revalidates context after evidence retrieval and verification", () =>
        {
            var retrieval = new Scenario();
            retrieval.DuringEvidenceRetrieval = () => retrieval.CurrentLandRevisionId = Guid.NewGuid();
            Check(retrieval.Preview().Status == CandidatePreviewResultStatus.StaleInputs,
                "Context change during evidence retrieval was accepted.");

            var verification = new Scenario();
            verification.DuringEvidenceVerification = verification.Persistence.AdvanceCurrentRevision;
            Check(verification.Preview().Status == CandidatePreviewResultStatus.StaleBaseRevision,
                "Context change during evidence verification was accepted.");
            Check(retrieval.Persistence.CandidateCount == 0 && verification.Persistence.CandidateCount == 0,
                "Validation-time context change persisted a candidate.");
        });

        run("candidate preview rejects wrongly scoped revision during final revalidation", () =>
        {
            foreach (var wrongProject in new[] { true, false })
            {
                var scenario = new Scenario();
                scenario.Persistence.CurrentResponseTransform = (call, current) => call == 2
                    ? current with
                    {
                        ProjectId = wrongProject ? Guid.NewGuid() : current.ProjectId,
                        PlanId = wrongProject ? current.PlanId : Guid.NewGuid()
                    }
                    : current;
                Check(scenario.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                    "Wrongly scoped final revision was accepted.");
                Check(scenario.Persistence.CandidateCount == 0, "Wrongly scoped context persisted a candidate.");
            }
        });

        run("candidate apply rejects candidate identity mismatch before transaction", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.LookupOverride = candidate with { CandidateId = Guid.NewGuid() };
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.NotFound,
                "Candidate identity mismatch disclosed a protected resource.");
            Check(scenario.Persistence.ApplyCalls == 0, "Identity mismatch reached apply transaction.");
        });

        run("candidate apply rejects all geometry binding and schema mismatches", () =>
        {
            foreach (var value in new[] { "project", "plan", "land", "requirements", "schema" })
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                var replacement = value == "schema" ? "draft.invalid" : Guid.NewGuid().ToString();
                var original = value switch
                {
                    "project" => candidate.ProjectId.ToString(),
                    "plan" => candidate.PlanId.ToString(),
                    "land" => candidate.LandRevisionId.ToString(),
                    "requirements" => candidate.RequirementsRevisionId.ToString(),
                    _ => candidate.GeometrySchemaVersion
                };
                var json = candidate.GeometryJson.Replace(original, replacement, StringComparison.Ordinal);
                var hash = PlanRevisionService.ComputeGeometryHash(json);
                scenario.Persistence.Replace(candidate with
                {
                    GeometryJson = json,
                    GeometryHash = hash,
                    Validation = scenario.EvidenceFor(hash)
                });
                Check(scenario.Apply(candidate).Status == CandidateActionStatus.GeometryTampered,
                    $"Candidate geometry {value} mismatch was accepted.");
                Check(scenario.Persistence.ApplyCalls == 0, "Invalid candidate reached apply transaction.");
            }
        });

        run("candidate apply rejects validation hash mismatch", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.Replace(candidate with
            {
                Validation = scenario.EvidenceFor("sha256:not-the-candidate")
            });
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.GeometryTampered,
                "Validation evidence hash mismatch was accepted.");
            Check(scenario.Persistence.ApplyCalls == 0, "Mismatched validation reached transaction.");
        });

        run("candidate apply rejects incorrectly scoped current revision", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.CurrentResponseTransform = (_, current) => current with { ProjectId = Guid.NewGuid() };
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.InvalidDependencyResponse,
                "Incorrect current revision identity was accepted.");
            Check(scenario.Persistence.ApplyCalls == 0, "Incorrect current revision reached transaction.");
        });

        run("candidate apply rejects structurally invalid geometry without transaction", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.Replace(candidate with
            {
                GeometryJson = "{\"schemaVersion\":\"draft.invalid\"}",
                GeometryHash = PlanRevisionService.ComputeGeometryHash("{\"schemaVersion\":\"draft.invalid\"}")
            });
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.GeometryTampered,
                "Invalid candidate geometry was accepted.");
            Check(scenario.Persistence.ApplyCalls == 0 && scenario.Persistence.RevisionCount == 1,
                "Invalid geometry changed revision history.");
        });

        run("candidate creation exceptions require reconciliation without automatic retry", () =>
        {
            foreach (var mode in new[] { CandidateCreateFailure.BeforeMutation, CandidateCreateFailure.AfterStored })
            {
                var scenario = new Scenario();
                scenario.Persistence.CreateFailure = mode;
                var result = scenario.Preview();
                Check(result.Status == CandidatePreviewResultStatus.PersistenceOutcomeUnknown,
                    $"Candidate persistence {mode} was reported as an ordinary rejection.");
                Check(result.Candidate is not null, "Stable candidate reconciliation identity was lost.");
                Check(scenario.Persistence.CreateCalls == 1, "Candidate creation was automatically retried.");
                Check(scenario.Persistence.RevisionCount == 1, "Candidate failure appended a revision.");
                Check(scenario.Persistence.CandidateCount == (mode == CandidateCreateFailure.AfterStored ? 1 : 0),
                    "Candidate side effect did not match the simulated persistence outcome.");
            }
        });

        run("candidate apply timeout and lost acknowledgement require reconciliation", () =>
        {
            var timeout = new Scenario();
            var timeoutCandidate = timeout.Preview().Candidate!;
            timeout.Persistence.ApplyFailure = MutationFailure.TimeoutBeforeMutation;
            Check(timeout.Apply(timeoutCandidate).Status == CandidateActionStatus.TransactionOutcomeUnknown,
                "Apply timeout was fabricated as rollback or success.");
            Check(timeout.Persistence.ApplyCalls == 1 && timeout.Persistence.RevisionCount == 1,
                "Apply timeout was retried or changed revision history.");

            var lost = new Scenario();
            var lostCandidate = lost.Preview().Candidate!;
            lost.Persistence.ApplyFailure = MutationFailure.CommitThenAcknowledgementLost;
            Check(lost.Apply(lostCandidate).Status == CandidateActionStatus.TransactionOutcomeUnknown,
                "Lost apply acknowledgement was fabricated as success.");
            Check(lost.Persistence.ApplyCalls == 1 && lost.Persistence.RevisionCount == 2 &&
                lost.Persistence.GetCandidate(lostCandidate.CandidateId)!.Status == CandidatePreviewStatus.Applied,
                "Committed apply was not available for reconciliation.");
        });

        run("candidate reject timeout and lost acknowledgement require reconciliation", () =>
        {
            var timeout = new Scenario();
            var timeoutCandidate = timeout.Preview().Candidate!;
            timeout.Persistence.RejectFailure = MutationFailure.TimeoutBeforeMutation;
            Check(timeout.Reject(timeoutCandidate).Status == CandidateActionStatus.TransactionOutcomeUnknown,
                "Reject timeout was fabricated as rollback or success.");
            Check(timeout.Persistence.RejectCalls == 1 &&
                timeout.Persistence.GetCandidate(timeoutCandidate.CandidateId)!.Status == CandidatePreviewStatus.Pending,
                "Reject timeout was retried or consumed the candidate.");

            var lost = new Scenario();
            var lostCandidate = lost.Preview().Candidate!;
            lost.Persistence.RejectFailure = MutationFailure.CommitThenAcknowledgementLost;
            Check(lost.Reject(lostCandidate).Status == CandidateActionStatus.TransactionOutcomeUnknown,
                "Lost reject acknowledgement was fabricated as success.");
            Check(lost.Persistence.RejectCalls == 1 &&
                lost.Persistence.GetCandidate(lostCandidate.CandidateId)!.Status == CandidatePreviewStatus.Rejected,
                "Committed rejection was not available for reconciliation.");
        });

        run("candidate lookup normalizes missing foreign and defective repository responses", () =>
        {
            var missing = new Scenario();
            var missingResult = missing.Service.ApplyAsync(
                new(Guid.NewGuid(), missing.ProjectId, missing.PlanId, true), missing.OwnerId).Result;
            var missingDiagnostic = PlanEditorFailureMapper.FromCandidateAction(
                missingResult.Status, "lookup-correlation");

            foreach (var mode in new[] { "owner", "project", "plan", "identity" })
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Persistence.LookupOverride = mode switch
                {
                    "owner" => candidate with { OwnerId = Guid.NewGuid() },
                    "project" => candidate with { ProjectId = Guid.NewGuid() },
                    "plan" => candidate with { PlanId = Guid.NewGuid() },
                    _ => candidate with { CandidateId = Guid.NewGuid() }
                };
                var apply = scenario.Apply(candidate);
                var reject = scenario.Reject(candidate);
                var diagnostic = PlanEditorFailureMapper.FromCandidateAction(
                    apply.Status, "lookup-correlation");
                Check(apply.Status == missingResult.Status && reject.Status == missingResult.Status,
                    $"{mode} candidate produced a distinguishable lookup status.");
                Check(diagnostic.Code == missingDiagnostic.Code &&
                    diagnostic.SafeMessage == missingDiagnostic.SafeMessage &&
                    PlanEditorFailureMapper.ProposedHttpStatus(diagnostic) ==
                        PlanEditorFailureMapper.ProposedHttpStatus(missingDiagnostic),
                    $"{mode} candidate produced a distinguishable public diagnostic.");
                Check(apply.Candidate is null && reject.Candidate is null,
                    "Protected candidate snapshot or identifiers were returned.");
                Check(scenario.Persistence.ApplyCalls == 0 && scenario.Persistence.RejectCalls == 0 &&
                    scenario.Persistence.RevisionCount == 1,
                    "Unauthorized candidate lookup reached a mutation boundary.");
                Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Pending,
                    "Unauthorized candidate lookup changed protected state.");
            }
        });

        run("candidate lookup rejects empty actor and unauthorized project before protected read", () =>
        {
            var empty = new Scenario();
            var candidate = empty.Preview().Candidate!;
            var emptyResult = empty.Service.ApplyAsync(
                new(candidate.CandidateId, empty.ProjectId, empty.PlanId, true), Guid.Empty).Result;
            Check(emptyResult.Status == CandidateActionStatus.Unauthorized &&
                empty.Persistence.CandidateReadCalls == 0 && empty.Persistence.ApplyCalls == 0,
                "Empty actor accessed protected candidate data.");

            var unauthorized = new Scenario();
            var protectedCandidate = unauthorized.Preview().Candidate!;
            unauthorized.Owned = false;
            var apply = unauthorized.Apply(protectedCandidate);
            var reject = unauthorized.Reject(protectedCandidate);
            Check(apply.Status == CandidateActionStatus.Unauthorized &&
                reject.Status == CandidateActionStatus.Unauthorized,
                "Unauthorized project request was not rejected.");
            Check(unauthorized.Persistence.CandidateReadCalls == 0 &&
                unauthorized.Persistence.ApplyCalls == 0 && unauthorized.Persistence.RejectCalls == 0,
                "Unauthorized project request accessed or mutated a candidate.");
        });

        run("foreign terminal state and expiry remain indistinguishable from absence", () =>
        {
            foreach (var status in new[]
            {
                CandidatePreviewStatus.Pending,
                CandidatePreviewStatus.Applied,
                CandidatePreviewStatus.Rejected,
                CandidatePreviewStatus.Expired
            })
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Persistence.LookupOverride = candidate with
                {
                    OwnerId = Guid.NewGuid(),
                    Status = status,
                    ExpiresAtUtc = scenario.Clock.GetUtcNow().AddMinutes(-1)
                };
                var result = scenario.Apply(candidate);
                Check(result.Status == CandidateActionStatus.NotFound && result.Candidate is null,
                    $"Foreign candidate terminal state {status} was disclosed.");
                Check(scenario.Persistence.ApplyCalls == 0,
                    "Foreign terminal candidate reached apply transaction.");
            }
        });

        run("candidate lookup dependency outage remains distinct from confirmed absence", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.LookupException = new TimeoutException("private candidate store timeout");
            var result = scenario.Apply(candidate);
            var diagnostic = PlanEditorFailureMapper.FromCandidateAction(result.Status, "dependency-correlation");
            Check(result.Status == CandidateActionStatus.DependencyTimeout && result.Candidate is null,
                "Candidate lookup outage was represented as confirmed absence.");
            Check(diagnostic.Code == PlanEditorErrorCodes.DependencyTimeout &&
                diagnostic.CorrelationId == "dependency-correlation" &&
                !diagnostic.SafeMessage.Contains("private", StringComparison.OrdinalIgnoreCase),
                "Candidate lookup dependency diagnostic was unsafe.");
            Check(scenario.Persistence.ApplyCalls == 0, "Lookup outage reached apply transaction.");
        });

        run("candidate expiration preserves lifecycle boundaries and never appends a revision", () =>
        {
            var unexpired = new Scenario();
            var unexpiredCandidate = unexpired.Preview().Candidate!;
            Check(unexpired.Apply(unexpiredCandidate).Status == CandidateActionStatus.Applied &&
                unexpired.Persistence.ExpireCalls == 0,
                "Unexpired candidate was prematurely expired.");

            var alreadyExpired = new Scenario();
            var expiredCandidate = alreadyExpired.Preview().Candidate!;
            alreadyExpired.Persistence.Replace(expiredCandidate with
            {
                Status = CandidatePreviewStatus.Expired,
                ExpiresAtUtc = alreadyExpired.Clock.GetUtcNow().AddSeconds(-1)
            });
            Check(alreadyExpired.Apply(expiredCandidate).Status == CandidateActionStatus.Expired &&
                alreadyExpired.Persistence.ExpireCalls == 0,
                "Already expired candidate was mutated again.");

            var applied = new Scenario();
            var appliedCandidate = applied.Preview().Candidate!;
            Check(applied.Apply(appliedCandidate).Status == CandidateActionStatus.Applied, "Apply setup failed.");
            applied.Clock.Advance(applied.Lifetime);
            Check(applied.Apply(appliedCandidate).Status == CandidateActionStatus.AlreadyApplied &&
                applied.Persistence.ExpireCalls == 0, "Applied candidate transitioned to expired.");

            var rejected = new Scenario();
            var rejectedCandidate = rejected.Preview().Candidate!;
            Check(rejected.Reject(rejectedCandidate).Status == CandidateActionStatus.Rejected, "Reject setup failed.");
            rejected.Clock.Advance(rejected.Lifetime);
            Check(rejected.Apply(rejectedCandidate).Status == CandidateActionStatus.AlreadyRejected &&
                rejected.Persistence.ExpireCalls == 0, "Rejected candidate transitioned to expired.");
            Check(alreadyExpired.Persistence.RevisionCount == 1 && rejected.Persistence.RevisionCount == 1,
                "Expiration lifecycle checks changed revision history.");
        });

        run("candidate expiration exceptions require reconciliation without retry", () =>
        {
            foreach (var failure in new[]
            {
                ExpireFailure.TimeoutBeforeMutation,
                ExpireFailure.TransportBeforeMutation,
                ExpireFailure.UnexpectedBeforeMutation,
                ExpireFailure.DependencyCancellation
            })
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Clock.Advance(scenario.Lifetime);
                scenario.Persistence.ExpireFailure = failure;
                var result = scenario.Apply(candidate);
                Check(result.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                    result.Candidate?.CandidateId == candidate.CandidateId,
                    $"Expiration {failure} was fabricated as success or rollback.");
                Check(scenario.Persistence.ExpireCalls == 1 && scenario.Persistence.ApplyCalls == 0 &&
                    scenario.Persistence.RejectCalls == 0 && scenario.Persistence.RevisionCount == 1,
                    "Uncertain expiration was retried or continued into another mutation.");
                Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Pending,
                    "Pre-mutation expiration failure changed candidate state.");
            }

            var lost = new Scenario();
            var lostCandidate = lost.Preview().Candidate!;
            lost.Clock.Advance(lost.Lifetime);
            lost.Persistence.ExpireFailure = ExpireFailure.CommitThenAcknowledgementLost;
            var lostResult = lost.Apply(lostCandidate);
            Check(lostResult.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                lostResult.Candidate?.CandidateId == lostCandidate.CandidateId,
                "Lost expiration acknowledgement was fabricated as confirmed expiration.");
            Check(lost.Persistence.ExpireCalls == 1 &&
                lost.Persistence.GetCandidate(lostCandidate.CandidateId)!.Status == CandidatePreviewStatus.Expired,
                "Committed expiration was not available for reconciliation.");
        });

        run("candidate expiration preserves confirmed rollback and rejects invalid transaction responses", () =>
        {
            var rollback = new Scenario();
            var rollbackCandidate = rollback.Preview().Candidate!;
            rollback.Clock.Advance(rollback.Lifetime);
            rollback.Persistence.ReturnConfirmedExpirationRollback = true;
            Check(rollback.Apply(rollbackCandidate).Status == CandidateActionStatus.PersistenceFailure &&
                rollback.Persistence.GetCandidate(rollbackCandidate.CandidateId)!.Status == CandidatePreviewStatus.Pending,
                "Confirmed expiration rollback was not preserved.");

            var invalid = new Scenario();
            var invalidCandidate = invalid.Preview().Candidate!;
            invalid.Clock.Advance(invalid.Lifetime);
            invalid.Persistence.ExpireFailure = ExpireFailure.IncorrectResponse;
            Check(invalid.Apply(invalidCandidate).Status == CandidateActionStatus.TransactionOutcomeUnknown,
                "Malformed expiration acknowledgement did not require reconciliation.");

            var unauthorized = new Scenario();
            var unauthorizedCandidate = unauthorized.Preview().Candidate!;
            unauthorized.Clock.Advance(unauthorized.Lifetime);
            unauthorized.Persistence.ExpireFailure = ExpireFailure.UnauthorizedResponse;
            var result = unauthorized.Apply(unauthorizedCandidate);
            Check(result.Status == CandidateActionStatus.NotFound && result.Candidate is null,
                "Expiration authorization response disclosed candidate existence.");
        });

        run("candidate expiration preserves caller cancellation and revocation protection", () =>
        {
            var before = new Scenario();
            var beforeCandidate = before.Preview().Candidate!;
            before.Clock.Advance(before.Lifetime);
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                CheckThrows<OperationCanceledException>(() => before.Service.ApplyAsync(
                    new(beforeCandidate.CandidateId, before.ProjectId, before.PlanId, true),
                    before.OwnerId, cancelled.Token).GetAwaiter().GetResult());
            }
            Check(before.Persistence.ExpireCalls == 0, "Pre-mutation cancellation invoked expiration.");

            var after = new Scenario();
            var afterCandidate = after.Preview().Candidate!;
            after.Clock.Advance(after.Lifetime);
            using (var cancelled = new CancellationTokenSource())
            {
                after.Persistence.ExpireFailure = ExpireFailure.CallerCancellationAfterCommit;
                after.Persistence.AfterExpirationCommit = cancelled.Cancel;
                CheckThrows<OperationCanceledException>(() => after.Service.ApplyAsync(
                    new(afterCandidate.CandidateId, after.ProjectId, after.PlanId, true),
                    after.OwnerId, cancelled.Token).GetAwaiter().GetResult());
            }
            Check(after.Persistence.ExpireCalls == 1 &&
                after.Persistence.GetCandidate(afterCandidate.CandidateId)!.Status == CandidatePreviewStatus.Expired,
                "Post-commit caller cancellation lost reconciliation state.");

            var revoked = new Scenario();
            var revokedCandidate = revoked.Preview().Candidate!;
            revoked.Clock.Advance(revoked.Lifetime);
            revoked.Persistence.AfterCandidateRead = () => revoked.Owned = false;
            var revokedResult = revoked.Apply(revokedCandidate);
            Check(revokedResult.Status == CandidateActionStatus.NotFound && revokedResult.Candidate is null &&
                revoked.Persistence.ExpireCalls == 0,
                "Ownership revocation triggered expiration or disclosed candidate state.");
        });

        run("candidate expiration wins safely against apply reject and repeated expiration", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Clock.Advance(scenario.Lifetime);
            var apply = Task.Run(() => scenario.Apply(candidate));
            var reject = Task.Run(() => scenario.Reject(candidate));
            Task.WaitAll(apply, reject);
            Check(apply.Result.Status == CandidateActionStatus.Expired &&
                reject.Result.Status == CandidateActionStatus.Expired,
                "Expiration race produced a non-expired terminal outcome.");
            Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Expired &&
                scenario.Persistence.RevisionCount == 1 && scenario.Persistence.ApplyCalls == 0 &&
                scenario.Persistence.RejectCalls == 0,
                "Expiration race applied, rejected or appended a revision.");
            var calls = scenario.Persistence.ExpireCalls;
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.Expired &&
                scenario.Persistence.ExpireCalls == calls,
                "Repeated expiration invoked another mutation.");
        });

        run("candidate apply commit with contradictory acknowledgement requires reconciliation", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.ApplyFailure = MutationFailure.CommitThenContradictoryResponse;
            var result = scenario.Apply(candidate);
            Check(scenario.Persistence.RevisionCount == 2 &&
                scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Applied,
                "Adversarial apply did not simulate a committed mutation.");
            Check(result.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                result.Candidate?.CandidateId == candidate.CandidateId,
                "Contradictory apply acknowledgement did not require reconciliation.");
        });

        run("candidate reject commit with contradictory acknowledgement requires reconciliation", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.RejectFailure = MutationFailure.CommitThenContradictoryResponse;
            var result = scenario.Reject(candidate);
            Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Rejected,
                "Adversarial reject did not simulate a committed mutation.");
            Check(result.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                result.Candidate?.CandidateId == candidate.CandidateId,
                "Contradictory reject acknowledgement did not require reconciliation.");
        });

        run("candidate expiration commit with malformed acknowledgement requires reconciliation", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            scenario.Clock.Advance(scenario.Lifetime);
            scenario.Persistence.ExpireFailure = ExpireFailure.CommitThenIncorrectResponse;
            var result = scenario.Apply(candidate);
            Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Expired,
                "Adversarial expiration did not simulate a committed mutation.");
            Check(result.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                result.Candidate?.CandidateId == candidate.CandidateId,
                "Malformed post-commit expiration acknowledgement did not require reconciliation.");
        });

        run("candidate apply validates every required acknowledgement binding", () =>
        {
            Func<CandidateActionResult, CandidateActionResult?>[] corruptions =
            [
                _ => null,
                r => r with { Status = (CandidateActionStatus)int.MaxValue },
                r => r with { Candidate = r.Candidate! with { OwnerId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { ProjectId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { PlanId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { Status = CandidatePreviewStatus.Pending } },
                r => r with { Revision = null },
                r => r with { Revision = r.Revision! with { RevisionId = Guid.NewGuid() } },
                r => r with { Revision = r.Revision! with { ProjectId = Guid.NewGuid() } },
                r => r with { Revision = r.Revision! with { PlanId = Guid.NewGuid() } },
                r => r with { Revision = r.Revision! with { ParentRevisionId = Guid.NewGuid() } },
                r => r with { Revision = r.Revision! with { GeometryHash = "untrusted" } },
                r => r with { Revision = r.Revision! with { EditCommandId = Guid.NewGuid() } }
            ];
            foreach (var corrupt in corruptions)
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Persistence.ApplyResponseTransform = corrupt;
                var result = scenario.Apply(candidate);
                Check(result.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                    result.Candidate?.CandidateId == candidate.CandidateId && result.Revision is null &&
                    scenario.Persistence.ApplyCalls == 1 && scenario.Persistence.RevisionCount == 2,
                    "Malformed Apply acknowledgement was trusted, disclosed, retried, or lost committed state.");
            }
        });

        run("candidate reject validates every required acknowledgement binding", () =>
        {
            Func<CandidateActionResult, CandidateActionResult?>[] corruptions =
            [
                _ => null,
                r => r with { Status = (CandidateActionStatus)int.MaxValue },
                r => r with { Candidate = r.Candidate! with { CandidateId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { OwnerId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { ProjectId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { PlanId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { Status = CandidatePreviewStatus.Applied } },
                r => r with { Revision = new Scenario().Persistence.CurrentRevision }
            ];
            foreach (var corrupt in corruptions)
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Persistence.RejectResponseTransform = corrupt;
                var result = scenario.Reject(candidate);
                Check(result.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                    result.Candidate?.CandidateId == candidate.CandidateId && result.Revision is null &&
                    scenario.Persistence.RejectCalls == 1 && scenario.Persistence.RevisionCount == 1,
                    "Malformed Reject acknowledgement was trusted, disclosed, retried, or changed revision history.");
            }
        });

        run("candidate expiration validates identity state and payload before continuing", () =>
        {
            Func<CandidateActionResult, CandidateActionResult?>[] corruptions =
            [
                _ => null,
                r => r with { Status = (CandidateActionStatus)int.MaxValue },
                r => r with { Candidate = r.Candidate! with { CandidateId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { OwnerId = Guid.NewGuid() } },
                r => r with { Candidate = r.Candidate! with { Status = CandidatePreviewStatus.Pending } },
                r => r with { Revision = new Scenario().Persistence.CurrentRevision }
            ];
            foreach (var corrupt in corruptions)
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Clock.Advance(scenario.Lifetime);
                scenario.Persistence.ExpireResponseTransform = corrupt;
                var result = scenario.Apply(candidate);
                Check(result.Status == CandidateActionStatus.TransactionOutcomeUnknown &&
                    result.Candidate?.CandidateId == candidate.CandidateId && result.Revision is null &&
                    scenario.Persistence.ExpireCalls == 1 && scenario.Persistence.ApplyCalls == 0 &&
                    scenario.Persistence.RejectCalls == 0 && scenario.Persistence.RevisionCount == 1,
                    "Malformed expiration acknowledgement was trusted, disclosed, retried, or continued.");
            }
        });

        run("candidate read dependencies classify failures without downstream mutation", () =>
        {
            var timeout = new Scenario { OwnershipException = new TimeoutException("private") };
            var transport = new Scenario { InputException = new HttpRequestException("private") };
            var unexpected = new Scenario { LandException = new InvalidOperationException("private") };
            var invalidInputs = new Scenario { ReturnInvalidInputs = true };
            Check(timeout.Preview().Status == CandidatePreviewResultStatus.DependencyTimeout,
                "Ownership timeout was misclassified.");
            Check(transport.Preview().Status == CandidatePreviewResultStatus.DependencyUnavailable,
                "Input transport failure was misclassified.");
            Check(unexpected.Preview().Status == CandidatePreviewResultStatus.UnexpectedInternalFailure,
                "Unexpected land failure was treated as ordinary absence.");
            Check(invalidInputs.Preview().Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                "Invalid applicable inputs reached downstream dependencies.");
            Check(timeout.Persistence.CandidateCount == 0 && transport.Persistence.CandidateCount == 0 &&
                unexpected.Persistence.CandidateCount == 0 && invalidInputs.ComputeCalls == 0,
                "Read dependency failure reached compute or persistence.");
        });

        run("candidate secondary read failures are classified before mutation", () =>
        {
            var failures = new (Exception Error, CandidateActionStatus Status)[]
            {
                (new TimeoutException("private"), CandidateActionStatus.DependencyTimeout),
                (new HttpRequestException("private"), CandidateActionStatus.DependencyUnavailable),
                (new OperationCanceledException("private"), CandidateActionStatus.DependencyTimeout),
                (new InvalidOperationException("private"), CandidateActionStatus.UnexpectedInternalFailure)
            };
            foreach (var failure in failures)
            {
                var authorization = new Scenario();
                var authorizationCandidate = authorization.Preview().Candidate!;
                authorization.OwnershipExceptionOnCall = authorization.OwnershipCalls + 1;
                authorization.OwnershipException = failure.Error;
                Check(authorization.Apply(authorizationCandidate).Status == failure.Status &&
                    authorization.Persistence.CandidateReadCalls == 0 && authorization.Persistence.ApplyCalls == 0,
                    "Initial action authorization failure was misclassified or reached lookup/mutation.");

                var lookup = new Scenario();
                var lookupCandidate = lookup.Preview().Candidate!;
                lookup.Persistence.LookupException = failure.Error;
                Check(lookup.Apply(lookupCandidate).Status == failure.Status && lookup.Persistence.ApplyCalls == 0,
                    "Candidate lookup failure was misclassified or reached mutation.");

                var current = new Scenario();
                var currentCandidate = current.Preview().Candidate!;
                current.Persistence.CurrentExceptionOnCall = current.Persistence.CurrentReadCalls + 1;
                current.Persistence.CurrentException = failure.Error;
                Check(current.Apply(currentCandidate).Status == failure.Status && current.Persistence.ApplyCalls == 0,
                    "Apply current-revision failure was misclassified or reached mutation.");

                var inputs = new Scenario();
                var inputCandidate = inputs.Preview().Candidate!;
                inputs.InputExceptionOnCall = inputs.InputCalls + 1;
                inputs.InputException = failure.Error;
                Check(inputs.Apply(inputCandidate).Status == failure.Status && inputs.Persistence.ApplyCalls == 0,
                    "Apply input failure was misclassified or reached mutation.");

                var lateCurrent = new Scenario();
                var lateCurrentCandidate = lateCurrent.Preview().Candidate!;
                lateCurrent.Persistence.CurrentExceptionOnCall = lateCurrent.Persistence.CurrentReadCalls + 2;
                lateCurrent.Persistence.CurrentException = failure.Error;
                Check(lateCurrent.Apply(lateCurrentCandidate).Status == failure.Status &&
                    lateCurrent.Persistence.ApplyCalls == 0,
                    "Late Apply current-revision failure was misclassified or reached mutation.");

                var lateInputs = new Scenario();
                var lateInputCandidate = lateInputs.Preview().Candidate!;
                lateInputs.InputExceptionOnCall = lateInputs.InputCalls + 2;
                lateInputs.InputException = failure.Error;
                Check(lateInputs.Apply(lateInputCandidate).Status == failure.Status &&
                    lateInputs.Persistence.ApplyCalls == 0,
                    "Late Apply input failure was misclassified or reached mutation.");

                var latePreview = new Scenario
                {
                    OwnershipExceptionOnCall = 2,
                    OwnershipException = failure.Error
                };
                var latePreviewResult = latePreview.Preview();
                var expectedPreview = failure.Status switch
                {
                    CandidateActionStatus.DependencyTimeout => CandidatePreviewResultStatus.DependencyTimeout,
                    CandidateActionStatus.DependencyUnavailable => CandidatePreviewResultStatus.DependencyUnavailable,
                    _ => CandidatePreviewResultStatus.UnexpectedInternalFailure
                };
                Check(latePreviewResult.Status == expectedPreview && latePreview.Persistence.CreateCalls == 0,
                    "Late Preview dependency failure was misclassified or reached persistence.");
            }
        });

        run("candidate late authorization and context failures stop every action", () =>
        {
            var preview = new Scenario { OwnershipExceptionOnCall = 2,
                OwnershipException = new TimeoutException("private") };
            Check(preview.Preview().Status == CandidatePreviewResultStatus.DependencyTimeout &&
                preview.Persistence.CreateCalls == 0, "Late Preview ownership timeout reached persistence.");

            var apply = new Scenario();
            var applyCandidate = apply.Preview().Candidate!;
            apply.OwnershipExceptionOnCall = apply.OwnershipCalls + 2;
            apply.OwnershipException = new HttpRequestException("private");
            Check(apply.Apply(applyCandidate).Status == CandidateActionStatus.DependencyUnavailable &&
                apply.Persistence.ApplyCalls == 0, "Late Apply ownership failure reached mutation.");

            var reject = new Scenario();
            var rejectCandidate = reject.Preview().Candidate!;
            reject.OwnershipExceptionOnCall = reject.OwnershipCalls + 2;
            reject.OwnershipException = new InvalidOperationException("private");
            Check(reject.Reject(rejectCandidate).Status == CandidateActionStatus.UnexpectedInternalFailure &&
                reject.Persistence.RejectCalls == 0, "Late Reject ownership failure reached mutation.");

            var expire = new Scenario();
            var expireCandidate = expire.Preview().Candidate!;
            expire.Clock.Advance(expire.Lifetime);
            expire.OwnershipExceptionOnCall = expire.OwnershipCalls + 2;
            expire.OwnershipException = new OperationCanceledException("private");
            Check(expire.Apply(expireCandidate).Status == CandidateActionStatus.DependencyTimeout &&
                expire.Persistence.ExpireCalls == 0, "Late expiration ownership failure reached mutation.");
        });

        run("candidate caller cancellation remains propagated before mutation", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;
            using var source = new CancellationTokenSource();
            source.Cancel();
            CheckThrows<OperationCanceledException>(() => scenario.Service.ApplyAsync(
                new(candidate.CandidateId, candidate.ProjectId, candidate.PlanId, true),
                scenario.OwnerId, source.Token).GetAwaiter().GetResult());
            Check(scenario.Persistence.ApplyCalls == 0, "Cancelled Apply reached mutation.");
        });

        run("candidate apply rejects complete current-revision corruption", () =>
        {
            Func<Scenario, PlanRevisionSnapshot, PlanRevisionSnapshot>[] corruptions =
            [
                (_, r) => r with { GeometryHash = "sha256:incorrect" },
                (s, r) => Rehash(s, r, "{}"),
                (s, r) => Rehash(s, r, "{"),
                (s, r) => Rehash(s, r, r.GeometryJson.Replace(
                    GeometrySchemaInfo.DraftVersion, "unsupported")),
                (s, r) => Rehash(s, r, r.GeometryJson.Replace(
                    s.ProjectId.ToString(), Guid.NewGuid().ToString())),
                (s, r) => Rehash(s, r, r.GeometryJson.Replace(
                    s.PlanId.ToString(), Guid.NewGuid().ToString())),
                (s, r) => Rehash(s, r, r.GeometryJson.Replace(
                    s.CurrentLandRevisionId.ToString(), Guid.NewGuid().ToString())),
                (s, r) => Rehash(s, r, r.GeometryJson.Replace(
                    s.CurrentRequirementsRevisionId.ToString(), Guid.NewGuid().ToString())),
                (_, r) => r with { VersionNumber = 0 },
                (_, r) => r with { Validation = null! },
                (s, r) => r with { Validation = s.CloneEvidence(r.Validation, validationId: Guid.Empty) },
                (s, r) => r with { Validation = s.CloneEvidence(r.Validation, geometryHash: "sha256:mismatch") }
            ];
            foreach (var corrupt in corruptions)
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Persistence.CurrentResponseTransform = (call, revision) =>
                    call == 3 ? corrupt(scenario, revision) : revision;
                var result = scenario.Apply(candidate);
                Check(result.Status == CandidateActionStatus.InvalidDependencyResponse &&
                    scenario.Persistence.ApplyCalls == 0,
                    "Corrupt current revision was accepted or reached Apply mutation.");
            }
        });

        run("candidate apply detects late immutable revision changes", () =>
        {
            Func<Scenario, PlanRevisionSnapshot, PlanRevisionSnapshot>[] changes =
            [
                (s, r) => Rehash(s, r, r.GeometryJson + " \r\n"),
                (s, r) => r with { Validation = s.CloneEvidence(r.Validation,
                    validatorVersion: "changed/2") },
                (_, r) => r with { CreatedAtUtc = r.CreatedAtUtc.AddSeconds(1) },
                (_, r) => r with { LandRevisionId = Guid.NewGuid() }
            ];
            foreach (var change in changes)
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;
                scenario.Persistence.CurrentResponseTransform = (call, revision) =>
                    call == 4 ? change(scenario, revision) : revision with { };
                var result = scenario.Apply(candidate);
                Check(result.Status == CandidateActionStatus.InvalidDependencyResponse &&
                    scenario.Persistence.ApplyCalls == 0,
                    "Same-revision immutable snapshot change reached Apply mutation.");
            }

            var stale = new Scenario();
            var staleCandidate = stale.Preview().Candidate!;
            stale.Persistence.CurrentResponseTransform = (call, revision) => call == 4
                ? revision with { RevisionId = Guid.NewGuid(), VersionNumber = revision.VersionNumber + 1 }
                : revision;
            Check(stale.Apply(staleCandidate).Status == CandidateActionStatus.StaleBaseRevision &&
                stale.Persistence.ApplyCalls == 0, "Legitimate newer revision was not reported as stale.");
        });

        run("candidate apply accepts exact noncanonical valid revision snapshots", () =>
        {
            var scenario = new Scenario();
            var revision = scenario.Persistence.CurrentRevision;
            scenario.Persistence.ReplaceCurrent(Rehash(scenario, revision, revision.GeometryJson + "  \r\n"));
            var candidate = scenario.Preview().Candidate!;
            scenario.Persistence.CurrentResponseTransform = (_, current) => current with { };
            Check(scenario.Apply(candidate).Status == CandidateActionStatus.Applied &&
                scenario.Persistence.ApplyCalls == 1,
                "Valid noncanonical or separately materialized current revision was rejected.");
        });

        run("GVR-01 candidate preview rejects maximum revision version", () =>
        {
            var scenario = new Scenario();
            scenario.Persistence.ReplaceCurrent(
                scenario.Persistence.CurrentRevision with
                {
                    VersionNumber = long.MaxValue
                });

            var result = scenario.Preview();

            Check(result.Status == CandidatePreviewResultStatus.InvalidDependencyResponse &&
                result.Candidate is null &&
                scenario.ComputeCalls == 0 &&
                scenario.Persistence.CreateCalls == 0 &&
                scenario.Persistence.RevisionCount == 1,
                "Maximum revision version reached computation or persistence.");
        });

        run("GVR-01 candidate apply rejects maximum revision version", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;

            scenario.Persistence.ReplaceCurrent(
                scenario.Persistence.CurrentRevision with
                {
                    VersionNumber = long.MaxValue
                });

            var result = scenario.Apply(candidate);

            Check(result.Status == CandidateActionStatus.InvalidDependencyResponse &&
                scenario.Persistence.ApplyCalls == 0 &&
                scenario.Persistence.RevisionCount == 1 &&
                scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status ==
                    CandidatePreviewStatus.Pending,
                "Maximum revision version reached Apply mutation.");
        });

        run("GVR-02 candidate actions reject missing or empty validation identity", () =>
        {
            foreach (var action in new[] { "apply", "reject", "expire" })
            {
                foreach (var missing in new[] { true, false })
                {
                    var scenario = new Scenario();
                    var candidate = scenario.Preview().Candidate!;

                    var corrupted = candidate with
                    {
                        Validation = missing
                            ? null!
                            : scenario.CloneEvidence(
                                candidate.Validation,
                                validationId: Guid.Empty)
                    };

                    scenario.Persistence.LookupOverride = corrupted;

                    if (action == "expire")
                        scenario.Clock.Advance(scenario.Lifetime);

                    var result = action == "reject"
                        ? scenario.Reject(candidate)
                        : scenario.Apply(candidate);

                    Check(result.Status == CandidateActionStatus.InvalidDependencyResponse &&
                        result.Candidate is null &&
                        result.Revision is null &&
                        scenario.Persistence.ApplyCalls == 0 &&
                        scenario.Persistence.RejectCalls == 0 &&
                        scenario.Persistence.ExpireCalls == 0 &&
                        scenario.Persistence.RevisionCount == 1,
                        $"Malformed validation evidence reached {action}.");
                }
            }
        });

        run("GVR-03 undefined candidate lifecycle states cannot reach mutation", () =>
        {
            foreach (var action in new[] { "apply", "reject", "expire" })
            {
                foreach (var invalidStatus in new[] { -1, int.MaxValue })
                {
                    var scenario = new Scenario();
                    var candidate = scenario.Preview().Candidate!;

                    scenario.Persistence.LookupOverride = candidate with
                    {
                        Status = (CandidatePreviewStatus)invalidStatus
                    };

                    if (action == "expire")
                        scenario.Clock.Advance(scenario.Lifetime);

                    var result = action == "reject"
                        ? scenario.Reject(candidate)
                        : scenario.Apply(candidate);

                    Check(result.Status == CandidateActionStatus.InvalidDependencyResponse &&
                        result.Candidate is null &&
                        result.Revision is null &&
                        scenario.Persistence.ApplyCalls == 0 &&
                        scenario.Persistence.RejectCalls == 0 &&
                        scenario.Persistence.ExpireCalls == 0 &&
                        scenario.Persistence.RevisionCount == 1,
                        $"Undefined candidate state reached {action}.");
                }
            }
        });

        run("GVR-02 and GVR-03 malformed foreign candidates remain protected", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;

            scenario.Persistence.LookupOverride = candidate with
            {
                OwnerId = Guid.NewGuid(),
                Status = (CandidatePreviewStatus)int.MaxValue,
                Validation = null!
            };

            var apply = scenario.Apply(candidate);
            var reject = scenario.Reject(candidate);

            Check(apply.Status == CandidateActionStatus.NotFound &&
                reject.Status == CandidateActionStatus.NotFound &&
                apply.Candidate is null &&
                reject.Candidate is null &&
                apply.Revision is null &&
                reject.Revision is null &&
                scenario.Persistence.ApplyCalls == 0 &&
                scenario.Persistence.RejectCalls == 0 &&
                scenario.Persistence.ExpireCalls == 0,
                "Malformed foreign candidate disclosed protected resource state.");
        });


        run("GVR-04 preview rejects late immutable revision corruption", () =>
        {
            Func<Scenario, PlanRevisionSnapshot, PlanRevisionSnapshot>[] corruptions =
            [
                (s, r) => Rehash(s, r, r.GeometryJson + " \r\n"),
                (s, r) => Rehash(s, r, "{"),
                (_, r) => r with { GeometryHash = "sha256:corrupted" },
                (_, r) => r with { Validation = null! },
                (s, r) => r with
                {
                    Validation = s.CloneEvidence(
                        r.Validation, validatorVersion: "changed/2")
                },
                (_, r) => r with { VersionNumber = r.VersionNumber + 1 },
                (_, r) => r with { CreatedBy = Guid.NewGuid() },
                (_, r) => r with { CreatedAtUtc = r.CreatedAtUtc.AddSeconds(1) },
                (_, r) => r with { CreatedAtUtc = r.CreatedAtUtc.ToOffset(TimeSpan.FromHours(2)) },
                (_, r) => r with { ChangeSummary = "unexpected change" },
                (_, r) => r with { LandRevisionId = Guid.NewGuid() },
                (_, r) => r with { RequirementsRevisionId = Guid.NewGuid() }
            ];

            foreach (var corrupt in corruptions)
            {
                var scenario = new Scenario();

                scenario.Persistence.CurrentResponseTransform =
                    (call, revision) => call == 2
                        ? corrupt(scenario, revision)
                        : revision;

                var result = scenario.Preview();

                Check(result.Status ==
                        CandidatePreviewResultStatus.InvalidDependencyResponse &&
                    result.Candidate is null &&
                    scenario.ComputeCalls == 1 &&
                    scenario.Persistence.CurrentReadCalls == 2 &&
                    scenario.Persistence.CreateCalls == 0 &&
                    scenario.Persistence.CandidateCount == 0 &&
                    scenario.Persistence.RevisionCount == 1,
                    "Late immutable revision corruption reached persistence.");
            }
        });

        run("GVR-04 preview distinguishes legitimate stale and identical snapshots", () =>
        {
            var stale = new Scenario();

            stale.Persistence.CurrentResponseTransform =
                (call, revision) => call == 2
                    ? revision with
                    {
                        RevisionId = Guid.NewGuid(),
                        VersionNumber = revision.VersionNumber + 1
                    }
                    : revision;

            Check(stale.Preview().Status ==
                    CandidatePreviewResultStatus.StaleBaseRevision &&
                stale.Persistence.CreateCalls == 0,
                "Legitimate newer revision was not reported as stale.");

            var identical = new Scenario();
            var revision = identical.Persistence.CurrentRevision;

            identical.Persistence.ReplaceCurrent(
                Rehash(identical, revision, revision.GeometryJson + " \r\n"));

            identical.Persistence.CurrentResponseTransform =
                (_, current) => current with
                {
                    Validation = identical.CloneEvidence(current.Validation)
                };

            Check(identical.Preview().Status ==
                    CandidatePreviewResultStatus.Created &&
                identical.Persistence.CreateCalls == 1 &&
                identical.Persistence.CandidateCount == 1 &&
                identical.Persistence.RevisionCount == 1,
                "Valid identical or noncanonical revision was rejected.");
        });

        run("GVR-05 transaction acknowledgements require complete evidence equality", () =>
        {
            PlanValidationReferenceDto AlterEvidence(
                PlanValidationReferenceDto source, string field) => new()
            {
                ValidationId = field == "validationId"
                    ? Guid.NewGuid() : source.ValidationId,

                ValidatorVersion = field == "validatorVersion"
                    ? "unexpected-version" : source.ValidatorVersion,

                GeometryHash = field == "geometryHash"
                    ? "sha256:unexpected" : source.GeometryHash,

                GeometryValid = field == "geometryValid"
                    ? !source.GeometryValid : source.GeometryValid,

                RequirementsMatch = field == "requirementsMatch"
                    ? source.RequirementsMatch is not true
                    : source.RequirementsMatch,

                RegulatoryEvidenceStatus = field == "regulatoryEvidenceStatus"
                    ? "unexpected-status" : source.RegulatoryEvidenceStatus,

                HasBlockingIssues = field == "hasBlockingIssues"
                    ? !source.HasBlockingIssues : source.HasBlockingIssues,

                ValidatedAtUtc = field == "validatedAtUtc"
                    ? source.ValidatedAtUtc.AddSeconds(1)
                    : field == "validationOffset"
                        ? source.ValidatedAtUtc.ToOffset(TimeSpan.FromHours(2))
                        : source.ValidatedAtUtc
            };

            string[] fields =
            [
                "validationId",
                "validatorVersion",
                "geometryHash",
                "geometryValid",
                "requirementsMatch",
                "regulatoryEvidenceStatus",
                "hasBlockingIssues",
                "validatedAtUtc",
                "validationOffset",
                "null"
            ];

            foreach (var field in fields)
            {
                foreach (var target in new[]
                {
                    "apply-candidate",
                    "apply-revision",
                    "reject",
                    "expire"
                })
                {
                    var scenario = new Scenario();
                    var candidate = scenario.Preview().Candidate!;

                    CandidateActionResult Corrupt(CandidateActionResult result)
                    {
                        if (target == "apply-revision")
                        {
                            return result with
                            {
                                Revision = result.Revision! with
                                {
                                    Validation = field == "null"
                                        ? null!
                                        : AlterEvidence(result.Revision!.Validation, field)
                                }
                            };
                        }

                        return result with
                        {
                            Candidate = result.Candidate! with
                            {
                                Validation = field == "null"
                                    ? null!
                                    : AlterEvidence(result.Candidate!.Validation, field)
                            }
                        };
                    }

                    CandidateActionResult outcome;

                    switch (target)
                    {
                        case "reject":
                            scenario.Persistence.RejectResponseTransform = Corrupt;
                            outcome = scenario.Reject(candidate);
                            break;

                        case "expire":
                            scenario.Clock.Advance(scenario.Lifetime);
                            scenario.Persistence.ExpireResponseTransform = Corrupt;
                            outcome = scenario.Apply(candidate);
                            break;

                        default:
                            scenario.Persistence.ApplyResponseTransform = Corrupt;
                            outcome = scenario.Apply(candidate);
                            break;
                    }

                    var isApply = target.StartsWith("apply-");

                    Check(outcome.Status ==
                            CandidateActionStatus.TransactionOutcomeUnknown &&
                        outcome.Candidate?.CandidateId == candidate.CandidateId &&
                        outcome.Candidate?.Validation.ValidationId ==
                            candidate.Validation.ValidationId &&
                        outcome.Revision is null &&
                        scenario.Persistence.ApplyCalls == (isApply ? 1 : 0) &&
                        scenario.Persistence.RejectCalls == (target == "reject" ? 1 : 0) &&
                        scenario.Persistence.ExpireCalls == (target == "expire" ? 1 : 0) &&
                        scenario.Persistence.RevisionCount == (isApply ? 2 : 1),
                        $"Malformed {target} acknowledgement was trusted for {field}.");

                    var terminalStatus = target switch
                    {
                        "reject" => CandidatePreviewStatus.Rejected,
                        "expire" => CandidatePreviewStatus.Expired,
                        _ => CandidatePreviewStatus.Applied
                    };

                    Check(scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status ==
                        terminalStatus,
                        "Committed state was not preserved for reconciliation.");
                }
            }
        });

        run("GVR-05 acknowledgement timestamps require exact equality", () =>
        {
            Func<CandidateActionResult, CandidateActionResult?>[] corruptions =
            [
                r => r with
                {
                    Candidate = r.Candidate! with
                    {
                        CreatedAtUtc = r.Candidate!.CreatedAtUtc.ToOffset(
                            TimeSpan.FromHours(2))
                    }
                },
                r => r with
                {
                    Candidate = r.Candidate! with
                    {
                        ExpiresAtUtc = r.Candidate!.ExpiresAtUtc.ToOffset(
                            TimeSpan.FromHours(2))
                    }
                },
                r => r with
                {
                    Revision = r.Revision! with
                    {
                        CreatedAtUtc = r.Revision!.CreatedAtUtc.ToOffset(
                            TimeSpan.FromHours(2))
                    }
                }
            ];

            foreach (var corrupt in corruptions)
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;

                scenario.Persistence.ApplyResponseTransform = corrupt;

                var outcome = scenario.Apply(candidate);

                Check(outcome.Status ==
                        CandidateActionStatus.TransactionOutcomeUnknown &&
                    outcome.Revision is null &&
                    scenario.Persistence.ApplyCalls == 1 &&
                    scenario.Persistence.RevisionCount == 2,
                    "Non-identical acknowledgement timestamp was trusted.");
            }
        });


        run("GVR-02 preview rejects empty validation source identity", () =>
        {
            var scenario = new Scenario { ReturnEmptyEvidenceId = true };
            var verificationCalls = 0;

            scenario.DuringEvidenceVerification = () => verificationCalls++;

            var result = scenario.Preview();

            Check(result.Status == CandidatePreviewResultStatus.InvalidDependencyResponse &&
                result.Candidate is null &&
                verificationCalls == 0 &&
                scenario.Persistence.CreateCalls == 0 &&
                scenario.Persistence.CandidateCount == 0,
                "Empty validation identity reached verification or persistence.");
        });

        run("GVR-06 compute exceptions receive distinct safe classifications", () =>
        {
            (Exception Error, CandidatePreviewResultStatus Expected)[] failures =
            [
                (new TimeoutException("private"),
                    CandidatePreviewResultStatus.DependencyTimeout),
                (new OperationCanceledException("private"),
                    CandidatePreviewResultStatus.DependencyTimeout),
                (new HttpRequestException("private"),
                    CandidatePreviewResultStatus.DependencyUnavailable),
                (new JsonException("private"),
                    CandidatePreviewResultStatus.InvalidDependencyResponse),
                (new InvalidOperationException("private"),
                    CandidatePreviewResultStatus.UnexpectedInternalFailure),
                (new ArgumentException("private"),
                    CandidatePreviewResultStatus.UnexpectedInternalFailure),
                (new IOException("private"),
                    CandidatePreviewResultStatus.UnexpectedInternalFailure)
            ];

            foreach (var failure in failures)
            {
                var scenario = new Scenario();
                var evidenceCalls = 0;

                scenario.DuringCompute = () => throw failure.Error;
                scenario.DuringEvidenceRetrieval = () => evidenceCalls++;

                var result = scenario.Preview();

                Check(result.Status == failure.Expected &&
                    result.Candidate is null &&
                    result.Diagnostics is null &&
                    scenario.ComputeCalls == 1 &&
                    evidenceCalls == 0 &&
                    scenario.Persistence.CreateCalls == 0 &&
                    scenario.Persistence.RevisionCount == 1,
                    "Compute exception was misclassified or reached persistence.");
            }
        });

        run("GVR-06 malformed compute responses fail closed", () =>
        {
            foreach (var corruption in new[]
            {
                "null", "command", "revision", "foreignGeometry",
                "status", "missingGeometry", "diagnostics",
                "changedEntities", "contradictoryRejection"
            })
            {
                var scenario = new Scenario();
                var evidenceCalls = 0;

                scenario.DuringEvidenceRetrieval = () => evidenceCalls++;

                scenario.ComputeResponseTransform = response =>
                {
                    if (corruption == "null")
                        return null;

                    return new PlanEditResultDto
                    {
                        SchemaVersion = response.SchemaVersion,
                        CommandId = corruption == "command"
                            ? Guid.NewGuid() : response.CommandId,
                        BaseRevisionId = corruption == "revision"
                            ? Guid.NewGuid() : response.BaseRevisionId,
                        Status = corruption == "status"
                            ? "unknown-status"
                            : corruption == "contradictoryRejection"
                                ? PlanEditStatus.Rejected : response.Status,
                        CandidateGeometry = corruption == "missingGeometry"
                            ? null
                            : corruption == "foreignGeometry"
                                ? Geometry(Guid.NewGuid(), scenario.PlanId,
                                    scenario.CurrentLandRevisionId,
                                    scenario.CurrentRequirementsRevisionId)
                                : response.CandidateGeometry,
                        Diagnostics = corruption == "diagnostics"
                            ? null! : response.Diagnostics,
                        ChangedEntityIds = corruption == "changedEntities"
                            ? null! : response.ChangedEntityIds
                    };
                };

                var result = scenario.Preview();

                Check(result.Status == CandidatePreviewResultStatus.InvalidDependencyResponse &&
                    result.Candidate is null &&
                    result.Diagnostics is null &&
                    evidenceCalls == 0 &&
                    scenario.ComputeCalls == 1 &&
                    scenario.Persistence.CreateCalls == 0 &&
                    scenario.Persistence.RevisionCount == 1,
                    $"Malformed compute response was trusted: {corruption}.");
            }
        });

        run("GVR-06 genuine compute rejection preserves domain result", () =>
        {
            var scenario = new Scenario();

            scenario.ComputeResponseTransform = response => new PlanEditResultDto
            {
                SchemaVersion = response.SchemaVersion,
                CommandId = response.CommandId,
                BaseRevisionId = response.BaseRevisionId,
                Status = PlanEditStatus.Rejected,
                CandidateGeometry = null,
                Diagnostics =
                [
                    new PlanEditDiagnosticDto
                    {
                        Code = "TEST_REJECTION",
                        Message = "Test-only domain rejection.",
                        Severity = "error"
                    }
                ],
                ChangedEntityIds = []
            };

            var result = scenario.Preview();

            Check(result.Status == CandidatePreviewResultStatus.CandidateRejected &&
                result.Candidate is null &&
                result.Diagnostics?.Count == 1 &&
                result.Diagnostics[0].Code == "TEST_REJECTION" &&
                scenario.Persistence.CreateCalls == 0,
                "Genuine domain rejection was not preserved.");
        });

        run("GVR-07 caller cancellation after compute blocks continuation", () =>
        {
            var scenario = new Scenario();
            using var cancellation = new CancellationTokenSource();
            var evidenceCalls = 0;

            scenario.DuringCompute = cancellation.Cancel;
            scenario.DuringEvidenceRetrieval = () => evidenceCalls++;

            CheckThrows<OperationCanceledException>(() => scenario.Service.PreviewAsync(
                new(scenario.ProjectId, scenario.PlanId,
                    scenario.Persistence.CurrentRevision.RevisionId,
                    ValidCommand(), "GVR-07-compute"),
                scenario.OwnerId, cancellation.Token).GetAwaiter().GetResult());

            Check(scenario.ComputeCalls == 1 &&
                evidenceCalls == 0 &&
                scenario.Persistence.CreateCalls == 0 &&
                scenario.Persistence.CandidateCount == 0,
                "Cancellation after compute reached downstream operations.");
        });

        run("GVR-07 late caller cancellation blocks candidate persistence", () =>
        {
            var scenario = new Scenario();
            using var cancellation = new CancellationTokenSource();

            scenario.Persistence.CurrentResponseTransform = (call, revision) =>
            {
                if (call == 2)
                    cancellation.Cancel();

                return revision;
            };

            CheckThrows<OperationCanceledException>(() => scenario.Service.PreviewAsync(
                new(scenario.ProjectId, scenario.PlanId,
                    scenario.Persistence.CurrentRevision.RevisionId,
                    ValidCommand(), "GVR-07-late"),
                scenario.OwnerId, cancellation.Token).GetAwaiter().GetResult());

            Check(scenario.ComputeCalls == 1 &&
                scenario.Persistence.CurrentReadCalls == 2 &&
                scenario.Persistence.CreateCalls == 0 &&
                scenario.Persistence.CandidateCount == 0 &&
                scenario.Persistence.RevisionCount == 1,
                "Late caller cancellation reached candidate persistence.");
        });


        run("GVR-08 structural validator rejects nested geometry nulls", () =>
        {
            const string fixturePath =
                "python-services/tests/m3_plan_contract/fixtures/advanced_geometry_cases.json";

            using var document = JsonDocument.Parse(
                File.ReadAllText(FixtureLocator.ResolveRepositoryFile(fixturePath)));

            var fixtureJson = document.RootElement
                .GetProperty("baseSharedWallPlan").GetRawText();

            var valid = JsonSerializer.Deserialize<PlanGeometryDto>(
                fixtureJson, PlanEditorJson.StrictOptions)!;

            PlanEditorContractValidator.Validate(valid);

            Action<System.Text.Json.Nodes.JsonNode>[] corruptions =
            [
                n => n["floors"] = null,
                n => n["floors"]![0] = null,
                n => n["floors"]![0]!["rooms"] = null,
                n => n["floors"]![0]!["walls"] = null,
                n => n["floors"]![0]!["openings"] = null,
                n => n["floors"]![0]!["rooms"]![0] = null,
                n => n["floors"]![0]!["walls"]![0] = null,
                n => n["floors"]![0]!["openings"]![0] = null,
                n => n["floors"]![0]!["rooms"]![0]!["polygon"] = null,
                n => n["floors"]![0]!["rooms"]![0]!["polygon"]![0] = null,
                n => n["floors"]![0]!["rooms"]![0]!["wallIds"] = null,
                n => n["coordinateSystem"] = null,
                n => n["coordinateSystem"]!["origin"] = null,
                n => n["floors"]![0]!["walls"]![0]!["start"] = null,
                n => n["floors"]![0]!["walls"]![0]!["end"] = null
            ];

            foreach (var corrupt in corruptions)
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(fixtureJson)!;
                corrupt(node);

                var malformed = JsonSerializer.Deserialize<PlanGeometryDto>(
                    node.ToJsonString())!;

                CheckThrows<ArgumentException>(() =>
                    PlanEditorContractValidator.Validate(malformed));
            }
        });

        run("GVR-08 malformed stored revisions cannot reach computation or mutation", () =>
        {
            string CorruptGeometry(string json)
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
                node["floors"]![0]!["rooms"] = null;
                return node.ToJsonString();
            }

            var initial = new Scenario();
            var initialRevision = initial.Persistence.CurrentRevision;

            initial.Persistence.ReplaceCurrent(
                Rehash(initial, initialRevision,
                    CorruptGeometry(initialRevision.GeometryJson)));

            Check(initial.Preview().Status ==
                    CandidatePreviewResultStatus.InvalidDependencyResponse &&
                initial.ComputeCalls == 0 &&
                initial.Persistence.CreateCalls == 0,
                "Malformed stored geometry reached computation.");

            var late = new Scenario();

            late.Persistence.CurrentResponseTransform =
                (call, revision) => call == 2
                    ? Rehash(late, revision,
                        CorruptGeometry(revision.GeometryJson))
                    : revision;

            Check(late.Preview().Status ==
                    CandidatePreviewResultStatus.InvalidDependencyResponse &&
                late.ComputeCalls == 1 &&
                late.Persistence.CreateCalls == 0,
                "Malformed late revision reached persistence.");

            var apply = new Scenario();
            var candidate = apply.Preview().Candidate!;
            var current = apply.Persistence.CurrentRevision;

            apply.Persistence.ReplaceCurrent(
                Rehash(apply, current,
                    CorruptGeometry(current.GeometryJson)));

            Check(apply.Apply(candidate).Status ==
                    CandidateActionStatus.InvalidDependencyResponse &&
                apply.Persistence.ApplyCalls == 0 &&
                apply.Persistence.RevisionCount == 1,
                "Malformed current revision reached Apply mutation.");
        });

        run("GVR-08 malformed compute geometry cannot reach validation or persistence", () =>
        {
            var scenario = new Scenario();
            var evidenceCalls = 0;

            scenario.DuringEvidenceRetrieval = () => evidenceCalls++;

            scenario.ComputeResponseTransform = response =>
            {
                var valid = response.CandidateGeometry!;

                var malformed = new PlanGeometryDto
                {
                    SchemaVersion = valid.SchemaVersion,
                    ProjectId = valid.ProjectId,
                    PlanId = valid.PlanId,
                    LandRevisionId = valid.LandRevisionId,
                    RequirementsRevisionId = valid.RequirementsRevisionId,
                    CoordinateSystem = valid.CoordinateSystem,
                    Floors = [null!]
                };

                return new PlanEditResultDto
                {
                    SchemaVersion = response.SchemaVersion,
                    CommandId = response.CommandId,
                    BaseRevisionId = response.BaseRevisionId,
                    Status = response.Status,
                    CandidateGeometry = malformed,
                    Diagnostics = [],
                    ChangedEntityIds = []
                };
            };

            var result = scenario.Preview();

            Check(result.Status ==
                    CandidatePreviewResultStatus.InvalidDependencyResponse &&
                result.Candidate is null &&
                evidenceCalls == 0 &&
                scenario.Persistence.CreateCalls == 0 &&
                scenario.Persistence.RevisionCount == 1,
                "Malformed compute geometry reached downstream operations.");
        });

        run("GVR-08 malformed candidate geometry cannot reach Apply transaction", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;

            var node = System.Text.Json.Nodes.JsonNode.Parse(
                candidate.GeometryJson)!;

            node["floors"]![0]!["rooms"] = null;

            var malformedJson = node.ToJsonString();
            var malformedHash =
                PlanRevisionService.ComputeGeometryHash(malformedJson);

            scenario.Persistence.LookupOverride = candidate with
            {
                GeometryJson = malformedJson,
                GeometryHash = malformedHash,
                Validation = scenario.CloneEvidence(
                    candidate.Validation, geometryHash: malformedHash)
            };

            var verificationCalls = 0;
            scenario.DuringEvidenceVerification = () => verificationCalls++;

            var result = scenario.Apply(candidate);

            Check(result.Status == CandidateActionStatus.GeometryTampered &&
                verificationCalls == 0 &&
                scenario.Persistence.ApplyCalls == 0 &&
                scenario.Persistence.RevisionCount == 1 &&
                scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status ==
                    CandidatePreviewStatus.Pending,
                "Malformed candidate geometry reached Apply mutation.");
        });


        run("GVR-09 altered raw candidate JSON cannot bypass exact hash verification", () =>
        {
            foreach (var updateStoredHash in new[] { false, true })
            {
                var scenario = new Scenario();
                var candidate = scenario.Preview().Candidate!;

                var alteredJson = candidate.GeometryJson + " \r\n";
                var alteredHash =
                    PlanRevisionService.ComputeGeometryHash(alteredJson);

                Check(alteredHash != candidate.GeometryHash,
                    "Raw snapshot corruption did not change its hash.");

                scenario.Persistence.LookupOverride = candidate with
                {
                    GeometryJson = alteredJson,
                    GeometryHash = updateStoredHash
                        ? alteredHash : candidate.GeometryHash
                };

                var verificationCalls = 0;
                scenario.DuringEvidenceVerification =
                    () => verificationCalls++;

                var result = scenario.Apply(candidate);

                Check(result.Status == CandidateActionStatus.GeometryTampered &&
                    verificationCalls == 0 &&
                    scenario.Persistence.ApplyCalls == 0 &&
                    scenario.Persistence.RevisionCount == 1 &&
                    scenario.Persistence.GetCandidate(candidate.CandidateId)!.Status ==
                        CandidatePreviewStatus.Pending,
                    "Altered raw candidate JSON bypassed exact hash verification.");
            }
        });

        run("GVR-09 exact verified noncanonical candidate snapshot remains usable", () =>
        {
            var scenario = new Scenario();
            var candidate = scenario.Preview().Candidate!;

            var exactJson = candidate.GeometryJson + " \r\n";
            var exactHash =
                PlanRevisionService.ComputeGeometryHash(exactJson);

            var verifiedCandidate = candidate with
            {
                GeometryJson = exactJson,
                GeometryHash = exactHash,
                Validation = scenario.CloneEvidence(
                    candidate.Validation, geometryHash: exactHash)
            };

            scenario.Persistence.Replace(verifiedCandidate);

            var verificationCalls = 0;
            scenario.DuringEvidenceVerification =
                () => verificationCalls++;

            var result = scenario.Apply(verifiedCandidate);

            Check(result.Status == CandidateActionStatus.Applied &&
                result.Revision is not null &&
                result.Revision.GeometryJson == exactJson &&
                result.Revision.GeometryHash == exactHash &&
                result.Revision.Validation.GeometryHash == exactHash &&
                verificationCalls == 1 &&
                scenario.Persistence.ApplyCalls == 1 &&
                scenario.Persistence.RevisionCount == 2,
                "Valid exact noncanonical snapshot was rejected or altered.");
        });

        run("GVR-10 ownership revocation during late context reads blocks Preview creation", () =>
        {
            var scenario = new Scenario();

            scenario.Persistence.CurrentResponseTransform =
                (call, revision) =>
                {
                    if (call == 2)
                        scenario.Owned = false;

                    return revision;
                };

            var result = scenario.Preview();

            Check(result.Status == CandidatePreviewResultStatus.Unauthorized &&
                result.Candidate is null &&
                scenario.ComputeCalls == 1 &&
                scenario.OwnershipCalls == 3 &&
                scenario.Persistence.CurrentReadCalls == 2 &&
                scenario.Persistence.CreateCalls == 0 &&
                scenario.Persistence.CandidateCount == 0 &&
                scenario.Persistence.RevisionCount == 1,
                "Late ownership revocation reached candidate persistence.");
        });

        run("GVR-10 final Preview ownership failures are classified without persistence", () =>
        {
            (Exception Error, CandidatePreviewResultStatus Expected)[] failures =
            [
                (new TimeoutException("private"),
                    CandidatePreviewResultStatus.DependencyTimeout),

                (new OperationCanceledException("private"),
                    CandidatePreviewResultStatus.DependencyTimeout),

                (new HttpRequestException("private"),
                    CandidatePreviewResultStatus.DependencyUnavailable),

                (new InvalidOperationException("private"),
                    CandidatePreviewResultStatus.UnexpectedInternalFailure)
            ];

            foreach (var failure in failures)
            {
                var scenario = new Scenario
                {
                    OwnershipException = failure.Error,
                    OwnershipExceptionOnCall = 3
                };

                var result = scenario.Preview();

                Check(result.Status == failure.Expected &&
                    result.Candidate is null &&
                    scenario.OwnershipCalls == 3 &&
                    scenario.ComputeCalls == 1 &&
                    scenario.Persistence.CreateCalls == 0 &&
                    scenario.Persistence.CandidateCount == 0 &&
                    scenario.Persistence.RevisionCount == 1,
                    "Final ownership failure was misclassified or reached persistence.");
            }
        });

        run("GVR-10 cancellation during final ownership check blocks persistence", () =>
        {
            var scenario = new Scenario();

            using var cancellation = new CancellationTokenSource();

            scenario.DuringOwnershipCheck = call =>
            {
                if (call == 3)
                    cancellation.Cancel();
            };

            CheckThrows<OperationCanceledException>(() =>
                scenario.Service.PreviewAsync(
                    new(scenario.ProjectId, scenario.PlanId,
                        scenario.Persistence.CurrentRevision.RevisionId,
                        ValidCommand(), "GVR-10-final-ownership"),
                    scenario.OwnerId,
                    cancellation.Token).GetAwaiter().GetResult());

            Check(scenario.OwnershipCalls == 3 &&
                scenario.ComputeCalls == 1 &&
                scenario.Persistence.CreateCalls == 0 &&
                scenario.Persistence.CandidateCount == 0 &&
                scenario.Persistence.RevisionCount == 1,
                "Cancellation during final ownership verification reached persistence.");
        });

    }

    private static PlanRevisionSnapshot Rehash(
        Scenario scenario, PlanRevisionSnapshot revision, string geometryJson)
    {
        var hash = PlanRevisionService.ComputeGeometryHash(geometryJson);
        return revision with
        {
            GeometryJson = geometryJson,
            GeometryHash = hash,
            Validation = scenario.CloneEvidence(revision.Validation, geometryHash: hash)
        };
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static bool NoRequirementDownstreamEffects(Scenario scenario) =>
        scenario.ComputeCalls == 0 &&
        scenario.EvidenceRetrievalCalls == 0 &&
        scenario.EvidenceVerificationCalls == 0 &&
        scenario.Persistence.CreateCalls == 0 &&
        scenario.Persistence.CandidateCount == 0;

    private static void CheckThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private sealed class TrustedActionCurrentUserDouble(Guid? userId)
        : BuildMind.Application.Abstractions.ICurrentUser
    {
        public bool IsAuthenticated => userId is not null && userId != Guid.Empty;
        public Guid? UserId => IsAuthenticated ? userId : null;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public int RequiredUserCalls { get; private set; }

        public Guid GetRequiredUserId()
        {
            RequiredUserCalls++;

            if (!IsAuthenticated)
                throw new BuildMind.Application.Abstractions.Errors.UnauthenticatedAccessException();

            return userId!.Value;
        }
    }
    private sealed class Scenario
    {
        public Guid OwnerId { get; } = Guid.NewGuid();
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Guid PlanId { get; } = Guid.NewGuid();
        public Guid CurrentLandRevisionId { get; set; } = Guid.NewGuid();
        public Guid CurrentRequirementsRevisionId { get; set; } = Guid.NewGuid();
        public TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(15);
        public MutableTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-17T00:00:00Z"));
        public CandidatePersistenceTestDouble Persistence { get; }
        public CandidatePreviewService Service { get; }
        public ValidationEvidenceStatus ValidationStatus { get; set; } = ValidationEvidenceStatus.Verified;
        public bool ReturnMismatchedEvidenceHash { get; set; }
        public bool HasBlockingIssues { get; set; }
        public bool HasWarnings { get; set; }
        public bool LandAvailable { get; set; } = true;
        public bool RequirementsAvailable { get; set; } = true;
        public RequirementRevisionAccessStatus RequirementAccessStatus { get; set; }
            = RequirementRevisionAccessStatus.Found;
        public Guid? RequirementAuthorizationActorId { get; set; }
        public Guid? RequirementAuthorizationProjectId { get; set; }
        public Guid? RequirementRevisionId { get; set; }
        public Exception? RequirementException { get; set; }
        public Action? DuringRequirementAccess { get; set; }
        public bool ReturnNullRequirementAccessResult { get; set; }
        public bool IncludePriorAuthorizationOnInvalidResponse { get; set; }
        public int RequirementCalls { get; private set; }
        public Guid? LastRequirementProjectId { get; private set; }
        public Guid? LastRequirementRevisionId { get; private set; }
        public Guid? LastRequirementActorId { get; private set; }
        public bool Owned { get; set; } = true;
        public Exception? OwnershipException { get; set; }
        public int? OwnershipExceptionOnCall { get; set; }
        public int OwnershipCalls { get; private set; }
        public Action<int>? DuringOwnershipCheck { get; set; }
        public Exception? InputException { get; set; }
        public int? InputExceptionOnCall { get; set; }
        public int InputCalls { get; private set; }
        public Exception? LandException { get; set; }
        public bool ReturnInvalidInputs { get; set; }
        public int ComputeCalls { get; private set; }
        public int EvidenceRetrievalCalls { get; private set; }
        public int EvidenceVerificationCalls { get; private set; }
        public Action? DuringCompute { get; set; }
        public Func<PlanEditResultDto, PlanEditResultDto?>? ComputeResponseTransform { get; set; }
        public bool ReturnEmptyEvidenceId { get; set; }
        public Action? DuringEvidenceRetrieval { get; set; }
        public Action? DuringEvidenceVerification { get; set; }
        private Guid InitialLandRevisionId { get; }
        private Guid InitialRequirementsRevisionId { get; }
        private Guid InitialRevisionId { get; }

        public Scenario()
        {
            var geometry = Geometry(ProjectId, PlanId, CurrentLandRevisionId, CurrentRequirementsRevisionId);
            var geometryJson = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
            var baseRevision = new PlanRevisionSnapshot(
                Guid.NewGuid(), PlanId, ProjectId, null, 1, geometry.SchemaVersion, geometryJson,
                PlanRevisionService.ComputeGeometryHash(geometryJson), CurrentLandRevisionId,
                CurrentRequirementsRevisionId, Evidence(PlanRevisionService.ComputeGeometryHash(geometryJson)),
                null, OwnerId, Clock.GetUtcNow(), "base");
            Persistence = new CandidatePersistenceTestDouble(baseRevision);
            InitialLandRevisionId = CurrentLandRevisionId;
            InitialRequirementsRevisionId = CurrentRequirementsRevisionId;
            InitialRevisionId = baseRevision.RevisionId;

            Service = new CandidatePreviewService(
                new TestProjectOwnershipGate((project, actor, _) =>
                {
                    OwnershipCalls++;
                    DuringOwnershipCheck?.Invoke(OwnershipCalls);
                    return OwnershipException is not null &&
                        (OwnershipExceptionOnCall is null || OwnershipExceptionOnCall == OwnershipCalls)
                        ? Task.FromException<bool>(OwnershipException)
                        : Task.FromResult(Owned && project == ProjectId && actor == OwnerId);
                }),
                Persistence,
                new TestApplicableInputRevisionSource((_, _) =>
                {
                    InputCalls++;
                    return InputException is not null &&
                        (InputExceptionOnCall is null || InputExceptionOnCall == InputCalls)
                        ? Task.FromException<ApplicableInputRevisions>(InputException)
                        : Task.FromResult(ReturnInvalidInputs
                        ? new ApplicableInputRevisions(Guid.Empty, CurrentRequirementsRevisionId)
                        : new ApplicableInputRevisions(CurrentLandRevisionId, CurrentRequirementsRevisionId));
                }),
                new TestConfirmedLandRevisionPort((project, revision, _) => LandException is null
                    ? Task.FromResult<ConfirmedLandRevision?>(LandAvailable ? Land(project, revision) : null)
                    : Task.FromException<ConfirmedLandRevision?>(LandException)),
                new TestAuthorizedRequirementRevisionPort((project, revision, actor, token) =>
                {
                    RequirementCalls++;
                    LastRequirementProjectId = project;
                    LastRequirementRevisionId = revision;
                    LastRequirementActorId = actor;
                    DuringRequirementAccess?.Invoke();
                    token.ThrowIfCancellationRequested();
                    if (ReturnNullRequirementAccessResult)
                        return Task.FromResult<RequirementRevisionAccessResult>(null!);
                    if (RequirementException is not null)
                        return Task.FromException<RequirementRevisionAccessResult>(RequirementException);
                    if (RequirementAccessStatus == RequirementRevisionAccessStatus.Unauthorized)
                        return Task.FromResult(new RequirementRevisionAccessResult(
                            RequirementRevisionAccessStatus.Unauthorized));
                    if (RequirementAccessStatus == RequirementRevisionAccessStatus.InvalidDependencyResponse)
                    {
                        var priorAuthorization = IncludePriorAuthorizationOnInvalidResponse
                            ? new ProjectAuthorizationSnapshot(
                                RequirementAuthorizationProjectId ?? project,
                                RequirementAuthorizationActorId ?? actor,
                                new AuthorizationVersion(1))
                            : null;
                        return Task.FromResult(new RequirementRevisionAccessResult(
                            RequirementRevisionAccessStatus.InvalidDependencyResponse,
                            authorization: priorAuthorization));
                    }

                    var authorizationProject = RequirementAuthorizationProjectId ?? project;
                    var authorization = new ProjectAuthorizationSnapshot(
                        authorizationProject,
                        RequirementAuthorizationActorId ?? actor,
                        new AuthorizationVersion(1));
                    if (!RequirementsAvailable || RequirementAccessStatus == RequirementRevisionAccessStatus.NotFound)
                        return Task.FromResult(new RequirementRevisionAccessResult(
                            RequirementRevisionAccessStatus.NotFound,
                            authorization: authorization));

                    return Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Found,
                        new RequirementRevisionReference(
                            authorizationProject,
                            RequirementRevisionId ?? revision,
                            "test-only/1"),
                        authorization));
                }),
                new TestPlanEditorComputeClient((request, _) =>
                {
                    ComputeCalls++;
                    DuringCompute?.Invoke();
                    var candidate = Geometry(ProjectId, PlanId, InitialLandRevisionId, InitialRequirementsRevisionId);
                    var response = new PlanEditResultDto
                    {
                        SchemaVersion = candidate.SchemaVersion,
                        CommandId = request.Command.CommandId,
                        BaseRevisionId = InitialRevisionId,
                        Status = PlanEditStatus.Accepted,
                        CandidateGeometry = candidate,
                        Diagnostics = [],
                        ChangedEntityIds = []
                    };
                    return Task.FromResult(ComputeResponseTransform is null
                        ? response : ComputeResponseTransform(response)!);
                }),
                new CandidateValidationEvidenceSourceTestDouble((_, _, hash, _) =>
                {
                    EvidenceRetrievalCalls++;
                    DuringEvidenceRetrieval?.Invoke();
                    var evidence = Evidence(
                        ReturnMismatchedEvidenceHash ? "sha256:mismatch" : hash);
                    return Task.FromResult<PlanValidationReferenceDto?>(
                        ReturnEmptyEvidenceId
                            ? CloneEvidence(evidence, validationId: Guid.Empty)
                            : evidence);
                }),
                new TestPlanValidationEvidenceVerifier((_, _, _, _, _) =>
                {
                    EvidenceVerificationCalls++;
                    DuringEvidenceVerification?.Invoke();
                    return Task.FromResult(ValidationStatus);
                }),
                Persistence, Persistence, Clock, Lifetime);
        }

        public CandidatePreviewResult Preview(
            Guid? actor = null, Guid? baseRevisionId = null, PlanEditCommandDto? command = null) =>
            Service.PreviewAsync(new(
                ProjectId, PlanId, baseRevisionId ?? Persistence.CurrentRevision.RevisionId,
                command ?? ValidCommand(), "candidate-contract-test"), actor ?? OwnerId).Result;

        public CandidatePreviewResult PreviewWithCancellation(CancellationToken cancellationToken) =>
            Service.PreviewAsync(new(
                ProjectId, PlanId, Persistence.CurrentRevision.RevisionId,
                ValidCommand(), "candidate-contract-test"), OwnerId, cancellationToken)
            .GetAwaiter().GetResult();

        public CandidateActionResult Apply(
            CandidatePreviewSnapshot candidate, Guid? actor = null, bool acknowledgeWarnings = true) =>
            Service.ApplyAsync(new(candidate.CandidateId, candidate.ProjectId, candidate.PlanId,
                acknowledgeWarnings, "candidate apply"), actor ?? OwnerId).Result;

        public CandidateActionResult Reject(CandidatePreviewSnapshot candidate) =>
            Service.RejectAsync(new(candidate.CandidateId, candidate.ProjectId, candidate.PlanId), OwnerId).Result;

        private PlanValidationReferenceDto Evidence(string hash) => new()
        {
            ValidationId = Guid.NewGuid(), ValidatorVersion = "explicit-candidate-test-double/1",
            GeometryHash = hash, GeometryValid = !HasBlockingIssues,
            RequirementsMatch = HasWarnings ? false : true,
            RegulatoryEvidenceStatus = HasWarnings ? "NOT_VERIFIED" : "VERIFIED",
            HasBlockingIssues = HasBlockingIssues, ValidatedAtUtc = Clock.GetUtcNow()
        };

        public PlanValidationReferenceDto EvidenceFor(string hash) => Evidence(hash);

        public PlanValidationReferenceDto CloneEvidence(
            PlanValidationReferenceDto source,
            Guid? validationId = null,
            string? geometryHash = null,
            string? validatorVersion = null) => new()
        {
            ValidationId = validationId ?? source.ValidationId,
            ValidatorVersion = validatorVersion ?? source.ValidatorVersion,
            GeometryHash = geometryHash ?? source.GeometryHash,
            GeometryValid = source.GeometryValid,
            RequirementsMatch = source.RequirementsMatch,
            RegulatoryEvidenceStatus = source.RegulatoryEvidenceStatus,
            HasBlockingIssues = source.HasBlockingIssues,
            ValidatedAtUtc = source.ValidatedAtUtc
        };
    }

    private sealed class CandidateValidationEvidenceSourceTestDouble(
        Func<Guid, Guid, string, CancellationToken, Task<PlanValidationReferenceDto?>> handler)
        : ICandidateValidationEvidenceSource
    {
        public Task<PlanValidationReferenceDto?> GetAsync(
            Guid projectId, Guid planId, string exactGeometryHash, CancellationToken cancellationToken) =>
            handler(projectId, planId, exactGeometryHash, cancellationToken);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now = now.Add(duration);
    }

    private sealed class CandidatePersistenceTestDouble(PlanRevisionSnapshot initial)
        : ICandidatePreviewStore, ICandidatePreviewTransaction, IPlanRevisionStore
    {
        private readonly object sync = new();
        private readonly Dictionary<Guid, CandidatePreviewSnapshot> candidates = [];
        private readonly List<PlanRevisionSnapshot> revisions = [initial];
        public bool ReturnConfirmedRollback { get; set; }
        public CandidateCreateFailure CreateFailure { get; set; }
        public MutationFailure ApplyFailure { get; set; }
        public MutationFailure RejectFailure { get; set; }
        public ExpireFailure ExpireFailure { get; set; }
        public bool ReturnConfirmedExpirationRollback { get; set; }
        public Action? AfterCandidateRead { get; set; }
        public Action? AfterExpirationCommit { get; set; }
        public int CreateCalls { get; private set; }
        public int ApplyCalls { get; private set; }
        public int RejectCalls { get; private set; }
        public int ExpireCalls { get; private set; }
        public int CandidateReadCalls { get; private set; }
        public Exception? LookupException { get; set; }
        public int CurrentReadCalls { get; private set; }
        public int? CurrentExceptionOnCall { get; set; }
        public Exception? CurrentException { get; set; }
        public CandidatePreviewSnapshot? LookupOverride { get; set; }
        public Func<int, PlanRevisionSnapshot, PlanRevisionSnapshot>? CurrentResponseTransform { get; set; }
        public Func<CandidateActionResult, CandidateActionResult?>? ApplyResponseTransform { get; set; }
        public Func<CandidateActionResult, CandidateActionResult?>? RejectResponseTransform { get; set; }
        public Func<CandidateActionResult, CandidateActionResult?>? ExpireResponseTransform { get; set; }
        public int RevisionCount { get { lock (sync) return revisions.Count; } }
        public int CandidateCount { get { lock (sync) return candidates.Count; } }
        public PlanRevisionSnapshot CurrentRevision { get { lock (sync) return revisions.MaxBy(x => x.VersionNumber)!; } }

        public Task CreateAsync(CandidatePreviewSnapshot candidate, CancellationToken cancellationToken)
        {
            CreateCalls++;
            if (CreateFailure == CandidateCreateFailure.BeforeMutation)
                throw new IOException("private candidate store failure");
            lock (sync) candidates.Add(candidate.CandidateId, candidate);
            if (CreateFailure == CandidateCreateFailure.AfterStored)
                throw new TimeoutException("private acknowledgement timeout");
            return Task.CompletedTask;
        }

        public Task<CandidatePreviewSnapshot?> GetAsync(Guid candidateId, CancellationToken cancellationToken)
        {
            CandidateReadCalls++;
            if (LookupException is not null)
                return Task.FromException<CandidatePreviewSnapshot?>(LookupException);
            CandidatePreviewSnapshot? result;
            lock (sync) result = LookupOverride ?? candidates.GetValueOrDefault(candidateId);
            AfterCandidateRead?.Invoke();
            return Task.FromResult(result);
        }

        public CandidatePreviewSnapshot? GetCandidate(Guid id) { lock (sync) return candidates.GetValueOrDefault(id); }
        public void Replace(CandidatePreviewSnapshot candidate) { lock (sync) candidates[candidate.CandidateId] = candidate; }
        public void ReplaceCurrent(PlanRevisionSnapshot revision)
        {
            lock (sync)
            {
                var index = revisions.FindIndex(x => x.RevisionId == CurrentRevision.RevisionId);
                revisions[index] = revision;
            }
        }

        public void AdvanceCurrentRevision()
        {
            lock (sync)
            {
                var current = revisions.MaxBy(x => x.VersionNumber)!;
                revisions.Add(current with
                {
                    RevisionId = Guid.NewGuid(), ParentRevisionId = current.RevisionId,
                    VersionNumber = current.VersionNumber + 1
                });
            }
        }

        public Task<CandidateActionResult> TryApplyAtomicallyAsync(
            CandidatePreviewSnapshot expected, PlanRevisionSnapshot proposed, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ApplyCalls++;
            if (ApplyFailure == MutationFailure.TimeoutBeforeMutation)
                throw new TimeoutException("private apply timeout");
            lock (sync)
            {
                if (ReturnConfirmedRollback)
                    return Task.FromResult(new CandidateActionResult(
                        CandidateActionStatus.PersistenceFailure, expected));
                var currentCandidate = candidates.GetValueOrDefault(expected.CandidateId);
                var terminal = Guard(currentCandidate, expected, now);
                if (terminal is not null) return Task.FromResult(terminal);
                if (revisions.MaxBy(x => x.VersionNumber)!.RevisionId != expected.BaseRevisionId)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.StaleBaseRevision, currentCandidate));
                var applied = currentCandidate! with { Status = CandidatePreviewStatus.Applied };
                candidates[expected.CandidateId] = applied;
                revisions.Add(proposed);
                if (ApplyFailure == MutationFailure.CommitThenContradictoryResponse)
                    return Task.FromResult(new CandidateActionResult(
                        CandidateActionStatus.Applied,
                        applied with { CandidateId = Guid.NewGuid() },
                        proposed with { PlanId = Guid.NewGuid() }));
                if (ApplyFailure == MutationFailure.CommitThenAcknowledgementLost)
                    throw new HttpRequestException("private apply acknowledgement lost");
                var response = new CandidateActionResult(CandidateActionStatus.Applied, applied, proposed);
                return Task.FromResult(ApplyResponseTransform is null ? response : ApplyResponseTransform(response)!);
            }
        }

        public Task<CandidateActionResult> TryRejectAtomicallyAsync(
            CandidatePreviewSnapshot expected, DateTimeOffset now, CancellationToken cancellationToken)
        {
            RejectCalls++;
            if (RejectFailure == MutationFailure.TimeoutBeforeMutation)
                throw new TimeoutException("private reject timeout");
            lock (sync)
            {
                var current = candidates.GetValueOrDefault(expected.CandidateId);
                var terminal = Guard(current, expected, now);
                if (terminal is not null) return Task.FromResult(terminal);
                var rejected = current! with { Status = CandidatePreviewStatus.Rejected };
                candidates[expected.CandidateId] = rejected;
                if (RejectFailure == MutationFailure.CommitThenContradictoryResponse)
                    return Task.FromResult(new CandidateActionResult(
                        CandidateActionStatus.Rejected,
                        rejected with { CandidateId = Guid.NewGuid() }));
                if (RejectFailure == MutationFailure.CommitThenAcknowledgementLost)
                    throw new HttpRequestException("private reject acknowledgement lost");
                var response = new CandidateActionResult(CandidateActionStatus.Rejected, rejected);
                return Task.FromResult(RejectResponseTransform is null ? response : RejectResponseTransform(response)!);
            }
        }

        public Task<CandidateActionResult> TryExpireAtomicallyAsync(
            CandidatePreviewSnapshot expected, DateTimeOffset now, CancellationToken cancellationToken)
        {
            ExpireCalls++;
            if (ExpireFailure == ExpireFailure.TimeoutBeforeMutation)
                throw new TimeoutException("private expiration timeout");
            if (ExpireFailure == ExpireFailure.TransportBeforeMutation)
                throw new HttpRequestException("private expiration transport failure");
            if (ExpireFailure == ExpireFailure.UnexpectedBeforeMutation)
                throw new InvalidOperationException("private expiration failure");
            if (ExpireFailure == ExpireFailure.DependencyCancellation)
                throw new OperationCanceledException("private dependency cancellation");
            lock (sync)
            {
                var current = candidates.GetValueOrDefault(expected.CandidateId);
                var terminal = Guard(current, expected, now, allowExpiry: true);
                if (terminal is not null) return Task.FromResult(terminal);
                if (ReturnConfirmedExpirationRollback)
                    return Task.FromResult(new CandidateActionResult(
                        CandidateActionStatus.PersistenceFailure, current));
                if (now < current!.ExpiresAtUtc)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.PersistenceFailure, current));
                if (ExpireFailure == ExpireFailure.UnauthorizedResponse)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.Unauthorized, current));
                if (ExpireFailure == ExpireFailure.IncorrectResponse)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.Expired, current));
                var expired = current with { Status = CandidatePreviewStatus.Expired };
                candidates[expected.CandidateId] = expired;
                if (ExpireFailure == ExpireFailure.CommitThenIncorrectResponse)
                    return Task.FromResult(new CandidateActionResult(
                        CandidateActionStatus.Expired,
                        expired with { CandidateId = Guid.NewGuid() }));
                AfterExpirationCommit?.Invoke();
                if (ExpireFailure == ExpireFailure.CommitThenAcknowledgementLost)
                    throw new HttpRequestException("private expiration acknowledgement lost");
                if (ExpireFailure == ExpireFailure.CallerCancellationAfterCommit)
                    throw new OperationCanceledException("caller cancelled after possible commit", cancellationToken);
                var response = new CandidateActionResult(CandidateActionStatus.Expired, expired);
                return Task.FromResult(ExpireResponseTransform is null ? response : ExpireResponseTransform(response)!);
            }
        }

        private static CandidateActionResult? Guard(
            CandidatePreviewSnapshot? current, CandidatePreviewSnapshot expected, DateTimeOffset now,
            bool allowExpiry = false)
        {
            if (current is null) return new(CandidateActionStatus.NotFound);
            if (current.OwnerId != expected.OwnerId || current.ProjectId != expected.ProjectId ||
                current.PlanId != expected.PlanId)
                return new(CandidateActionStatus.Unauthorized, current);
            if (current.Status == CandidatePreviewStatus.Applied)
                return new(CandidateActionStatus.AlreadyApplied, current);
            if (current.Status == CandidatePreviewStatus.Rejected)
                return new(CandidateActionStatus.AlreadyRejected, current);
            if (current.Status == CandidatePreviewStatus.Expired)
                return new(CandidateActionStatus.Expired, current);
            if (!allowExpiry && now >= current.ExpiresAtUtc)
                return new(CandidateActionStatus.Expired, current);
            return null;
        }

        public Task<PlanRevisionSnapshot?> GetAsync(
            Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
        {
            lock (sync) return Task.FromResult(revisions.SingleOrDefault(
                x => x.ProjectId == projectId && x.PlanId == planId && x.RevisionId == revisionId));
        }

        public Task<PlanRevisionSnapshot?> GetCurrentAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                CurrentReadCalls++;
                if (CurrentException is not null &&
                    (CurrentExceptionOnCall is null || CurrentExceptionOnCall == CurrentReadCalls))
                    return Task.FromException<PlanRevisionSnapshot?>(CurrentException);
                var current = revisions.MaxBy(x => x.VersionNumber)!;
                return Task.FromResult<PlanRevisionSnapshot?>(
                    CurrentResponseTransform?.Invoke(CurrentReadCalls, current) ?? current);
            }
        }

        public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken)
        {
            lock (sync) return Task.FromResult<IReadOnlyList<PlanRevisionSnapshot>>(
                revisions.Where(x => x.ProjectId == projectId && x.PlanId == planId).ToArray());
        }

        public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
            Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Candidate tests require the combined candidate/revision transaction.");
    }

    private enum CandidateCreateFailure { None, BeforeMutation, AfterStored }
    private enum MutationFailure
    {
        None,
        TimeoutBeforeMutation,
        CommitThenAcknowledgementLost,
        CommitThenContradictoryResponse
    }
    private enum ExpireFailure
    {
        None,
        TimeoutBeforeMutation,
        TransportBeforeMutation,
        UnexpectedBeforeMutation,
        DependencyCancellation,
        CommitThenAcknowledgementLost,
        CommitThenIncorrectResponse,
        CallerCancellationAfterCommit,
        UnauthorizedResponse,
        IncorrectResponse
    }

    private static ConfirmedLandRevision Land(Guid projectId, Guid revisionId)
    {
        using var document = JsonDocument.Parse("{\"type\":\"Polygon\",\"coordinates\":[]}");
        return new(projectId, revisionId, "test-only/1", document.RootElement.Clone());
    }

    private static PlanEditCommandDto ValidCommand()
    {
        using var payload = JsonDocument.Parse("{\"deltaX\":1,\"deltaY\":0}");
        return new()
        {
            CommandId = Guid.NewGuid(), CommandType = PlanEditCommandTypes.MoveRoom,
            FloorId = Guid.NewGuid(), TargetId = Guid.NewGuid(), Payload = payload.RootElement.Clone()
        };
    }

    private static PlanGeometryDto Geometry(Guid projectId, Guid planId, Guid landId, Guid requirementsId) => new()
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = projectId,
        PlanId = planId,
        LandRevisionId = landId,
        RequirementsRevisionId = requirementsId,
        CoordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = 0, Y = 0 },
            XDirection = "right", YDirection = "up"
        },
        Floors = [new FloorDto { Id = Guid.NewGuid(), Level = 0, Rooms = [], Walls = [], Openings = [] }]
    };
}

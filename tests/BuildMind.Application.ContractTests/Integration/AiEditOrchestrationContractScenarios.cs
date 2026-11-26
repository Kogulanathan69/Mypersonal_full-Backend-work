using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

internal static class AiEditOrchestrationContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("AI edit creates one pending candidate without appending revision", () =>
        {
            var scenario = new Scenario();
            var before = scenario.Persistence.Current.GeometryJson;
            var result = scenario.Execute();
            Check(result is { Status: AiEditOrchestrationStatus.CandidateCreated,
                Candidate.Status: CandidatePreviewStatus.Pending }, "Pending candidate was not created.");
            Check(scenario.Persistence.RevisionCount == 1, "AI preview appended a revision.");
            Check(scenario.Persistence.Current.GeometryJson == before, "AI preview mutated saved geometry.");
            Check(scenario.Persistence.AppliedCount == 0, "AI preview automatically applied a candidate.");
        });

        run("AI edit empty actor is rejected before revision or provider access", () =>
        {
            var scenario = new Scenario();
            var result = scenario.Service.PreviewAsync(
                scenario.Request(),
                Guid.Empty).GetAwaiter().GetResult();
            Check(result.Status == AiEditOrchestrationStatus.Unauthorized, "Empty actor was accepted.");
            Check(scenario.Persistence.ReadCount == 0 && scenario.AiCalls == 0,
                "Unauthorized request accessed revisions or AI.");
        });

        run("AI edit unauthorized project is rejected before revision or provider access", () =>
        {
            var scenario = new Scenario { Owned = false };
            var result = scenario.Execute();
            Check(result.Status == AiEditOrchestrationStatus.Unauthorized, "Non-owner was accepted.");
            Check(scenario.Persistence.ReadCount == 0 && scenario.AiCalls == 0,
                "Non-owner request accessed revisions or AI.");
        });

        run("AI edit invalid required identifiers are rejected", () =>
        {
            foreach (var request in new[]
            {
                new AiEditPreviewCommand(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "move room", "c"),
                new AiEditPreviewCommand(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), "move room", "c"),
                new AiEditPreviewCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, "move room", "c")
            })
            {
                var scenario = new Scenario();
                Check(scenario.Service.PreviewAsync(request, scenario.OwnerId).Result.Status
                    == AiEditOrchestrationStatus.InvalidRequest, "Invalid identifier was accepted.");
                Check(scenario.OwnershipCalls == 0 && scenario.AiCalls == 0,
                    "Invalid request reached ownership or AI.");
            }
        });

        run("AI edit empty and whitespace instructions are rejected", () =>
        {
            foreach (var instruction in new[] { "", "   " })
            {
                var scenario = new Scenario();
                Check(scenario.Execute(instruction: instruction).Status
                    == AiEditOrchestrationStatus.InvalidRequest, "Blank instruction was accepted.");
                Check(scenario.AiCalls == 0, "Blank instruction reached AI.");
            }
        });

        run("AI edit missing revision is rejected without provider invocation", () =>
        {
            var scenario = new Scenario();
            scenario.Persistence.ReturnMissing = true;
            Check(scenario.Execute().Status == AiEditOrchestrationStatus.RevisionNotFound,
                "Missing revision was not rejected.");
            Check(scenario.AiCalls == 0, "Missing revision reached AI.");
        });

        run("AI edit cross-project and cross-plan revision responses are rejected", () =>
        {
            foreach (var crossProject in new[] { true, false })
            {
                var scenario = new Scenario();
                scenario.Persistence.IdentityOverride = crossProject
                    ? (Guid.NewGuid(), scenario.PlanId)
                    : (scenario.ProjectId, Guid.NewGuid());
                Check(scenario.Execute().Status == AiEditOrchestrationStatus.InvalidDependencyResponse,
                    "Cross-scope revision response was accepted.");
                Check(scenario.AiCalls == 0, "Cross-scope revision reached AI.");
            }
        });

        run("AI edit stale base is rejected without provider invocation", () =>
        {
            var scenario = new Scenario();
            Check(scenario.Execute(baseRevisionId: Guid.NewGuid()).Status
                == AiEditOrchestrationStatus.StaleBaseRevision, "Stale base was accepted.");
            Check(scenario.AiCalls == 0, "Stale base reached AI.");
        });

        run("AI edit stale land and requirements are rejected before provider", () =>
        {
            var staleLand = new Scenario { CurrentLandId = Guid.NewGuid() };
            var staleRequirements = new Scenario { CurrentRequirementsId = Guid.NewGuid() };
            Check(staleLand.Execute().Status == AiEditOrchestrationStatus.StaleInputs,
                "Stale land was accepted.");
            Check(staleRequirements.Execute().Status == AiEditOrchestrationStatus.StaleInputs,
                "Stale requirements were accepted.");
            Check(staleLand.AiCalls == 0 && staleRequirements.AiCalls == 0,
                "Stale inputs reached AI.");
        });

        run("AI edit provider unavailable and rejected statuses remain truthful", () =>
        {
            var unavailable = new Scenario { ProposalStatus = AiEditProposalStatus.Unavailable };
            var rejected = new Scenario { ProposalStatus = AiEditProposalStatus.Rejected };
            Check(unavailable.Execute().Status == AiEditOrchestrationStatus.ProposalUnavailable,
                "Unavailable AI was fabricated as success.");
            Check(rejected.Execute().Status == AiEditOrchestrationStatus.ProposalRejected,
                "Rejected AI proposal was fabricated as success.");
            Check(unavailable.Persistence.CandidateCount == 0 && rejected.Persistence.CandidateCount == 0,
                "Failed proposal persisted a candidate.");
        });

        run("AI edit null and unknown proposals fail closed", () =>
        {
            var nullProposal = new Scenario { ReturnNullProposal = true };
            var unknown = new Scenario { ProposalStatus = (AiEditProposalStatus)999 };
            Check(nullProposal.Execute().Status == AiEditOrchestrationStatus.ProposalInvalid,
                "Null proposal was accepted.");
            Check(unknown.Execute().Status == AiEditOrchestrationStatus.ProposalInvalid,
                "Unknown proposal status was accepted.");
        });

        run("AI edit null empty multiple and null command collections fail closed", () =>
        {
            foreach (var mode in new[] { ProposalMode.NullCollection, ProposalMode.Empty,
                         ProposalMode.Multiple, ProposalMode.NullCommand })
            {
                var scenario = new Scenario { ProposalMode = mode };
                Check(scenario.Execute().Status == AiEditOrchestrationStatus.ProposalInvalid,
                    $"Invalid proposal collection {mode} was accepted.");
                Check(scenario.ComputeCalls == 0 && scenario.Persistence.CandidateCount == 0,
                    "Invalid proposal was partially processed.");
            }
        });

        run("AI edit unsupported command invalid identifiers and payload fail before compute", () =>
        {
            foreach (var mode in new[]
            {
                ProposalMode.EmptyCommandId,
                ProposalMode.EmptyFloorId,
                ProposalMode.EmptyTargetId,
                ProposalMode.UnsupportedCommand,
                ProposalMode.InvalidPayload
            })
            {
                var scenario = new Scenario { ProposalMode = mode };
                Check(scenario.Execute().Status == AiEditOrchestrationStatus.ProposalInvalid,
                    $"Invalid command {mode} was accepted.");
                Check(scenario.ComputeCalls == 0, "Invalid AI command reached compute.");
            }
        });

        run("AI edit provider HTTP timeout and dependency cancellation are safe", () =>
        {
            var http = new Scenario { ProviderException = new HttpRequestException("secret provider endpoint") };
            var timeout = new Scenario { ProviderException = new TimeoutException("secret deadline") };
            var cancelled = new Scenario { ProviderException = new OperationCanceledException("provider deadline") };
            Check(http.Execute().Status == AiEditOrchestrationStatus.DependencyUnavailable,
                "HTTP failure mapping changed.");
            Check(timeout.Execute().Status == AiEditOrchestrationStatus.DependencyTimeout,
                "Timeout mapping changed.");
            Check(cancelled.Execute().Status == AiEditOrchestrationStatus.DependencyTimeout,
                "Dependency cancellation mapping changed.");
            Check(!http.Execute().Diagnostic!.SafeMessage.Contains("secret", StringComparison.OrdinalIgnoreCase),
                "Provider detail escaped into safe diagnostic.");
        });

        run("AI edit caller cancellation is preserved", () =>
        {
            var scenario = new Scenario();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            CheckThrows<OperationCanceledException>(() => scenario.Service.PreviewAsync(
                scenario.Request(), scenario.OwnerId, cancellation.Token).GetAwaiter().GetResult());
            Check(scenario.AiCalls == 0, "Cancelled caller reached AI.");
        });

        run("AI preflight and unexpected provider failures remain distinct", () =>
        {
            var ownershipTimeout = new Scenario { OwnershipException = new TimeoutException("private") };
            var inputTransport = new Scenario { InputException = new HttpRequestException("private") };
            var invalidInputs = new Scenario { ReturnInvalidInputs = true };
            var unexpectedProvider = new Scenario
                { ProviderException = new InvalidOperationException("private provider defect") };
            Check(ownershipTimeout.Execute().Status == AiEditOrchestrationStatus.DependencyTimeout,
                "AI ownership timeout was misclassified.");
            Check(inputTransport.Execute().Status == AiEditOrchestrationStatus.DependencyUnavailable,
                "AI input transport failure was misclassified.");
            Check(invalidInputs.Execute().Status == AiEditOrchestrationStatus.InvalidDependencyResponse &&
                invalidInputs.AiCalls == 0, "Invalid AI preflight inputs reached provider.");
            Check(unexpectedProvider.Execute().Status == AiEditOrchestrationStatus.UnexpectedInternalFailure,
                "Unexpected provider defect was labeled malformed proposal.");
        });

        run("AI edit raw provider diagnostics are not returned", () =>
        {
            var scenario = new Scenario
            {
                ProposalStatus = AiEditProposalStatus.Rejected,
                RawDiagnosticMessage = "private prompt api_key=do-not-expose"
            };
            var result = scenario.Execute();
            Check(result.Diagnostic is not null &&
                !result.Diagnostic.SafeMessage.Contains("api_key", StringComparison.OrdinalIgnoreCase),
                "Raw AI diagnostic was exposed.");
        });

        run("AI edit correlation propagates to AI and compute", () =>
        {
            var scenario = new Scenario();
            var result = scenario.Execute(correlationId: "trusted-ai-correlation");
            Check(result.Status == AiEditOrchestrationStatus.CandidateCreated, "AI preview failed.");
            Check(scenario.AiCorrelation == "trusted-ai-correlation" &&
                scenario.ComputeCorrelation == "trusted-ai-correlation", "Correlation ID was not propagated.");
        });

        run("AI edit validation unavailable and rejected remain truthful", () =>
        {
            var unavailable = new Scenario { ValidationStatus = ValidationEvidenceStatus.Unavailable };
            var rejected = new Scenario { ValidationStatus = ValidationEvidenceStatus.Rejected };
            Check(unavailable.Execute().Status == AiEditOrchestrationStatus.ValidationUnavailable,
                "Unavailable validation was fabricated as success.");
            Check(rejected.Execute().Status == AiEditOrchestrationStatus.ValidationRejected,
                "Rejected validation was fabricated as success.");
            Check(unavailable.Persistence.CandidateCount == 0 && rejected.Persistence.CandidateCount == 0,
                "Failed validation persisted a candidate.");
        });

        run("AI edit candidate rejection remains truthful and exposes no dependency diagnostics", () =>
        {
            var scenario = new Scenario
            {
                ComputeStatus = PlanEditStatus.Rejected,
                RawComputeDiagnostic = "private compute implementation detail"
            };
            var result = scenario.Execute();
            Check(result.Status == AiEditOrchestrationStatus.CandidateRejected,
                "Rejected candidate was fabricated as success.");
            Check(result.Diagnostic is not null &&
                !result.Diagnostic.SafeMessage.Contains("private", StringComparison.OrdinalIgnoreCase),
                "Raw candidate dependency diagnostic was exposed.");
            Check(scenario.Persistence.CandidateCount == 0,
                "Rejected candidate was persisted.");
        });

        run("AI edit base change during provider call is rejected downstream", () =>
        {
            var scenario = new Scenario();
            scenario.DuringProvider = scenario.Persistence.AdvanceCurrent;
            Check(scenario.Execute().Status == AiEditOrchestrationStatus.StaleBaseRevision,
                "Base change during AI call was accepted.");
            Check(scenario.Persistence.CandidateCount == 0, "Stale AI response persisted a candidate.");
        });

        run("AI edit input changes during provider call are rejected downstream", () =>
        {
            foreach (var changeLand in new[] { true, false })
            {
                var scenario = new Scenario();
                scenario.DuringProvider = () =>
                {
                    if (changeLand) scenario.CurrentLandId = Guid.NewGuid();
                    else scenario.CurrentRequirementsId = Guid.NewGuid();
                };
                Check(scenario.Execute().Status == AiEditOrchestrationStatus.StaleInputs,
                    "Input change during AI call was accepted.");
            }
        });

        run("AI edit ownership revocation during provider call is rejected downstream", () =>
        {
            var scenario = new Scenario();
            scenario.DuringProvider = () => scenario.Owned = false;
            Check(scenario.Execute().Status == AiEditOrchestrationStatus.Unauthorized,
                "Revoked ownership was accepted.");
            Check(scenario.Persistence.CandidateCount == 0, "Revoked ownership persisted a candidate.");
        });

        run("AI edit rejects cross-project and cross-plan revisions after provider response", () =>
        {
            foreach (var wrongProject in new[] { true, false })
            {
                var scenario = new Scenario();
                scenario.Persistence.IdentityOverride = wrongProject
                    ? (Guid.NewGuid(), scenario.PlanId)
                    : (scenario.ProjectId, Guid.NewGuid());
                scenario.Persistence.IdentityOverrideReadNumber = 2;
                Check(scenario.Execute().Status == AiEditOrchestrationStatus.InvalidDependencyResponse,
                    "Cross-scope downstream revision was accepted.");
                Check(scenario.ComputeCalls == 0 && scenario.Persistence.CandidateCount == 0,
                    "Cross-scope geometry reached compute or candidate persistence.");
            }
        });

        run("AI edit candidate persistence acknowledgement loss requires reconciliation without replay", () =>
        {
            var scenario = new Scenario();
            scenario.Persistence.LoseCreateAcknowledgement = true;
            var result = scenario.Execute();
            Check(result.Status == AiEditOrchestrationStatus.CandidatePersistenceOutcomeUnknown,
                "Uncertain candidate persistence was reported as proposal or candidate rejection.");
            Check(result.Candidate is not null && scenario.Persistence.CandidateCount == 1,
                "Stable candidate reconciliation identity was not preserved.");
            Check(scenario.AiCalls == 1 && scenario.Persistence.CreateCalls == 1,
                "AI proposal or candidate persistence was automatically replayed.");
            Check(scenario.Persistence.RevisionCount == 1 && scenario.Persistence.AppliedCount == 0,
                "Uncertain candidate creation appended or applied a revision.");
        });

        run("GVR18 malformed stored revision never reaches AI", () =>
        {
            foreach (var transform in new Func<PlanRevisionSnapshot, PlanRevisionSnapshot>[]
            {
                r => r with { RevisionId = Guid.Empty },
                r => r with { VersionNumber = 0 },
                r => r with { VersionNumber = long.MaxValue },
                r => r with { GeometrySchemaVersion = "invalid" },
                r => r with { GeometryJson = r.GeometryJson + " " },
                r => r with { GeometryHash = "invalid" },
                r => r with { Validation = null! },
                r => r with { LandRevisionId = Guid.NewGuid() },
                r => r with { RequirementsRevisionId = Guid.NewGuid() }
            })
            {
                var scenario = new Scenario();
                scenario.Persistence.CurrentTransform = transform;
                var result = scenario.Execute();
                Check(result.Status == AiEditOrchestrationStatus.InvalidDependencyResponse &&
                    scenario.AiCalls == 0 && scenario.ComputeCalls == 0 && scenario.Persistence.CreateCalls == 0,
                    "Invalid stored revision reached AI or persistence.");
            }
        });

        run("GVR19 revoked ownership or late ownership failure prevents AI invocation", () =>
        {
            var revoked = new Scenario();
            revoked.DuringInputs = () => revoked.Owned = false;
            Check(revoked.Execute().Status == AiEditOrchestrationStatus.Unauthorized &&
                revoked.OwnershipCalls == 2 && revoked.AiCalls == 0, "Revoked ownership reached AI.");
            var failed = new Scenario();
            failed.DuringOwnership = () => { if (failed.OwnershipCalls == 2)
                failed.OwnershipException = new TimeoutException("private"); };
            Check(failed.Execute().Status == AiEditOrchestrationStatus.DependencyTimeout &&
                failed.AiCalls == 0 && failed.Persistence.CreateCalls == 0, "Late authorization failure reached AI.");
        });

        run("GVR20 cancellation at read authorization and provider boundaries stops downstream work", () =>
        {
            for (var phase = 0; phase < 5; phase++)
            {
                using var token = new CancellationTokenSource();
                var scenario = new Scenario();
                if (phase == 0) scenario.DuringOwnership = token.Cancel;
                if (phase == 1) scenario.Persistence.DuringRead = token.Cancel;
                if (phase == 2) scenario.DuringInputs = token.Cancel;
                if (phase == 3) scenario.DuringOwnership = () => { if (scenario.OwnershipCalls == 2) token.Cancel(); };
                if (phase == 4) scenario.DuringProvider = token.Cancel;
                CheckThrows<OperationCanceledException>(() => scenario.Service.PreviewAsync(
                    scenario.Request(), scenario.OwnerId, token.Token).GetAwaiter().GetResult());
                Check(scenario.AiCalls == (phase == 4 ? 1 : 0) && scenario.ComputeCalls == 0 &&
                    scenario.Persistence.CreateCalls == 0, "Cancelled request proceeded downstream.");
                if (phase == 0) Check(scenario.Persistence.ReadCount == 0, "Cancelled authorization reached read.");
            }
        });

        run("GVR21 and GVR23 malformed dependency failures retain matching diagnostics", () =>
        {
            foreach (var scenario in new[]
            {
                new Scenario { OwnershipException = new JsonException("private") },
                new Scenario { InputException = new FormatException("private") },
                new Scenario { ProviderException = new JsonException("private") }
            })
            {
                var result = scenario.Execute();
                Check(result is { Status: AiEditOrchestrationStatus.InvalidDependencyResponse,
                    Diagnostic.Code: PlanEditorErrorCodes.InvalidDependencyResponse,
                    Diagnostic.RetrySafety: PlanEditorRetrySafety.Never } &&
                    scenario.Persistence.CreateCalls == 0, "Malformed dependency classification changed.");
            }
            var read = new Scenario();
            read.Persistence.ReadException = new JsonException("private");
            Check(read.Execute() is { Status: AiEditOrchestrationStatus.InvalidDependencyResponse,
                Diagnostic.Code: PlanEditorErrorCodes.InvalidDependencyResponse } && read.AiCalls == 0,
                "Malformed revision read reached AI.");
            var provider = new Scenario { ProviderException = new InvalidOperationException("private") };
            Check(provider.Execute() is { Status: AiEditOrchestrationStatus.UnexpectedInternalFailure,
                Diagnostic.Code: PlanEditorErrorCodes.UnexpectedInternalFailure }, "Provider defect mismatch.");
        });

        run("GVR22 escaping candidate workflow exceptions require reconciliation", () =>
        {
            foreach (var exception in new Exception[] { new TimeoutException("private"),
                new HttpRequestException("private"), new InvalidOperationException("private") })
            {
                var scenario = new Scenario { ClockException = exception };
                var result = scenario.Execute();
                Check(result is { Status: AiEditOrchestrationStatus.CandidatePersistenceOutcomeUnknown,
                    Candidate: null, Diagnostic.Code: PlanEditorErrorCodes.PersistenceOutcomeUnknown,
                    Diagnostic.RetrySafety: PlanEditorRetrySafety.Conditional } &&
                    scenario.AiCalls == 1 && scenario.Persistence.CreateCalls == 0 &&
                    !result.Diagnostic!.SafeMessage.Contains("private", StringComparison.Ordinal),
                    "Escaping candidate failure allowed blind retry or fabricated identity.");
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

    private enum ProposalMode
    {
        Valid,
        NullCollection,
        Empty,
        Multiple,
        NullCommand,
        UnsupportedCommand,
        InvalidPayload,
        EmptyCommandId,
        EmptyFloorId,
        EmptyTargetId
    }

    private sealed class Scenario
    {
        public Guid OwnerId { get; } = Guid.NewGuid();
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Guid PlanId { get; } = Guid.NewGuid();
        public bool Owned { get; set; } = true;
        public Exception? OwnershipException { get; set; }
        public Exception? InputException { get; set; }
        public bool ReturnInvalidInputs { get; set; }
        public int OwnershipCalls { get; private set; }
        public int AiCalls { get; private set; }
        public int ComputeCalls { get; private set; }
        public string? AiCorrelation { get; private set; }
        public string? ComputeCorrelation { get; private set; }
        public Guid CurrentLandId { get; set; }
        public Guid CurrentRequirementsId { get; set; }
        public AiEditProposalStatus ProposalStatus { get; set; } = AiEditProposalStatus.Proposed;
        public ProposalMode ProposalMode { get; set; }
        public bool ReturnNullProposal { get; set; }
        public string RawDiagnosticMessage { get; set; } = "provider diagnostic";
        public Exception? ProviderException { get; set; }
        public Action? DuringProvider { get; set; }
        public Action? DuringInputs { get; set; }
        public Action? DuringOwnership { get; set; }
        public Exception? ClockException { get; set; }
        public ValidationEvidenceStatus ValidationStatus { get; set; } = ValidationEvidenceStatus.Verified;
        public string ComputeStatus { get; set; } = PlanEditStatus.Accepted;
        public string RawComputeDiagnostic { get; set; } = "";
        public AiPersistenceTestDouble Persistence { get; }
        public AiEditOrchestrationService Service { get; }

        public Scenario()
        {
            CurrentLandId = Guid.NewGuid();
            CurrentRequirementsId = Guid.NewGuid();
            var geometry = Geometry(ProjectId, PlanId, CurrentLandId, CurrentRequirementsId);
            var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
            var hash = PlanRevisionService.ComputeGeometryHash(json);
            Persistence = new AiPersistenceTestDouble(new PlanRevisionSnapshot(
                Guid.NewGuid(), PlanId, ProjectId, null, 1, geometry.SchemaVersion, json, hash,
                CurrentLandId, CurrentRequirementsId, Validation(hash), null, OwnerId,
                DateTimeOffset.Parse("2026-11-17T12:00:00Z"), "base"));

            var ownership = new TestProjectOwnershipGate((project, actor, _) =>
            {
                OwnershipCalls++;
                DuringOwnership?.Invoke();
                return OwnershipException is null
                    ? Task.FromResult(Owned && project == ProjectId && actor == OwnerId)
                    : Task.FromException<bool>(OwnershipException);
            });
            var inputs = new TestApplicableInputRevisionSource((_, _) =>
            {
                DuringInputs?.Invoke();
                return InputException is null
                    ? Task.FromResult(ReturnInvalidInputs
                        ? new ApplicableInputRevisions(Guid.Empty, CurrentRequirementsId)
                        : new ApplicableInputRevisions(CurrentLandId, CurrentRequirementsId))
                    : Task.FromException<ApplicableInputRevisions>(InputException);
            });
            var ai = new TestAiEditProposalPort((_, _, _, _, correlation, _) =>
            {
                AiCalls++;
                AiCorrelation = correlation;
                DuringProvider?.Invoke();
                if (ProviderException is not null) return Task.FromException<AiEditProposal>(ProviderException);
                if (ReturnNullProposal) return Task.FromResult<AiEditProposal>(null!);
                return Task.FromResult(Proposal());
            });
            var compute = new TestPlanEditorComputeClient((request, _) =>
            {
                ComputeCalls++;
                ComputeCorrelation = request.CorrelationId;
                var candidate = Geometry(ProjectId, PlanId, CurrentLandId, CurrentRequirementsId);
                return Task.FromResult(new PlanEditResultDto
                {
                    SchemaVersion = candidate.SchemaVersion,
                    CommandId = request.Command.CommandId,
                    BaseRevisionId = Persistence.Current.RevisionId,
                    Status = ComputeStatus,
                    CandidateGeometry = ComputeStatus == PlanEditStatus.Rejected ? null : candidate,
                    Diagnostics = string.IsNullOrEmpty(RawComputeDiagnostic) ? [] : [new PlanEditDiagnosticDto
                    {
                        Code = "RAW_COMPUTE", Message = RawComputeDiagnostic, Severity = "error"
                    }],
                    ChangedEntityIds = []
                });
            });
            var candidateService = new CandidatePreviewService(
                ownership,
                Persistence,
                inputs,
                new TestConfirmedLandRevisionPort((project, revision, _) =>
                    Task.FromResult<ConfirmedLandRevision?>(Land(project, revision))),
                new TestAuthorizedRequirementRevisionPort((project, revision, actor, _) =>
                    Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Found,
                        new RequirementRevisionReference(project, revision, "test-only/1"),
                        new ProjectAuthorizationSnapshot(project, actor, new AuthorizationVersion(1))))),
                compute,
                new AiValidationEvidenceSource((_, _, exactHash, _) =>
                    Task.FromResult<PlanValidationReferenceDto?>(Validation(exactHash))),
                new TestPlanValidationEvidenceVerifier((_, _, _, _, _) => Task.FromResult(ValidationStatus)),
                Persistence,
                Persistence,
                new AiTestClock(() => ClockException),
                TimeSpan.FromMinutes(15));
            Service = new AiEditOrchestrationService(ownership, Persistence, inputs, ai, candidateService);
        }

        public AiEditPreviewCommand Request(
            Guid? baseRevisionId = null,
            string instruction = "move the room",
            string correlationId = "ai-contract-correlation") =>
            new(ProjectId, PlanId, baseRevisionId ?? Persistence.Current.RevisionId, instruction, correlationId);

        public AiEditOrchestrationResult Execute(
            Guid? actor = null,
            Guid? baseRevisionId = null,
            string instruction = "move the room",
            string correlationId = "ai-contract-correlation") =>
            Service.PreviewAsync(Request(baseRevisionId, instruction, correlationId), actor ?? OwnerId).Result;

        private AiEditProposal Proposal()
        {
            var diagnostic = new PlanEditDiagnosticDto
            {
                Code = "RAW_PROVIDER", Message = RawDiagnosticMessage, Severity = "error"
            };
            IReadOnlyList<PlanEditCommandDto> commands = ProposalMode switch
            {
                ProposalMode.NullCollection => null!,
                ProposalMode.Empty => [],
                ProposalMode.Multiple => [ValidCommand(), ValidCommand()],
                ProposalMode.NullCommand => [null!],
                ProposalMode.UnsupportedCommand => [Command("executeCode", "{}")],
                ProposalMode.InvalidPayload => [Command(PlanEditCommandTypes.MoveRoom, "{}")],
                ProposalMode.EmptyCommandId => [CommandWithIdentifiers(Guid.Empty, Guid.NewGuid(), Guid.NewGuid())],
                ProposalMode.EmptyFloorId => [CommandWithIdentifiers(Guid.NewGuid(), Guid.Empty, Guid.NewGuid())],
                ProposalMode.EmptyTargetId => [CommandWithIdentifiers(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty)],
                _ => [ValidCommand()]
            };
            return new AiEditProposal(ProposalStatus, commands, [diagnostic]);
        }

        private static PlanEditCommandDto ValidCommand() =>
            Command(PlanEditCommandTypes.MoveRoom, "{\"deltaX\":1,\"deltaY\":0}");

        private static PlanEditCommandDto CommandWithIdentifiers(
            Guid commandId,
            Guid floorId,
            Guid targetId)
        {
            using var payload = JsonDocument.Parse("{\"deltaX\":1,\"deltaY\":0}");
            return new PlanEditCommandDto
            {
                CommandId = commandId,
                CommandType = PlanEditCommandTypes.MoveRoom,
                FloorId = floorId,
                TargetId = targetId,
                Payload = payload.RootElement.Clone()
            };
        }

        private static PlanEditCommandDto Command(string type, string payloadJson)
        {
            using var payload = JsonDocument.Parse(payloadJson);
            return new PlanEditCommandDto
            {
                CommandId = Guid.NewGuid(), CommandType = type, FloorId = Guid.NewGuid(),
                TargetId = Guid.NewGuid(), Payload = payload.RootElement.Clone()
            };
        }

        private PlanValidationReferenceDto Validation(string hash) => new()
        {
            ValidationId = Guid.NewGuid(), ValidatorVersion = "ai-test-double/1",
            GeometryHash = hash, GeometryValid = true, RequirementsMatch = true,
            RegulatoryEvidenceStatus = "VERIFIED", HasBlockingIssues = false,
            ValidatedAtUtc = DateTimeOffset.Parse("2026-11-17T12:00:00Z")
        };
    }

    private sealed class AiTestClock(Func<Exception?> error) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => error() is { } exception
            ? throw exception : DateTimeOffset.UtcNow;
    }

    private sealed class AiValidationEvidenceSource(
        Func<Guid, Guid, string, CancellationToken, Task<PlanValidationReferenceDto?>> handler)
        : ICandidateValidationEvidenceSource
    {
        public Task<PlanValidationReferenceDto?> GetAsync(
            Guid projectId, Guid planId, string exactGeometryHash, CancellationToken cancellationToken) =>
            handler(projectId, planId, exactGeometryHash, cancellationToken);
    }

    private sealed class AiPersistenceTestDouble(PlanRevisionSnapshot initial)
        : IPlanRevisionStore, ICandidatePreviewStore, ICandidatePreviewTransaction
    {
        private readonly List<PlanRevisionSnapshot> revisions = [initial];
        private readonly Dictionary<Guid, CandidatePreviewSnapshot> candidates = [];
        public bool ReturnMissing { get; set; }
        public Func<PlanRevisionSnapshot, PlanRevisionSnapshot>? CurrentTransform { get; set; }
        public Exception? ReadException { get; set; }
        public Action? DuringRead { get; set; }
        public (Guid ProjectId, Guid PlanId)? IdentityOverride { get; set; }
        public int? IdentityOverrideReadNumber { get; set; }
        public int ReadCount { get; private set; }
        public int AppliedCount { get; private set; }
        public int CreateCalls { get; private set; }
        public bool LoseCreateAcknowledgement { get; set; }
        public int RevisionCount => revisions.Count;
        public int CandidateCount => candidates.Count;
        public PlanRevisionSnapshot Current => revisions.MaxBy(x => x.VersionNumber)!;

        public void AdvanceCurrent()
        {
            var current = Current;
            revisions.Add(current with
            {
                RevisionId = Guid.NewGuid(), ParentRevisionId = current.RevisionId,
                VersionNumber = current.VersionNumber + 1
            });
        }

        public Task<PlanRevisionSnapshot?> GetAsync(
            Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(revisions.SingleOrDefault(x => x.RevisionId == revisionId));
        }

        public Task<PlanRevisionSnapshot?> GetCurrentAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken)
        {
            ReadCount++;
            DuringRead?.Invoke();
            if (ReadException is not null) return Task.FromException<PlanRevisionSnapshot?>(ReadException);
            if (ReturnMissing) return Task.FromResult<PlanRevisionSnapshot?>(null);
            var current = Current;
            if (IdentityOverride is { } identity &&
                (IdentityOverrideReadNumber is null || IdentityOverrideReadNumber == ReadCount))
                current = current with { ProjectId = identity.ProjectId, PlanId = identity.PlanId };
            return Task.FromResult<PlanRevisionSnapshot?>(CurrentTransform?.Invoke(current) ?? current);
        }

        public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlanRevisionSnapshot>>(revisions.ToArray());

        public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
            Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision,
            CancellationToken cancellationToken) =>
            throw new Exception("AI preview must not append a revision.");

        public Task CreateAsync(CandidatePreviewSnapshot candidate, CancellationToken cancellationToken)
        {
            CreateCalls++;
            candidates.Add(candidate.CandidateId, candidate);
            if (LoseCreateAcknowledgement)
                throw new TimeoutException("private candidate acknowledgement lost");
            return Task.CompletedTask;
        }

        public Task<CandidatePreviewSnapshot?> GetAsync(Guid candidateId, CancellationToken cancellationToken) =>
            Task.FromResult(candidates.GetValueOrDefault(candidateId));

        public Task<CandidateActionResult> TryApplyAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, PlanRevisionSnapshot proposedRevision,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            AppliedCount++;
            throw new Exception("AI orchestration must not apply a candidate.");
        }

        public Task<CandidateActionResult> TryRejectAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new Exception("AI orchestration must not reject automatically.");

        public Task<CandidateActionResult> TryExpireAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, DateTimeOffset now,
            CancellationToken cancellationToken) =>
            throw new Exception("AI orchestration must not expire automatically.");
    }

    private static ConfirmedLandRevision Land(Guid projectId, Guid revisionId)
    {
        using var boundary = JsonDocument.Parse("{\"type\":\"Polygon\",\"coordinates\":[]}");
        return new(projectId, revisionId, "test-only/1", boundary.RootElement.Clone());
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

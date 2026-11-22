using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.Application.ContractTests.Integration;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

var failures = new List<string>();
var passed = 0;
string geometryFixturePath;
try
{
    geometryFixturePath = FixtureLocator.ResolveGeometryFixture(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Fixture discovery failed: {exception.Message}");
    return 2;
}

Run("geometry fixture round trip", () =>
{
    var json = File.ReadAllText(geometryFixturePath);
    var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(json, PlanEditorJson.StrictOptions) ?? throw new Exception("Deserialization returned null.");
    PlanEditorContractValidator.Validate(geometry);
    var roundTrip = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    Assert(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(roundTrip)), "Round trip lost geometry data.");
});

Run("missing required member", () =>
{
    const string invalid = "{\"schemaVersion\":\"0.1-draft\"}";
    AssertThrows<JsonException>(() => JsonSerializer.Deserialize<PlanGeometryDto>(invalid));
});

Run("unknown JSON member", () =>
{
    const string invalid = "{\"schemaVersion\":\"0.1-draft\",\"unexpected\":true}";
    AssertThrows<JsonException>(() => JsonSerializer.Deserialize<PlanGeometryDto>(invalid, PlanEditorJson.StrictOptions));
});

Run("unsupported command", () =>
{
    using var payload = JsonDocument.Parse("{}");
    var request = new PlanEditRequestDto
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        BaseRevisionId = Guid.NewGuid(),
        Command = new PlanEditCommandDto
        {
            CommandId = Guid.NewGuid(),
            CommandType = "teleportRoom",
            FloorId = Guid.NewGuid(),
            TargetId = Guid.NewGuid(),
            Payload = payload.RootElement.Clone()
        }
    };
    AssertThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(request));
});

Run("all eight command payloads accept Python-compatible shapes", () =>
{
    foreach (var (commandType, payloadJson) in ValidCommandPayloads())
        PlanEditorContractValidator.Validate(EditRequest(commandType, payloadJson));
});

Run("all eight command payloads reject unexpected properties", () =>
{
    foreach (var (commandType, payloadJson) in ValidCommandPayloads())
    {
        var node = JsonNode.Parse(payloadJson)!.AsObject();
        node["unexpected"] = true;
        AssertThrows<ArgumentException>(() =>
            PlanEditorContractValidator.Validate(EditRequest(commandType, node.ToJsonString())));
    }
});

Run("command-specific malformed payloads are rejected", () =>
{
    var invalid = new Dictionary<string, string>
    {
        [PlanEditCommandTypes.AddRoom] = "{}",
        [PlanEditCommandTypes.RemoveRoom] = "{\"removeOrphanOpenings\":null}",
        [PlanEditCommandTypes.MoveRoom] = "{\"deltaX\":1}",
        [PlanEditCommandTypes.ResizeRoom] = "{\"scaleX\":0,\"scaleY\":1}",
        [PlanEditCommandTypes.UpdateWall] = "{}",
        [PlanEditCommandTypes.AddOpening] = "{}",
        [PlanEditCommandTypes.MoveOpening] = "{\"offset\":-1}",
        [PlanEditCommandTypes.RemoveOpening] = "{\"value\":1}"
    };
    foreach (var item in invalid)
        AssertThrows<ArgumentException>(() =>
            PlanEditorContractValidator.Validate(EditRequest(item.Key, item.Value)));
});

Run("shared command fixture matches C# acceptance", () =>
{
    var samplePath = FixtureLocator.ResolveCommandFixture(geometryFixturePath);
    using var samples = JsonDocument.Parse(File.ReadAllText(samplePath));
    foreach (var sample in samples.RootElement.EnumerateArray())
    {
        var commandType = sample.GetProperty("commandType").GetString()!;
        PlanEditorContractValidator.Validate(EditRequest(commandType, sample.GetProperty("validPayload").GetRawText()));
        AssertThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(
            EditRequest(commandType, sample.GetProperty("invalidPayload").GetRawText())));
    }
});

Run("command rejects non-finite numeric payload and missing target", () =>
{
    AssertThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(
        EditRequest(PlanEditCommandTypes.MoveRoom, "{\"deltaX\":1e400,\"deltaY\":0}")));
    using var payload = JsonDocument.Parse("{\"deltaX\":1,\"deltaY\":0}");
    var request = EditRequest(PlanEditCommandTypes.MoveRoom, payload.RootElement.GetRawText());
    request = new PlanEditRequestDto
    {
        SchemaVersion = request.SchemaVersion,
        ProjectId = request.ProjectId,
        PlanId = request.PlanId,
        BaseRevisionId = request.BaseRevisionId,
        Command = new PlanEditCommandDto
        {
            CommandId = request.Command.CommandId,
            CommandType = request.Command.CommandType,
            FloorId = request.Command.FloorId,
            TargetId = null,
            Payload = request.Command.Payload
        }
    };
    AssertThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(request));
});

Run("non-finite coordinate", () =>
{
    var geometry = MinimalGeometry(double.NaN);
    AssertThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(geometry));
});

Run("all result statuses", () =>
{
    var statuses = new[]
    {
        PlanEditStatus.Accepted,
        PlanEditStatus.AcceptedWithWarnings,
        PlanEditStatus.Rejected,
        PlanEditStatus.RevisionConflict,
        PlanEditStatus.ValidationUnavailable
    };
    foreach (var status in statuses)
    {
        var result = new PlanEditResultDto
        {
            SchemaVersion = GeometrySchemaInfo.DraftVersion,
            CommandId = Guid.NewGuid(),
            BaseRevisionId = Guid.NewGuid(),
            Status = status,
            CandidateGeometry = status.StartsWith("accepted", StringComparison.Ordinal) ? MinimalGeometry(0) : null,
            Diagnostics = [],
            ChangedEntityIds = []
        };
        PlanEditorContractValidator.Validate(result);
    }
});

Run("immutable revision and stale-write protection", () =>
{
    var geometry = MinimalGeometry(0);
    var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    var hash = PlanRevisionService.ComputeGeometryHash(json);
    var validation = Validation(hash);
    var baseRevision = Revision(geometry, hash, 1);
    var store = new MemoryRevisionStore(baseRevision);
    var service = new PlanRevisionService(new OwnershipGate(true), store, new ValidationVerifier(ValidationEvidenceStatus.Verified), TimeProvider.System);
    var command = new CreatePlanRevisionCommand(
        geometry.ProjectId, geometry.PlanId, baseRevision.RevisionId, geometry,
        validation, Guid.NewGuid(), "contract test");
    var actor = Guid.NewGuid();
    var created = service.CreateAsync(command, actor).GetAwaiter().GetResult();
    Assert(created.Status == AppendRevisionStatus.Created, "Expected a created revision.");
    Assert(created.Revision?.ParentRevisionId == baseRevision.RevisionId, "Parent revision was not preserved.");
    Assert(created.Revision?.CreatedBy == actor, "Authenticated actor was not recorded.");
    var stale = service.CreateAsync(command with { EditCommandId = Guid.NewGuid() }, actor).GetAwaiter().GetResult();
    Assert(stale.Status == AppendRevisionStatus.RevisionConflict, "Stale edit did not conflict.");
});

Run("revision fails closed when authoritative validation is unavailable", () =>
{
    var geometry = MinimalGeometry(0);
    var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    var hash = PlanRevisionService.ComputeGeometryHash(json);
    var baseRevision = Revision(geometry, hash, 1);
    var store = new MemoryRevisionStore(baseRevision);
    var service = new PlanRevisionService(new OwnershipGate(true), store, new ValidationVerifier(ValidationEvidenceStatus.Unavailable), TimeProvider.System);
    var result = service.CreateAsync(
        new(geometry.ProjectId, geometry.PlanId, baseRevision.RevisionId, geometry,
            Validation(hash), Guid.NewGuid(), "must not persist"),
        Guid.NewGuid()).GetAwaiter().GetResult();
    Assert(result.Status == AppendRevisionStatus.ValidationUnavailable,
        "Unavailable authoritative validation did not fail closed.");
    Assert(store.GetHistoryAsync(geometry.ProjectId, geometry.PlanId, default).Result.Count == 1,
        "A revision was persisted despite unavailable validation.");
});

Run("duplicate edit command replay is rejected atomically", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseRevision = Revision(geometry, hash, 1);
    var store = new MemoryRevisionStore(baseRevision);
    var service = new PlanRevisionService(new OwnershipGate(true), store, new ValidationVerifier(ValidationEvidenceStatus.Verified), TimeProvider.System);
    var commandId = Guid.NewGuid();
    var first = service.CreateAsync(
        new(geometry.ProjectId, geometry.PlanId, baseRevision.RevisionId, geometry, Validation(hash), commandId, "first"),
        Guid.NewGuid()).Result;
    var replay = service.CreateAsync(
        new(geometry.ProjectId, geometry.PlanId, first.Revision!.RevisionId, geometry, Validation(hash), commandId, "replay"),
        Guid.NewGuid()).Result;
    Assert(replay.Status == AppendRevisionStatus.DuplicateCommand, "Duplicate command replay was accepted.");
    Assert(store.GetHistoryAsync(geometry.ProjectId, geometry.PlanId, default).Result.Count == 2,
        "Replay changed revision history.");
});

Run("concurrent edits allow one winner", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseRevision = Revision(geometry, hash, 1);
    var store = new MemoryRevisionStore(baseRevision);
    var service = new PlanRevisionService(new OwnershipGate(true), store, new ValidationVerifier(ValidationEvidenceStatus.Verified), TimeProvider.System);
    Task<AppendRevisionResult> Create(string summary) => service.CreateAsync(
        new(geometry.ProjectId, geometry.PlanId, baseRevision.RevisionId, geometry,
            Validation(hash), Guid.NewGuid(), summary), Guid.NewGuid());
    var results = Task.WhenAll(Create("A"), Create("B")).Result;
    Assert(results.Count(item => item.Status == AppendRevisionStatus.Created) == 1, "Expected one concurrent winner.");
    Assert(results.Count(item => item.Status == AppendRevisionStatus.RevisionConflict) == 1, "Expected one stale conflict.");
});

foreach (var restore in new[] { false, true })
{
    foreach (var scenario in new[]
    {
        "authorized",
        "empty actor",
        "non-owner",
        "cross-project",
        "ownership failure"
    })
    {
        var operation = restore ? "restore" : "create";

        Run($"M3 revision ownership security - {operation} - {scenario}", () =>
        {
            var geometry = MinimalGeometry(0);
            var json = JsonSerializer.Serialize(
                geometry, PlanEditorJson.StrictOptions);

            var hash = PlanRevisionService.ComputeGeometryHash(json);
            var validation = Validation(hash);

            var owner = Guid.NewGuid();

            var baseRevision = Revision(
                geometry, hash, 1, validation);

            var store = new TestPlanRevisionStore(baseRevision);

            var actor = scenario switch
            {
                "empty actor" => Guid.Empty,
                "non-owner" => Guid.NewGuid(),
                _ => owner
            };

            var requestedProject = scenario == "cross-project"
                ? Guid.NewGuid()
                : geometry.ProjectId;

            var ownershipCalls = 0;
            var verificationCalls = 0;

            var ownership = new TestProjectOwnershipGate(
                (project, user, _) =>
                {
                    ownershipCalls++;

                    if (scenario == "ownership failure")
                        throw new InvalidOperationException(
                            "Explicit ownership test failure.");

                    return Task.FromResult(
                        project == geometry.ProjectId &&
                        user == owner);
                });

            var verifier = new TestPlanValidationEvidenceVerifier(
                (_, _, _, _, _) =>
                {
                    verificationCalls++;
                    return Task.FromResult(
                        ValidationEvidenceStatus.Verified);
                });

            var guardedStore = new RevisionSecurityTrackingStore(store);

            var service = new PlanRevisionService(
                ownership,
                guardedStore,
                verifier,
                TimeProvider.System);

            Task<AppendRevisionResult> Execute() => restore
                ? service.RestoreAsync(
                    requestedProject,
                    geometry.PlanId,
                    baseRevision.RevisionId,
                    baseRevision.RevisionId,
                    validation,
                    actor)
                : service.CreateAsync(
                    new CreatePlanRevisionCommand(
                        requestedProject,
                        geometry.PlanId,
                        baseRevision.RevisionId,
                        geometry,
                        validation,
                        Guid.NewGuid(),
                        "ownership security test"),
                    actor);

            {
                var result = Execute().GetAwaiter().GetResult();

                var expectedStatus = scenario == "authorized"
                    ? AppendRevisionStatus.Created
                    : scenario == "ownership failure"
                        ? AppendRevisionStatus.UnexpectedInternalFailure
                        : AppendRevisionStatus.Unauthorized;

                Assert(result.Status == expectedStatus,
                    $"Unexpected revision status: {result.Status}");

                if (scenario == "authorized")
                {
                    Assert(result.Revision?.CreatedBy == owner,
                        "Trusted actor identity was not preserved.");

                    Assert(result.Revision?.ParentRevisionId ==
                        baseRevision.RevisionId,
                        "Immutable parent revision was not preserved.");
                }
                else
                {
                    Assert(result.Revision is null,
                        "Unauthorized request disclosed revision data.");
                }
            }

            var allowed = scenario == "authorized";

            Assert(ownershipCalls ==
                (scenario == "empty actor" ? 0 :
                    allowed ? (restore ? 3 : 2) : 1),
                "Unexpected ownership check count.");

            Assert(verificationCalls == (allowed ? 1 : 0),
                "Unauthorized request reached validation.");

            if (!allowed)
            {
                Assert(guardedStore.ReadCalls == 0,
                    "Unauthorized request accessed revision data.");

                Assert(guardedStore.AppendCalls == 0,
                    "Unauthorized request attempted a write.");
            }
            else
            {
                Assert(guardedStore.AppendCalls == 1,
                    "Authorized request did not append exactly once.");
            }

            var history = store.GetHistoryAsync(
                geometry.ProjectId,
                geometry.PlanId,
                default).GetAwaiter().GetResult();

            Assert(history.Count == (allowed ? 2 : 1),
                "Revision history was unexpectedly modified.");

            Assert(history.Any(item =>
                item.RevisionId == baseRevision.RevisionId &&
                item.GeometryJson == baseRevision.GeometryJson),
                "Original revision was not preserved.");
        });
    }
}

Run("exact revision confirmation and acknowledgement", () =>
{
    var geometry = MinimalGeometry(0);
    var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    var hash = PlanRevisionService.ComputeGeometryHash(json);
    var revision = Revision(geometry, hash, 2, Validation(hash));
    var store = new MemoryRevisionStore(revision);
    var transaction = new ConfirmationTransaction();
    var sink = new ConfirmationSink();
    var service = new FinalPlanConfirmationService(
        new OwnershipGate(true), store,
        new InputSource(geometry.LandRevisionId, geometry.RequirementsRevisionId),
        new FreshnessPolicy(true), transaction, sink, TimeProvider.System);
    var withoutAcknowledgement = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, false), Guid.NewGuid())
        .GetAwaiter().GetResult();
    Assert(withoutAcknowledgement.Status == ConfirmationStatus.WarningAcknowledgementRequired,
        "Unverified evidence must require acknowledgement.");
    var confirmed = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, true), Guid.NewGuid())
        .GetAwaiter().GetResult();
    Assert(confirmed.Status == ConfirmationStatus.Confirmed, "Expected exact revision confirmation.");
    Assert(sink.PublishedRevisionId == revision.RevisionId, "Report sink did not receive the exact snapshot.");
});

Run("confirmation rejects unauthenticated actor", () =>
{
    var geometry = MinimalGeometry(0);
    var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    var hash = PlanRevisionService.ComputeGeometryHash(json);
    var revision = Revision(geometry, hash, 1, Validation(hash));
    var service = new FinalPlanConfirmationService(
        new OwnershipGate(true), new MemoryRevisionStore(revision),
        new InputSource(geometry.LandRevisionId, geometry.RequirementsRevisionId),
        new FreshnessPolicy(true), new ConfirmationTransaction(), new ConfirmationSink(), TimeProvider.System);
    var result = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, true), Guid.Empty)
        .GetAwaiter().GetResult();
    Assert(result.Status == ConfirmationStatus.Unauthorized, "Empty authenticated identity was accepted.");
});

Run("confirmation denies cross-project ownership", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var revision = Revision(geometry, hash, 1, Validation(hash));
    var service = ConfirmationService(geometry, revision, new OwnershipGate(false));
    var result = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, true), Guid.NewGuid()).Result;
    Assert(result.Status == ConfirmationStatus.Unauthorized, "Cross-project access was accepted.");
});

Run("confirmation rejects expired validation", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var revision = Revision(geometry, hash, 1, Validation(hash));
    var service = new FinalPlanConfirmationService(
        new OwnershipGate(true), new MemoryRevisionStore(revision),
        new InputSource(geometry.LandRevisionId, geometry.RequirementsRevisionId),
        new FreshnessPolicy(false), new ConfirmationTransaction(), new ConfirmationSink(), TimeProvider.System);
    var result = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, true), Guid.NewGuid()).Result;
    Assert(result.Status == ConfirmationStatus.ValidationStale, "Expired validation was accepted.");
});

Run("duplicate confirmation result is preserved", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var revision = Revision(geometry, hash, 1, Validation(hash));
    var sink = new ConfirmationSink();
    var service = new FinalPlanConfirmationService(
        new OwnershipGate(true), new MemoryRevisionStore(revision),
        new InputSource(geometry.LandRevisionId, geometry.RequirementsRevisionId),
        new FreshnessPolicy(true), new ConfirmationTransaction(ConfirmationStatus.AlreadyConfirmed), sink, TimeProvider.System);
    var result = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, true), Guid.NewGuid()).Result;
    Assert(result.Status == ConfirmationStatus.AlreadyConfirmed, "Duplicate confirmation status was lost.");
    Assert(sink.PublishedRevisionId is null, "Duplicate confirmation was republished.");
});

Run("confirmation transaction failure does not publish report", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var revision = Revision(geometry, hash, 1, Validation(hash));
    var sink = new ConfirmationSink();
    var service = new FinalPlanConfirmationService(
        new OwnershipGate(true), new MemoryRevisionStore(revision),
        new InputSource(geometry.LandRevisionId, geometry.RequirementsRevisionId),
        new FreshnessPolicy(true), new ConfirmationTransaction(throwFailure: true), sink, TimeProvider.System);
    var result = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, true), Guid.NewGuid()).Result;
    Assert(result.Status == ConfirmationStatus.TransactionOutcomeUnknown &&
        result.ExpectedConfirmationId.HasValue && result.ExpectedRevisionId == revision.RevisionId,
        "Transaction exception did not preserve an indeterminate confirmation identity.");
    Assert(sink.PublishedRevisionId is null, "Report was published after transaction failure.");
});

Run("confirmation rejects geometry hash mismatch", () =>
{
    var geometry = MinimalGeometry(0);
    var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    var hash = PlanRevisionService.ComputeGeometryHash(json);
    var revision = Revision(geometry, hash, 1, Validation(hash));
    var service = new FinalPlanConfirmationService(
        new OwnershipGate(true), new MemoryRevisionStore(revision),
        new InputSource(geometry.LandRevisionId, geometry.RequirementsRevisionId),
        new FreshnessPolicy(true), new ConfirmationTransaction(), new ConfirmationSink(), TimeProvider.System);
    var result = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, "sha256:wrong", true), Guid.NewGuid())
        .GetAwaiter().GetResult();
    Assert(result.Status == ConfirmationStatus.GeometryHashMismatch, "Mismatched hash was accepted.");
});

Run("M1 ownership test double preserves owner and cross-project decisions", () =>
{
    var ownedProject = Guid.NewGuid();
    var owner = Guid.NewGuid();
    IProjectOwnershipGate gate = new TestProjectOwnershipGate(
        (projectId, actorId, _) => Task.FromResult(projectId == ownedProject && actorId == owner));
    Assert(gate.OwnsProjectAsync(ownedProject, owner, default).Result, "Owner access was not preserved.");
    Assert(!gate.OwnsProjectAsync(Guid.NewGuid(), owner, default).Result, "Cross-project access was granted.");
});

Run("M1 lifecycle eligibility exposes allowed and blocked responses", () =>
{
    IProjectLifecycleEligibilityPort allowed = new TestProjectLifecycleEligibilityPort(
        (_, _, _) => Task.FromResult(new ProjectLifecycleEligibility(true, true, null)));
    IProjectLifecycleEligibilityPort blocked = new TestProjectLifecycleEligibilityPort(
        (_, _, _) => Task.FromResult(new ProjectLifecycleEligibility(true, false, "PROJECT_STATE_BLOCKED")));
    Assert(allowed.GetConfirmationEligibilityAsync(Guid.NewGuid(), Guid.NewGuid(), default).Result.CanConfirm,
        "Allowed lifecycle response was lost.");
    Assert(blocked.GetConfirmationEligibilityAsync(Guid.NewGuid(), Guid.NewGuid(), default).Result.BlockingCode
        == "PROJECT_STATE_BLOCKED", "Blocked lifecycle reason was lost.");
});

Run("M2 confirmed land test double distinguishes success missing and cross-project response", () =>
{
    var projectId = Guid.NewGuid();
    var revisionId = Guid.NewGuid();
    using var boundary = JsonDocument.Parse("{\"type\":\"Polygon\",\"coordinates\":[]}");
    IConfirmedLandRevisionPort port = new TestConfirmedLandRevisionPort((requestedProject, requestedRevision, _) =>
        Task.FromResult<ConfirmedLandRevision?>(requestedRevision == revisionId
            ? new ConfirmedLandRevision(requestedProject, revisionId, "test-only/1", boundary.RootElement.Clone())
            : null));
    var found = port.GetAsync(projectId, revisionId, default).Result;
    Assert(found?.ProjectId == projectId, "Confirmed land response identity was lost.");
    Assert(port.GetAsync(projectId, Guid.NewGuid(), default).Result is null, "Missing land revision was fabricated.");

    IConfirmedLandRevisionPort invalid = new TestConfirmedLandRevisionPort((_, _, _) =>
        Task.FromResult<ConfirmedLandRevision?>(new(
            Guid.NewGuid(), revisionId, "test-only/1", boundary.RootElement.Clone())));
    var invalidResponse = invalid.GetAsync(projectId, revisionId, default).Result;
    Assert(invalidResponse?.ProjectId != projectId,
        "The invalid-response fixture must remain cross-project for future adapter rejection tests.");
});

Run("M5 requirement revision test double distinguishes success and missing", () =>
{
    var projectId = Guid.NewGuid();
    var revisionId = Guid.NewGuid();
    IRequirementRevisionPort port = new TestRequirementRevisionPort((requestedProject, requestedRevision, _) =>
        Task.FromResult<RequirementRevisionReference?>(requestedRevision == revisionId
            ? new RequirementRevisionReference(requestedProject, revisionId, "test-only/1")
            : null));
    Assert(port.GetAsync(projectId, revisionId, default).Result?.ProjectId == projectId,
        "Requirement revision response identity was lost.");
    Assert(port.GetAsync(projectId, Guid.NewGuid(), default).Result is null,
        "Missing requirement revision was fabricated.");
});

Run("M4 compute client propagates correlation and validates successful response", () =>
{
    var geometry = MinimalGeometry(0);
    var request = new PlanEditorComputeRequest(
        geometry, EditRequest(PlanEditCommandTypes.MoveRoom, "{\"deltaX\":1,\"deltaY\":0}").Command,
        "contract-correlation-id");
    string? observedCorrelation = null;
    IPlanEditorComputeClient client = new TestPlanEditorComputeClient((received, _) =>
    {
        observedCorrelation = received.CorrelationId;
        return Task.FromResult(new PlanEditResultDto
        {
            SchemaVersion = geometry.SchemaVersion,
            CommandId = received.Command.CommandId,
            BaseRevisionId = Guid.NewGuid(),
            Status = PlanEditStatus.Accepted,
            CandidateGeometry = geometry,
            Diagnostics = [],
            ChangedEntityIds = []
        });
    });
    var response = client.PreviewAsync(request, default).Result;
    PlanEditorContractValidator.Validate(response);
    Assert(observedCorrelation == request.CorrelationId, "Compute correlation ID was not propagated.");
});

Run("M4 compute client exposes unavailability and cancellation", () =>
{
    IPlanEditorComputeClient unavailable = new TestPlanEditorComputeClient(
        (_, _) => Task.FromException<PlanEditResultDto>(new HttpRequestException("Explicit unavailable test double.")));
    AssertThrows<HttpRequestException>(() => unavailable.PreviewAsync(
        new(MinimalGeometry(0), EditRequest(PlanEditCommandTypes.RemoveOpening, "{}").Command, "c"), default)
        .GetAwaiter().GetResult());

    IPlanEditorComputeClient timeout = new TestPlanEditorComputeClient(async (_, token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("Unreachable.");
    });
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    AssertThrows<TaskCanceledException>(() => timeout.PreviewAsync(
        new(MinimalGeometry(0), EditRequest(PlanEditCommandTypes.RemoveOpening, "{}").Command, "c"),
        cancellation.Token).GetAwaiter().GetResult());
});

Run("M4 persistence failure leaves revision history unchanged", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(
        JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));

    var baseRevision = Revision(geometry, hash, 1);
    var store = new TestPlanRevisionStore(baseRevision) { ThrowOnAppend = true };
    var trackedStore = new RevisionSecurityTrackingStore(store);

    var service = new PlanRevisionService(
        new OwnershipGate(true),
        trackedStore,
        new TestPlanValidationEvidenceVerifier(
            (_, _, _, _, _) => Task.FromResult(ValidationEvidenceStatus.Verified)),
        TimeProvider.System);

    var commandId = Guid.NewGuid();

    var result = service.CreateAsync(
        new(geometry.ProjectId, geometry.PlanId, baseRevision.RevisionId, geometry,
            Validation(hash), commandId, "uncertain append"),
        Guid.NewGuid()).GetAwaiter().GetResult();

    Assert(result.Status == AppendRevisionStatus.TransactionOutcomeUnknown,
        "Persistence exception did not produce an unknown outcome.");

    Assert(result.Revision is null,
        "Uncertain transaction returned a successful revision.");

    Assert(result.ExpectedRevisionId.HasValue &&
        result.ExpectedRevisionId.Value != Guid.Empty &&
        result.ExpectedRevisionId.Value != baseRevision.RevisionId,
        "Expected reconciliation revision identity is invalid.");

    Assert(result.ExpectedEditCommandId == commandId,
        "Reconciliation command identity was not preserved.");

    Assert(trackedStore.AppendCalls == 1,
        "Persistence failure triggered an unexpected retry.");

    Assert(trackedStore.ReadCalls == 2,
        "Expected exactly two pre-mutation reads and no reconciliation read.");

    Assert(store.GetHistoryAsync(geometry.ProjectId, geometry.PlanId, default).Result.Count == 1,
        "Persistence failure changed revision history.");
});

Run("revision base and restore source reject defective repository snapshots", () =>
{
    var geometry = MinimalGeometry(0);
    var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    var hash = PlanRevisionService.ComputeGeometryHash(json);
    var baseline = Revision(geometry, hash, 1, Validation(hash));

    var foreignGeometryJson = json.Replace(
        geometry.ProjectId.ToString(), Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase);
    var foreignGeometryHash = PlanRevisionService.ComputeGeometryHash(foreignGeometryJson);
    var invalidNested = JsonNode.Parse(json)!.AsObject();
    invalidNested["floors"] = null;
    var invalidNestedJson = invalidNested.ToJsonString();
    var invalidNestedHash = PlanRevisionService.ComputeGeometryHash(invalidNestedJson);

    Func<PlanRevisionSnapshot, PlanRevisionSnapshot?>[] corruptions =
    [
        _ => null,
        r => r with { RevisionId = Guid.NewGuid() },
        r => r with { ProjectId = Guid.NewGuid() },
        r => r with { PlanId = Guid.NewGuid() },
        r => r with { GeometrySchemaVersion = "unsupported" },
        r => r with { LandRevisionId = Guid.NewGuid() },
        r => r with { RequirementsRevisionId = Guid.NewGuid() },
        r => r with { GeometryHash = "sha256:incorrect" },
        r => r with { Validation = Validation("sha256:incorrect") },
        r => r with { GeometryJson = "null", GeometryHash = PlanRevisionService.ComputeGeometryHash("null"),
            Validation = Validation(PlanRevisionService.ComputeGeometryHash("null")) },
        r => r with { GeometryJson = "{malformed" },
        r => r with { GeometryJson = foreignGeometryJson, GeometryHash = foreignGeometryHash,
            Validation = Validation(foreignGeometryHash) },
        r => r with { GeometryJson = invalidNestedJson, GeometryHash = invalidNestedHash,
            Validation = Validation(invalidNestedHash) },
        r => r with { VersionNumber = 0 },
        r => r with { VersionNumber = long.MaxValue }
    ];

    foreach (var corrupt in corruptions)
    {
        var createStore = new AdversarialRevisionStore(baseline)
        {
            ReadTransform = (_, revision) => revision is null ? null : corrupt(revision)
        };
        var service = RevisionAuditService(createStore);
        var result = service.CreateAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, geometry,
                Validation(hash), Guid.NewGuid(), "integrity"), Guid.NewGuid()).Result;
        Assert(result.Status is AppendRevisionStatus.NotFound or AppendRevisionStatus.InvalidDependencyResponse,
            "Defective base snapshot reached append.");
        Assert(createStore.AppendCalls == 0 && createStore.HistoryCount == 1,
            "Defective base snapshot mutated history.");

        var restoreStore = new AdversarialRevisionStore(baseline)
        {
            ReadTransform = (_, revision) => revision is null ? null : corrupt(revision)
        };
        var restore = RevisionAuditService(restoreStore).RestoreAsync(
            geometry.ProjectId, geometry.PlanId, baseline.RevisionId, baseline.RevisionId,
            Validation(hash), Guid.NewGuid()).Result;
        Assert(restore.Status is AppendRevisionStatus.NotFound or AppendRevisionStatus.InvalidDependencyResponse,
            "Defective restore source reached append.");
        Assert(restoreStore.AppendCalls == 0 && restoreStore.HistoryCount == 1,
            "Defective restore source mutated history.");
    }
});

Run("revision append preserves late ownership and cancellation boundaries", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(
        JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1, Validation(hash));
    var ownershipCalls = 0;
    var revokedStore = new AdversarialRevisionStore(baseline);
    var revoked = new PlanRevisionService(
        new TestProjectOwnershipGate((_, _, _) => Task.FromResult(++ownershipCalls == 1)),
        revokedStore, new ValidationVerifier(ValidationEvidenceStatus.Verified), TimeProvider.System);
    var command = new CreatePlanRevisionCommand(
        geometry.ProjectId, geometry.PlanId, baseline.RevisionId, geometry,
        Validation(hash), Guid.NewGuid(), "revoked");
    Assert(revoked.CreateAsync(command, Guid.NewGuid()).Result.Status == AppendRevisionStatus.Unauthorized &&
        revokedStore.AppendCalls == 0, "Ownership revocation reached append.");

    using var before = new CancellationTokenSource();
    before.Cancel();
    var cancelledStore = new AdversarialRevisionStore(baseline);
    AssertThrows<OperationCanceledException>(() => RevisionAuditService(cancelledStore)
        .CreateAsync(command, Guid.NewGuid(), before.Token).GetAwaiter().GetResult());
    Assert(cancelledStore.AppendCalls == 0, "Pre-mutation cancellation reached append.");

    var dependencyStore = new AdversarialRevisionStore(baseline)
        { Failure = RevisionAppendFailure.DependencyCancellation };
    var dependencyResult = RevisionAuditService(dependencyStore)
        .CreateAsync(command, Guid.NewGuid()).Result;
    Assert(dependencyResult.Status == AppendRevisionStatus.TransactionOutcomeUnknown &&
        dependencyStore.AppendCalls == 1, "Dependency cancellation was fabricated as rollback.");

    using var during = new CancellationTokenSource();
    var duringStore = new AdversarialRevisionStore(baseline)
        { Failure = RevisionAppendFailure.CallerCancellationAfterCommit, CancellationSource = during };
    AssertThrows<OperationCanceledException>(() => RevisionAuditService(duringStore)
        .CreateAsync(command, Guid.NewGuid(), during.Token).GetAwaiter().GetResult());
    Assert(duringStore.AppendCalls == 1 && duringStore.HistoryCount == 2,
        "Post-commit caller cancellation did not preserve actual state.");
});

Run("revision append exceptions require reconciliation without retry", () =>
{
    foreach (var failure in new[]
    {
        RevisionAppendFailure.Timeout,
        RevisionAppendFailure.Transport,
        RevisionAppendFailure.Unexpected,
        RevisionAppendFailure.AcknowledgementLostAfterCommit
    })
    {
        var geometry = MinimalGeometry(0);
        var hash = PlanRevisionService.ComputeGeometryHash(
            JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
        var baseline = Revision(geometry, hash, 1, Validation(hash));
        var store = new AdversarialRevisionStore(baseline) { Failure = failure };
        var commandId = Guid.NewGuid();
        var result = RevisionAuditService(store).CreateAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, geometry,
                Validation(hash), commandId, failure.ToString()), Guid.NewGuid()).Result;
        Assert(result.Status == AppendRevisionStatus.TransactionOutcomeUnknown && result.Revision is null &&
            result.ExpectedRevisionId == store.LastProposed?.RevisionId &&
            result.ExpectedEditCommandId == commandId,
            "Append exception lost local reconciliation identity or fabricated an outcome.");
        Assert(store.AppendCalls == 1 && store.ReadCalls == 2,
            "Append exception triggered replay or reconciliation read.");
        Assert(store.HistoryCount ==
            (failure == RevisionAppendFailure.AcknowledgementLostAfterCommit ? 2 : 1),
            "Actual mutation state did not match the injected failure boundary.");
    }
});

Run("revision append validates exact acknowledgement and redacts foreign payloads", () =>
{
    Func<AppendRevisionResult, AppendRevisionResult?>[] corruptions =
    [
        _ => null,
        r => r with { Status = (AppendRevisionStatus)int.MaxValue },
        r => r with { Status = AppendRevisionStatus.TransactionOutcomeUnknown },
        r => r with { Revision = null },
        r => r with { Revision = r.Revision! with { RevisionId = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { ProjectId = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { PlanId = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { ParentRevisionId = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { VersionNumber = r.Revision.VersionNumber + 1 } },
        r => r with { Revision = r.Revision! with { GeometrySchemaVersion = "wrong" } },
        r => r with { Revision = r.Revision! with { GeometryJson = "{}" } },
        r => r with { Revision = r.Revision! with { GeometryHash = "sha256:wrong" } },
        r => r with { Revision = r.Revision! with { LandRevisionId = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { RequirementsRevisionId = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { Validation = Validation(r.Revision.GeometryHash) } },
        r => r with { Revision = r.Revision! with { EditCommandId = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { CreatedBy = Guid.NewGuid() } },
        r => r with { Revision = r.Revision! with { CreatedAtUtc = r.Revision.CreatedAtUtc.AddTicks(1) } },
        r => r with { Revision = r.Revision! with { ChangeSummary = "foreign" } },
        r => r with { Status = AppendRevisionStatus.RevisionConflict },
        r => r with { ExpectedRevisionId = Guid.NewGuid(), ExpectedEditCommandId = Guid.NewGuid() }
    ];

    foreach (var corrupt in corruptions)
    {
        var geometry = MinimalGeometry(0);
        var hash = PlanRevisionService.ComputeGeometryHash(
            JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
        var baseline = Revision(geometry, hash, 1, Validation(hash));
        var store = new AdversarialRevisionStore(baseline) { ResponseTransform = corrupt };
        var commandId = Guid.NewGuid();
        var result = RevisionAuditService(store).CreateAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, geometry,
                Validation(hash), commandId, "exact acknowledgement"), Guid.NewGuid()).Result;
        Assert(result.Status == AppendRevisionStatus.TransactionOutcomeUnknown && result.Revision is null &&
            result.ExpectedRevisionId == store.LastProposed?.RevisionId &&
            result.ExpectedEditCommandId == commandId,
            "Malformed acknowledgement was trusted or exposed foreign data.");
        Assert(store.AppendCalls == 1 && store.ReadCalls == 2 && store.HistoryCount == 2,
            "Malformed acknowledgement caused replay, reconciliation read, or extra mutation.");
    }

    var validGeometry = MinimalGeometry(0);
    var validHash = PlanRevisionService.ComputeGeometryHash(
        JsonSerializer.Serialize(validGeometry, PlanEditorJson.StrictOptions));
    var validBase = Revision(validGeometry, validHash, 1, Validation(validHash));
    var validStore = new AdversarialRevisionStore(validBase);
    var valid = RevisionAuditService(validStore).CreateAsync(
        new(validGeometry.ProjectId, validGeometry.PlanId, validBase.RevisionId, validGeometry,
            Validation(validHash), Guid.NewGuid(), "valid"), Guid.NewGuid()).Result;
    Assert(valid.Status == AppendRevisionStatus.Created && valid.Revision == validStore.LastProposed &&
        valid.Revision?.ParentRevisionId == validBase.RevisionId && valid.Revision.VersionNumber == 2,
        "Valid exact acknowledgement was rejected.");
});

Run("revision verifier unknown status fails as invalid dependency response", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(
        JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1, Validation(hash));
    var store = new AdversarialRevisionStore(baseline);
    var service = new PlanRevisionService(new OwnershipGate(true), store,
        new ValidationVerifier((ValidationEvidenceStatus)int.MaxValue), TimeProvider.System);
    var result = service.CreateAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, geometry,
            Validation(hash), Guid.NewGuid(), "unknown verifier"), Guid.NewGuid()).Result;
    Assert(result.Status == AppendRevisionStatus.InvalidDependencyResponse && store.AppendCalls == 0,
        "Unknown validation status reached persistence.");
});

Run("revision and confirmation prerequisite failures stop before mutation", () =>
{
    foreach (var item in new (Exception Error, AppendRevisionStatus Revision, ConfirmationStatus Confirmation)[]
    {
        (new TimeoutException("private"), AppendRevisionStatus.DependencyTimeout, ConfirmationStatus.DependencyTimeout),
        (new HttpRequestException("private"), AppendRevisionStatus.DependencyUnavailable, ConfirmationStatus.DependencyUnavailable),
        (new OperationCanceledException("private"), AppendRevisionStatus.DependencyTimeout, ConfirmationStatus.DependencyTimeout),
        (new InvalidOperationException("private"), AppendRevisionStatus.UnexpectedInternalFailure, ConfirmationStatus.UnexpectedInternalFailure)
    })
    {
        var geometry = MinimalGeometry(0);
        var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
        var baseline = Revision(geometry, hash, 1, Validation(hash));
        var revisionStore = new ConfirmationAuditStore(baseline) { SelectedException = item.Error };
        var revision = RevisionAuditService(revisionStore).CreateAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, geometry,
                Validation(hash), Guid.NewGuid(), "dependency"), Guid.NewGuid()).Result;
        Assert(revision.Status == item.Revision, "Revision prerequisite failure was misclassified.");

        var confirmationCalls = 0;
        var publishCalls = 0;
        var confirmationStore = new ConfirmationAuditStore(baseline) { SelectedException = item.Error };
        var confirmation = ConfirmationAuditService(confirmationStore,
            new TestFinalConfirmationTransaction((c, _, _) =>
            { confirmationCalls++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
            new TestConfirmedPlanSnapshotSink((_, _, _) => { publishCalls++; return Task.CompletedTask; }))
            .ConfirmAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).Result;
        Assert(confirmation.Status == item.Confirmation && confirmationCalls == 0 && publishCalls == 0,
            "Confirmation prerequisite failure reached mutation or publication.");
    }
});

Run("M6 validation test double rejects mismatched authoritative evidence", () =>
{
    IPlanValidationEvidenceVerifier verifier = new TestPlanValidationEvidenceVerifier(
        (_, _, evidence, exactHash, _) => Task.FromResult(
            evidence.GeometryHash == exactHash ? ValidationEvidenceStatus.Verified : ValidationEvidenceStatus.Rejected));
    var status = verifier.VerifyAsync(Guid.NewGuid(), Guid.NewGuid(), Validation("sha256:wrong"),
        "sha256:expected", default).Result;
    Assert(status == ValidationEvidenceStatus.Rejected, "Mismatched validation evidence was accepted.");
});

Run("M5 report publication failure remains visible after confirmed transaction", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var revision = Revision(geometry, hash, 1, Validation(hash));
    var sink = new TestConfirmedPlanSnapshotSink((_, _, _) =>
        Task.FromException(new InvalidOperationException("Explicit report publication test-double failure.")));
    var service = new FinalPlanConfirmationService(
        new TestProjectOwnershipGate((_, _, _) => Task.FromResult(true)), new TestPlanRevisionStore(revision),
        new TestApplicableInputRevisionSource((_, _) => Task.FromResult(
            new ApplicableInputRevisions(geometry.LandRevisionId, geometry.RequirementsRevisionId))),
        new TestValidationFreshnessPolicy(true),
        new TestFinalConfirmationTransaction((confirmation, _, _) =>
            Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation))),
        sink, TimeProvider.System);
    var result = service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, revision.RevisionId, hash, true), Guid.NewGuid()).Result;
    Assert(result.Status == ConfirmationStatus.Confirmed &&
        result.PublicationStatus == ConfirmationPublicationStatus.OutcomeUnknown,
        "Publication failure incorrectly changed confirmed transaction state.");
    Assert(sink.PublishedRevisionId is null, "Failed report publication was recorded as delivered.");
});

Run("confirmation rejects defective selected and current revision snapshots", () =>
{
    var geometry = MinimalGeometry(0);
    var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
    var hash = PlanRevisionService.ComputeGeometryHash(json);
    var baseline = Revision(geometry, hash, 1, Validation(hash));
    var invalidNested = JsonNode.Parse(json)!.AsObject();
    invalidNested["floors"] = null;
    var invalidJson = invalidNested.ToJsonString();
    var invalidHash = PlanRevisionService.ComputeGeometryHash(invalidJson);
    var foreignGeometryJson = json.Replace(
        geometry.ProjectId.ToString(), Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase);
    var foreignGeometryHash = PlanRevisionService.ComputeGeometryHash(foreignGeometryJson);
    Func<PlanRevisionSnapshot, PlanRevisionSnapshot?>[] corruptions =
    [
        _ => null,
        r => r with { RevisionId = Guid.NewGuid() },
        r => r with { ProjectId = Guid.NewGuid() },
        r => r with { PlanId = Guid.NewGuid() },
        r => r with { VersionNumber = 0 },
        r => r with { GeometrySchemaVersion = "unsupported" },
        r => r with { GeometryJson = "null", GeometryHash = PlanRevisionService.ComputeGeometryHash("null"),
            Validation = Validation(PlanRevisionService.ComputeGeometryHash("null")) },
        r => r with { GeometryJson = "{malformed" },
        r => r with { GeometryJson = invalidJson, GeometryHash = invalidHash, Validation = Validation(invalidHash) },
        r => r with { GeometryJson = foreignGeometryJson, GeometryHash = foreignGeometryHash,
            Validation = Validation(foreignGeometryHash) },
        r => r with { GeometryHash = "sha256:wrong" },
        r => r with { LandRevisionId = Guid.NewGuid() },
        r => r with { RequirementsRevisionId = Guid.NewGuid() },
        r => r with { Validation = Validation("sha256:wrong") },
        r => r with { Validation = null! }
    ];
    foreach (var corrupt in corruptions)
    {
        var transactionCalls = 0;
        var publishCalls = 0;
        var store = new ConfirmationAuditStore(baseline)
            { SelectedTransform = r => r is null ? null : corrupt(r) };
        var result = ConfirmationAuditService(store,
            new TestFinalConfirmationTransaction((c, _, _) =>
            { transactionCalls++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
            new TestConfirmedPlanSnapshotSink((_, _, _) => { publishCalls++; return Task.CompletedTask; }))
            .ConfirmAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).Result;
        Assert(result.Status is ConfirmationStatus.RevisionNotFound or ConfirmationStatus.InvalidDependencyResponse &&
            transactionCalls == 0 && publishCalls == 0,
            "Defective selected revision reached confirmation or publication.");
    }

    var inconsistent = new ConfirmationAuditStore(baseline)
        { CurrentTransform = (_, r) => r with { ChangeSummary = "foreign" } };
    Assert(ConfirmationAuditService(inconsistent).ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).Result.Status
        == ConfirmationStatus.InvalidDependencyResponse,
        "Inconsistent selected/current snapshots were accepted.");

    var noncanonicalJson = json + "  \r\n";
    var noncanonicalHash = PlanRevisionService.ComputeGeometryHash(noncanonicalJson);
    var noncanonical = baseline with
    {
        GeometryJson = noncanonicalJson, GeometryHash = noncanonicalHash,
        Validation = Validation(noncanonicalHash)
    };
    var valid = ConfirmationAuditService(new ConfirmationAuditStore(noncanonical)).ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, noncanonical.RevisionId, noncanonicalHash, true), Guid.NewGuid()).Result;
    Assert(valid.Status == ConfirmationStatus.Confirmed,
        "Valid noncanonical JSON with an exact stored hash was rejected.");
});

Run("confirmation revalidates authorization revision inputs and freshness", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1, Validation(hash));
    var actor = Guid.NewGuid();

    var ownershipCalls = 0;
    var revokedTransactionCalls = 0;
    var revoked = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
        new TestFinalConfirmationTransaction((c, _, _) =>
        { revokedTransactionCalls++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
        ownership: new TestProjectOwnershipGate((_, _, _) => Task.FromResult(++ownershipCalls == 1)));
    Assert(revoked.ConfirmAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), actor)
        .Result.Status == ConfirmationStatus.Unauthorized && revokedTransactionCalls == 0,
        "Revoked ownership reached transaction.");

    var failingOwnershipCalls = 0;
    var ownershipFailureTransactionCalls = 0;
    var ownershipFailure = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
        new TestFinalConfirmationTransaction((c, _, _) =>
        { ownershipFailureTransactionCalls++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
        ownership: new TestProjectOwnershipGate((_, _, _) =>
        {
            if (++failingOwnershipCalls == 2) throw new InvalidOperationException("private ownership failure");
            return Task.FromResult(true);
        }));
    var ownershipFailureResult = ownershipFailure.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), actor).Result;
    Assert(ownershipFailureResult.Status == ConfirmationStatus.UnexpectedInternalFailure &&
        ownershipFailureTransactionCalls == 0, "Ownership dependency failure reached transaction or escaped.");

    var changedCurrent = new ConfirmationAuditStore(baseline)
        { CurrentTransform = (call, r) => call == 2 ? r with { RevisionId = Guid.NewGuid() } : r };
    Assert(ConfirmationAuditService(changedCurrent).ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), actor).Result.Status
        == ConfirmationStatus.NotCurrentRevision, "Changed current revision reached transaction.");

    var inputCalls = 0;
    var changedInputs = new TestApplicableInputRevisionSource((_, _) => Task.FromResult(
        ++inputCalls == 1
            ? new ApplicableInputRevisions(geometry.LandRevisionId, geometry.RequirementsRevisionId)
            : new ApplicableInputRevisions(Guid.NewGuid(), geometry.RequirementsRevisionId)));
    Assert(ConfirmationAuditService(new ConfirmationAuditStore(baseline), inputs: changedInputs).ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), actor).Result.Status
        == ConfirmationStatus.StaleInputs, "Changed input revisions reached transaction.");

    inputCalls = 0;
    var changedRequirements = new TestApplicableInputRevisionSource((_, _) => Task.FromResult(
        ++inputCalls == 1
            ? new ApplicableInputRevisions(geometry.LandRevisionId, geometry.RequirementsRevisionId)
            : new ApplicableInputRevisions(geometry.LandRevisionId, Guid.NewGuid())));
    Assert(ConfirmationAuditService(new ConfirmationAuditStore(baseline), inputs: changedRequirements).ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), actor).Result.Status
        == ConfirmationStatus.StaleInputs, "Changed requirements revision reached transaction.");

    var freshnessCalls = 0;
    var freshness = new DynamicFreshnessPolicy(_ => ++freshnessCalls == 1);
    Assert(ConfirmationAuditService(new ConfirmationAuditStore(baseline), freshness: freshness).ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), actor).Result.Status
        == ConfirmationStatus.ValidationStale, "Validation that became stale reached transaction.");

    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    AssertThrows<OperationCanceledException>(() => ConfirmationAuditService(new ConfirmationAuditStore(baseline))
        .ConfirmAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), actor, cancelled.Token)
        .GetAwaiter().GetResult());
});

Run("confirmation transaction uncertainty preserves local identity without publication", () =>
{
    Func<PlanConfirmationSnapshot, ConfirmationResult?>[] responses =
    [
        _ => null,
        _ => new ConfirmationResult((ConfirmationStatus)int.MaxValue),
        c => new ConfirmationResult(ConfirmationStatus.TransactionOutcomeUnknown)
            { ExpectedConfirmationId = Guid.NewGuid(), ExpectedRevisionId = Guid.NewGuid() },
        _ => new ConfirmationResult(ConfirmationStatus.Confirmed),
        c => new ConfirmationResult(ConfirmationStatus.AlreadyConfirmed, c)
    ];
    foreach (var response in responses)
    {
        var geometry = MinimalGeometry(0);
        var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
        var baseline = Revision(geometry, hash, 1, Validation(hash));
        var transactionCalls = 0;
        var publishCalls = 0;
        PlanConfirmationSnapshot? proposed = null;
        var service = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
            new TestFinalConfirmationTransaction((c, _, _) =>
            { transactionCalls++; proposed = c; return Task.FromResult(response(c)!); }),
            new TestConfirmedPlanSnapshotSink((_, _, _) => { publishCalls++; return Task.CompletedTask; }));
        var result = service.ConfirmAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).Result;
        Assert(result.Status == ConfirmationStatus.TransactionOutcomeUnknown && result.Confirmation is null &&
            result.ExpectedConfirmationId == proposed!.ConfirmationId &&
            result.ExpectedRevisionId == baseline.RevisionId && transactionCalls == 1 && publishCalls == 0,
            "Malformed transaction response was trusted, replayed, published, or exposed.");
    }

    foreach (var failure in new[] { "timeout", "transport", "dependency-cancel", "unexpected", "commit-lost" })
    {
        var geometry = MinimalGeometry(0);
        var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
        var baseline = Revision(geometry, hash, 1, Validation(hash));
        var calls = 0;
        var committed = false;
        var transaction = new TestFinalConfirmationTransaction((_, _, _) =>
        {
            calls++;
            if (failure == "commit-lost") committed = true;
            throw failure switch
            {
                "timeout" => new TimeoutException("private"),
                "transport" or "commit-lost" => new HttpRequestException("private"),
                "dependency-cancel" => new OperationCanceledException("private"),
                _ => new InvalidOperationException("private")
            };
        });
        var result = ConfirmationAuditService(new ConfirmationAuditStore(baseline), transaction).ConfirmAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).Result;
        Assert(result.Status == ConfirmationStatus.TransactionOutcomeUnknown && calls == 1 &&
            (failure != "commit-lost" || committed), "Transaction exception fabricated rollback or retried.");
    }

    var cancelledGeometry = MinimalGeometry(0);
    var cancelledHash = PlanRevisionService.ComputeGeometryHash(
        JsonSerializer.Serialize(cancelledGeometry, PlanEditorJson.StrictOptions));
    var cancelledRevision = Revision(cancelledGeometry, cancelledHash, 1, Validation(cancelledHash));
    using var cancelled = new CancellationTokenSource();
    var cancelledCalls = 0;
    var cancelledPublishes = 0;
    var cancelledService = ConfirmationAuditService(new ConfirmationAuditStore(cancelledRevision),
        new TestFinalConfirmationTransaction((_, _, token) =>
        {
            cancelledCalls++;
            cancelled.Cancel();
            throw new OperationCanceledException("caller", token);
        }),
        new TestConfirmedPlanSnapshotSink((_, _, _) =>
        { cancelledPublishes++; return Task.CompletedTask; }));
    AssertThrows<OperationCanceledException>(() => cancelledService.ConfirmAsync(
        new(cancelledGeometry.ProjectId, cancelledGeometry.PlanId, cancelledRevision.RevisionId,
            cancelledHash, true), Guid.NewGuid(), cancelled.Token).GetAwaiter().GetResult());
    Assert(cancelledCalls == 1 && cancelledPublishes == 0,
        "Caller cancellation during transaction retried or published.");
});

Run("confirmation validates every acknowledgement field", () =>
{
    Func<PlanConfirmationSnapshot, PlanConfirmationSnapshot>[] corruptions =
    [
        c => c with { ConfirmationId = Guid.NewGuid() },
        c => c with { ProjectId = Guid.NewGuid() },
        c => c with { PlanId = Guid.NewGuid() },
        c => c with { ConfirmedRevisionId = Guid.NewGuid() },
        c => c with { GeometryHash = "sha256:wrong" },
        c => c with { ValidationId = Guid.NewGuid() },
        c => c with { ConfirmedBy = Guid.NewGuid() },
        c => c with { ConfirmedAtUtc = c.ConfirmedAtUtc.AddTicks(1) },
        c => c with { WarningAcknowledgement = !c.WarningAcknowledgement }
    ];
    foreach (var corrupt in corruptions)
    {
        var geometry = MinimalGeometry(0);
        var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
        var baseline = Revision(geometry, hash, 1, Validation(hash));
        var publishCalls = 0;
        var service = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
            new TestFinalConfirmationTransaction((c, _, _) => Task.FromResult(
                new ConfirmationResult(ConfirmationStatus.Confirmed, corrupt(c)))),
            new TestConfirmedPlanSnapshotSink((_, _, _) => { publishCalls++; return Task.CompletedTask; }));
        var result = service.ConfirmAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).Result;
        Assert(result.Status == ConfirmationStatus.TransactionOutcomeUnknown && result.Confirmation is null &&
            publishCalls == 0, "Contradictory confirmation acknowledgement was accepted or published.");
    }
});

Run("confirmed publication uncertainty never implies transaction rollback", () =>
{
    foreach (var failure in new[] { "throw", "timeout", "dependency-cancel", "delivered-lost" })
    {
        var geometry = MinimalGeometry(0);
        var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
        var baseline = Revision(geometry, hash, 1, Validation(hash));
        var transactionCalls = 0;
        var publishCalls = 0;
        var delivered = false;
        var transaction = new TestFinalConfirmationTransaction((c, _, _) =>
        { transactionCalls++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); });
        var sink = new TestConfirmedPlanSnapshotSink((_, _, _) =>
        {
            publishCalls++;
            if (failure == "delivered-lost") delivered = true;
            throw failure switch
            {
                "timeout" => new TimeoutException("private"),
                "dependency-cancel" => new OperationCanceledException("private"),
                _ => new InvalidOperationException("private")
            };
        });
        var result = ConfirmationAuditService(new ConfirmationAuditStore(baseline), transaction, sink).ConfirmAsync(
            new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).Result;
        Assert(result.Status == ConfirmationStatus.Confirmed && result.Confirmation is not null &&
            result.PublicationStatus == ConfirmationPublicationStatus.OutcomeUnknown &&
            transactionCalls == 1 && publishCalls == 1 &&
            (failure != "delivered-lost" || delivered),
            "Publication uncertainty fabricated rollback, delivery success, or retry.");
    }


    var cancelledGeometry = MinimalGeometry(0);
    var cancelledHash = PlanRevisionService.ComputeGeometryHash(
        JsonSerializer.Serialize(cancelledGeometry, PlanEditorJson.StrictOptions));
    var cancelledRevision = Revision(cancelledGeometry, cancelledHash, 1, Validation(cancelledHash));
    using var cancelled = new CancellationTokenSource();
    var cancelledTransactionCalls = 0;
    var cancelledPublicationCalls = 0;
    var cancelledService = ConfirmationAuditService(new ConfirmationAuditStore(cancelledRevision),
        new TestFinalConfirmationTransaction((confirmation, _, _) =>
        { cancelledTransactionCalls++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation)); }),
        new TestConfirmedPlanSnapshotSink((_, _, token) =>
        {
            cancelledPublicationCalls++;
            cancelled.Cancel();
            throw new OperationCanceledException("caller", token);
        }));
    AssertThrows<OperationCanceledException>(() => cancelledService.ConfirmAsync(
        new(cancelledGeometry.ProjectId, cancelledGeometry.PlanId, cancelledRevision.RevisionId,
            cancelledHash, true), Guid.NewGuid(), cancelled.Token).GetAwaiter().GetResult());
    Assert(cancelledTransactionCalls == 1 && cancelledPublicationCalls == 1,
        "Caller cancellation after verified confirmation replayed transaction or publication.");
});

Run("M7 AI proposal test double exposes unavailable and malformed proposals", () =>
{
    IAiEditProposalPort unavailable = new TestAiEditProposalPort((_, _, _, _, _, _) =>
        Task.FromResult(new AiEditProposal(AiEditProposalStatus.Unavailable, [], [])));
    Assert(unavailable.ProposeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "move room", "c", default)
        .Result.Status == AiEditProposalStatus.Unavailable, "Unavailable AI state was fabricated as success.");

    using var malformedPayload = JsonDocument.Parse("{}");
    IAiEditProposalPort malformed = new TestAiEditProposalPort((_, _, _, _, _, _) =>
        Task.FromResult(new AiEditProposal(AiEditProposalStatus.Proposed,
        [new PlanEditCommandDto
        {
            CommandId = Guid.NewGuid(), CommandType = PlanEditCommandTypes.MoveRoom,
            FloorId = Guid.NewGuid(), TargetId = Guid.NewGuid(), Payload = malformedPayload.RootElement.Clone()
        }], [])));
    var proposal = malformed.ProposeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "move room", "c", default).Result;
    var proposedCommand = proposal.Commands.Single();
    AssertThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(new PlanEditRequestDto
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = Guid.NewGuid(), PlanId = Guid.NewGuid(), BaseRevisionId = Guid.NewGuid(), Command = proposedCommand
    }));
});

Run("GVR-11 stored revision and evidence identities cannot be empty", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    var evidenceNode = JsonSerializer.SerializeToNode(Validation(hash), PlanEditorJson.StrictOptions)!.AsObject();
    evidenceNode["validationId"] = Guid.Empty.ToString();
    var emptyEvidence = evidenceNode.Deserialize<PlanValidationReferenceDto>(PlanEditorJson.StrictOptions)!;
    foreach (var corrupt in new[] { baseline with { RevisionId = Guid.Empty },
        baseline with { Validation = emptyEvidence }, baseline with { Validation = null! } })
    {
        foreach (var restore in new[] { false, true })
        {
            var store = new AdversarialRevisionStore(corrupt);
            var service = RevisionAuditService(store);
            var result = restore
                ? service.RestoreAsync(geometry.ProjectId, geometry.PlanId, corrupt.RevisionId, corrupt.RevisionId,
                    Validation(hash), Guid.NewGuid()).GetAwaiter().GetResult()
                : service.CreateAsync(new(geometry.ProjectId, geometry.PlanId, corrupt.RevisionId, geometry,
                    Validation(hash), Guid.NewGuid(), "identity"), Guid.NewGuid()).GetAwaiter().GetResult();
            Assert(result.Status == AppendRevisionStatus.InvalidDependencyResponse && store.AppendCalls == 0 &&
                store.HistoryCount == 1, "Invalid stored identity reached mutation.");
        }
    }
});

Run("GVR-11 and GVR-12 incoming empty or null validation identity fails before verifier", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    var node = JsonSerializer.SerializeToNode(Validation(hash), PlanEditorJson.StrictOptions)!.AsObject();
    node["validationId"] = Guid.Empty.ToString();
    var empty = node.Deserialize<PlanValidationReferenceDto>(PlanEditorJson.StrictOptions)!;
    foreach (var evidence in new PlanValidationReferenceDto?[] { null, empty })
    {
        foreach (var restore in new[] { false, true })
        {
            var store = new AdversarialRevisionStore(baseline);
            var verifierCalls = 0;
            var service = new PlanRevisionService(new OwnershipGate(true), store,
                new TestPlanValidationEvidenceVerifier((_, _, _, _, _) =>
                { verifierCalls++; return Task.FromResult(ValidationEvidenceStatus.Verified); }), TimeProvider.System);
            AssertThrows<ArgumentException>(() =>
            {
                if (restore)
                    service.RestoreAsync(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, baseline.RevisionId,
                        evidence!, Guid.NewGuid()).GetAwaiter().GetResult();
                else
                    service.CreateAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, geometry,
                        evidence!, Guid.NewGuid(), "identity"), Guid.NewGuid()).GetAwaiter().GetResult();
            });
            Assert(verifierCalls == 0 && store.AppendCalls == 0 && store.HistoryCount == 1,
                "Invalid incoming evidence reached verifier or mutation.");
        }
    }
});

Run("GVR-13 late revision integrity and stale base are checked before append", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    var cases = new (PlanRevisionSnapshot? Snapshot, AppendRevisionStatus Status)[]
    {
        (null, AppendRevisionStatus.InvalidDependencyResponse),
        (baseline with { ChangeSummary = "changed" }, AppendRevisionStatus.InvalidDependencyResponse),
        (baseline with { Validation = Validation(hash) }, AppendRevisionStatus.InvalidDependencyResponse),
        (baseline with { GeometryJson = baseline.GeometryJson + " " }, AppendRevisionStatus.InvalidDependencyResponse),
        (baseline with { ProjectId = Guid.NewGuid() }, AppendRevisionStatus.InvalidDependencyResponse),
        (baseline with { RevisionId = Guid.NewGuid(), VersionNumber = 2 }, AppendRevisionStatus.RevisionConflict),
        (baseline, AppendRevisionStatus.Created)
    };
    foreach (var item in cases)
    {
        var store = new AdversarialRevisionStore(baseline) { CurrentTransform = _ => item.Snapshot };
        var result = RevisionAuditService(store).CreateAsync(new(geometry.ProjectId, geometry.PlanId,
            baseline.RevisionId, geometry, Validation(hash), Guid.NewGuid(), "late base"), Guid.NewGuid()).GetAwaiter().GetResult();
        var success = item.Status == AppendRevisionStatus.Created;
        Assert(result.Status == item.Status && store.ReadCalls == 2 && store.AppendCalls == (success ? 1 : 0) &&
            store.HistoryCount == (success ? 2 : 1), "Late base integrity result was incorrect.");
    }
});

Run("GVR-13 late revision read failures and cancellation block append", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    var command = new CreatePlanRevisionCommand(geometry.ProjectId, geometry.PlanId, baseline.RevisionId,
        geometry, Validation(hash), Guid.NewGuid(), "late read");
    foreach (var item in new (Exception Error, AppendRevisionStatus Status)[] {
        (new TimeoutException(), AppendRevisionStatus.DependencyTimeout),
        (new OperationCanceledException(), AppendRevisionStatus.DependencyTimeout),
        (new HttpRequestException(), AppendRevisionStatus.DependencyUnavailable),
        (new InvalidOperationException(), AppendRevisionStatus.UnexpectedInternalFailure) })
    {
        var store = new AdversarialRevisionStore(baseline) { CurrentTransform = _ => throw item.Error };
        var result = RevisionAuditService(store).CreateAsync(command, Guid.NewGuid()).GetAwaiter().GetResult();
        Assert(result.Status == item.Status && store.AppendCalls == 0 && store.HistoryCount == 1,
            "Late read failure reached append or was misclassified.");
    }
    using var cancelled = new CancellationTokenSource();
    var cancelledStore = new AdversarialRevisionStore(baseline)
        { CurrentTransform = r => { cancelled.Cancel(); return r; } };
    AssertThrows<OperationCanceledException>(() => RevisionAuditService(cancelledStore)
        .CreateAsync(command, Guid.NewGuid(), cancelled.Token).GetAwaiter().GetResult());
    Assert(cancelledStore.AppendCalls == 0, "Late read cancellation reached append.");
});

Run("GVR-14 final confirmation authorization rejects revocation and failures", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    foreach (var item in new (Exception? Error, ConfirmationStatus Status)[] {
        (null, ConfirmationStatus.Unauthorized), (new TimeoutException(), ConfirmationStatus.DependencyTimeout),
        (new OperationCanceledException(), ConfirmationStatus.DependencyTimeout),
        (new HttpRequestException(), ConfirmationStatus.DependencyUnavailable),
        (new InvalidOperationException(), ConfirmationStatus.UnexpectedInternalFailure) })
    {
        var owned = true; var reads = 0; var checks = 0; var writes = 0; var publishes = 0;
        var service = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
            new TestFinalConfirmationTransaction((c, _, _) => { writes++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
            new TestConfirmedPlanSnapshotSink((_, _, _) => { publishes++; return Task.CompletedTask; }),
            ownership: new TestProjectOwnershipGate((_, _, _) => { checks++; if (!owned && item.Error is not null) throw item.Error; return Task.FromResult(owned); }),
            inputs: new TestApplicableInputRevisionSource((_, _) => { if (++reads == 2) owned = false;
                return Task.FromResult(new ApplicableInputRevisions(geometry.LandRevisionId, geometry.RequirementsRevisionId)); }));
        var result = service.ConfirmAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).GetAwaiter().GetResult();
        Assert(result.Status == item.Status && result.Confirmation is null && checks == 3 && writes == 0 && publishes == 0,
            "Final authorization failure reached confirmation or publication.");
    }
});

Run("GVR-15 final confirmation cancellation prevents transaction", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    using var cancelled = new CancellationTokenSource();
    var checks = 0; var writes = 0; var publishes = 0;
    var service = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
        new TestFinalConfirmationTransaction((c, _, _) => { writes++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
        new TestConfirmedPlanSnapshotSink((_, _, _) => { publishes++; return Task.CompletedTask; }),
        freshness: new DynamicFreshnessPolicy(_ => { if (++checks == 2) cancelled.Cancel(); return true; }));
    AssertThrows<OperationCanceledException>(() => service.ConfirmAsync(
        new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid(), cancelled.Token).GetAwaiter().GetResult());
    Assert(checks == 2 && writes == 0 && publishes == 0, "Final cancellation reached mutation.");
});

Run("GVR-16 validation expiry during final dependency reads prevents confirmation", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    var fresh = true; var reads = 0; var writes = 0; var publishes = 0;
    var service = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
        new TestFinalConfirmationTransaction((c, _, _) => { writes++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
        new TestConfirmedPlanSnapshotSink((_, _, _) => { publishes++; return Task.CompletedTask; }),
        inputs: new TestApplicableInputRevisionSource((_, _) => { if (++reads == 2) fresh = false;
            return Task.FromResult(new ApplicableInputRevisions(geometry.LandRevisionId, geometry.RequirementsRevisionId)); }),
        freshness: new DynamicFreshnessPolicy(_ => fresh));
    var result = service.ConfirmAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).GetAwaiter().GetResult();
    Assert(result.Status == ConfirmationStatus.ValidationStale && writes == 0 && publishes == 0,
        "Expired validation reached confirmation.");
});

Run("GVR-17 initial and final freshness exceptions are classified", () =>
{
    var geometry = MinimalGeometry(0);
    var hash = PlanRevisionService.ComputeGeometryHash(JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions));
    var baseline = Revision(geometry, hash, 1);
    foreach (var failOn in new[] { 1, 2 })
    foreach (var item in new (Exception Error, ConfirmationStatus Status)[] {
        (new TimeoutException(), ConfirmationStatus.DependencyTimeout),
        (new OperationCanceledException(), ConfirmationStatus.DependencyTimeout),
        (new HttpRequestException(), ConfirmationStatus.DependencyUnavailable),
        (new InvalidOperationException(), ConfirmationStatus.UnexpectedInternalFailure) })
    {
        var checks = 0; var writes = 0; var publishes = 0;
        var service = ConfirmationAuditService(new ConfirmationAuditStore(baseline),
            new TestFinalConfirmationTransaction((c, _, _) => { writes++; return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, c)); }),
            new TestConfirmedPlanSnapshotSink((_, _, _) => { publishes++; return Task.CompletedTask; }),
            freshness: new DynamicFreshnessPolicy(_ => { if (++checks == failOn) throw item.Error; return true; }));
        var result = service.ConfirmAsync(new(geometry.ProjectId, geometry.PlanId, baseline.RevisionId, hash, true), Guid.NewGuid()).GetAwaiter().GetResult();
        Assert(result.Status == item.Status && checks == failOn && writes == 0 && publishes == 0,
            "Freshness exception escaped or reached mutation.");
    }
});

CandidatePreviewContractScenarios.Register(Run);
RevisionHistoryContractScenarios.Register(Run);
EditValidationApplicationFacadeContractScenarios.Register(Run);
TrustedCandidateActionFacadeContractScenarios.Register(Run);
CrossMemberContractCompatibilityScenarios.Register(Run);
ProductionAdapterContractScenarios.Register(Run);
AuthorizationContractScenarios.Register(Run);
CommandFingerprintContractScenarios.Register(Run);
F02SecurityContractScenarios.Register(Run);
MutationFingerprintContractScenarios.Register(Run);
AtomicPlanMutationContractScenarios.Register(Run);
AtomicPlanMutationConsumerContractScenarios.Register(Run);
FinalPlanConfirmationJsonSecurityContractScenarios.Register(Run);
DiagnosticContractScenarios.Register(Run);
PersistenceContractScenarios.Register(Run);
AdvancedGeometryContractScenarios.Register(Run);
AiEditOrchestrationContractScenarios.Register(Run);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{passed} passed, {failures.Count} failed");
    foreach (var failure in failures) Console.Error.WriteLine(failure);
    return 1;
}

Console.WriteLine($"{passed} passed, 0 failed");
return 0;

void Run(string name, Action test)
{
    try { test(); passed++; }
    catch (Exception exception) { failures.Add($"{name}: {exception.Message}"); }
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void AssertThrows<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

static PlanGeometryDto MinimalGeometry(double originX)
{
    var floorId = Guid.NewGuid();
    return new PlanGeometryDto
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        LandRevisionId = Guid.NewGuid(),
        RequirementsRevisionId = Guid.NewGuid(),
        CoordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = originX, Y = 0 },
            XDirection = "right", YDirection = "up"
        },
        Floors = [new FloorDto { Id = floorId, Level = 0, Rooms = [], Walls = [], Openings = [] }]
    };
}

static PlanEditRequestDto EditRequest(string commandType, string payloadJson)
{
    using var document = JsonDocument.Parse(payloadJson);
    return new PlanEditRequestDto
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        BaseRevisionId = Guid.NewGuid(),
        Command = new PlanEditCommandDto
        {
            CommandId = Guid.NewGuid(),
            CommandType = commandType,
            FloorId = Guid.NewGuid(),
            TargetId = commandType is PlanEditCommandTypes.AddRoom or PlanEditCommandTypes.AddOpening
                ? null : Guid.NewGuid(),
            Payload = document.RootElement.Clone()
        }
    };
}

static IReadOnlyDictionary<string, string> ValidCommandPayloads() => new Dictionary<string, string>
{
    [PlanEditCommandTypes.AddRoom] = """
        {"room":{"id":"10000000-0000-4000-8000-000000000001","name":"Room","type":"bedroom","polygon":[{"x":0,"y":0},{"x":4,"y":0},{"x":0,"y":3},{"x":0,"y":0}],"wallIds":["20000000-0000-4000-8000-000000000001","20000000-0000-4000-8000-000000000002","20000000-0000-4000-8000-000000000003"]},"walls":[{"id":"20000000-0000-4000-8000-000000000001","start":{"x":0,"y":0},"end":{"x":4,"y":0},"thickness":0.2},{"id":"20000000-0000-4000-8000-000000000002","start":{"x":4,"y":0},"end":{"x":0,"y":3},"thickness":0.2},{"id":"20000000-0000-4000-8000-000000000003","start":{"x":0,"y":3},"end":{"x":0,"y":0},"thickness":0.2}],"openings":[]}
        """,
    [PlanEditCommandTypes.RemoveRoom] = "{\"removeOrphanOpenings\":true}",
    [PlanEditCommandTypes.MoveRoom] = "{\"deltaX\":1.25,\"deltaY\":-0.5}",
    [PlanEditCommandTypes.ResizeRoom] = "{\"scaleX\":1.2,\"scaleY\":0.8,\"anchorX\":null,\"anchorY\":0}",
    [PlanEditCommandTypes.UpdateWall] = "{\"thickness\":0.25}",
    [PlanEditCommandTypes.AddOpening] = "{\"opening\":{\"id\":\"30000000-0000-4000-8000-000000000001\",\"type\":\"door\",\"wallId\":\"20000000-0000-4000-8000-000000000001\",\"offset\":0.5,\"width\":0.9}}",
    [PlanEditCommandTypes.MoveOpening] = "{\"offset\":1.5}",
    [PlanEditCommandTypes.RemoveOpening] = "{}"
};

static PlanValidationReferenceDto Validation(string hash) => new()
{
    ValidationId = Guid.NewGuid(), ValidatorVersion = "explicit-test-double/1", GeometryHash = hash,
    GeometryValid = true, RequirementsMatch = true, RegulatoryEvidenceStatus = "NOT_VERIFIED",
    HasBlockingIssues = false, ValidatedAtUtc = DateTimeOffset.UtcNow
};

static FinalPlanConfirmationService ConfirmationService(
    PlanGeometryDto geometry, PlanRevisionSnapshot revision, IProjectOwnershipGate ownership) => new(
        ownership, new MemoryRevisionStore(revision),
        new InputSource(geometry.LandRevisionId, geometry.RequirementsRevisionId),
        new FreshnessPolicy(true), new ConfirmationTransaction(), new ConfirmationSink(), TimeProvider.System);

static FinalPlanConfirmationService ConfirmationAuditService(
    IPlanRevisionStore store,
    IFinalConfirmationTransaction? transaction = null,
    IConfirmedPlanSnapshotSink? sink = null,
    IProjectOwnershipGate? ownership = null,
    IApplicableInputRevisionSource? inputs = null,
    IValidationFreshnessPolicy? freshness = null) => new(
        ownership ?? new OwnershipGate(true),
        store,
        inputs ?? new TestApplicableInputRevisionSource((projectId, _) =>
        {
            var current = ((ConfirmationAuditStore)store).Initial;
            return Task.FromResult(new ApplicableInputRevisions(
                current.LandRevisionId, current.RequirementsRevisionId));
        }),
        freshness ?? new FreshnessPolicy(true),
        transaction ?? new TestFinalConfirmationTransaction((confirmation, _, _) =>
            Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation))),
        sink ?? new TestConfirmedPlanSnapshotSink(),
        TimeProvider.System);

static PlanRevisionService RevisionAuditService(IPlanRevisionStore store) => new(
    new OwnershipGate(true), store,
    new ValidationVerifier(ValidationEvidenceStatus.Verified), TimeProvider.System);

static PlanRevisionSnapshot Revision(
    PlanGeometryDto geometry,
    string hash,
    long version,
    PlanValidationReferenceDto? validation = null) => new(
        Guid.NewGuid(), geometry.PlanId, geometry.ProjectId, null, version, geometry.SchemaVersion,
        JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions), hash, geometry.LandRevisionId, geometry.RequirementsRevisionId,
        validation ?? Validation(hash), null, Guid.NewGuid(), DateTimeOffset.UtcNow, null);

sealed class MemoryRevisionStore(PlanRevisionSnapshot initial) : IPlanRevisionStore
{
    private readonly List<PlanRevisionSnapshot> revisions = [initial];
    private readonly object sync = new();

    public Task<PlanRevisionSnapshot?> GetAsync(Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken) =>
        Task.FromResult(Read(() => revisions.SingleOrDefault(item => item.ProjectId == projectId && item.PlanId == planId && item.RevisionId == revisionId)));

    public Task<PlanRevisionSnapshot?> GetCurrentAsync(Guid projectId, Guid planId, CancellationToken cancellationToken) =>
        Task.FromResult(Read(() => revisions.Where(item => item.ProjectId == projectId && item.PlanId == planId).MaxBy(item => item.VersionNumber)));

    public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(Guid projectId, Guid planId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlanRevisionSnapshot>>(Read(() => revisions.Where(item => item.ProjectId == projectId && item.PlanId == planId).ToArray()));

    public Task<AppendRevisionResult> TryAppendAtomicallyAsync(Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision, CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (proposedRevision.EditCommandId is not null
                && revisions.Any(item => item.ProjectId == proposedRevision.ProjectId
                    && item.PlanId == proposedRevision.PlanId
                    && item.EditCommandId == proposedRevision.EditCommandId))
                return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.DuplicateCommand));
            if (revisions.MaxBy(item => item.VersionNumber)!.RevisionId != expectedBaseRevisionId)
                return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.RevisionConflict));
            revisions.Add(proposedRevision);
            return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.Created, proposedRevision));
        }
    }

    private T Read<T>(Func<T> action) { lock (sync) return action(); }
}

sealed class OwnershipGate(bool result) : IProjectOwnershipGate
{
    public Task<bool> OwnsProjectAsync(Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken) => Task.FromResult(result);
}

sealed class InputSource(Guid land, Guid requirements) : IApplicableInputRevisionSource
{
    public Task<ApplicableInputRevisions> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken) =>
        Task.FromResult(new ApplicableInputRevisions(land, requirements));
}

sealed class FreshnessPolicy(bool result) : IValidationFreshnessPolicy
{
    public bool IsFresh(PlanRevisionSnapshot revision, DateTimeOffset now) => result;
}

sealed class DynamicFreshnessPolicy(Func<PlanRevisionSnapshot, bool> handler) : IValidationFreshnessPolicy
{
    public bool IsFresh(PlanRevisionSnapshot revision, DateTimeOffset now) => handler(revision);
}

sealed class ConfirmationTransaction(
    ConfirmationStatus status = ConfirmationStatus.Confirmed,
    bool throwFailure = false) : IFinalConfirmationTransaction
{
    public Task<ConfirmationResult> ConfirmAtomicallyAsync(PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision, CancellationToken cancellationToken)
    {
        if (throwFailure) throw new InvalidOperationException("Explicit transaction test-double failure.");
        return Task.FromResult(new ConfirmationResult(
            status, status == ConfirmationStatus.Confirmed ? confirmation : null));
    }
}

sealed class ConfirmationSink : IConfirmedPlanSnapshotSink
{
    public Guid? PublishedRevisionId { get; private set; }
    public Task PublishAsync(PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision, CancellationToken cancellationToken)
    {
        PublishedRevisionId = exactRevision.RevisionId;
        return Task.CompletedTask;
    }
}

sealed class ValidationVerifier(ValidationEvidenceStatus status) : IPlanValidationEvidenceVerifier
{
    public Task<ValidationEvidenceStatus> VerifyAsync(
        Guid projectId, Guid planId, PlanValidationReferenceDto evidence, string exactGeometryHash,
        CancellationToken cancellationToken) => Task.FromResult(status);
}

sealed class RevisionSecurityTrackingStore(
    IPlanRevisionStore inner) : IPlanRevisionStore
{
    public int ReadCalls { get; private set; }
    public int AppendCalls { get; private set; }

    public Task<PlanRevisionSnapshot?> GetAsync(
        Guid projectId,
        Guid planId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        ReadCalls++;

        return inner.GetAsync(
            projectId, planId, revisionId, cancellationToken);
    }

    public Task<PlanRevisionSnapshot?> GetCurrentAsync(
        Guid projectId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        ReadCalls++;

        return inner.GetCurrentAsync(
            projectId, planId, cancellationToken);
    }

    public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
        Guid projectId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        ReadCalls++;

        return inner.GetHistoryAsync(
            projectId, planId, cancellationToken);
    }

    public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
        Guid expectedBaseRevisionId,
        PlanRevisionSnapshot proposedRevision,
        CancellationToken cancellationToken)
    {
        AppendCalls++;

        return inner.TryAppendAtomicallyAsync(
            expectedBaseRevisionId,
            proposedRevision,
            cancellationToken);
    }
}

enum RevisionAppendFailure
{
    None,
    Timeout,
    Transport,
    Unexpected,
    DependencyCancellation,
    AcknowledgementLostAfterCommit,
    CallerCancellationAfterCommit
}

sealed class AdversarialRevisionStore(PlanRevisionSnapshot initial) : IPlanRevisionStore
{
    private readonly List<PlanRevisionSnapshot> revisions = [initial];
    private readonly object sync = new();

    public Func<Guid, PlanRevisionSnapshot?, PlanRevisionSnapshot?>? ReadTransform { get; init; }
    public Func<PlanRevisionSnapshot?, PlanRevisionSnapshot?>? CurrentTransform { get; init; }
    public Func<AppendRevisionResult, AppendRevisionResult?>? ResponseTransform { get; init; }
    public RevisionAppendFailure Failure { get; init; }
    public CancellationTokenSource? CancellationSource { get; init; }
    public int ReadCalls { get; private set; }
    public int AppendCalls { get; private set; }
    public PlanRevisionSnapshot? LastProposed { get; private set; }
    public int HistoryCount { get { lock (sync) return revisions.Count; } }

    public Task<PlanRevisionSnapshot?> GetAsync(
        Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCalls++;
        PlanRevisionSnapshot? result;
        lock (sync)
            result = revisions.SingleOrDefault(item => item.ProjectId == projectId &&
                item.PlanId == planId && item.RevisionId == revisionId);
        return Task.FromResult(ReadTransform is null ? result : ReadTransform(revisionId, result));
    }

    public Task<PlanRevisionSnapshot?> GetCurrentAsync(
        Guid projectId, Guid planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCalls++;
        PlanRevisionSnapshot? result;
        lock (sync)
            result = revisions.Where(item => item.ProjectId == projectId &&
                item.PlanId == planId).MaxBy(item => item.VersionNumber);
        return Task.FromResult(CurrentTransform is null ? result : CurrentTransform(result));
    }

    public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
        Guid projectId, Guid planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCalls++;
        lock (sync)
            return Task.FromResult<IReadOnlyList<PlanRevisionSnapshot>>(revisions.Where(item =>
                item.ProjectId == projectId && item.PlanId == planId).ToArray());
    }

    public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
        Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AppendCalls++;
        LastProposed = proposedRevision;

        if (Failure == RevisionAppendFailure.Timeout)
            throw new TimeoutException("private timeout");
        if (Failure == RevisionAppendFailure.Transport)
            throw new HttpRequestException("private transport failure");
        if (Failure == RevisionAppendFailure.Unexpected)
            throw new InvalidOperationException("private provider failure");
        if (Failure == RevisionAppendFailure.DependencyCancellation)
            throw new OperationCanceledException("private dependency cancellation");

        lock (sync) revisions.Add(proposedRevision);

        if (Failure == RevisionAppendFailure.AcknowledgementLostAfterCommit)
            throw new HttpRequestException("private acknowledgement lost");
        if (Failure == RevisionAppendFailure.CallerCancellationAfterCommit)
        {
            CancellationSource!.Cancel();
            throw new OperationCanceledException("caller cancelled after commit", cancellationToken);
        }

        var response = new AppendRevisionResult(AppendRevisionStatus.Created, proposedRevision);
        return Task.FromResult(ResponseTransform is null ? response : ResponseTransform(response)!);
    }
}

sealed class ConfirmationAuditStore : IPlanRevisionStore
{
    private readonly PlanRevisionSnapshot initial;
    public ConfirmationAuditStore(PlanRevisionSnapshot initial) => this.initial = initial;
    public PlanRevisionSnapshot Initial => initial;
    public Func<PlanRevisionSnapshot?, PlanRevisionSnapshot?>? SelectedTransform { get; init; }
    public Func<int, PlanRevisionSnapshot, PlanRevisionSnapshot>? CurrentTransform { get; init; }
    public Exception? SelectedException { get; init; }
    public Exception? CurrentException { get; init; }
    public int SelectedReads { get; private set; }
    public int CurrentReads { get; private set; }

    public Task<PlanRevisionSnapshot?> GetAsync(
        Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SelectedReads++;
        if (SelectedException is not null)
            return Task.FromException<PlanRevisionSnapshot?>(SelectedException);
        PlanRevisionSnapshot? result = initial.ProjectId == projectId && initial.PlanId == planId &&
            initial.RevisionId == revisionId ? initial : null;
        return Task.FromResult(SelectedTransform is null ? result : SelectedTransform(result));
    }

    public Task<PlanRevisionSnapshot?> GetCurrentAsync(
        Guid projectId, Guid planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CurrentReads++;
        if (CurrentException is not null)
            return Task.FromException<PlanRevisionSnapshot?>(CurrentException);
        var result = CurrentTransform is null ? initial : CurrentTransform(CurrentReads, initial);
        return Task.FromResult<PlanRevisionSnapshot?>(result);
    }

    public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
        Guid projectId, Guid planId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlanRevisionSnapshot>>([initial]);

    public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
        Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

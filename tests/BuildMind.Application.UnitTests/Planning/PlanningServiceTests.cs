using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Generation.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Planning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Survey.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.UnitTests.Planning;

public sealed class PlanningServiceTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();

    [Fact]
    public void Room_program_is_deterministic_and_uses_policy_dimensions()
    {
        var requirement = Requirement(("bedroom", 2), ("kitchen", 1));
        var (first, _) = GenerationService.BuildRoomProgram(requirement, Policy());
        var (second, _) = GenerationService.BuildRoomProgram(requirement, Policy());
        Assert.Equal(first!.Rooms.Select(r => r.SlotId), second!.Rooms.Select(r => r.SlotId));
        Assert.Equal(3, first.Rooms.Count);
        Assert.Equal(3.0, first.Rooms.First(r => r.RoomType == "bedroom").Dimensions.MinWidthM);
        Assert.Equal(GenerationSchemas.RoomCatalogue, first.CatalogueVersion);
    }

    [Theory]
    [InlineData("other-catalogue", 1, "bedroom", "CATALOGUE_UNSUPPORTED")]
    [InlineData("synthetic-fixture-v1", 2, "bedroom", "FLOOR_SCOPE_UNSUPPORTED")]
    [InlineData("synthetic-fixture-v1", 1, "garage", "ROOM_TYPE_UNSUPPORTED")]
    public void Unsupported_requirements_never_produce_a_program(string catalogue, int floors, string type, string code)
    {
        var requirement = new RequirementRevision(Guid.NewGuid(), Project, 1, "requirements/1", catalogue,
            new RequirementContent(floors, [new RoomCount(type, 1)]), DateTimeOffset.UtcNow);
        var (program, actual) = GenerationService.BuildRoomProgram(requirement, Policy());
        Assert.Null(program);
        Assert.Equal(code, actual);
    }

    [Fact]
    public void Adjacency_uses_first_slots_and_deduplicates_pairs()
    {
        var requirement = new RequirementRevision(Guid.NewGuid(), Project, 1, "requirements/1", "synthetic-fixture-v1",
            new RequirementContent(1, [new RoomCount("bedroom", 2), new RoomCount("kitchen", 1)], [],
                [new RoomRelationshipPreference("kitchen", "bedroom", "adjacent"), new RoomRelationshipPreference("bedroom", "kitchen", "separated"),
                 new RoomRelationshipPreference("bedroom", "bedroom", "adjacent")]), DateTimeOffset.UtcNow);
        var (program, _) = GenerationService.BuildRoomProgram(requirement, Policy());
        var adjacency = GenerationService.BuildAdjacency(requirement, program!);
        Assert.Single(adjacency);
        Assert.Equal("adjacent", adjacency[0].Relationship);
    }

    [Fact]
    public void Synthetic_evidence_is_labelled_and_covers_every_edge()
    {
        var evidence = GenerationService.SyntheticEvidence(Land(), Policy());
        Assert.Equal(RegulatoryEvidenceStatuses.Reviewed, evidence.Status);
        Assert.Equal(4, evidence.Constraints.Count);
        Assert.All(evidence.Constraints, c =>
        {
            Assert.Contains("Synthetic", c.Source.Jurisdiction, StringComparison.Ordinal);
            Assert.Equal("synthetic-fixture-not-reviewed", c.ReviewReference);
        });
    }

    [Fact]
    public async Task Evidence_mode_none_yields_missing_evidence_never_values()
    {
        var provider = new PolicyRegulatoryEvidenceProvider();
        var policy = Policy() with { EvidenceMode = PlanningSchemas.EvidenceModeNone };
        var evidence = (await provider.GetAsync(Project, Land(), Guid.NewGuid(), policy, default)).Evidence;
        Assert.Equal(RegulatoryEvidenceStatuses.Missing, evidence.Status);
        Assert.Empty(evidence.Constraints);
        Assert.Equal(PlanningSchemas.EvidenceModeNone, provider.Mode(policy));
    }

    [Fact]
    public async Task Successful_generation_persists_three_candidates_and_replays_idempotently()
    {
        var harness = new Harness();
        var command = harness.Command("key-success-1");
        var first = await harness.Service.StartAsync(Project, command, default);
        Assert.Equal(PlanningStatus.Success, first.Status);
        Assert.Equal(3, first.Value!.Candidates.Count);
        Assert.Equal(GenerationJobStatus.Succeeded, first.Value.Job.Status);
        Assert.All(first.Value.Candidates, c => Assert.StartsWith("sha256:", c.GeometryHash, StringComparison.Ordinal));

        var replay = await harness.Service.StartAsync(Project, command, default);
        Assert.Equal(PlanningStatus.Success, replay.Status);
        Assert.Equal(first.Value.Job.Id, replay.Value!.Job.Id);
        Assert.Equal(1, harness.Compute.Runs);

        var reused = await harness.Service.StartAsync(Project, command with { Seed = 99 }, default);
        Assert.Equal(PlanningStatus.Conflict, reused.Status);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", reused.Code);
    }

    [Fact]
    public async Task Stale_or_missing_inputs_and_bad_keys_are_rejected_before_any_compute()
    {
        var harness = new Harness();
        Assert.Equal(PlanningStatus.StaleInputs,
            (await harness.Service.StartAsync(Project, harness.Command("key-stale-01") with { ExpectedLandRevisionId = Guid.NewGuid() }, default)).Status);
        Assert.Equal(PlanningStatus.ValidationFailed,
            (await harness.Service.StartAsync(Project, harness.Command("bad key!"), default)).Status);
        harness.Lands.Current = null;
        Assert.Equal(PlanningStatus.InputsMissing,
            (await harness.Service.StartAsync(Project, harness.Command("key-missing-1"), default)).Status);
        Assert.Equal(0, harness.Compute.Runs);
    }

    [Fact]
    public async Task Unsuccessful_generation_is_recorded_truthfully_without_candidates()
    {
        var harness = new Harness();
        harness.Compute.Outcome = GenerationOutcomes.NeedsReview;
        var result = await harness.Service.StartAsync(Project, harness.Command("key-review-1"), default);
        Assert.Equal(PlanningStatus.GenerationUnsuccessful, result.Status);
        Assert.Equal("REGULATORY_EVIDENCE_MISSING", result.Code);
        Assert.Empty(result.Value!.Candidates);
        Assert.Equal(GenerationJobStatus.Failed, harness.Jobs.Completed!.Status);
    }

    [Fact]
    public async Task Dependency_failure_marks_the_job_failed_and_maps_to_unavailable()
    {
        var harness = new Harness();
        harness.Compute.Failure = new DependencyFailureException("python", DependencyFailureKind.Timeout);
        var result = await harness.Service.StartAsync(Project, harness.Command("key-timeout-1"), default);
        Assert.Equal(PlanningStatus.DependencyTimeout, result.Status);
        Assert.Equal(GenerationOutcomes.Unavailable, harness.Jobs.Completed!.Outcome);
    }

    [Fact]
    public async Task Other_users_are_denied_without_touching_jobs()
    {
        var harness = new Harness(authorized: false);
        Assert.Equal(PlanningStatus.NotFound, (await harness.Service.StartAsync(Project, harness.Command("key-denied-1"), default)).Status);
        Assert.Null(harness.Jobs.Started);
    }

    [Fact]
    public async Task Saving_refuses_candidates_that_fail_server_revalidation()
    {
        var harness = new Harness();
        var generated = await harness.Service.StartAsync(Project, harness.Command("key-save-0001"), default);
        harness.Compute.PlanValid = false;
        var save = new PlanSaveService(harness.Access, harness.Jobs, harness.Lands, harness.Requirements, harness.Policies,
            harness.Compute, harness.Plans, TimeProvider.System);
        var result = await save.SaveCandidateAsync(Project, generated.Value!.Job.Id, generated.Value.Candidates[0].Id, new(null), default);
        Assert.Equal(PlanningStatus.ValidationFailed, result.Status);
        Assert.Null(harness.Plans.Created);

        harness.Compute.PlanValid = true;
        result = await save.SaveCandidateAsync(Project, generated.Value.Job.Id, generated.Value.Candidates[0].Id, new(null), default);
        Assert.Equal(PlanningStatus.Success, result.Status);
        Assert.Equal(PlanningSchemas.SyntheticEvidenceStatus, result.Value!.Validation.RegulatoryEvidenceStatus);
        Assert.False(result.Value.Validation.HasBlockingIssues);
        Assert.Equal(1, result.Value.VersionNumber);
    }

    private static GenerationPolicy Policy() => new("synthetic-policy", "DEVELOPMENT ONLY",
        new Dictionary<string, RoomDimensionsDto>
        {
            ["bedroom"] = new() { MinWidthM = 3, MinDepthM = 3 },
            ["kitchen"] = new() { MinWidthM = 2.5, MinDepthM = 2.5 },
        }, PlanningSchemas.EvidenceModeSynthetic, 1, "synthetic-captain-v1");

    private static RequirementRevision Requirement(params (string Type, int Count)[] rooms) =>
        new(Guid.NewGuid(), Project, 1, "requirements/1", "synthetic-fixture-v1",
            new RequirementContent(1, rooms.Select(r => new RoomCount(r.Type, r.Count))), DateTimeOffset.UtcNow);

    private static LandRevision Land() => new(Guid.NewGuid(), Project, 1, new MetricLandDto
    {
        SchemaVersion = PlanningSchemas.MetricLand,
        CoordinateSystem = new CoordinateSystemDto { Type = "localCartesian", Unit = "m", Origin = new() { X = 0, Y = 0 }, XDirection = "right", YDirection = "up" },
        Boundary = new PolygonBoundaryDto { Exterior = new[] { (0d, 0d), (12d, 0d), (12d, 10d), (0d, 10d), (0d, 0d) }.Select(p => new CoordinatePointDto { X = p.Item1, Y = p.Item2 }).ToArray() },
    }, 120, 4, null, Actor, DateTimeOffset.UtcNow);

    private sealed class Harness
    {
        public FakeLands Lands { get; } = new();
        public FakeRequirements Requirements { get; } = new();
        public FakeJobs Jobs { get; } = new();
        public FakeCompute Compute { get; } = new();
        public FakePlans Plans { get; } = new();
        public FakePolicies Policies { get; } = new();
        public PlanningAccess Access { get; }
        public GenerationService Service { get; }

        public Harness(bool authorized = true)
        {
            Lands.Current = Land();
            Requirements.Current = Requirement(("bedroom", 2), ("kitchen", 1));
            Access = new PlanningAccess(new User(), new Authorization(authorized));
            Service = new GenerationService(Access, Lands, Requirements, Policies, new PolicyRegulatoryEvidenceProvider(), Jobs, Compute, TimeProvider.System);
        }

        public StartGenerationCommand Command(string key) => new(key, Lands.Current?.Id ?? Guid.NewGuid(), Requirements.Current!.Id, null);
    }

    private sealed class User : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Actor;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId() => Actor;
    }

    private sealed class Authorization(bool authorized) : IProjectAuthorizationPort
    {
        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid actorId, CancellationToken token) =>
            Task.FromResult(authorized
                ? new ProjectAuthorizationResult(ProjectAuthorizationStatus.Authorized, new ProjectAuthorizationSnapshot(projectId, actorId, new AuthorizationVersion(3)))
                : new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied));
    }

    [Fact]
    public async Task Land_submit_computes_perimeter_and_forwards_previous_frame_anchor()
    {
        var lands = new FakeLands
        {
            Current = Land() with
            {
                FrameJson = """{"frame":{"frameId":"00000000-0000-0000-0000-000000000000","crs":"EPSG:5235","anchor":[500100,500200]},"edges":[{"edgeId":"11111111-1111-1111-1111-111111111111","start":[0,0],"end":[12,0],"lengthM":12,"gridBearingDeg":90,"role":"UNASSIGNED","parentEdgeId":null}]}""",
            },
        };
        var compute = new FakeCompute();
        var access = new PlanningAccess(new User(), new Authorization(true));
        var service = new LandService(access, lands, compute, new FakeSurveys(), TimeProvider.System);
        var exterior = new[]
        {
            new CoordinatePointDto { X = 0, Y = 0 },
            new CoordinatePointDto { X = 12, Y = 0 },
            new CoordinatePointDto { X = 12, Y = 10 },
            new CoordinatePointDto { X = 0, Y = 10 },
        };

        var result = await service.SubmitAsync(Project, new SubmitLandCommand(exterior, lands.Current.Id, null), CancellationToken.None);

        Assert.Equal(PlanningStatus.Success, result.Status);
        Assert.Equal(44d, result.Value!.PerimeterM);
        Assert.NotNull(compute.LastLandFrameInput);
        Assert.Equal("EPSG:5235", compute.LastLandFrameInput!.PreviousCrs);
        Assert.NotNull(compute.LastLandFrameInput.PreviousAnchor);
        Assert.Equal([500100d, 500200d], compute.LastLandFrameInput.PreviousAnchor!);
        Assert.Single(compute.LastLandFrameInput.PreviousEdges!);
    }

    private sealed class FakeSurveys : ISurveyRepository
    {
        public Task<Survey?> GetAsync(Guid projectId, Guid surveyId, CancellationToken cancellationToken) =>
            Task.FromResult<Survey?>(null);
        public Task<Survey?> AddAsync(Survey survey, CancellationToken cancellationToken) =>
            Task.FromResult<Survey?>(survey);
        public Task<bool> UpdateAsync(Survey survey, long expectedStateVersion, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    private sealed class FakePolicies : IGenerationPolicyProvider { public GenerationPolicy? Get() => Policy(); }

    private sealed class FakeLands : ILandRevisionStore
    {
        public LandRevision? Current { get; set; }
        public Task<LandRevision?> GetCurrentAsync(Guid projectId, CancellationToken token) => Task.FromResult(Current);
        public Task<LandRevision?> GetAsync(Guid projectId, Guid id, CancellationToken token) => Task.FromResult(Current?.Id == id ? Current : null);
        public Task<ProtectedWriteStatus> TryAppendAsync(LandRevision revision, Guid? expected, long version, CancellationToken token) =>
            Task.FromResult(ProtectedWriteStatus.Written);
    }

    private sealed class FakeRequirements : IRequirementRevisionRepository
    {
        public RequirementRevision? Current { get; set; }
        public Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken token) => Task.FromResult(Current);
        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken token) => Task.FromResult(Current);
        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expected, RequirementRevision proposed, CancellationToken token) =>
            throw new NotSupportedException();
    }

    private sealed class FakeJobs : IGenerationJobStore
    {
        private readonly List<GenerationJob> jobs = [];
        private readonly Dictionary<Guid, IReadOnlyList<GenerationCandidate>> candidates = [];
        public GenerationJob? Started { get; private set; }
        public GenerationJob? Completed { get; private set; }

        public Task<GenerationJob?> FindByKeyAsync(Guid projectId, Guid actorId, string key, CancellationToken token) =>
            Task.FromResult(jobs.SingleOrDefault(j => j.ProjectId == projectId && j.RequestedBy == actorId && j.IdempotencyKey == key));
        public Task<GenerationJob?> GetAsync(Guid projectId, Guid jobId, CancellationToken token) =>
            Task.FromResult(jobs.SingleOrDefault(j => j.ProjectId == projectId && j.Id == jobId));
        public Task<IReadOnlyList<GenerationCandidate>> GetCandidatesAsync(Guid projectId, Guid jobId, CancellationToken token) =>
            Task.FromResult(candidates.GetValueOrDefault(jobId) ?? []);
        public Task<ProtectedWriteStatus> TryStartAsync(GenerationJob running, long version, CancellationToken token)
        {
            Assert.Equal(3, version);
            Started = running;
            jobs.Add(running);
            return Task.FromResult(ProtectedWriteStatus.Written);
        }
        public Task<ProtectedWriteStatus> TryCompleteAsync(GenerationJob completed, IReadOnlyList<GenerationCandidate> items, long version, CancellationToken token)
        {
            Completed = completed;
            jobs.RemoveAll(j => j.Id == completed.Id);
            jobs.Add(completed);
            candidates[completed.Id] = items;
            return Task.FromResult(ProtectedWriteStatus.Written);
        }
    }

    private sealed class FakePlans : IPlanCreationStore
    {
        public PlanRevisionSnapshot? Created { get; private set; }
        public Task<ProtectedWriteStatus> TryCreateAsync(PlanRevisionSnapshot first, IssuedValidationEvidence evidence, long version, CancellationToken token)
        {
            Created = first;
            return Task.FromResult(ProtectedWriteStatus.Written);
        }
    }

    private sealed class FakeCompute : IPlanningComputeClient
    {
        public int Runs { get; private set; }
        public string Outcome { get; set; } = GenerationOutcomes.Success;
        public Exception? Failure { get; set; }
        public bool PlanValid { get; set; } = true;
        public LandFrameInput? LastLandFrameInput { get; private set; }

        public Task<LandFrameResult> BuildLandFrameAsync(LandFrameInput input, CancellationToken cancellationToken)
        {
            LastLandFrameInput = input;
            var pts = input.Exterior;
            var open = pts.Count > 1 && pts[0].X == pts[^1].X && pts[0].Y == pts[^1].Y ? pts.Take(pts.Count - 1).ToArray() : pts.ToArray();
            var digest = LandFrameDigest.Compute("LOCAL", 0, 0, open.Select(p => (p.X, p.Y)).ToArray());
            return Task.FromResult(new LandFrameResult(true, "VALID", open, """{"frameId":"00000000-0000-0000-0000-000000000000","crs":"LOCAL"}""",
                digest, [], "LOCAL", [0, 0]));
        }

        public Task<LandCheckResult> ValidateLandAsync(Guid projectId, Guid landRevisionId, MetricLandDto land, CancellationToken token) =>
            Task.FromResult(new LandCheckResult(true, "LAND_VALID", 120, 4));

        public Task<GenerationResultDto> RunGenerationAsync(GenerationInputDto input, bool synthetic, CancellationToken token)
        {
            Runs++;
            if (Failure is not null) throw Failure;
            Assert.True(synthetic);
            var success = Outcome == GenerationOutcomes.Success;
            return Task.FromResult(new GenerationResultDto
            {
                SchemaVersion = GenerationSchemas.Result, ProjectId = input.ProjectId, LandRevisionId = input.LandRevisionId,
                RequirementsRevisionId = input.RequirementsRevisionId, Outcome = Outcome, Seed = input.Seed,
                ReasonCode = success ? null : "REGULATORY_EVIDENCE_MISSING", AlgorithmVersion = "test",
                Diagnostics = success ? [] : [new DiagnosticDto { Code = "REGULATORY_EVIDENCE_MISSING", Severity = "CRITICAL", Message = "Missing." }],
                Alternatives = success ? Enumerable.Range(0, 3).Select(i => Alternative(input, i)).ToArray() : [],
            });
        }

        public Task<PlanCheckResult> ValidatePlanAsync(PlanCheckRequest request, CancellationToken token) =>
            Task.FromResult(PlanValid
                ? new PlanCheckResult(true, true, "captain-plan-validation/1", [])
                : new PlanCheckResult(false, true, "captain-plan-validation/1",
                    [new DiagnosticDto { Code = "ROOMS_OVERLAP", Severity = "CRITICAL", Message = "Two rooms overlap." }]));

        private static GenerationAlternativeDto Alternative(GenerationInputDto input, int index)
        {
            var plan = Guid.NewGuid();
            return new GenerationAlternativeDto
            {
                ProjectId = input.ProjectId, LandRevisionId = input.LandRevisionId, RequirementsRevisionId = input.RequirementsRevisionId,
                AlternativeId = Guid.NewGuid(), AlgorithmVersion = "test",
                Geometry = new PlanGeometryDto
                {
                    SchemaVersion = GeometrySchemaInfo.DraftVersion, ProjectId = input.ProjectId, PlanId = plan,
                    LandRevisionId = input.LandRevisionId, RequirementsRevisionId = input.RequirementsRevisionId,
                    CoordinateSystem = input.Land.CoordinateSystem, Floors = [new FloorDto { Id = Guid.NewGuid(), Level = 0, Rooms = [], Walls = [], Openings = [] }],
                },
                Metrics = new AlternativeMetricsDto { FloorAreaM2 = 30, FootprintAreaM2 = 30, RoomCount = 3 },
                Validation = new ValidationResultDto
                {
                    ProjectId = input.ProjectId, LandRevisionId = input.LandRevisionId, RequirementsRevisionId = input.RequirementsRevisionId,
                    ValidationId = Guid.NewGuid(), CandidateId = plan, GeometryHash = new string('b', 64), GeometryValid = true,
                    RequirementsMatch = true, RegulatoryEvidenceStatus = "REVIEWED", HasBlockingIssues = false, ValidatorVersion = "test",
                    RuleSetVersion = "synthetic", ValidatedAtUtc = DateTimeOffset.UtcNow, Diagnostics = [],
                },
                DiversitySignature = new DiversitySignatureDto { Version = "v", Value = $"s{index}" },
            };
        }
    }
}

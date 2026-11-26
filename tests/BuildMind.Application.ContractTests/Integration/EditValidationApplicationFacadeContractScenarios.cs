using System.Text.Json;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

internal static class EditValidationApplicationFacadeContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("edit-validation facade uses only the trusted current actor", () =>
        {
            var fixture = new Fixture();

            var result = fixture.Validate();

            Check(result.Status == CandidatePreviewResultStatus.Created, "Trusted preview did not complete.");
            Check(result.Candidate?.OwnerId == fixture.ActorId, "Candidate was not bound to the trusted actor.");
            Check(fixture.CurrentUser.RequiredUserCalls == 1, "Trusted identity was not read exactly once.");
            Check(fixture.LastOwnershipActor == fixture.ActorId, "Ownership used an untrusted actor.");
            Check(fixture.LastRequirementActor == fixture.ActorId, "Requirement access used an untrusted actor.");
            Check(typeof(CreateCandidatePreviewCommand).GetProperties().All(property =>
                    !property.Name.Contains("Actor", StringComparison.OrdinalIgnoreCase) &&
                    !property.Name.Contains("Owner", StringComparison.OrdinalIgnoreCase) &&
                    !property.Name.Contains("User", StringComparison.OrdinalIgnoreCase)),
                "The internal request unexpectedly exposes a client actor field.");
        });

        run("edit-validation facade fails closed for missing and invalid principals", () =>
        {
            foreach (var actor in new Guid?[] { null, Guid.Empty })
            {
                var fixture = new Fixture(actor, useDefaultActor: false);

                var result = fixture.Validate();

                Check(result.Status == CandidatePreviewResultStatus.Unauthorized,
                    "A missing or invalid principal was not denied.");
                Check(result.Candidate is null && result.Diagnostics is null,
                    "Principal denial disclosed protected output.");
                Check(fixture.OwnershipCalls == 0 && fixture.Store.CreateCalls == 0,
                    "Principal denial reached protected orchestration.");
            }
        });

        run("edit-validation facade preserves exact project plan and revision bindings", () =>
        {
            var fixture = new Fixture();

            var result = fixture.Validate();

            var candidate = result.Candidate ?? throw new Exception("Candidate payload was missing.");
            Check(candidate.ProjectId == fixture.ProjectId && candidate.PlanId == fixture.PlanId,
                "Project or plan binding changed.");
            Check(candidate.BaseRevisionId == fixture.BaseRevisionId,
                "Base revision binding changed.");
            Check(fixture.LastOwnershipProject == fixture.ProjectId,
                "Ownership received a different project.");
            Check(fixture.LastRequirementProject == fixture.ProjectId &&
                  fixture.LastRequirementRevision == fixture.RequirementsRevisionId,
                "Authorized requirement lookup lost its exact binding.");
            Check(fixture.LastComputeRequest?.SourceGeometry.ProjectId == fixture.ProjectId &&
                  fixture.LastComputeRequest.SourceGeometry.PlanId == fixture.PlanId &&
                  fixture.LastComputeRequest.Command.CommandId == fixture.Command.CommandId,
                "Compute orchestration received contradictory identities.");
        });

        run("edit-validation facade propagates caller cancellation before protected work", () =>
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            CaptureThrows<OperationCanceledException>(() => fixture.Validate(cancellation.Token));

            Check(fixture.CurrentUser.RequiredUserCalls == 0 && fixture.OwnershipCalls == 0 &&
                  fixture.ComputeCalls == 0 && fixture.Store.CreateCalls == 0,
                "Pre-cancelled validation reached protected work.");
        });

        run("edit-validation facade preserves dependency failure classifications without payload", () =>
        {
            foreach (var item in new (Exception Failure, CandidatePreviewResultStatus Status)[]
                     {
                         (new TimeoutException("test-only timeout"), CandidatePreviewResultStatus.DependencyTimeout),
                         (new HttpRequestException("test-only unavailable"), CandidatePreviewResultStatus.DependencyUnavailable)
                     })
            {
                var fixture = new Fixture { ComputeFailure = item.Failure };

                var result = fixture.Validate();

                Check(result.Status == item.Status, "Dependency failure classification changed.");
                Check(result.Candidate is null && result.Diagnostics is null,
                    "Dependency failure disclosed protected output.");
                Check(fixture.ComputeCalls == 1 && fixture.Store.CreateCalls == 0,
                    "Dependency failure did not stop before persistence.");
            }
        });

        run("edit-validation facade preserves existing candidate-preview orchestration", () =>
        {
            var fixture = new Fixture();

            var result = fixture.Validate();

            Check(result.Status == CandidatePreviewResultStatus.Created,
                "Existing successful orchestration was not preserved.");
            var candidate = result.Candidate ?? throw new Exception("Candidate payload was missing.");
            Check(fixture.OwnershipCalls == 3 && fixture.RequirementCalls == 1 &&
                  fixture.ComputeCalls == 1 && fixture.EvidenceCalls == 1 &&
                  fixture.VerificationCalls == 1 && fixture.Store.CreateCalls == 1,
                "Facade changed the existing orchestration call sequence.");
            Check(fixture.Store.Stored?.CandidateId == candidate.CandidateId,
                "Facade replaced or bypassed the existing candidate result.");
        });
    }

    private sealed class Fixture
    {
        private readonly FixedTimeProvider clock = new(DateTimeOffset.Parse("2026-11-21T00:00:00Z"));
        private readonly CandidatePreviewService service;

        public Fixture(Guid? actorId = null, bool useDefaultActor = true)
        {
            ActorId = useDefaultActor && actorId is null ? Id(1) : actorId ?? Guid.Empty;
            CurrentUser = new CurrentUserDouble(useDefaultActor && actorId is null ? ActorId : actorId);
            ProjectId = Id(2);
            PlanId = Id(3);
            BaseRevisionId = Id(4);
            LandRevisionId = Id(5);
            RequirementsRevisionId = Id(6);
            Command = ValidCommand();

            var geometry = Geometry(ProjectId, PlanId, LandRevisionId, RequirementsRevisionId);
            var geometryJson = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
            var geometryHash = PlanRevisionService.ComputeGeometryHash(geometryJson);
            var evidence = Evidence(geometryHash);
            var revision = new PlanRevisionSnapshot(
                BaseRevisionId, PlanId, ProjectId, null, 1, geometry.SchemaVersion, geometryJson, geometryHash,
                LandRevisionId, RequirementsRevisionId, evidence, null, ActorId, clock.GetUtcNow(), "base");
            Revisions = new TestPlanRevisionStore(revision);
            Store = new PreviewStoreDouble();

            service = new CandidatePreviewService(
                new TestProjectOwnershipGate((project, actor, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    OwnershipCalls++;
                    LastOwnershipProject = project;
                    LastOwnershipActor = actor;
                    return Task.FromResult(project == ProjectId && actor == ActorId);
                }),
                Revisions,
                new TestApplicableInputRevisionSource((project, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Check(project == ProjectId, "Applicable input lookup received another project.");
                    return Task.FromResult(new ApplicableInputRevisions(LandRevisionId, RequirementsRevisionId));
                }),
                new TestConfirmedLandRevisionPort((project, revisionId, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<ConfirmedLandRevision?>(Land(project, revisionId));
                }),
                new TestAuthorizedRequirementRevisionPort((project, revisionId, actor, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    RequirementCalls++;
                    LastRequirementProject = project;
                    LastRequirementRevision = revisionId;
                    LastRequirementActor = actor;
                    var authorization = new ProjectAuthorizationSnapshot(project, actor, new AuthorizationVersion(1));
                    return Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Found,
                        new RequirementRevisionReference(project, revisionId, "test-only/1"),
                        authorization));
                }),
                new TestPlanEditorComputeClient((request, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    ComputeCalls++;
                    LastComputeRequest = request;
                    if (ComputeFailure is not null)
                        return Task.FromException<PlanEditResultDto>(ComputeFailure);
                    return Task.FromResult(new PlanEditResultDto
                    {
                        SchemaVersion = geometry.SchemaVersion,
                        CommandId = request.Command.CommandId,
                        BaseRevisionId = BaseRevisionId,
                        Status = PlanEditStatus.Accepted,
                        CandidateGeometry = geometry,
                        Diagnostics = [],
                        ChangedEntityIds = []
                    });
                }),
                new ValidationEvidenceSourceDouble((project, plan, hash, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    EvidenceCalls++;
                    Check(project == ProjectId && plan == PlanId, "Evidence lookup received another scope.");
                    return Task.FromResult<PlanValidationReferenceDto?>(Evidence(hash));
                }),
                new TestPlanValidationEvidenceVerifier((project, plan, supplied, hash, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    VerificationCalls++;
                    Check(project == ProjectId && plan == PlanId && supplied.GeometryHash == hash,
                        "Evidence verification received another scope.");
                    return Task.FromResult(ValidationEvidenceStatus.Verified);
                }),
                Store,
                Store,
                clock,
                TimeSpan.FromMinutes(15));
        }

        public Guid ActorId { get; }
        public Guid ProjectId { get; }
        public Guid PlanId { get; }
        public Guid BaseRevisionId { get; }
        public Guid LandRevisionId { get; }
        public Guid RequirementsRevisionId { get; }
        public PlanEditCommandDto Command { get; }
        public CurrentUserDouble CurrentUser { get; }
        public TestPlanRevisionStore Revisions { get; }
        public PreviewStoreDouble Store { get; }
        public Exception? ComputeFailure { get; init; }
        public int OwnershipCalls { get; private set; }
        public int RequirementCalls { get; private set; }
        public int ComputeCalls { get; private set; }
        public int EvidenceCalls { get; private set; }
        public int VerificationCalls { get; private set; }
        public Guid LastOwnershipProject { get; private set; }
        public Guid LastOwnershipActor { get; private set; }
        public Guid LastRequirementProject { get; private set; }
        public Guid LastRequirementRevision { get; private set; }
        public Guid LastRequirementActor { get; private set; }
        public PlanEditorComputeRequest? LastComputeRequest { get; private set; }

        public CandidatePreviewResult Validate(CancellationToken cancellationToken = default)
        {
            var facade = new EditValidationApplicationFacade(CurrentUser, service);
            return facade.ValidateAsync(
                    new CreateCandidatePreviewCommand(
                        ProjectId, PlanId, BaseRevisionId, Command, "test-only-correlation"),
                    cancellationToken)
                .GetAwaiter().GetResult();
        }
    }

    private sealed class CurrentUserDouble(Guid? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null && userId != Guid.Empty;
        public Guid? UserId => IsAuthenticated ? userId : null;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public int RequiredUserCalls { get; private set; }

        public Guid GetRequiredUserId()
        {
            RequiredUserCalls++;
            return userId ?? throw new UnauthenticatedAccessException();
        }
    }

    private sealed class ValidationEvidenceSourceDouble(
        Func<Guid, Guid, string, CancellationToken, Task<PlanValidationReferenceDto?>> handler)
        : ICandidateValidationEvidenceSource
    {
        public Task<PlanValidationReferenceDto?> GetAsync(
            Guid projectId, Guid planId, string exactGeometryHash, CancellationToken cancellationToken) =>
            handler(projectId, planId, exactGeometryHash, cancellationToken);
    }

    private sealed class PreviewStoreDouble : ICandidatePreviewStore, ICandidatePreviewTransaction
    {
        public CandidatePreviewSnapshot? Stored { get; private set; }
        public int CreateCalls { get; private set; }

        public Task CreateAsync(CandidatePreviewSnapshot candidate, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCalls++;
            Stored = candidate;
            return Task.CompletedTask;
        }

        public Task<CandidatePreviewSnapshot?> GetAsync(Guid candidateId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Stored?.CandidateId == candidateId ? Stored : null);
        }

        public Task<CandidateActionResult> TryApplyAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, PlanRevisionSnapshot proposedRevision,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The edit-validation facade test must not apply a candidate.");

        public Task<CandidateActionResult> TryRejectAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The edit-validation facade test must not reject a candidate.");

        public Task<CandidateActionResult> TryExpireAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The edit-validation facade test must not expire a candidate.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PlanValidationReferenceDto Evidence(string hash) => new()
    {
        ValidationId = Id(20),
        ValidatorVersion = "test-only/1",
        GeometryHash = hash,
        GeometryValid = true,
        RequirementsMatch = true,
        RegulatoryEvidenceStatus = "VERIFIED",
        HasBlockingIssues = false,
        ValidatedAtUtc = DateTimeOffset.Parse("2026-11-21T00:00:00Z")
    };

    private static ConfirmedLandRevision Land(Guid projectId, Guid revisionId)
    {
        using var document = JsonDocument.Parse("{\"type\":\"Polygon\",\"coordinates\":[]}");
        return new ConfirmedLandRevision(projectId, revisionId, "test-only/1", document.RootElement.Clone());
    }

    private static PlanEditCommandDto ValidCommand()
    {
        using var payload = JsonDocument.Parse("{\"deltaX\":1,\"deltaY\":0}");
        return new PlanEditCommandDto
        {
            CommandId = Id(10),
            CommandType = PlanEditCommandTypes.MoveRoom,
            FloorId = Id(11),
            TargetId = Id(12),
            Payload = payload.RootElement.Clone()
        };
    }

    private static PlanGeometryDto Geometry(
        Guid projectId, Guid planId, Guid landRevisionId, Guid requirementsRevisionId) => new()
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = projectId,
        PlanId = planId,
        LandRevisionId = landRevisionId,
        RequirementsRevisionId = requirementsRevisionId,
        CoordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian",
            Unit = "m",
            Origin = new CoordinatePointDto { X = 0, Y = 0 },
            XDirection = "right",
            YDirection = "up"
        },
        Floors =
        [
            new FloorDto
            {
                Id = Id(13),
                Level = 0,
                Rooms = [],
                Walls = [],
                Openings = []
            }
        ]
    };

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    private static T CaptureThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
        }

        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}

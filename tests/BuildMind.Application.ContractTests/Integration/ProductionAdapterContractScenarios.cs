using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using System.Text.Json;

namespace BuildMind.Application.ContractTests.Integration;

internal static class ProductionAdapterContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("Production adapter constructors reject null repositories", () =>
        {
            CheckThrows<ArgumentNullException>(() => new ProjectOwnershipGate(null!));
            CheckThrows<ArgumentNullException>(() => new RequirementRevisionAdapter(null!));
        });

        run("Production project ownership adapter accepts only the exact owner-scoped project", () =>
        {
            var ownerId = Guid.NewGuid();
            var project = Project(ownerId);
            var repository = new ProjectRepositoryDouble((projectId, actorId, _) =>
                Task.FromResult<Project?>(projectId == project.Id && actorId == ownerId ? project : null));
            var adapter = new ProjectOwnershipGate(repository);

            Check(adapter.OwnsProjectAsync(project.Id, ownerId, default).Result,
                "The exact project owner was rejected.");
            Check(!adapter.OwnsProjectAsync(project.Id, Guid.NewGuid(), default).Result,
                "A non-owner was accepted.");
            Check(!adapter.OwnsProjectAsync(Guid.NewGuid(), ownerId, default).Result,
                "A missing project was represented as owned.");
        });

        run("Production project ownership adapter rejects empty identifiers without repository access", () =>
        {
            var repository = new ProjectRepositoryDouble((_, _, _) =>
                throw new Exception("The repository must not be called for empty identifiers."));
            var adapter = new ProjectOwnershipGate(repository);

            Check(!adapter.OwnsProjectAsync(Guid.Empty, Guid.NewGuid(), default).Result,
                "An empty project identifier was accepted.");
            Check(!adapter.OwnsProjectAsync(Guid.NewGuid(), Guid.Empty, default).Result,
                "An empty actor identifier was accepted.");
            Check(repository.OwnerLookups == 0, "Invalid identifiers reached the M1 repository.");
        });

        run("Production project ownership adapter rejects contradictory dependency identities", () =>
        {
            var ownerId = Guid.NewGuid();
            var requested = Project(ownerId);
            var wrongProject = Project(ownerId);
            var wrongOwner = Project(Guid.NewGuid());

            Check(!new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, _) =>
                    Task.FromResult<Project?>(wrongProject)))
                .OwnsProjectAsync(requested.Id, ownerId, default).Result,
                "A contradictory project identity was accepted.");
            Check(!new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, _) =>
                    Task.FromResult<Project?>(wrongOwner)))
                .OwnsProjectAsync(wrongOwner.Id, ownerId, default).Result,
                "A contradictory owner identity was accepted.");
        });

        run("Production project ownership adapter preserves cancellation and dependency failures", () =>
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            var cancelled = new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<Project?>(null);
            }));
            CheckThrows<OperationCanceledException>(() => cancelled
                .OwnsProjectAsync(Guid.NewGuid(), Guid.NewGuid(), source.Token).GetAwaiter().GetResult());

            foreach (var error in new Exception[]
                     {
                         new TimeoutException("synthetic timeout"),
                         new HttpRequestException("synthetic unavailability"),
                         new InvalidOperationException("synthetic unexpected failure")
                     })
            {
                var failing = new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, _) =>
                    Task.FromException<Project?>(error)));
                CheckThrows(error.GetType(), () => failing
                    .OwnsProjectAsync(Guid.NewGuid(), Guid.NewGuid(), default).GetAwaiter().GetResult());
            }
        });

        run("Production requirement adapter maps the exact M5 revision", () =>
        {
            var revision = RequirementRevision();
            var repository = new RequirementRepositoryDouble((projectId, revisionId, _) =>
                Task.FromResult<RequirementRevision?>(
                    projectId == revision.ProjectId && revisionId == revision.Id ? revision : null));
            var mapped = new RequirementRevisionAdapter(repository)
                .GetAsync(revision.ProjectId, revision.Id, default).Result;

            Check(mapped is not null && mapped.ProjectId == revision.ProjectId &&
                  mapped.RevisionId == revision.Id && mapped.SchemaVersion == revision.SchemaVersion,
                "The exact requirement identity or schema version was not preserved.");
        });

        run("Production requirement adapter preserves missing revision semantics", () =>
        {
            var adapter = new RequirementRevisionAdapter(new RequirementRepositoryDouble((_, _, _) =>
                Task.FromResult<RequirementRevision?>(null)));
            Check(adapter.GetAsync(Guid.NewGuid(), Guid.NewGuid(), default).Result is null,
                "A missing requirement revision was fabricated.");
        });

        run("Production requirement adapter rejects invalid and contradictory responses", () =>
        {
            var repository = new RequirementRepositoryDouble((_, _, _) =>
                throw new Exception("Invalid identifiers must not reach M5."));
            var adapter = new RequirementRevisionAdapter(repository);
            CheckThrows<ArgumentException>(() => adapter
                .GetAsync(Guid.Empty, Guid.NewGuid(), default).GetAwaiter().GetResult());
            CheckThrows<ArgumentException>(() => adapter
                .GetAsync(Guid.NewGuid(), Guid.Empty, default).GetAwaiter().GetResult());
            Check(repository.Reads == 0, "Invalid identifiers reached the M5 repository.");

            var requestedProject = Guid.NewGuid();
            var requestedRevision = Guid.NewGuid();
            foreach (var contradictory in new[]
                     {
                         RequirementRevision(Guid.NewGuid(), requestedRevision),
                         RequirementRevision(requestedProject, Guid.NewGuid())
                     })
            {
                var contradictoryAdapter = new RequirementRevisionAdapter(
                    new RequirementRepositoryDouble((_, _, _) =>
                        Task.FromResult<RequirementRevision?>(contradictory)));
                CheckThrows<Exception>(() => contradictoryAdapter
                    .GetAsync(requestedProject, requestedRevision, default).GetAwaiter().GetResult());
            }

            var ownerId = Guid.NewGuid();
            var project = Project(ownerId);
            var expectedRequirementId = Guid.NewGuid();
            foreach (var contradictory in new[]
                     {
                         RequirementRevision(Guid.NewGuid(), expectedRequirementId),
                         RequirementRevision(project.Id, Guid.NewGuid())
                     })
            {
                var calls = new List<string>();
                var dependencies = new ComposedPreviewDependencies(project.Id, expectedRequirementId, calls);
                var ownership = new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, _) =>
                    Task.FromResult<Project?>(project)));
                var requirements = new TestAuthorizedRequirementRevisionPort((_, _, actor, _) =>
                    Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Found,
                        new RequirementRevisionReference(
                            contradictory.ProjectId, contradictory.Id, contradictory.SchemaVersion),
                        new ProjectAuthorizationSnapshot(
                            contradictory.ProjectId, actor, new AuthorizationVersion(1)))));

                var result = PreviewService(ownership, requirements, dependencies).PreviewAsync(
                    Command(project.Id, dependencies.PlanId, Guid.NewGuid()), ownerId).Result;
                var diagnostic = PlanEditorFailureMapper.FromCandidatePreview(result.Status);

                Check(result.Status == CandidatePreviewResultStatus.InvalidDependencyResponse,
                    "A contradictory requirement identity was classified as an internal failure.");
                Check(dependencies.UnexpectedAccesses == 0,
                    "A contradictory requirement response reached later dependencies.");
                Check(!diagnostic.SafeMessage.Contains(project.Id.ToString(), StringComparison.Ordinal) &&
                      !diagnostic.SafeMessage.Contains(expectedRequirementId.ToString(), StringComparison.Ordinal),
                    "A contradictory requirement response disclosed protected identifiers.");
            }
        });

        run("Production requirement adapter preserves cancellation and dependency failures", () =>
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            var cancelled = new RequirementRevisionAdapter(new RequirementRepositoryDouble((_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<RequirementRevision?>(null);
            }));
            CheckThrows<OperationCanceledException>(() => cancelled
                .GetAsync(Guid.NewGuid(), Guid.NewGuid(), source.Token).GetAwaiter().GetResult());

            foreach (var error in new Exception[]
                     {
                         new TimeoutException("synthetic timeout"),
                         new HttpRequestException("synthetic unavailability"),
                         new InvalidOperationException("synthetic unexpected failure")
                     })
            {
                var failing = new RequirementRevisionAdapter(new RequirementRepositoryDouble((_, _, _) =>
                    Task.FromException<RequirementRevision?>(error)));
                CheckThrows(error.GetType(), () => failing
                    .GetAsync(Guid.NewGuid(), Guid.NewGuid(), default).GetAwaiter().GetResult());
            }
        });

        run("Candidate preview uses authorization-aware exact requirement retrieval", () =>
        {
            var calls = new List<string>();
            var ownerId = Guid.NewGuid();
            var project = Project(ownerId);
            var requirement = RequirementRevision(project.Id);
            var dependencies = new ComposedPreviewDependencies(project.Id, requirement.Id, calls);
            var ownership = new ProjectOwnershipGate(new ProjectRepositoryDouble((projectId, actorId, token) =>
            {
                token.ThrowIfCancellationRequested();
                calls.Add("M1 ownership");
                return Task.FromResult<Project?>(projectId == project.Id && actorId == ownerId ? project : null);
            }));
            var requirements = new TestAuthorizedRequirementRevisionPort(
                (projectId, revisionId, actorId, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    calls.Add("authorized requirement");
                    return Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Found,
                        new RequirementRevisionReference(projectId, revisionId, requirement.SchemaVersion),
                        new ProjectAuthorizationSnapshot(projectId, actorId, new AuthorizationVersion(1))));
                });
            var service = PreviewService(ownership, requirements, dependencies);

            var result = service.PreviewAsync(
                Command(project.Id, dependencies.PlanId, Guid.NewGuid()), ownerId).Result;

            Check(result.Status == CandidatePreviewResultStatus.StaleBaseRevision,
                "The composed flow did not reach the safe stale-base milestone after exact dependency reads.");
            Check(calls.SequenceEqual(new[]
                {
                    "M1 ownership", "revision", "inputs", "land", "authorized requirement"
                }), "The consumer executed outside the required authorization order.");
            Check(dependencies.UnexpectedAccesses == 0,
                "The composed contract test reached validation or persistence unexpectedly.");
        });

        run("Composed production adapters block denied and contradictory ownership before M5", () =>
        {
            foreach (var contradictory in new[] { false, true })
            {
                var requirementReads = 0;
                var ownerId = Guid.NewGuid();
                var requested = Project(ownerId);
                var returned = contradictory ? Project(ownerId) : null;
                var ownership = new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, _) =>
                    Task.FromResult(returned)));
                var requirements = new TestAuthorizedRequirementRevisionPort((_, _, _, _) =>
                {
                    requirementReads++;
                    return Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Unauthorized));
                });
                var service = PreviewService(ownership, requirements);

                var result = service.PreviewAsync(
                    Command(requested.Id, Guid.NewGuid(), Guid.NewGuid()), ownerId).Result;
                Check(result.Status == CandidatePreviewResultStatus.Unauthorized && requirementReads == 0,
                    "Denied or contradictory ownership reached the M5 repository.");
            }
        });

        run("Composed production adapters contain ownership failures before M5", () =>
        {
            foreach (var scenario in new (Exception Error, CandidatePreviewResultStatus Status)[]
                     {
                         (new TimeoutException("synthetic timeout"), CandidatePreviewResultStatus.DependencyTimeout),
                         (new HttpRequestException("synthetic unavailability"), CandidatePreviewResultStatus.DependencyUnavailable),
                         (new InvalidOperationException("synthetic failure"), CandidatePreviewResultStatus.UnexpectedInternalFailure)
                     })
            {
                var requirementReads = 0;
                var ownership = new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, _) =>
                    Task.FromException<Project?>(scenario.Error)));
                var requirements = new TestAuthorizedRequirementRevisionPort((_, _, _, _) =>
                {
                    requirementReads++;
                    return Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Unauthorized));
                });
                var result = PreviewService(ownership, requirements)
                    .PreviewAsync(Command(), Guid.NewGuid()).Result;
                Check(result.Status == scenario.Status && requirementReads == 0,
                    "An ownership dependency failure reached the M5 repository.");
            }
        });

        run("Composed production adapters propagate caller cancellation before M5", () =>
        {
            var requirementReads = 0;
            using var source = new CancellationTokenSource();
            source.Cancel();
            var ownership = new ProjectOwnershipGate(new ProjectRepositoryDouble((_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<Project?>(null);
            }));
            var requirements = new TestAuthorizedRequirementRevisionPort((_, _, _, _) =>
            {
                requirementReads++;
                return Task.FromResult(new RequirementRevisionAccessResult(
                    RequirementRevisionAccessStatus.Unauthorized));
            });

            CheckThrows<OperationCanceledException>(() => PreviewService(ownership, requirements)
                .PreviewAsync(Command(), Guid.NewGuid(), source.Token).GetAwaiter().GetResult());
            Check(requirementReads == 0, "Caller cancellation reached the M5 repository.");
        });

        run("Candidate preview rejects a missing actor before ownership and requirement access", () =>
        {
            var ownershipCalls = 0;
            var requirementReads = 0;
            var service = PreviewService(
                new TestProjectOwnershipGate((_, _, _) => { ownershipCalls++; return Task.FromResult(true); }),
                RequirementPort(() => requirementReads++));

            var result = service.PreviewAsync(Command(), Guid.Empty).Result;
            Check(result.Status == CandidatePreviewResultStatus.Unauthorized &&
                  ownershipCalls == 0 && requirementReads == 0,
                "A missing actor reached an authorization or requirement dependency.");
        });

        run("Candidate preview checks ownership before requirement access and fails closed when denied", () =>
        {
            var requirementReads = 0;
            var service = PreviewService(
                new TestProjectOwnershipGate((_, _, _) => Task.FromResult(false)),
                RequirementPort(() => requirementReads++));

            var result = service.PreviewAsync(Command(), Guid.NewGuid()).Result;
            Check(result.Status == CandidatePreviewResultStatus.Unauthorized && requirementReads == 0,
                "A denied actor reached the M5 requirement dependency.");
        });

        run("Candidate preview contains ownership failures before requirement access", () =>
        {
            foreach (var scenario in new (Exception Error, CandidatePreviewResultStatus Status)[]
                     {
                         (new TimeoutException("synthetic timeout"), CandidatePreviewResultStatus.DependencyTimeout),
                         (new HttpRequestException("synthetic unavailability"), CandidatePreviewResultStatus.DependencyUnavailable),
                         (new InvalidOperationException("synthetic failure"), CandidatePreviewResultStatus.UnexpectedInternalFailure)
                     })
            {
                var requirementReads = 0;
                var service = PreviewService(
                    new TestProjectOwnershipGate((_, _, _) => Task.FromException<bool>(scenario.Error)),
                    RequirementPort(() => requirementReads++));
                var result = service.PreviewAsync(Command(), Guid.NewGuid()).Result;
                Check(result.Status == scenario.Status && requirementReads == 0,
                    "An ownership failure reached the M5 requirement dependency.");
            }
        });

        run("Candidate preview propagates caller cancellation before requirement access", () =>
        {
            var requirementReads = 0;
            using var source = new CancellationTokenSource();
            source.Cancel();
            var service = PreviewService(
                new TestProjectOwnershipGate((_, _, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult(true);
                }),
                RequirementPort(() => requirementReads++));

            CheckThrows<OperationCanceledException>(() => service
                .PreviewAsync(Command(), Guid.NewGuid(), source.Token).GetAwaiter().GetResult());
            Check(requirementReads == 0, "Caller cancellation reached the M5 requirement dependency.");
        });
    }

    private static CandidatePreviewService PreviewService(
        IProjectOwnershipGate ownership,
        IAuthorizedRequirementRevisionPort requirements,
        PreviewDependencySentinel? dependencies = null)
    {
        dependencies ??= new PreviewDependencySentinel();
        return new(
            ownership,
            dependencies,
            dependencies,
            dependencies,
            requirements,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            dependencies,
            TimeProvider.System,
            TimeSpan.FromMinutes(5));
    }

    private static IAuthorizedRequirementRevisionPort RequirementPort(Action read) =>
        new TestAuthorizedRequirementRevisionPort((_, _, _, _) =>
        {
            read();
            throw new Exception("Requirement access was not expected in this authorization scenario.");
        });

    private static CreateCandidatePreviewCommand Command(
        Guid? projectId = null,
        Guid? planId = null,
        Guid? baseRevisionId = null) => new(
        projectId ?? Guid.NewGuid(),
        planId ?? Guid.NewGuid(),
        baseRevisionId ?? Guid.NewGuid(),
        new PlanEditCommandDto
        {
            CommandId = Guid.NewGuid(),
            CommandType = PlanEditCommandTypes.MoveRoom,
            FloorId = Guid.NewGuid(),
            TargetId = Guid.NewGuid(),
            Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { })
        },
        "synthetic-correlation");

    private static Project Project(Guid ownerId) => new()
    {
        OwnerId = ownerId,
        Name = "Synthetic production-adapter project",
        CreatedAtUtc = DateTimeOffset.Parse("2026-11-18T00:00:00Z")
    };

    private static RequirementRevision RequirementRevision(
        Guid? projectId = null,
        Guid? revisionId = null,
        string schemaVersion = "requirements/0.1") => new(
            revisionId ?? Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            1,
            schemaVersion,
            "synthetic-catalogue/1",
            new RequirementContent(1, [new RoomCount("bedroom", 1)]),
            DateTimeOffset.Parse("2026-11-18T00:00:00Z"));

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

    private static void CheckThrows(Type expected, Action action)
    {
        try { action(); }
        catch (Exception error) when (error.GetType() == expected) { return; }
        throw new Exception($"Expected {expected.Name}.");
    }

    private sealed class ProjectRepositoryDouble(
        Func<Guid, Guid, CancellationToken, Task<Project?>> lookup) : IProjectRepository
    {
        public int OwnerLookups { get; private set; }

        public Task<Project?> GetByIdAndOwnerAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            OwnerLookups++;
            return lookup(id, ownerId, cancellationToken);
        }

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task AddAsync(Project project, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<bool> UpdateAsync(
            Project project, Guid ownerId, long expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RequirementRepositoryDouble(
        Func<Guid, Guid, CancellationToken, Task<RequirementRevision?>> read) : IRequirementRevisionRepository
    {
        public int Reads { get; private set; }

        public Task<RequirementRevision?> GetAsync(
            Guid projectId, Guid revisionId, CancellationToken cancellationToken)
        {
            Reads++;
            return read(projectId, revisionId, cancellationToken);
        }

        public Task<RequirementRevision?> GetCurrentAsync(
            Guid projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(
            Guid? expectedRevisionId,
            RequirementRevision proposedRevision,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private class PreviewDependencySentinel :
        IPlanRevisionStore,
        IApplicableInputRevisionSource,
        IConfirmedLandRevisionPort,
        IPlanEditorComputeClient,
        ICandidateValidationEvidenceSource,
        IPlanValidationEvidenceVerifier,
        ICandidatePreviewStore,
        ICandidatePreviewTransaction
    {
        public int UnexpectedAccesses { get; private set; }

        protected Exception Unexpected(string dependency)
        {
            UnexpectedAccesses++;
            return new InvalidOperationException(
                $"Unexpected {dependency} access in an authorization-boundary scenario.");
        }

        public virtual Task<PlanRevisionSnapshot?> GetCurrentAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken) =>
            throw Unexpected("plan revision");
        public Task<PlanRevisionSnapshot?> GetAsync(
            Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken) =>
            throw Unexpected("plan revision");
        public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken) =>
            throw Unexpected("plan history");
        public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
            Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision,
            CancellationToken cancellationToken) => throw Unexpected("revision persistence");
        public virtual Task<ApplicableInputRevisions> GetCurrentAsync(
            Guid projectId, CancellationToken cancellationToken) => throw Unexpected("input revision");
        public virtual Task<ConfirmedLandRevision?> GetAsync(
            Guid projectId, Guid revisionId, CancellationToken cancellationToken) =>
            throw Unexpected("land revision");
        public Task<PlanEditResultDto> PreviewAsync(
            PlanEditorComputeRequest request, CancellationToken cancellationToken) =>
            throw Unexpected("editor compute");
        public Task<PlanValidationReferenceDto?> GetAsync(
            Guid projectId, Guid planId, string exactGeometryHash, CancellationToken cancellationToken) =>
            throw Unexpected("validation evidence");
        public Task<ValidationEvidenceStatus> VerifyAsync(
            Guid projectId, Guid planId, PlanValidationReferenceDto evidence,
            string exactGeometryHash, CancellationToken cancellationToken) =>
            throw Unexpected("validation verification");
        public Task CreateAsync(CandidatePreviewSnapshot candidate, CancellationToken cancellationToken) =>
            throw Unexpected("candidate persistence");
        public Task<CandidatePreviewSnapshot?> GetAsync(
            Guid candidateId, CancellationToken cancellationToken) => throw Unexpected("candidate persistence");
        public Task<CandidateActionResult> TryApplyAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, PlanRevisionSnapshot proposedRevision,
            DateTimeOffset now, CancellationToken cancellationToken) =>
            throw Unexpected("candidate transaction");
        public Task<CandidateActionResult> TryRejectAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, DateTimeOffset now,
            CancellationToken cancellationToken) => throw Unexpected("candidate transaction");
        public Task<CandidateActionResult> TryExpireAtomicallyAsync(
            CandidatePreviewSnapshot expectedCandidate, DateTimeOffset now,
            CancellationToken cancellationToken) => throw Unexpected("candidate transaction");
    }

    private sealed class ComposedPreviewDependencies(
        Guid projectId,
        Guid requirementRevisionId,
        List<string> calls) : PreviewDependencySentinel
    {
        public Guid PlanId { get; } = Guid.NewGuid();
        private Guid LandRevisionId { get; } = Guid.NewGuid();
        private PlanRevisionSnapshot? revision;

        public override Task<PlanRevisionSnapshot?> GetCurrentAsync(
            Guid requestedProjectId, Guid requestedPlanId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("revision");
            revision ??= Revision(projectId, PlanId, LandRevisionId, requirementRevisionId);
            return Task.FromResult<PlanRevisionSnapshot?>(
                requestedProjectId == projectId && requestedPlanId == PlanId ? revision : null);
        }

        public override Task<ApplicableInputRevisions> GetCurrentAsync(
            Guid requestedProjectId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("inputs");
            return Task.FromResult(new ApplicableInputRevisions(LandRevisionId, requirementRevisionId));
        }

        public override Task<ConfirmedLandRevision?> GetAsync(
            Guid requestedProjectId, Guid requestedRevisionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("land");
            return Task.FromResult<ConfirmedLandRevision?>(new(
                projectId, LandRevisionId, GeometrySchemaInfo.DraftVersion,
                JsonSerializer.SerializeToElement(new { synthetic = true })));
        }

        private static PlanRevisionSnapshot Revision(
            Guid projectId, Guid planId, Guid landRevisionId, Guid requirementRevisionId)
        {
            var geometry = new PlanGeometryDto
            {
                SchemaVersion = GeometrySchemaInfo.DraftVersion,
                ProjectId = projectId,
                PlanId = planId,
                LandRevisionId = landRevisionId,
                RequirementsRevisionId = requirementRevisionId,
                CoordinateSystem = new CoordinateSystemDto
                {
                    Type = "localCartesian",
                    Unit = "m",
                    Origin = new CoordinatePointDto { X = 0, Y = 0 },
                    XDirection = "right",
                    YDirection = "up"
                },
                Floors = [new FloorDto
                {
                    Id = Guid.NewGuid(), Level = 0, Rooms = [], Walls = [], Openings = []
                }]
            };
            var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
            var hash = PlanRevisionService.ComputeGeometryHash(json);
            return new PlanRevisionSnapshot(
                Guid.NewGuid(), planId, projectId, null, 1, geometry.SchemaVersion,
                json, hash, landRevisionId, requirementRevisionId,
                new PlanValidationReferenceDto
                {
                    ValidationId = Guid.NewGuid(),
                    ValidatorVersion = "synthetic-composed-contract/1",
                    GeometryHash = hash,
                    GeometryValid = true,
                    RequirementsMatch = true,
                    RegulatoryEvidenceStatus = "synthetic-not-authoritative",
                    HasBlockingIssues = false,
                    ValidatedAtUtc = DateTimeOffset.Parse("2026-11-18T00:00:00Z")
                },
                null, Guid.NewGuid(), DateTimeOffset.Parse("2026-11-18T00:00:00Z"), "synthetic base");
        }
    }
}

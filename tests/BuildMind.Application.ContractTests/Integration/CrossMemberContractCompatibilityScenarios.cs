using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.ContractTests.Integration;

/// <summary>
/// Test-only consumers proving that M3's provisional ports can consume the currently merged
/// M1/M4/M5 contracts. These are not production adapters or live integration evidence.
/// </summary>
internal static class CrossMemberContractCompatibilityScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("M1 project repository satisfies M3 owner and protected-absence expectations", () =>
        {
            var ownerId = Guid.NewGuid();
            var project = Project(ownerId);
            var repository = new ProjectRepositoryDouble((projectId, requestedOwner, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<Project?>(
                    projectId == project.Id && requestedOwner == ownerId ? project : null);
            });
            var consumer = new TestOnlyProjectOwnershipConsumer(repository);

            Check(consumer.OwnsProjectAsync(project.Id, ownerId, default).Result,
                "The valid project owner was rejected.");
            Check(!consumer.OwnsProjectAsync(project.Id, Guid.NewGuid(), default).Result,
                "A non-owner was accepted.");
            Check(!consumer.OwnsProjectAsync(Guid.NewGuid(), ownerId, default).Result,
                "A missing project was represented as owned.");
            Check(!consumer.OwnsProjectAsync(project.Id, Guid.Empty, default).Result,
                "An empty authenticated identity reached the repository.");
            Check(repository.OwnerLookups == 3,
                "The test-only consumer did not preserve the authentication boundary.");
        });

        run("M1 contradictory ownership responses fail closed", () =>
        {
            var ownerId = Guid.NewGuid();
            var requested = Project(ownerId);
            var foreign = Project(ownerId);
            var repository = new ProjectRepositoryDouble((_, _, _) => Task.FromResult<Project?>(foreign));
            var consumer = new TestOnlyProjectOwnershipConsumer(repository);

            Check(!consumer.OwnsProjectAsync(requested.Id, ownerId, default).Result,
                "A project with the wrong identity was accepted as the requested project.");
        });

        run("M1 cancellation and dependency failures propagate through the test-only consumer", () =>
        {
            var projectId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            using var source = new CancellationTokenSource();
            source.Cancel();
            var cancellationRepository = new ProjectRepositoryDouble((_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<Project?>(null);
            });
            CheckThrows<OperationCanceledException>(() => new TestOnlyProjectOwnershipConsumer(cancellationRepository)
                .OwnsProjectAsync(projectId, ownerId, source.Token).GetAwaiter().GetResult());

            var failure = new TimeoutException("test-only dependency failure");
            var failingRepository = new ProjectRepositoryDouble((_, _, _) =>
                Task.FromException<Project?>(failure));
            CheckThrows<TimeoutException>(() => new TestOnlyProjectOwnershipConsumer(failingRepository)
                .OwnsProjectAsync(projectId, ownerId, default).GetAwaiter().GetResult());
        });

        run("M5 requirement revision maps exactly to the M3 reference", () =>
        {
            var revision = RequirementRevision();
            var repository = new RequirementRepositoryDouble((projectId, revisionId, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<RequirementRevision?>(
                    projectId == revision.ProjectId && revisionId == revision.Id ? revision : null);
            });
            var consumer = new TestOnlyRequirementRevisionConsumer(repository);
            var mapped = consumer.GetAuthorizedAsync(
                revision.ProjectId, revision.Id, ownershipEstablished: true, default).Result;

            Check(mapped is not null && mapped.ProjectId == revision.ProjectId &&
                mapped.RevisionId == revision.Id && mapped.SchemaVersion == revision.SchemaVersion,
                "The M5 revision identity or schema was not preserved for M3.");
            Check(repository.Reads == 1, "The exact M5 revision was not read once.");
        });

        run("M5 missing foreign and unauthorized requirement reads fail closed", () =>
        {
            var requestedProject = Guid.NewGuid();
            var requestedRevision = Guid.NewGuid();
            var missingRepository = new RequirementRepositoryDouble((_, _, _) =>
                Task.FromResult<RequirementRevision?>(null));
            var missingConsumer = new TestOnlyRequirementRevisionConsumer(missingRepository);
            Check(missingConsumer.GetAuthorizedAsync(
                requestedProject, requestedRevision, true, default).Result is null,
                "A missing requirement revision was fabricated.");

            var foreign = RequirementRevision();
            var foreignRepository = new RequirementRepositoryDouble((_, _, _) =>
                Task.FromResult<RequirementRevision?>(foreign));
            CheckThrows<InvalidDataException>(() => new TestOnlyRequirementRevisionConsumer(foreignRepository)
                .GetAuthorizedAsync(requestedProject, requestedRevision, true, default).GetAwaiter().GetResult());

            var unauthorizedRepository = new RequirementRepositoryDouble((_, _, _) =>
                Task.FromResult<RequirementRevision?>(foreign));
            CheckThrows<UnauthenticatedAccessException>(() =>
                new TestOnlyRequirementRevisionConsumer(unauthorizedRepository)
                    .GetAuthorizedAsync(requestedProject, requestedRevision, false, default)
                    .GetAwaiter().GetResult());
            Check(unauthorizedRepository.Reads == 0,
                "The M5 repository was called before the M1 ownership boundary.");
        });

        run("M5 cancellation and dependency failures remain distinguishable", () =>
        {
            var projectId = Guid.NewGuid();
            var revisionId = Guid.NewGuid();
            using var source = new CancellationTokenSource();
            source.Cancel();
            var cancellationRepository = new RequirementRepositoryDouble((_, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<RequirementRevision?>(null);
            });
            CheckThrows<OperationCanceledException>(() =>
                new TestOnlyRequirementRevisionConsumer(cancellationRepository)
                    .GetAuthorizedAsync(projectId, revisionId, true, source.Token).GetAwaiter().GetResult());

            var failureRepository = new RequirementRepositoryDouble((_, _, _) =>
                Task.FromException<RequirementRevision?>(new HttpRequestException("test-only transport failure")));
            CheckThrows<HttpRequestException>(() => new TestOnlyRequirementRevisionConsumer(failureRepository)
                .GetAuthorizedAsync(projectId, revisionId, true, default).GetAwaiter().GetResult());
        });

        run("M4 current-user and clock contracts fit M3 service boundaries", () =>
        {
            var actorId = Guid.NewGuid();
            ICurrentUser authenticated = new CurrentUserDouble(
                actorId, new HashSet<string>(StringComparer.Ordinal) { "Customer" });
            ICurrentUser anonymous = new CurrentUserDouble(
                null, new HashSet<string>(StringComparer.Ordinal));

            Check(authenticated.IsAuthenticated && authenticated.GetRequiredUserId() == actorId &&
                authenticated.Roles.Contains("Customer"),
                "The authenticated M4 current-user contract did not yield the M3 actor identity.");
            CheckThrows<UnauthenticatedAccessException>(() => anonymous.GetRequiredUserId());
            Check(TimeProvider.System.GetUtcNow().Offset == TimeSpan.Zero,
                "The shared TimeProvider does not provide a UTC application clock.");
        });

        run("M4 shared error identifiers align with M3 dependency and revision conventions", () =>
        {
            Check(PlanEditorErrorCodes.DependencyUnavailable == SharedErrorCodes.DependencyUnavailable,
                "Dependency-unavailable codes diverge.");
            Check(PlanEditorErrorCodes.DependencyTimeout == SharedErrorCodes.DependencyTimeout,
                "Dependency-timeout codes diverge.");
            Check(PlanEditorErrorCodes.RevisionConflict == SharedErrorCodes.RevisionConflict,
                "Revision-conflict codes diverge.");
            Check(RequirementSaveStatus.RevisionConflict.ToString() == nameof(AppendRevisionStatus.RevisionConflict),
                "M5 and M3 revision-conflict result names diverge.");
        });

        run("M2 M6 and M7 dependencies remain explicit unimplemented application ports", () =>
        {
            var application = typeof(IConfirmedLandRevisionPort).Assembly;
            var ports = new[]
            {
                typeof(IConfirmedLandRevisionPort),
                typeof(ICandidateValidationEvidenceSource),
                typeof(IPlanValidationEvidenceVerifier),
                typeof(IAiEditProposalPort)
            };
            foreach (var port in ports)
            {
                Check(port.IsInterface, $"{port.Name} is no longer an explicit port.");
                Check(!application.GetTypes().Any(type => type is { IsAbstract: false, IsInterface: false } &&
                    port.IsAssignableFrom(type)),
                    $"{port.Name} unexpectedly acquired an Application implementation.");
            }
        });
    }

    private static Project Project(Guid ownerId) => new()
    {
        OwnerId = ownerId,
        Name = "Synthetic compatibility project",
        CreatedAtUtc = DateTimeOffset.Parse("2026-11-18T00:00:00Z")
    };

    private static RequirementRevision RequirementRevision() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        1,
        "requirements/0.1",
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

    private sealed class TestOnlyProjectOwnershipConsumer(IProjectRepository repository)
        : IProjectOwnershipGate
    {
        public async Task<bool> OwnsProjectAsync(
            Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken)
        {
            if (projectId == Guid.Empty || authenticatedUserId == Guid.Empty) return false;
            var project = await repository.GetByIdAndOwnerAsync(
                projectId, authenticatedUserId, cancellationToken);
            return project is not null && project.Id == projectId && project.OwnerId == authenticatedUserId;
        }
    }

    private sealed class TestOnlyRequirementRevisionConsumer(IRequirementRevisionRepository repository)
        : IRequirementRevisionPort
    {
        public async Task<RequirementRevisionReference?> GetAsync(
            Guid projectId, Guid revisionId, CancellationToken cancellationToken)
        {
            var revision = await repository.GetAsync(projectId, revisionId, cancellationToken);
            if (revision is null) return null;
            if (revision.ProjectId != projectId || revision.Id != revisionId ||
                string.IsNullOrWhiteSpace(revision.SchemaVersion))
                throw new InvalidDataException("The requirement dependency returned a contradictory reference.");
            return new(revision.ProjectId, revision.Id, revision.SchemaVersion);
        }

        public Task<RequirementRevisionReference?> GetAuthorizedAsync(
            Guid projectId, Guid revisionId, bool ownershipEstablished,
            CancellationToken cancellationToken)
        {
            if (!ownershipEstablished) throw new UnauthenticatedAccessException();
            return GetAsync(projectId, revisionId, cancellationToken);
        }
    }

    private sealed class ProjectRepositoryDouble(
        Func<Guid, Guid, CancellationToken, Task<Project?>> ownerLookup) : IProjectRepository
    {
        public int OwnerLookups { get; private set; }

        public Task<Project?> GetByIdAndOwnerAsync(
            Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            OwnerLookups++;
            return ownerLookup(id, ownerId, cancellationToken);
        }

        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test double exercises only owner-scoped lookup.");
        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test double exercises only owner-scoped lookup.");
        public Task AddAsync(Project project, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test double is read-only.");
        public Task<bool> UpdateAsync(
            Project project, Guid ownerId, long expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test double is read-only.");
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
            Guid projectId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test double exercises exact revision lookup.");
        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(
            Guid? expectedRevisionId, RequirementRevision proposedRevision,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("This test double is read-only.");
    }

    private sealed class CurrentUserDouble(Guid? userId, IReadOnlySet<string> roles) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null && userId != Guid.Empty;
        public Guid? UserId => IsAuthenticated ? userId : null;
        public IReadOnlySet<string> Roles { get; } = roles;
        public Guid GetRequiredUserId() => UserId ?? throw new UnauthenticatedAccessException();
    }
}

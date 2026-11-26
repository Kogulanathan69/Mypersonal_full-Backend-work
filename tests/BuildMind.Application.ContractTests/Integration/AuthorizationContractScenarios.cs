using BuildMind.Application.Features.PlanEditor.Integration;

namespace BuildMind.Application.ContractTests.Integration;

internal static class AuthorizationContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("authorization version accepts only positive opaque values", () =>
        {
            var version = new AuthorizationVersion(1);
            Check(version is { Value: 1, IsValid: true }, "A positive authorization version was not preserved.");
            CheckThrows<ArgumentOutOfRangeException>(() => _ = new AuthorizationVersion(0));
            CheckThrows<ArgumentOutOfRangeException>(() => _ = new AuthorizationVersion(-1));
            Check(!default(AuthorizationVersion).IsValid, "The default authorization version was valid.");
        });

        run("authorization snapshot requires exact nonempty identities and version", () =>
        {
            var projectId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var snapshot = new ProjectAuthorizationSnapshot(projectId, actorId, new AuthorizationVersion(7));

            Check(snapshot.ProjectId == projectId && snapshot.ActorId == actorId && snapshot.Version.Value == 7,
                "The authorization snapshot did not preserve its exact identities and version.");
            CheckThrows<ArgumentException>(() => new ProjectAuthorizationSnapshot(
                Guid.Empty, actorId, new AuthorizationVersion(1)));
            CheckThrows<ArgumentException>(() => new ProjectAuthorizationSnapshot(
                projectId, Guid.Empty, new AuthorizationVersion(1)));
            CheckThrows<ArgumentException>(() => new ProjectAuthorizationSnapshot(
                projectId, actorId, default));
        });

        run("authorized result requires one exact snapshot", () =>
        {
            var snapshot = Snapshot();
            var result = new ProjectAuthorizationResult(ProjectAuthorizationStatus.Authorized, snapshot);
            Check(result.Status == ProjectAuthorizationStatus.Authorized && result.Snapshot == snapshot,
                "Authorized result lost its snapshot.");
            CheckThrows<ArgumentNullException>(() =>
                new ProjectAuthorizationResult(ProjectAuthorizationStatus.Authorized));
        });

        run("denied authorization result discloses no snapshot", () =>
        {
            var denied = new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied);
            Check(denied.Snapshot is null, "Denied authorization disclosed a snapshot.");
            CheckThrows<ArgumentException>(() =>
                new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied, Snapshot()));
        });

        run("authorization result rejects undefined status", () =>
        {
            CheckThrows<ArgumentOutOfRangeException>(() =>
                new ProjectAuthorizationResult((ProjectAuthorizationStatus)999));
        });

        run("found requirement result binds exact revision to authorization project", () =>
        {
            var snapshot = Snapshot();
            var revision = new RequirementRevisionReference(snapshot.ProjectId, Guid.NewGuid(), "requirements/1");
            var result = new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.Found, revision, snapshot);

            Check(result.Revision == revision && result.Authorization == snapshot,
                "Found result did not preserve the exact revision and authorization evidence.");
            CheckThrows<ArgumentException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.Found,
                revision with { ProjectId = Guid.NewGuid() },
                snapshot));
        });

        run("found requirement result rejects missing or malformed payload", () =>
        {
            var snapshot = Snapshot();
            CheckThrows<ArgumentNullException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.Found, null, snapshot));
            CheckThrows<ArgumentNullException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.Found,
                new RequirementRevisionReference(snapshot.ProjectId, Guid.NewGuid(), "requirements/1")));
            CheckThrows<ArgumentException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.Found,
                new RequirementRevisionReference(snapshot.ProjectId, Guid.Empty, "requirements/1"),
                snapshot));
            CheckThrows<ArgumentException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.Found,
                new RequirementRevisionReference(snapshot.ProjectId, Guid.NewGuid(), " "),
                snapshot));
        });

        run("authorized not-found requires evidence and no revision", () =>
        {
            var snapshot = Snapshot();
            var result = new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.NotFound, authorization: snapshot);
            Check(result.Revision is null && result.Authorization == snapshot,
                "Authorized absence did not preserve the expected shape.");
            CheckThrows<ArgumentNullException>(() =>
                new RequirementRevisionAccessResult(RequirementRevisionAccessStatus.NotFound));
            CheckThrows<ArgumentException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.NotFound,
                new RequirementRevisionReference(snapshot.ProjectId, Guid.NewGuid(), "requirements/1"),
                snapshot));
        });

        run("unauthorized requirement result discloses no payload", () =>
        {
            var result = new RequirementRevisionAccessResult(RequirementRevisionAccessStatus.Unauthorized);
            Check(result.Revision is null && result.Authorization is null,
                "Unauthorized access disclosed protected data.");
            CheckThrows<ArgumentException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.Unauthorized,
                authorization: Snapshot()));
        });

        run("invalid dependency result never exposes a revision", () =>
        {
            var snapshot = Snapshot();
            var withoutPriorEvidence = new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.InvalidDependencyResponse);
            var withPriorEvidence = new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.InvalidDependencyResponse,
                authorization: snapshot);
            Check(withoutPriorEvidence.Revision is null && withPriorEvidence.Authorization == snapshot,
                "Invalid dependency result did not preserve its safe payload rules.");
            CheckThrows<ArgumentException>(() => new RequirementRevisionAccessResult(
                RequirementRevisionAccessStatus.InvalidDependencyResponse,
                new RequirementRevisionReference(snapshot.ProjectId, Guid.NewGuid(), "requirements/1")));
        });

        run("requirement access result rejects undefined status", () =>
        {
            CheckThrows<ArgumentOutOfRangeException>(() =>
                new RequirementRevisionAccessResult((RequirementRevisionAccessStatus)999));
        });

        run("authorization-aware requirement port preserves exact request identities", () =>
        {
            var projectId = Guid.NewGuid();
            var revisionId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var calls = 0;
            IAuthorizedRequirementRevisionPort port = new AuthorizedRequirementPortDouble(
                (project, revision, actor, _) =>
                {
                    calls++;
                    Check(project == projectId && revision == revisionId && actor == actorId,
                        "The authorization-aware port received altered request identities.");
                    return Task.FromResult(new RequirementRevisionAccessResult(
                        RequirementRevisionAccessStatus.Unauthorized));
                });

            var result = port.GetAsync(projectId, revisionId, actorId, default).GetAwaiter().GetResult();
            Check(result.Status == RequirementRevisionAccessStatus.Unauthorized && calls == 1,
                "The authorization-aware request was not invoked exactly once.");
        });

        run("authorization-aware ports preserve caller cancellation", () =>
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            IProjectAuthorizationPort authorization = new ProjectAuthorizationPortDouble(
                (_, _, token) => Task.FromCanceled<ProjectAuthorizationResult>(token));
            IAuthorizedRequirementRevisionPort requirements = new AuthorizedRequirementPortDouble(
                (_, _, _, token) => Task.FromCanceled<RequirementRevisionAccessResult>(token));

            CheckThrows<OperationCanceledException>(() => authorization
                .AuthorizeOwnerAsync(Guid.NewGuid(), Guid.NewGuid(), source.Token).GetAwaiter().GetResult());
            CheckThrows<OperationCanceledException>(() => requirements
                .GetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), source.Token).GetAwaiter().GetResult());
        });

        run("authorization-aware ports do not collapse dependency failures into denial", () =>
        {
            foreach (var exception in new Exception[]
            {
                new TimeoutException("test timeout"),
                new HttpRequestException("test transport failure"),
                new InvalidDataException("test contradictory response")
            })
            {
                IAuthorizedRequirementRevisionPort port = new AuthorizedRequirementPortDouble(
                    (_, _, _, _) => Task.FromException<RequirementRevisionAccessResult>(exception));
                var caught = CheckThrows<Exception>(() => port
                    .GetAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default)
                    .GetAwaiter().GetResult());
                Check(ReferenceEquals(caught, exception),
                    "A dependency exception was replaced with an authorization result.");
            }
        });
    }

    private static ProjectAuthorizationSnapshot Snapshot() =>
        new(Guid.NewGuid(), Guid.NewGuid(), new AuthorizationVersion(1));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static T CheckThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private sealed class ProjectAuthorizationPortDouble(
        Func<Guid, Guid, CancellationToken, Task<ProjectAuthorizationResult>> handler)
        : IProjectAuthorizationPort
    {
        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
            Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken) =>
            handler(projectId, authenticatedActorId, cancellationToken);
    }

    private sealed class AuthorizedRequirementPortDouble(
        Func<Guid, Guid, Guid, CancellationToken, Task<RequirementRevisionAccessResult>> handler)
        : IAuthorizedRequirementRevisionPort
    {
        public Task<RequirementRevisionAccessResult> GetAsync(
            Guid projectId,
            Guid revisionId,
            Guid authenticatedActorId,
            CancellationToken cancellationToken) =>
            handler(projectId, revisionId, authenticatedActorId, cancellationToken);
    }
}

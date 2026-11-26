using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Domain.Entities;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Services;

public sealed class RequirementMutationCoordinatorTests
{
    [Theory]
    [InlineData(RequirementMutationStatus.Committed)]
    [InlineData(RequirementMutationStatus.Reused)]
    [InlineData(RequirementMutationStatus.Denied)]
    public async Task Existing_receipt_decision_precedes_mutable_dependencies(RequirementMutationStatus decision)
    {
        var setup = new RequirementsApplicationServiceTests.Setup();
        var store = new Store(decision);
        var coordinator = new RequirementMutationCoordinator(setup.User, new Authorization(true),
            new(setup.Store), setup.Catalogues, store, TimeProvider.System);
        var result = await coordinator.ExecuteAsync(setup.Project.Id, Guid.NewGuid(), new(RequirementMutationCategory.Full, F.Request()), default);
        Assert.Equal(decision, result.Status);
        Assert.Equal(1, store.Lookups);
        setup.AssertNoProtectedAccess();
    }

    [Fact]
    public async Task Denied_authorization_never_observes_a_receipt()
    {
        var setup = new RequirementsApplicationServiceTests.Setup();
        var store = new Store(RequirementMutationStatus.Absent);
        var coordinator = new RequirementMutationCoordinator(setup.User, new Authorization(false),
            new(setup.Store), setup.Catalogues, store, TimeProvider.System);
        Assert.Equal(RequirementMutationStatus.Denied,
            (await coordinator.ExecuteAsync(setup.Project.Id, Guid.NewGuid(), null, default)).Status);
        Assert.Equal(0, store.Lookups);
    }

    [Fact]
    public async Task Stale_preparation_observes_again_before_reporting_conflict()
    {
        var setup = new RequirementsApplicationServiceTests.Setup();
        var store = new Store(RequirementMutationStatus.Absent, RequirementMutationStatus.Reused);
        var coordinator = new RequirementMutationCoordinator(setup.User, new Authorization(true),
            new(setup.Store), setup.Catalogues, store, TimeProvider.System);
        var result = await coordinator.ExecuteAsync(setup.Project.Id, Guid.NewGuid(),
            new(RequirementMutationCategory.Full, F.Request(expected: Guid.NewGuid())), default);
        Assert.Equal(RequirementMutationStatus.Reused, result.Status);
        Assert.Equal(2, store.Lookups);
        Assert.Equal(0, setup.Catalogues.Calls);
    }

    private sealed class Authorization(bool allowed) : IProjectAuthorizationPort
    {
        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid actorId, CancellationToken token) =>
            Task.FromResult(allowed ? new ProjectAuthorizationResult(ProjectAuthorizationStatus.Authorized, new(projectId, actorId, new(1)))
                : new(ProjectAuthorizationStatus.Denied));
    }

    private sealed class Store(params RequirementMutationStatus[] decisions) : IRequirementMutationStore
    {
        public int Lookups { get; private set; }
        public Task<RequirementMutationResult> ObserveAsync(RequirementMutationScope scope, RequirementMutation? mutation,
            CancellationToken token) => Task.FromResult(new RequirementMutationResult(decisions[Lookups++]));
        public Task<RequirementMutationResult> CommitAsync(RequirementMutationScope scope, RequirementMutation mutation,
            RequirementRevision proposed, CancellationToken token) => throw new InvalidOperationException("Unexpected write.");
    }
}

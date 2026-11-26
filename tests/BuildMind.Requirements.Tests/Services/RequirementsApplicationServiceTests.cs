using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Domain.Entities;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Services;

public sealed class RequirementsApplicationServiceTests
{
    [Theory]
    [InlineData("unchanged", true, RequirementsOperationStatus.Success)]
    [InlineData("unchanged", false, RequirementsOperationStatus.NoSavedRevision)]
    [InlineData("unauthorized", true, RequirementsOperationStatus.NotFound)]
    [InlineData("transfer", true, RequirementsOperationStatus.NotFound)]
    [InlineData("bump", true, RequirementsOperationStatus.NotFound)]
    [InlineData("bump", false, RequirementsOperationStatus.NotFound)]
    [InlineData("wrong-project", true, RequirementsOperationStatus.InvalidDependencyResponse)]
    [InlineData("wrong-actor", true, RequirementsOperationStatus.InvalidDependencyResponse)]
    [InlineData("null", true, RequirementsOperationStatus.InvalidDependencyResponse)]
    [InlineData("timeout", true, RequirementsOperationStatus.DependencyTimeout)]
    [InlineData("dependency-cancel", true, RequirementsOperationStatus.DependencyUnavailable)]
    [InlineData("caller-cancel", true, RequirementsOperationStatus.DependencyUnavailable)]
    public async Task REQ_G14_Read_requires_two_matching_fresh_snapshots(string change, bool hasRevision,
        RequirementsOperationStatus expected)
    {
        var setup = new Setup();
        if (hasRevision) Assert.Equal(RequirementsOperationStatus.Success, (await setup.Call(true)).Status);
        var initialReads = setup.Store.Reads;
        using var deadline = new CancellationTokenSource();
        var afterRead = false;
        setup.Store.OnRead = () => afterRead = true;
        var calls = 0;
        var port = new Authorization((project, actor) =>
        {
            calls++;
            Assert.Equal(setup.Project.Id, project);
            Assert.Equal(setup.User.Id, actor);
            Assert.Equal(calls == 2, afterRead);
            if (change == "unauthorized" || (afterRead && change == "transfer"))
                return new(ProjectAuthorizationStatus.Denied);
            if (afterRead)
            {
                if (change == "null") return null!;
                if (change == "wrong-actor") actor = Guid.NewGuid();
                if (change == "wrong-project") project = Guid.NewGuid();
                if (change == "timeout") throw new TimeoutException();
                if (change == "dependency-cancel") throw new OperationCanceledException();
                if (change == "caller-cancel") deadline.Cancel();
            }
            return new(ProjectAuthorizationStatus.Authorized, new(project, actor, new(afterRead && change == "bump" ? 2 : 1)));
        });
        var service = new RequirementsApplicationService(setup.User, setup.Projects, setup.Catalogues,
            new(setup.Store), new(setup.Store, TimeProvider.System), port);
        if (change == "caller-cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(setup.Project.Id, deadline.Token));
        else
        {
            var result = await service.GetAsync(setup.Project.Id, deadline.Token);
            Assert.Equal(expected, result.Status);
            if (expected == RequirementsOperationStatus.Success) Assert.Equal(setup.Store.Current!.Id, result.Revision!.RevisionId);
            else { Assert.Null(result.Revision); Assert.Empty(result.Issues); }
        }
        Assert.Equal(change == "unauthorized" ? 1 : 2, calls);
        Assert.Equal(initialReads + (change == "unauthorized" ? 0 : 1), setup.Store.Reads);
    }

    private sealed class Authorization(Func<Guid, Guid, ProjectAuthorizationResult> read) : IProjectAuthorizationPort
    {
        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(Guid projectId, Guid actorId, CancellationToken ct) =>
            Task.FromResult(read(projectId, actorId));
    }

    [Fact]
    public async Task SaveReadReplace_PreservesHistoryAndExplicitlyClearsOptionalSelections()
    {
        var setup = new Setup();
        Assert.Equal(RequirementsOperationStatus.NoSavedRevision, (await setup.Service.GetAsync(setup.Project.Id, default)).Status);
        Assert.Equal(0, setup.Catalogues.Calls);
        var request = new SaveRequirementsRequest(null, F.Version, 1, F.Request().GroundFloorRooms,
            [new("SPACE_TEXT", "text")], [new("ROOM_A", "ROOM_B", "NEAR")], "METHOD_A");
        var first = await setup.Service.SaveAsync(setup.Project.Id, request, default);
        Assert.Equal(RequirementsOperationStatus.Success, first.Status);
        Assert.Equal(1L, first.Revision!.RevisionNumber);
        var read = await setup.Service.GetAsync(setup.Project.Id, default);
        Assert.Equal(first.Revision.RevisionId, read.Revision!.RevisionId);
        Assert.Single(read.Revision.AdditionalSpaces);
        var second = await setup.Service.SaveAsync(setup.Project.Id, F.Request(expected: first.Revision.RevisionId), default);
        Assert.Equal(RequirementsOperationStatus.Success, second.Status);
        Assert.Equal(2L, second.Revision!.RevisionNumber);
        Assert.Empty(second.Revision.AdditionalSpaces);
        Assert.Empty(second.Revision.Relationships);
        Assert.Null(second.Revision.DesignMethod);
        Assert.Single(setup.Store.History[first.Revision.RevisionId].Content.AdditionalSpaces);
        Assert.Equal(2, setup.Store.History.Count);
        Assert.Equal(setup.Project.Id, setup.Catalogues.ProjectId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unauthenticated_NoProjectPolicyOrRevisionAccess(bool save)
    {
        var setup = new Setup(); setup.User.Authenticated = false;
        Assert.Equal(RequirementsOperationStatus.Unauthenticated, (await setup.Call(save)).Status);
        setup.AssertNoProtectedAccess();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InconsistentAuthenticatedIdentity_FailsClosed(bool empty)
    {
        var setup = new Setup(); setup.User.RequiredId = empty ? Guid.Empty : Guid.NewGuid();
        Assert.Equal(RequirementsOperationStatus.Unauthenticated, (await setup.Call(true)).Status);
        setup.AssertNoProtectedAccess();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndNonOwnedProject_AreUniformlyNotFound(bool anotherOwner)
    {
        var setup = new Setup();
        setup.Projects.Returned = anotherOwner ? new Project { OwnerId = Guid.NewGuid(), Name = "Synthetic other account" } : null;
        Assert.Equal(RequirementsOperationStatus.NotFound, (await setup.Call(true)).Status);
        Assert.Equal(0, setup.Store.Reads);
        Assert.Equal(0, setup.Catalogues.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOwnerScopedRepositoryResponse_IsRejectedWithoutDisclosure(bool wrongOwner)
    {
        var setup = new Setup(); setup.Projects.IgnoreFilter = true;
        setup.Projects.Returned = new Project { OwnerId = wrongOwner ? Guid.NewGuid() : setup.User.Id, Name = "Synthetic confidential row" };
        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, (await setup.Call(false)).Status);
        Assert.Equal(0, setup.Store.Reads);
    }

    [Fact]
    public async Task SameProjectReturnedForDifferentAuthenticatedOwner_IsRejected()
    {
        var setup = new Setup(); setup.Projects.IgnoreFilter = true; setup.User.Id = Guid.NewGuid();
        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, (await setup.Call(true)).Status);
        Assert.Equal(0, setup.Store.Reads);
        Assert.Equal(0, setup.Catalogues.Calls);
    }

    [Fact]
    public async Task CancellationDuringOwnerLookup_StopsBeforePolicyOrRevisionAccess()
    {
        var setup = new Setup(); using var source = new CancellationTokenSource();
        setup.Projects.Callback = source.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Service.SaveAsync(setup.Project.Id, F.Request(), source.Token));
        Assert.Equal(0, setup.Catalogues.Calls);
        Assert.Equal(0, setup.Store.Reads);
        Assert.Equal(0, setup.Store.Writes);
    }

    [Fact]
    public async Task MissingCatalogue_IsUnavailableAndNeverSaved()
    {
        var setup = new Setup(); setup.Catalogues.Policy = null;
        Assert.Equal(RequirementsOperationStatus.DependencyUnavailable, (await setup.Call(true)).Status);
        Assert.Equal(0, setup.Store.Writes);
    }

    [Fact]
    public async Task ClientVersion_DoesNotSelectAuthoritativeCatalogue()
    {
        var setup = new Setup();
        var result = await setup.Service.SaveAsync(setup.Project.Id, F.Request(version: "synthetic-client-version"), default);
        Assert.Equal(RequirementsOperationStatus.CatalogueConflict, result.Status);
        Assert.All(result.Issues, item => Assert.Equal("CATALOGUE_VERSION_MISMATCH", item.Code));
        Assert.Equal(1, setup.Catalogues.Calls);
        Assert.Equal(0, setup.Store.Reads);
        Assert.Equal(0, setup.Store.Writes);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("unknown")]
    public async Task InvalidSnapshot_ReturnsValidationIssuesWithoutSave(string kind)
    {
        var setup = new Setup();
        SaveRequirementsRequest? request = kind switch
        {
            "missing" => null,
            "duplicate" => F.WithRooms(new("ROOM_A", 1), new("ROOM_A", 1)),
            "negative" => F.WithRooms(new RoomCountInput("ROOM_A", -1)),
            _ => F.WithRooms(new RoomCountInput("SYNTHETIC_UNKNOWN", 1))
        };
        var result = await setup.Service.SaveAsync(setup.Project.Id, request, default);
        Assert.Equal(RequirementsOperationStatus.ValidationFailed, result.Status);
        Assert.NotEmpty(result.Issues);
        Assert.Null(result.Revision);
        Assert.Equal(0, setup.Store.Writes);
    }

    [Fact]
    public async Task ConfiguredEmptyDesignMethods_IsValidationFailureNotUnavailable()
    {
        var setup = new Setup(); setup.Catalogues.Policy = F.WithMethods([]);
        var result = await setup.Service.SaveAsync(setup.Project.Id, F.Request(method: "METHOD_A"), default);
        Assert.Equal(RequirementsOperationStatus.ValidationFailed, result.Status);
    }

    [Theory]
    [InlineData(RequirementSaveStatus.RevisionConflict, RequirementsOperationStatus.RevisionConflict)]
    [InlineData(RequirementSaveStatus.ProjectNotFound, RequirementsOperationStatus.NotFound)]
    public async Task AtomicSaveRejection_Preserved(RequirementSaveStatus source, RequirementsOperationStatus target)
    {
        var setup = new Setup(); setup.Store.Rejection = source;
        Assert.Equal(target, (await setup.Call(true)).Status);
        Assert.Equal(1, setup.Store.Writes);
    }

    [Fact]
    public async Task StaleExpectedRevision_CannotOverwriteCurrent()
    {
        var setup = new Setup(); var first = await setup.Call(true);
        var result = await setup.Call(true);
        Assert.Equal(RequirementsOperationStatus.RevisionConflict, result.Status);
        Assert.Equal(first.Revision!.RevisionId, setup.Store.Current!.Id);
        Assert.Equal(1, setup.Store.Writes);
    }

    [Fact]
    public async Task RevisionOverflow_IsSafeFailureWithoutWrite()
    {
        var setup = new Setup(); setup.Store.Current = new(F.RevisionId, setup.Project.Id, long.MaxValue, F.Schema, F.Version, F.Content(), F.Timestamp);
        var result = await setup.Service.SaveAsync(setup.Project.Id, F.Request(expected: F.RevisionId), default);
        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, result.Status);
        Assert.Equal(0, setup.Store.Writes);
    }

    [Theory]
    [InlineData("timeout", RequirementsOperationStatus.DependencyTimeout)]
    [InlineData("unavailable", RequirementsOperationStatus.DependencyUnavailable)]
    [InlineData("dependency-timeout", RequirementsOperationStatus.DependencyTimeout)]
    [InlineData("bad-response", RequirementsOperationStatus.InvalidDependencyResponse)]
    public async Task KnownDependencyFailures_AreSafe(string kind, RequirementsOperationStatus status)
    {
        var setup = new Setup(); setup.Catalogues.Failure = kind switch
        {
            "timeout" => new TimeoutException("Synthetic sensitive details"),
            "unavailable" => new DependencyFailureException("synthetic", DependencyFailureKind.Unavailable),
            "dependency-timeout" => new DependencyFailureException("synthetic", DependencyFailureKind.Timeout),
            _ => new InvalidOperationException("Synthetic sensitive response")
        };
        var result = await setup.Call(true);
        Assert.Equal(status, result.Status);
        Assert.Null(result.Revision);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task OpaqueUncertainOutcome_PropagatesWithoutRetry()
    {
        var setup = new Setup(); var failure = new SyntheticUnknownOutcome(); setup.Store.Failure = failure;
        var result = await Assert.ThrowsAsync<SyntheticUnknownOutcome>(() => setup.Call(true));
        Assert.Same(failure, result);
        Assert.Equal(1, setup.Store.Writes);
    }

    [Theory]
    [InlineData(DependencyFailureKind.MalformedResponse)]
    [InlineData(DependencyFailureKind.UnexpectedResponse)]
    [InlineData(DependencyFailureKind.ResponseTooLarge)]
    [InlineData((DependencyFailureKind)99)]
    public async Task CorruptOrUndefinedDependencyFailure_IsInternalFailureNotUnavailable(DependencyFailureKind kind)
    {
        var setup = new Setup(); setup.Catalogues.Failure = new DependencyFailureException("synthetic", kind);
        var result = await setup.Call(true);
        Assert.Equal(RequirementsOperationStatus.InvalidDependencyResponse, result.Status);
        Assert.Null(result.Revision);
        Assert.Empty(result.Issues);
        Assert.Equal(0, setup.Store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_IsNotConvertedToFailure(bool afterPolicy)
    {
        var setup = new Setup(); using var source = new CancellationTokenSource();
        if (afterPolicy) setup.Catalogues.Callback = source.Cancel; else source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Service.SaveAsync(setup.Project.Id, F.Request(), source.Token));
        Assert.Equal(0, setup.Store.Writes);
    }

    [Fact]
    public async Task DependencyOwnedCancellation_IsUnavailable()
    {
        var setup = new Setup(); setup.Catalogues.Failure = new OperationCanceledException();
        Assert.Equal(RequirementsOperationStatus.DependencyUnavailable, (await setup.Call(true)).Status);
    }

    [Fact]
    public async Task ConfirmedSave_RemainsSuccessAfterCancellation()
    {
        var setup = new Setup(); using var source = new CancellationTokenSource(); setup.Store.AfterCommit = source.Cancel;
        var result = await setup.Service.SaveAsync(setup.Project.Id, F.Request(), source.Token);
        Assert.Equal(RequirementsOperationStatus.Success, result.Status);
    }

    [Fact]
    public async Task InvalidProject_IsRejectedBeforeAccess()
    {
        var setup = new Setup();
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Service.GetAsync(Guid.Empty, default));
        setup.AssertNoProtectedAccess();
    }

    private sealed class SyntheticUnknownOutcome : Exception { }
    internal sealed class Setup
    {
        public User User { get; } = new();
        public Project Project { get; }
        public Projects Projects { get; }
        public Catalogues Catalogues { get; } = new();
        public Store Store { get; } = new();
        public RequirementsApplicationService Service { get; }
        public Setup()
        {
            Project = new() { OwnerId = User.Id, Name = "Synthetic owned project" };
            Projects = new() { Returned = Project };
            Service = new(User, Projects, Catalogues, new(Store), new(Store, TimeProvider.System));
        }
        public Task<RequirementsOperationResult> Call(bool save) => save ? Service.SaveAsync(Project.Id, F.Request(), default) : Service.GetAsync(Project.Id, default);
        public void AssertNoProtectedAccess()
        {
            Assert.Equal(0, Projects.Calls); Assert.Equal(0, Catalogues.Calls); Assert.Equal(0, Store.Reads); Assert.Equal(0, Store.Writes);
        }
    }
    internal sealed class User : ICurrentUser
    {
        public Guid Id { get; set; } = Guid.Parse("70000000-0000-0000-0000-000000000007");
        public bool Authenticated { get; set; } = true;
        public Guid? RequiredId { get; set; }
        public bool IsAuthenticated => Authenticated;
        public Guid? UserId => Authenticated ? Id : null;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId() => RequiredId ?? Id;
    }
    internal sealed class Projects : IProjectRepository
    {
        public Project? Returned { get; set; }
        public bool IgnoreFilter { get; set; }
        public Action? Callback { get; set; }
        public int Calls { get; private set; }
        public Task<Project?> GetByIdAndOwnerAsync(Guid id, Guid ownerId, CancellationToken cancellationToken)
        {
            Calls++;
            Callback?.Invoke();
            return Task.FromResult(IgnoreFilter || (Returned?.Id == id && Returned.OwnerId == ownerId) ? Returned : null);
        }
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(Guid ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAsync(Project project, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(Project project, Guid ownerId, long expectedRevision, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    internal sealed class Catalogues : IRequirementCatalogueProvider
    {
        public RequirementCatalogue? Policy { get; set; } = F.Catalogue();
        public Exception? Failure { get; set; }
        public Action? Callback { get; set; }
        public int Calls { get; private set; }
        public Guid ProjectId { get; private set; }
        public Task<RequirementCatalogue?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken)
        {
            Calls++; ProjectId = projectId; Callback?.Invoke();
            return Failure is null ? Task.FromResult(Policy) : Task.FromException<RequirementCatalogue?>(Failure);
        }
    }
    internal sealed class Store : IRequirementRevisionRepository
    {
        private readonly object _gate = new();
        public RequirementRevision? Current { get; set; }
        public Dictionary<Guid, RequirementRevision> History { get; } = [];
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public RequirementSaveStatus? Rejection { get; set; }
        public Exception? Failure { get; set; }
        public Action? AfterCommit { get; set; }
        public Action? OnRead { get; set; }
        public Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken)
        {
            Reads++;
            OnRead?.Invoke();
            return Task.FromResult(Current);
        }
        public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken) => Task.FromResult(History.GetValueOrDefault(revisionId));
        public Task<RequirementSaveResult> TrySaveAtomicallyAsync(Guid? expectedRevisionId, RequirementRevision proposedRevision, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Writes++;
                if (Failure is not null) return Task.FromException<RequirementSaveResult>(Failure);
                if (Rejection is not null) return Task.FromResult(new RequirementSaveResult(Rejection.Value));
                if (Current?.Id != expectedRevisionId) return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.RevisionConflict));
                Current = proposedRevision; History.Add(Current.Id, Current); AfterCommit?.Invoke();
                return Task.FromResult(new RequirementSaveResult(RequirementSaveStatus.Saved, Current));
            }
        }
    }
}

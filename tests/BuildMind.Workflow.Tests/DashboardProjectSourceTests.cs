using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Dashboard.Abstractions;
using BuildMind.Application.Features.Dashboard.Services;
using BuildMind.Domain.Entities;
using Xunit;

namespace BuildMind.Workflow.Tests;

public sealed class DashboardProjectSourceTests
{
    private static readonly Guid OwnerA = Guid.NewGuid();
    private static readonly Guid OwnerB = Guid.NewGuid();

    private sealed class TestCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated { get; set; } = true;
        public Guid? UserId { get; set; } = OwnerA;
        public IReadOnlySet<string> Roles { get; } =
            new HashSet<string>();

        public Guid GetRequiredUserId() =>
            UserId ?? Guid.Empty;
    }

    private sealed class TestRepository : IProjectRepository
    {
        public IReadOnlyList<Project> Projects { get; set; } =
            Array.Empty<Project>();

        public Guid? RequestedOwner { get; private set; }
        public CancellationToken ReceivedToken { get; private set; }
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Action? OnGet { get; set; }

        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(
            Guid ownerId, CancellationToken cancellationToken)
        {
            Calls++;
            RequestedOwner = ownerId;
            ReceivedToken = cancellationToken;
            OnGet?.Invoke();

            if (Failure is not null)
                return Task.FromException<IReadOnlyList<Project>>(Failure);

            return Task.FromResult(Projects);
        }

        public Task<Project?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Project?> GetByIdAndOwnerAsync(
            Guid id, Guid ownerId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task AddAsync(
            Project project,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> UpdateAsync(
            Project project, Guid ownerId,
            long expectedRevision,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private static Project CreateProject(Guid ownerId) =>
        new()
        {
            OwnerId = ownerId,
            Name = "Test Project"
        };

    [Fact]
    public async Task LoadsEmptyDashboard()
    {
        var repository = new TestRepository();
        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        var result = await source.GetForCurrentUserAsync(default);

        Assert.Equal(DashboardProjectReadStatus.Loaded, result.Status);
        Assert.Empty(result.Projects!);
        Assert.Equal(OwnerA, repository.RequestedOwner);
    }

    [Fact]
    public async Task MapsActualProject()
    {
        var project = CreateProject(OwnerA);
        var repository = new TestRepository
        {
            Projects = new[] { project }
        };

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        var result = await source.GetForCurrentUserAsync(default);
        var row = Assert.Single(result.Projects!);

        Assert.Equal(project.Id, row.Project.ProjectId);
        Assert.Equal(project.Name, row.Project.Name);
        Assert.Equal(project.Status, row.Project.StageCode);
        Assert.Equal(OwnerA, row.OwnerId);
    }

    [Fact]
    public async Task LoadsMultipleOwnedProjects()
    {
        var repository = new TestRepository
        {
            Projects = new[]
            {
                CreateProject(OwnerA),
                CreateProject(OwnerA)
            }
        };

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        var result = await source.GetForCurrentUserAsync(default);

        Assert.Equal(2, result.Projects!.Count);
    }

    [Fact]
    public async Task RejectsUnauthenticatedCaller()
    {
        var repository = new TestRepository();

        var source = new DashboardProjectSource(
            new TestCurrentUser { IsAuthenticated = false },
            repository);

        var result = await source.GetForCurrentUserAsync(default);

        Assert.Equal(
            DashboardProjectReadStatus.Unauthenticated,
            result.Status);

        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task RejectsEmptyIdentity()
    {
        var repository = new TestRepository();

        var source = new DashboardProjectSource(
            new TestCurrentUser { UserId = Guid.Empty },
            repository);

        var result = await source.GetForCurrentUserAsync(default);

        Assert.Equal(
            DashboardProjectReadStatus.Unauthenticated,
            result.Status);

        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task RejectsMissingIdentity()
    {
        var repository = new TestRepository();

        var source = new DashboardProjectSource(
            new TestCurrentUser { UserId = null },
            repository);

        var result = await source.GetForCurrentUserAsync(default);

        Assert.Equal(
            DashboardProjectReadStatus.Unauthenticated,
            result.Status);

        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task RejectsForeignOwnerProjects()
    {
        var repository = new TestRepository
        {
            Projects = new[] { CreateProject(OwnerB) }
        };

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.GetForCurrentUserAsync(default));
    }

    [Fact]
    public async Task RejectsMixedOwnerProjects()
    {
        var repository = new TestRepository
        {
            Projects = new[]
            {
                CreateProject(OwnerA),
                CreateProject(OwnerB)
            }
        };

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.GetForCurrentUserAsync(default));
    }

    [Fact]
    public async Task RejectsDuplicateProjects()
    {
        var project = CreateProject(OwnerA);

        var repository = new TestRepository
        {
            Projects = new[] { project, project }
        };

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await Assert.ThrowsAsync<ArgumentException>(
            () => source.GetForCurrentUserAsync(default));
    }

    [Fact]
    public async Task PropagatesRepositoryFailure()
    {
        var repository = new TestRepository
        {
            Failure = new InvalidOperationException()
        };

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.GetForCurrentUserAsync(default));
    }

    [Fact]
    public async Task PropagatesTimeout()
    {
        var repository = new TestRepository
        {
            Failure = new TimeoutException()
        };

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await Assert.ThrowsAsync<TimeoutException>(
            () => source.GetForCurrentUserAsync(default));
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var repository = new TestRepository();

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.GetForCurrentUserAsync(cts.Token));

        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task CancellationAfterRepositoryDispatchDiscardsReturnedProjects()
    {
        using var cancellation = new CancellationTokenSource();
        var repository = new TestRepository
        {
            Projects = new[] { CreateProject(OwnerA) },
            OnGet = cancellation.Cancel
        };
        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.GetForCurrentUserAsync(cancellation.Token));

        Assert.Equal(1, repository.Calls);
        Assert.Equal(OwnerA, repository.RequestedOwner);
        Assert.Equal(cancellation.Token, repository.ReceivedToken);
    }

    [Fact]
    public async Task ForwardsCancellationToken()
    {
        using var cts = new CancellationTokenSource();

        var repository = new TestRepository();

        var source = new DashboardProjectSource(
            new TestCurrentUser(), repository);

        await source.GetForCurrentUserAsync(cts.Token);

        Assert.Equal(cts.Token, repository.ReceivedToken);
    }
}
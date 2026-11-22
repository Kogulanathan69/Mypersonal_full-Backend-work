using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Projects.DTOs;
using BuildMind.Application.Features.Projects.Services;
using BuildMind.Domain.Entities;
using Xunit;

namespace BuildMind.Application.UnitTests.Projects;

public sealed class ProjectServiceTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly FakeRepository _repository = new();

    private ProjectService CreateService(bool authenticated = true)
        => new(
            _repository,
            new FakeCurrentUser(authenticated ? _owner : null),
            TimeProvider.System);

    private static CreateProjectRequest CreateRequest()
        => new()
        {
            Name = "Test House",
            ProjectType = "Residential"
        };

    private Project Seed(Guid? owner = null)
    {
        var project = new Project
        {
            OwnerId = owner ?? _owner,
            Name = "Test House",
            ProjectType = "Residential"
        };

        _repository.Projects.Add(project);

        return project;
    }

    [Fact]
    public async Task CreateProject_ShouldSucceed()
    {
        var result = await CreateService().CreateAsync(
            CreateRequest(), CancellationToken.None);

        Assert.Equal(ProjectOperationStatus.Success, result.Status);
        Assert.Equal(_owner, Assert.Single(_repository.Projects).OwnerId);
        Assert.Equal(1L, result.Value!.Revision);
    }

    [Fact]
    public async Task AnonymousCreation_ShouldFail()
    {
        var result = await CreateService(false).CreateAsync(
            CreateRequest(), CancellationToken.None);

        Assert.Equal(
            ProjectOperationStatus.Unauthenticated,
            result.Status);

        Assert.Empty(_repository.Projects);
    }

    [Fact]
    public async Task InvalidProjectName_ShouldFail()
    {
        var result = await CreateService().CreateAsync(
            new CreateProjectRequest
            {
                Name = "",
                ProjectType = "Residential"
            },
            CancellationToken.None);

        Assert.Equal(
            ProjectOperationStatus.ValidationFailed,
            result.Status);

        Assert.Empty(_repository.Projects);
    }

    [Fact]
    public async Task OwnerCanReadProject()
    {
        var project = Seed();

        var result = await CreateService().GetAsync(
            project.Id, CancellationToken.None);

        Assert.Equal(ProjectOperationStatus.Success, result.Status);
        Assert.Equal(project.Id, result.Value!.Id);
    }

    [Fact]
    public async Task OtherOwnerCannotReadProject()
    {
        var project = Seed(Guid.NewGuid());

        var result = await CreateService().GetAsync(
            project.Id, CancellationToken.None);

        Assert.Equal(ProjectOperationStatus.NotFound, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task UnknownProject_ShouldReturnNotFound()
    {
        var result = await CreateService().GetAsync(
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ProjectOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task List_ShouldReturnOwnedProjectsOnly()
    {
        var owned = Seed();

        Seed(Guid.NewGuid());

        var result = await CreateService().ListAsync(
            CancellationToken.None);

        Assert.Equal(ProjectOperationStatus.Success, result.Status);
        Assert.Equal(owned.Id, Assert.Single(result.Value!).Id);
    }

    [Fact]
    public async Task OwnerCanUpdateProject()
    {
        var project = Seed();

        var result = await CreateService().UpdateAsync(
            project.Id,
            new UpdateProjectRequest
            {
                Name = "Updated House",
                Revision = 1
            },
            CancellationToken.None);

        Assert.Equal(ProjectOperationStatus.Success, result.Status);
        Assert.Equal("Updated House", result.Value!.Name);
        Assert.Equal(2L, result.Value.Revision);
        Assert.Equal(project.Status, result.Value.Status);
    }

    [Fact]
    public async Task OtherOwnerCannotUpdateProject()
    {
        var project = Seed(Guid.NewGuid());

        var result = await CreateService().UpdateAsync(
            project.Id,
            new UpdateProjectRequest
            {
                Name = "Unauthorized Update",
                Revision = 1
            },
            CancellationToken.None);

        Assert.Equal(ProjectOperationStatus.NotFound, result.Status);
        Assert.Equal("Test House", project.Name);
    }

    [Fact]
    public async Task StaleRevision_ShouldFail()
    {
        var project = Seed();

        var result = await CreateService().UpdateAsync(
            project.Id,
            new UpdateProjectRequest
            {
                Name = "Stale Update",
                Revision = 2
            },
            CancellationToken.None);

        Assert.Equal(
            ProjectOperationStatus.RevisionConflict,
            result.Status);
    }

    [Fact]
    public async Task ConcurrentUpdateFailure_ShouldReturnConflict()
    {
        var project = Seed();

        _repository.RejectUpdate = true;

        var result = await CreateService().UpdateAsync(
            project.Id,
            new UpdateProjectRequest
            {
                Name = "Concurrent Update",
                Revision = 1
            },
            CancellationToken.None);

        Assert.Equal(
            ProjectOperationStatus.RevisionConflict,
            result.Status);
    }

    [Fact]
    public async Task AnonymousListing_ShouldFail()
    {
        var result = await CreateService(false).ListAsync(
            CancellationToken.None);

        Assert.Equal(
            ProjectOperationStatus.Unauthenticated,
            result.Status);
    }

    private sealed class FakeCurrentUser(Guid? id)
        : ICurrentUser
    {
        public bool IsAuthenticated => id.HasValue;

        public Guid? UserId => id;

        public IReadOnlySet<string> Roles { get; }
            = new HashSet<string>();

        public Guid GetRequiredUserId()
            => id ?? throw new InvalidOperationException();
    }

    private sealed class FakeRepository : IProjectRepository
    {
        public List<Project> Projects { get; } = new();

        public bool RejectUpdate { get; set; }

        public Task<Project?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken)
            => Task.FromResult(
                Projects.FirstOrDefault(p => p.Id == id));

        public Task<Project?> GetByIdAndOwnerAsync(
            Guid id,
            Guid ownerId,
            CancellationToken cancellationToken)
            => Task.FromResult(Projects.FirstOrDefault(
                p => p.Id == id && p.OwnerId == ownerId));

        public Task<IReadOnlyList<Project>> GetByOwnerIdAsync(
            Guid ownerId,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Project>>(
                Projects.Where(p => p.OwnerId == ownerId).ToArray());

        public Task AddAsync(
            Project project,
            CancellationToken cancellationToken)
        {
            Projects.Add(project);

            return Task.CompletedTask;
        }

        public Task<bool> UpdateAsync(
            Project project,
            Guid ownerId,
            long expectedRevision,
            CancellationToken cancellationToken)
        {
            if (RejectUpdate)
                return Task.FromResult(false);

            var index = Projects.FindIndex(p =>
                p.Id == project.Id &&
                p.OwnerId == ownerId &&
                p.Revision == expectedRevision);

            if (index < 0)
                return Task.FromResult(false);

            Projects[index] = project;

            return Task.FromResult(true);
        }
    }
}

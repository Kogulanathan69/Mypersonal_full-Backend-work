using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Projects.DTOs;
using BuildMind.Application.Features.Projects.Validators;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Projects.Services;

public enum ProjectOperationStatus
{
    Success,
    Unauthenticated,
    ValidationFailed,
    NotFound,
    RevisionConflict
}

public sealed record ProjectOperationResult<T>(
    ProjectOperationStatus Status,
    T? Value = null) where T : class;

public sealed class ProjectService(
    IProjectRepository repository,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    private Guid? OwnerId =>
        currentUser.IsAuthenticated &&
        currentUser.UserId is Guid id &&
        id != Guid.Empty ? id : null;

    public async Task<ProjectOperationResult<ProjectResponse>> CreateAsync(
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (OwnerId is not Guid owner)
            return new(ProjectOperationStatus.Unauthenticated);

        if (!new CreateProjectRequestValidator()
            .Validate(request).IsValid)
        {
            return new(ProjectOperationStatus.ValidationFailed);
        }

        var project = new Project
        {
            OwnerId = owner,
            Name = request.Name.Trim(),
            ProjectType = request.ProjectType.Trim(),
            LocationReference = request.LocationReference,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        await repository.AddAsync(
            project, cancellationToken);

        return new(
            ProjectOperationStatus.Success,
            Map(project));
    }

    public async Task<ProjectOperationResult<ProjectResponse>> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (OwnerId is not Guid owner)
            return new(ProjectOperationStatus.Unauthenticated);

        var project = await repository.GetByIdAndOwnerAsync(
            projectId, owner, cancellationToken);

        if (project is null ||
      project.Id != projectId ||
      project.OwnerId != owner)
            return new(ProjectOperationStatus.NotFound);

        return new(
            ProjectOperationStatus.Success,
            Map(project));
    }

    public async Task<ProjectOperationResult<IReadOnlyList<ProjectResponse>>>
        ListAsync(CancellationToken cancellationToken)
    {
        if (OwnerId is not Guid owner)
            return new(ProjectOperationStatus.Unauthenticated);

        var projects = await repository.GetByOwnerIdAsync(
            owner, cancellationToken);

        var response = projects
            .Where(p => p.OwnerId == owner)
            .Select(Map)
            .ToArray();

        return new(ProjectOperationStatus.Success, response);
    }

    public async Task<ProjectOperationResult<ProjectResponse>> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (OwnerId is not Guid owner)
            return new(ProjectOperationStatus.Unauthenticated);

        if (!new UpdateProjectRequestValidator()
            .Validate(request).IsValid)
        {
            return new(ProjectOperationStatus.ValidationFailed);
        }

        var project = await repository.GetByIdAndOwnerAsync(
            projectId, owner, cancellationToken);

        if (project is null || project.OwnerId != owner)
            return new(ProjectOperationStatus.NotFound);

        if (project.Revision != request.Revision ||
            project.Revision == long.MaxValue)
        {
            return new(ProjectOperationStatus.RevisionConflict);
        }

        var updated = project.WithUpdatedDetails(
            request.Name,
            request.ProjectType,
            request.LocationReference,
            timeProvider.GetUtcNow());

        var saved = await repository.UpdateAsync(
            updated,
            owner,
            request.Revision,
            cancellationToken);

        if (!saved)
            return new(ProjectOperationStatus.RevisionConflict);

        return new(
            ProjectOperationStatus.Success,
            Map(updated));
    }

    private static ProjectResponse Map(Project project)
    {
        return new ProjectResponse
        {
            Id = project.Id,
            Name = project.Name,
            ProjectType = project.ProjectType,
            LocationReference = project.LocationReference,
            Status = project.Status,
            Revision = project.Revision,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc
        };
    }
}
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Confirmation;

namespace BuildMind.Application.Features.PlanEditor.Integration;

/// <summary>
/// Adapts M1's owner-scoped project repository contract to the M3 authorization boundary.
/// Authentication remains the responsibility of the calling API/application boundary.
/// </summary>
public sealed class ProjectOwnershipGate(IProjectRepository projects) : IProjectOwnershipGate
{
    private readonly IProjectRepository projects =
        projects ?? throw new ArgumentNullException(nameof(projects));

    public async Task<bool> OwnsProjectAsync(
        Guid projectId,
        Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || authenticatedUserId == Guid.Empty)
            return false;

        var project = await projects.GetByIdAndOwnerAsync(
            projectId, authenticatedUserId, cancellationToken);

        return project is not null &&
               project.Id == projectId &&
               project.OwnerId == authenticatedUserId;
    }
}

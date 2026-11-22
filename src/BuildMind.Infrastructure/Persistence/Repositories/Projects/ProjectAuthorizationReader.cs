using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Projects;

// Isolated read projection. M1 must confirm its revocation/write guarantees before composition.
internal sealed class ProjectAuthorizationReader(BuildMindDbContext context) : IProjectAuthorizationPort
{
    public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
        Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken) => PlanHistoryDatabaseRead.RunAsync(
            () => AuthorizeCoreAsync(projectId, authenticatedActorId, cancellationToken), cancellationToken);

    private async Task<ProjectAuthorizationResult> AuthorizeCoreAsync(
        Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty || authenticatedActorId == Guid.Empty)
            return new(ProjectAuthorizationStatus.Denied);

        var version = await context.Set<ProjectRecord>().AsNoTracking()
            .Where(project => project.Id == projectId && project.OwnerId == authenticatedActorId)
            .Select(project => (long?)project.AuthorizationVersion)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (version is null)
            return new(ProjectAuthorizationStatus.Denied);
        if (!ProjectAuthorizationPolicy.IsValid(version.Value))
            throw new InvalidDataException("The project authorization version is invalid.");

        return new(ProjectAuthorizationStatus.Authorized,
            new ProjectAuthorizationSnapshot(projectId, authenticatedActorId, new AuthorizationVersion(version.Value)));
    }
}

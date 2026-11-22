using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Projects.Abstractions;
using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Projects;

/// <summary>Atomic authorization-version increments (STEP 05 decision 4).</summary>
internal sealed class ProjectAuthorizationVersionStore(BuildMindDbContext context) : IProjectAuthorizationVersionStore
{
    public async Task<long?> TryIncrementAsync(Guid projectId, long expectedVersion, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || expectedVersion < 1 || expectedVersion == long.MaxValue)
            return null;

        var updated = await context.Set<ProjectRecord>()
            .Where(p => p.Id == projectId && p.AuthorizationVersion == expectedVersion)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.AuthorizationVersion, p => p.AuthorizationVersion + 1), cancellationToken);

        return updated == 1 ? expectedVersion + 1 : null;
    }
}

/// <summary>
/// Option A entry point for the shared fresh, owner-scoped authorization read.
/// </summary>
/// <remarks>Not registered: the port stays inactive until M3, M1 and M4 approve its composition (decision 4).</remarks>
internal sealed class ProjectOwnerAuthorizationPort(BuildMindDbContext context) : IProjectAuthorizationPort
{
    private readonly ProjectAuthorizationReader reader = new(context);

    public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
        Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken) =>
        reader.AuthorizeOwnerAsync(projectId, authenticatedActorId, cancellationToken);
}

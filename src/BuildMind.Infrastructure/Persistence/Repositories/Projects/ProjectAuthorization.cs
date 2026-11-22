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
/// M1/M4 implementation of M3's <see cref="IProjectAuthorizationPort"/>: the owner of the project is authorized and
/// receives a snapshot bound to the current authorization version. Everyone else (and unknown projects) is denied
/// without disclosure. Database failures and cancellation propagate unchanged, as the port contract requires.
/// </summary>
/// <remarks>Not registered: the port stays inactive until M3, M1 and M4 approve its composition (decision 4).</remarks>
internal sealed class ProjectOwnerAuthorizationPort(BuildMindDbContext context) : IProjectAuthorizationPort
{
    public async Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
        Guid projectId, Guid authenticatedActorId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || authenticatedActorId == Guid.Empty)
            return new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied);

        var row = await context.Set<ProjectRecord>()
            .AsNoTracking()
            .Where(p => p.Id == projectId && p.OwnerId == authenticatedActorId)
            .Select(p => new { p.AuthorizationVersion })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null || row.AuthorizationVersion < 1
            ? new ProjectAuthorizationResult(ProjectAuthorizationStatus.Denied)
            : new ProjectAuthorizationResult(
                ProjectAuthorizationStatus.Authorized,
                new ProjectAuthorizationSnapshot(projectId, authenticatedActorId, new AuthorizationVersion(row.AuthorizationVersion)));
    }
}

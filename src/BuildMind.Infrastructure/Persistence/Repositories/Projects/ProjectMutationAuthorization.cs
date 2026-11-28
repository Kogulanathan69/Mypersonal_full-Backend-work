using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Projects;

/// <summary>
/// Isolated M4 building block. Acquires row locks in fixed global hierarchy order (<c>users</c> -> <c>projects</c>)
/// before acquiring feature-head locks in the same transaction.
/// </summary>
internal sealed class ProjectMutationAuthorization(BuildMindDbContext context)
{
    public async Task<bool> LockAndCheckAsync(Guid projectId, Guid trustedActorId, long expectedVersion,
        CancellationToken cancellationToken, long? expectedTokenVersion = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty || trustedActorId == Guid.Empty || !ProjectAuthorizationPolicy.IsValid(expectedVersion))
            return false;

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Project mutation authorization requires an active database transaction.");

        // Global lock order: users row before projects row, preventing deadlocks with session/account mutations.
        if (expectedTokenVersion is { } tokenVersion)
        {
            if (!await new M1MutationSessionGuard(context)
                .LockAndCheckAsync(trustedActorId, tokenVersion, cancellationToken).ConfigureAwait(false))
                return false;
        }
        else
        {
            var account = await SessionSql.LockAccountAsync(context, trustedActorId, cancellationToken).ConfigureAwait(false);
            if (account is not { IsActive: true })
                return false;
        }

        // Execute a locking read on the project primary key. Do not compose LINQ over the SQL:
        // the FOR UPDATE clause must apply to the project row in the current transaction.
        var rows = await context.Database.SqlQuery<ProjectMutationRow>($"""
            SELECT authorization_version AS AuthorizationVersion
            FROM projects WHERE id = {projectId} AND owner_id = {trustedActorId} FOR UPDATE
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        var row = rows.SingleOrDefault();
        if (row is null)
            return false;
        if (!ProjectAuthorizationPolicy.IsValid(row.AuthorizationVersion))
            throw new InvalidDataException("The project authorization version is invalid.");

        return row.AuthorizationVersion == expectedVersion;
    }

    private sealed class ProjectMutationRow
    {
        public long AuthorizationVersion { get; set; }
    }
}

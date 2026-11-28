using BuildMind.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Identity;

internal sealed class M1MutationSessionGuard(BuildMindDbContext context)
{
    public async Task<bool> LockAndCheckAsync(Guid trustedActorId, long expectedTokenVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (trustedActorId == Guid.Empty || expectedTokenVersion < 1) return false;
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Account lock requires an active transaction.");

        var rows = await context.Database.SqlQuery<AccountSecurityRow>($"""
            SELECT CASE WHEN is_active THEN 1 ELSE 0 END AS ActiveFlag, token_version AS TokenVersion
            FROM users WHERE id = {trustedActorId} FOR UPDATE
            """).ToListAsync(cancellationToken).ConfigureAwait(false);
        var account = rows.SingleOrDefault();
        return account is not null && account.ActiveFlag == 1 && account.TokenVersion == expectedTokenVersion;
    }

    private sealed class AccountSecurityRow
    {
        public int ActiveFlag { get; set; }
        public long TokenVersion { get; set; }
    }
}

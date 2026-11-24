using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Identity;

/// <summary>
/// Account session state (STEP 05 decisions 1 and 2). Reads are a primary-key lookup; every write runs in one
/// transaction and increments <c>users.token_version</c> with a single atomic SQL expression.
/// </summary>
internal sealed class UserSessionStore(BuildMindDbContext context, TransactionRunner transactions) : IUserSessionStore
{
    public Task<UserSecurityState?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        context.Set<UserRecord>()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserSecurityState(u.IsActive, u.TokenVersion))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> RevokeAllSessionsAsync(
        Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async token =>
        {
            var users = await context.Set<UserRecord>()
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), token);
            if (users == 0)
                return false;

            await SessionSql.RevokeUserRefreshTokensAsync(context, userId, nowUtc, reason, token);
            return true;
        }, cancellationToken);

    public Task<bool> DisableAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async token =>
        {
            var users = await context.Set<UserRecord>()
                .Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.IsActive, false)
                    .SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), token);
            if (users == 0)
                return false;

            await SessionSql.RevokeUserRefreshTokensAsync(context, userId, nowUtc, "account_disabled", token);
            return true;
        }, cancellationToken);
}

/// <summary>Refresh-token rotation with replay detection (STEP 05 decision 3).</summary>
internal sealed class RefreshTokenStore(BuildMindDbContext context, TransactionRunner transactions) : IRefreshTokenStore
{
    public async Task CreateAsync(NewRefreshToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        context.Set<RefreshTokenRecord>().Add(new RefreshTokenRecord
        {
            Id = token.Id,
            UserId = token.UserId,
            FamilyId = token.FamilyId,
            TokenHash = token.TokenHash,
            CreatedAtUtc = token.CreatedAtUtc,
            ExpiresAtUtc = token.ExpiresAtUtc,
            AbsoluteExpiresAtUtc = token.AbsoluteExpiresAtUtc,
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<RefreshRotationResult> RotateAsync(
        string presentedTokenHash,
        Guid replacementId,
        string replacementTokenHash,
        DateTimeOffset nowUtc,
        TimeSpan slidingLifetime,
        CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async token =>
        {
            // LOCK ORDER (all session writers): users row FIRST, then refresh-token rows. logout-all, disable and
            // password reset already start with an UPDATE of the users row; rotation and logout now lock it explicitly.
            // History: the original rotation took token/family locks before the users row (FK check on insert, replay
            // token_version bump) and intermittently deadlocked (SessionSecurityLiveTests.S03, Captain run 2026-11-24);
            // a token-row-first fix removed same-token races but still ran opposite to logout-all.
            var owner = await context.Set<RefreshTokenRecord>().AsNoTracking()
                .Where(t => t.TokenHash == presentedTokenHash).Select(t => (Guid?)t.UserId).SingleOrDefaultAsync(token);
            if (owner is not Guid ownerId)
                return new RefreshRotationResult(RefreshRotationStatus.Invalid);
            await SessionSql.LockUserAsync(context, ownerId, token);

            // Then serialize presenters of this exact token on its row. The claim below is a current read, so a loser
            // whose snapshot predates the winner's commit still finds nothing to claim and takes the replay path.
            await context.Database
                .SqlQuery<int>($"SELECT 1 AS Value FROM refresh_tokens WHERE token_hash = {presentedTokenHash} FOR UPDATE")
                .ToListAsync(token);

            var current = await context.Set<RefreshTokenRecord>()
                .AsNoTracking()
                .Where(t => t.TokenHash == presentedTokenHash)
                .Select(t => new { t.Id, t.UserId, t.FamilyId, t.ExpiresAtUtc, t.AbsoluteExpiresAtUtc, t.ConsumedAtUtc, t.RevokedAtUtc })
                .SingleOrDefaultAsync(token);

            if (current is null || current.RevokedAtUtc is not null)
                return new RefreshRotationResult(RefreshRotationStatus.Invalid);

            if (current.ConsumedAtUtc is not null)
                return await ReplayAsync(current.UserId, current.FamilyId, nowUtc, token);

            if (current.ExpiresAtUtc <= nowUtc || current.AbsoluteExpiresAtUtc <= nowUtc)
                return new RefreshRotationResult(RefreshRotationStatus.Expired);

            // Atomic claim: only one caller can move consumed_at from NULL. A concurrent loser waits on the row
            // lock, re-evaluates the predicate after the winner commits, matches nothing and is treated as a replay.
            var claimed = await context.Set<RefreshTokenRecord>()
                .Where(t => t.Id == current.Id && t.ConsumedAtUtc == null && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.ConsumedAtUtc, nowUtc)
                    .SetProperty(t => t.ReplacedByTokenId, replacementId), token);
            if (claimed == 0)
                return await ReplayAsync(current.UserId, current.FamilyId, nowUtc, token);

            var expiresAt = nowUtc.Add(slidingLifetime);
            if (expiresAt > current.AbsoluteExpiresAtUtc)
                expiresAt = current.AbsoluteExpiresAtUtc;

            context.Set<RefreshTokenRecord>().Add(new RefreshTokenRecord
            {
                Id = replacementId,
                UserId = current.UserId,
                FamilyId = current.FamilyId,
                TokenHash = replacementTokenHash,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = expiresAt,
                AbsoluteExpiresAtUtc = current.AbsoluteExpiresAtUtc,
            });
            await context.SaveChangesAsync(token);

            return new RefreshRotationResult(RefreshRotationStatus.Rotated, current.UserId, expiresAt);
        }, cancellationToken);

    public Task<bool> RevokeFamilyAsync(
        string presentedTokenHash, Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async token =>
        {
            await SessionSql.LockUserAsync(context, userId, token); // lock order: users row first (see RotateAsync)
            var family = await context.Set<RefreshTokenRecord>()
                .AsNoTracking()
                .Where(t => t.TokenHash == presentedTokenHash && t.UserId == userId)
                .Select(t => (Guid?)t.FamilyId)
                .SingleOrDefaultAsync(token);
            if (family is not Guid familyId)
                return false;

            await SessionSql.RevokeFamilyAsync(context, familyId, nowUtc, reason, token);
            return true;
        }, cancellationToken);

    private async Task<RefreshRotationResult> ReplayAsync(Guid userId, Guid familyId, DateTimeOffset nowUtc, CancellationToken token)
    {
        // A consumed token was presented again: assume theft. Revoke the family and every issued access token.
        await SessionSql.RevokeFamilyAsync(context, familyId, nowUtc, "replay_detected", token);
        await context.Set<UserRecord>()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), token);
        return new RefreshRotationResult(RefreshRotationStatus.ReplayDetected, userId);
    }
}

/// <summary>Password-reset token persistence (STEP 05 decision 7). Endpoints stay unregistered until email exists.</summary>
internal sealed class PasswordResetRepository(BuildMindDbContext context, TransactionRunner transactions) : IPasswordResetRepository
{
    public Task<bool> ReplaceTokenAsync(PasswordResetTokenRecord token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return transactions.ExecuteAsync(async ct =>
        {
            await context.Set<PasswordResetRecord>().Where(r => r.UserId == token.UserId).ExecuteDeleteAsync(ct);
            context.Set<PasswordResetRecord>().Add(new PasswordResetRecord
            {
                UserId = token.UserId,
                TokenHash = token.TokenHash,
                CreatedAtUtc = token.CreatedAtUtc,
                ExpiresAtUtc = token.ExpiresAtUtc,
            });
            await context.SaveChangesAsync(ct);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> ResetPasswordAtomicallyAsync(
        Guid userId, string tokenHash, string newPasswordHash, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        try
        {
            return await transactions.ExecuteAsync(async ct =>
            {
                // Single-use: exactly one request can consume an unexpired, unconsumed token.
                var consumed = await context.Set<PasswordResetRecord>()
                    .Where(r => r.UserId == userId && r.TokenHash == tokenHash
                        && r.ConsumedAtUtc == null && r.ExpiresAtUtc > nowUtc)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.ConsumedAtUtc, nowUtc), ct);
                if (consumed == 0)
                    return false;

                var updated = await context.Set<UserRecord>()
                    .Where(u => u.Id == userId && u.IsActive)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.PasswordHash, newPasswordHash)
                        .SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), ct);
                if (updated == 0)
                    throw new ResetAbortedException(); // roll back the consumption: nothing changes

                await SessionSql.RevokeUserRefreshTokensAsync(context, userId, nowUtc, "password_reset", ct);
                return true;
            }, cancellationToken);
        }
        catch (ResetAbortedException)
        {
            return false;
        }
    }

    private sealed class ResetAbortedException : Exception;
}

internal static class SessionSql
{
    /// <summary>Takes the users row lock that orders every session-mutating transaction (users before tokens).</summary>
    public static Task<List<int>> LockUserAsync(BuildMindDbContext context, Guid userId, CancellationToken token) =>
        context.Database.SqlQuery<int>($"SELECT 1 AS Value FROM users WHERE id = {userId} FOR UPDATE").ToListAsync(token);

    public static Task<int> RevokeUserRefreshTokensAsync(
        BuildMindDbContext context, Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken token) =>
        context.Set<RefreshTokenRecord>()
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtUtc, nowUtc)
                .SetProperty(t => t.RevocationReason, reason), token);

    public static Task<int> RevokeFamilyAsync(
        BuildMindDbContext context, Guid familyId, DateTimeOffset nowUtc, string reason, CancellationToken token) =>
        context.Set<RefreshTokenRecord>()
            .Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAtUtc, nowUtc)
                .SetProperty(t => t.RevocationReason, reason), token);
}

using System.Text.Json;
using BuildMind.Application.Features.Administration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.Administration;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.CaptainIntegration;

internal static class SecurityEvents
{
    public static void Add(BuildMindDbContext context, Guid userId, string kind, object? details, DateTimeOffset now) =>
        context.Add(new SecurityEventRecord
        {
            Id = Guid.NewGuid(), UserId = userId, Kind = kind, OccurredAtUtc = now, DetailsJson = JsonSerializer.Serialize(details ?? new { }),
        });
}

internal sealed class MySqlAccountSecurityStore(BuildMindDbContext context, TransactionRunner transactions) : IAccountSecurityStore
{
    public async Task<AccountSecurityStatus> GetStatusAsync(Guid userId, CancellationToken token)
    {
        var row = await context.Set<AccountSecurityRecord>().AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId, token).ConfigureAwait(false);
        return new AccountSecurityStatus(row?.EmailVerifiedAtUtc is not null, row?.EmailVerifiedAtUtc, row?.VerificationCodeHash is not null);
    }

    private async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken token)
    {
        try
        {
            return await transactions.ExecuteAsync(work, token).ConfigureAwait(false);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    public Task IssueVerificationAsync(Guid userId, string codeHash, DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc, CancellationToken token) =>
        InTransactionAsync(async inner =>
        {
            await SessionSql.LockUserAsync(context, userId, inner).ConfigureAwait(false);
            var row = await context.Set<AccountSecurityRecord>().SingleOrDefaultAsync(a => a.UserId == userId, inner).ConfigureAwait(false);
            if (row is null) context.Add(row = new AccountSecurityRecord { UserId = userId });
            row.VerificationCodeHash = codeHash;
            row.VerificationExpiresAtUtc = expiresAtUtc;
            SecurityEvents.Add(context, userId, "EMAIL_VERIFICATION_REQUESTED", null, nowUtc);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return true;
        }, token);

    public Task<bool> ConfirmVerificationAsync(Guid userId, string codeHash, DateTimeOffset nowUtc, CancellationToken token) =>
        InTransactionAsync(async inner =>
        {
            var consumed = await context.Set<AccountSecurityRecord>()
                .Where(a => a.UserId == userId && a.VerificationCodeHash == codeHash && a.VerificationExpiresAtUtc > nowUtc)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.EmailVerifiedAtUtc, nowUtc)
                    .SetProperty(a => a.VerificationCodeHash, (string?)null)
                    .SetProperty(a => a.VerificationExpiresAtUtc, (DateTimeOffset?)null), inner).ConfigureAwait(false);
            if (consumed != 1) return false;
            SecurityEvents.Add(context, userId, "EMAIL_VERIFIED", null, nowUtc);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return true;
        }, token);

    public Task<bool> ChangePasswordAsync(Guid userId, string currentHash, string newHash, DateTimeOffset nowUtc, CancellationToken token) =>
        InTransactionAsync(async inner =>
        {
            await SessionSql.LockUserAsync(context, userId, inner).ConfigureAwait(false);
            var updated = await context.Set<UserRecord>().Where(u => u.Id == userId && u.IsActive && u.PasswordHash == currentHash)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, newHash).SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1), inner)
                .ConfigureAwait(false);
            if (updated != 1) return false; // a concurrent change or reset won; the caller retries with the current password
            await SessionSql.RevokeUserRefreshTokensAsync(context, userId, nowUtc, "password_changed", inner).ConfigureAwait(false);
            SecurityEvents.Add(context, userId, "PASSWORD_CHANGED", null, nowUtc);
            await context.SaveChangesAsync(inner).ConfigureAwait(false);
            return true;
        }, token);

    public async Task<PagedResult<SecurityEventView>> ListEventsAsync(Guid userId, PageRequest page, CancellationToken token)
    {
        var query = context.Set<SecurityEventRecord>().AsNoTracking().Where(e => e.UserId == userId);
        var total = await query.LongCountAsync(token).ConfigureAwait(false);
        var rows = await query.OrderByDescending(e => e.OccurredAtUtc).Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .Select(e => new { e.Id, e.Kind, e.OccurredAtUtc, e.DetailsJson }).ToListAsync(token).ConfigureAwait(false);
        return new PagedResult<SecurityEventView>(rows.Select(e => new SecurityEventView(e.Id, e.Kind, e.OccurredAtUtc, e.DetailsJson)).ToArray(),
            page.Page, page.PageSize, total);
    }
}

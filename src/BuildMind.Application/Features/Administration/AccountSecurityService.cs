using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;

namespace BuildMind.Application.Features.Administration;

public sealed record SecurityEventView(Guid EventId, string Kind, DateTimeOffset OccurredAtUtc, string DetailsJson);

public sealed record AccountSecurityStatus(bool EmailVerified, DateTimeOffset? EmailVerifiedAtUtc, bool VerificationPending);

public interface IAccountSecurityStore
{
    Task<AccountSecurityStatus> GetStatusAsync(Guid userId, CancellationToken token);

    /// <summary>Stores only the SHA-256 of a single outstanding verification code (replacing any previous one).</summary>
    Task IssueVerificationAsync(Guid userId, string codeHash, DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc, CancellationToken token);

    /// <summary>Single-use, unexpired consumption; records EMAIL_VERIFIED.</summary>
    Task<bool> ConfirmVerificationAsync(Guid userId, string codeHash, DateTimeOffset nowUtc, CancellationToken token);

    /// <summary>CAS on the current hash; bumps the token version and revokes refresh tokens; records PASSWORD_CHANGED.</summary>
    Task<bool> ChangePasswordAsync(Guid userId, string currentHash, string newHash, DateTimeOffset nowUtc, CancellationToken token);

    Task<PagedResult<SecurityEventView>> ListEventsAsync(Guid userId, PageRequest page, CancellationToken token);
}

public enum AccountSecurityOutcome { Done, Unauthenticated, InvalidRequest, CurrentPasswordIncorrect, Conflict, InvalidCode, EmailUnavailable, AlreadyVerified }

/// <summary>
/// Password change, email verification and the caller's own security-event history. Codes are 64-hex, hashed at rest,
/// single-use and expiring; they are never returned by the API or logged. Verification is recorded but not (yet)
/// enforced for login: enforcement is a product policy decision (deny-by-default switch documented, not assumed).
/// </summary>
public sealed class AccountSecurityService(
    ICurrentUser currentUser, IUserRepository users, IPasswordHashService passwords, IPasswordResetTokenProvider codes,
    IAccountSecurityStore store, ISystemEmailSender email, TimeProvider time)
{
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromHours(24);

    public async Task<AccountSecurityOutcome> ChangePasswordAsync(string? current, string? next, string? confirm, CancellationToken token)
    {
        if (currentUser.UserId is not { } id) return AccountSecurityOutcome.Unauthenticated;
        if (string.IsNullOrEmpty(current) || next is null || next.Length is < 15 or > 128 || !string.Equals(next, confirm, StringComparison.Ordinal)
            || string.Equals(current, next, StringComparison.Ordinal))
            return AccountSecurityOutcome.InvalidRequest;
        var user = await users.GetByIdAsync(id, token).ConfigureAwait(false);
        if (user is null || !user.IsActive) return AccountSecurityOutcome.Unauthenticated;
        if (!passwords.VerifyPassword(current, user.PasswordHash)) return AccountSecurityOutcome.CurrentPasswordIncorrect;
        return await store.ChangePasswordAsync(id, user.PasswordHash, passwords.HashPassword(next), time.GetUtcNow(), token).ConfigureAwait(false)
            ? AccountSecurityOutcome.Done : AccountSecurityOutcome.Conflict;
    }

    public async Task<AccountSecurityOutcome> RequestVerificationAsync(CancellationToken token)
    {
        if (currentUser.UserId is not { } id) return AccountSecurityOutcome.Unauthenticated;
        if ((await store.GetStatusAsync(id, token).ConfigureAwait(false)).EmailVerified) return AccountSecurityOutcome.AlreadyVerified;
        if (!email.IsConfigured) return AccountSecurityOutcome.EmailUnavailable;
        var user = await users.GetByIdAsync(id, token).ConfigureAwait(false);
        if (user is null || !user.IsActive) return AccountSecurityOutcome.Unauthenticated;
        var now = time.GetUtcNow();
        var code = codes.Generate();
        await store.IssueVerificationAsync(id, codes.Hash(code), now + VerificationLifetime, now, token).ConfigureAwait(false);
        var sent = await email.SendAsync(user.Email, "BuildMind email verification",
            $"Confirm your BuildMind email address with this code:\n{code}\n\nIt expires at {now + VerificationLifetime:u} and works once.\n",
            token).ConfigureAwait(false);
        return sent ? AccountSecurityOutcome.Done : AccountSecurityOutcome.EmailUnavailable;
    }

    public async Task<AccountSecurityOutcome> ConfirmVerificationAsync(string? code, CancellationToken token)
    {
        if (currentUser.UserId is not { } id) return AccountSecurityOutcome.Unauthenticated;
        if (code is null || code.Length != 64 || !code.All(Uri.IsHexDigit)) return AccountSecurityOutcome.InvalidCode;
        return await store.ConfirmVerificationAsync(id, codes.Hash(code), time.GetUtcNow(), token).ConfigureAwait(false)
            ? AccountSecurityOutcome.Done : AccountSecurityOutcome.InvalidCode;
    }
}

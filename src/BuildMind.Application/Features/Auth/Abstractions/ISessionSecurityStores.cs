namespace BuildMind.Application.Features.Auth.Abstractions;

/// <summary>Current session-relevant state of an account (STEP 05 decisions 1 and 2).</summary>
public sealed record UserSecurityState(bool IsActive, long TokenVersion);

/// <summary>Per-request lookup of account state by primary key. Must not swallow dependency failures.</summary>
public interface IUserSecurityStateReader
{
    Task<UserSecurityState?> GetAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Account-level session revocation. Every operation is atomic in one database transaction.</summary>
public interface IUserSessionStore : IUserSecurityStateReader
{
    /// <summary>
    /// Increments the token version (invalidating every issued access token) and revokes every active refresh
    /// token. Returns false when the user does not exist.
    /// </summary>
    Task<bool> RevokeAllSessionsAsync(Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken cancellationToken);

    /// <summary>Disables the account and revokes all of its sessions (for future administrator operations).</summary>
    Task<bool> DisableAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public sealed record NewRefreshToken(
    Guid Id,
    Guid UserId,
    Guid FamilyId,
    string TokenHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc);

public enum RefreshRotationStatus
{
    Rotated,
    Invalid,
    Expired,
    ReplayDetected
}

public sealed record RefreshRotationResult(RefreshRotationStatus Status, Guid? UserId = null, DateTimeOffset? ExpiresAtUtc = null);

/// <summary>Refresh-token persistence (STEP 05 decision 3). Only SHA-256 hashes of tokens are ever stored.</summary>
public interface IRefreshTokenStore
{
    Task CreateAsync(NewRefreshToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically consumes the presented token and stores its replacement in the same family. A token that was
    /// already consumed (including a concurrent second use) is a replay: the whole family is revoked and the
    /// user's token version is incremented in the same transaction.
    /// </summary>
    Task<RefreshRotationResult> RotateAsync(
        string presentedTokenHash,
        Guid replacementId,
        string replacementTokenHash,
        DateTimeOffset nowUtc,
        TimeSpan slidingLifetime,
        CancellationToken cancellationToken);

    /// <summary>Revokes the family of the presented token only when it belongs to <paramref name="userId"/>.</summary>
    Task<bool> RevokeFamilyAsync(
        string presentedTokenHash,
        Guid userId,
        DateTimeOffset nowUtc,
        string reason,
        CancellationToken cancellationToken);
}

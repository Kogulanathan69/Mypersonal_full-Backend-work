using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;

namespace BuildMind.Application.Features.Auth.Services;

/// <summary>Session lifetimes. Access tokens stay at 15 minutes (decision 3); refresh tokens rotate on every use.</summary>
public sealed class RefreshTokenOptions
{
    public const string SectionName = "Authentication:RefreshTokens";

    /// <summary>Refresh tokens are issued and accepted only when explicitly enabled (default off).</summary>
    public bool Enabled { get; init; }

    /// <summary>Lifetime of each individual refresh token (renewed by rotation).</summary>
    public TimeSpan SlidingLifetime { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Maximum lifetime of a login session regardless of rotation.</summary>
    public TimeSpan AbsoluteLifetime { get; init; } = TimeSpan.FromDays(30);

    public void Validate()
    {
        if (SlidingLifetime <= TimeSpan.Zero || AbsoluteLifetime < SlidingLifetime || AbsoluteLifetime > TimeSpan.FromDays(90))
            throw new InvalidOperationException("Refresh token lifetimes are invalid.");
    }
}

/// <summary>
/// Login, refresh-token rotation and logout (STEP 05 decisions 2 and 3). Failures are uniform (null/false) so
/// callers cannot distinguish unknown, expired, revoked and replayed tokens.
/// </summary>
public sealed class SessionService(
    AuthenticationService authentication,
    IUserRepository users,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenStore refreshTokens,
    IUserSessionStore sessions,
    RefreshTokenOptions options,
    TimeProvider timeProvider)
{
    public const string ReasonLogout = "logout";
    public const string ReasonLogoutAll = "logout_all";
    public const string ReasonInactive = "account_inactive";

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authentication.LoginAsync(request, cancellationToken);
        if (response is null || !options.Enabled)
            return response;

        var now = timeProvider.GetUtcNow();
        var token = RefreshTokenCodec.Generate();
        var expiresAt = now.Add(options.SlidingLifetime);
        await refreshTokens.CreateAsync(
            new NewRefreshToken(Guid.NewGuid(), response.User.Id, Guid.NewGuid(), RefreshTokenCodec.Hash(token),
                now, expiresAt, now.Add(options.AbsoluteLifetime)),
            cancellationToken);

        return WithRefresh(response, token, expiresAt);
    }

    public async Task<AuthResponse?> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (!options.Enabled || !RefreshTokenCodec.IsWellFormed(refreshToken))
            return null;

        var now = timeProvider.GetUtcNow();
        var replacement = RefreshTokenCodec.Generate();
        var rotation = await refreshTokens.RotateAsync(
            RefreshTokenCodec.Hash(refreshToken!), Guid.NewGuid(), RefreshTokenCodec.Hash(replacement),
            now, options.SlidingLifetime, cancellationToken);

        if (rotation.Status != RefreshRotationStatus.Rotated || rotation.UserId is not Guid userId)
            return null;

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            await sessions.RevokeAllSessionsAsync(userId, now, ReasonInactive, cancellationToken);
            return null;
        }

        var access = accessTokens.Issue(user);
        return WithRefresh(new AuthResponse
        {
            AccessToken = access.AccessToken,
            TokenType = "Bearer",
            ExpiresAtUtc = access.ExpiresAtUtc,
            User = new UserResponse { Id = user.Id, Email = user.Email, IsActive = user.IsActive, CreatedAtUtc = user.CreatedAtUtc }
        }, replacement, rotation.ExpiresAtUtc ?? now.Add(options.SlidingLifetime));
    }

    /// <summary>Ends one login session (its refresh-token family). Already issued access tokens expire naturally.</summary>
    public async Task<bool> LogoutAsync(Guid userId, string? refreshToken, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || !RefreshTokenCodec.IsWellFormed(refreshToken))
            return false;

        return await refreshTokens.RevokeFamilyAsync(
            RefreshTokenCodec.Hash(refreshToken!), userId, timeProvider.GetUtcNow(), ReasonLogout, cancellationToken);
    }

    /// <summary>Ends every session immediately: all access tokens (token version) and all refresh tokens.</summary>
    public Task<bool> LogoutAllAsync(Guid userId, CancellationToken cancellationToken) =>
        userId == Guid.Empty
            ? Task.FromResult(false)
            : sessions.RevokeAllSessionsAsync(userId, timeProvider.GetUtcNow(), ReasonLogoutAll, cancellationToken);

    private static AuthResponse WithRefresh(AuthResponse response, string token, DateTimeOffset expiresAt) => new()
    {
        AccessToken = response.AccessToken,
        TokenType = response.TokenType,
        ExpiresAtUtc = response.ExpiresAtUtc,
        User = response.User,
        RefreshToken = token,
        RefreshTokenExpiresAtUtc = expiresAt,
    };
}

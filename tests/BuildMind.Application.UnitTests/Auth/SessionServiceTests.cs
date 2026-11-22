using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Services;
using BuildMind.Domain.Entities;
using Xunit;

namespace BuildMind.Application.UnitTests.Auth;

/// <summary>STEP 05 decisions 2-3: refresh-token codec and session orchestration (persistence is faked).</summary>
public sealed class SessionServiceTests
{
    private const string Password = "SyntheticPassword123!";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Codec_generates_256_bit_url_safe_unique_tokens_and_hashes_deterministically()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => RefreshTokenCodec.Generate()).ToArray();

        Assert.All(tokens, t => Assert.True(RefreshTokenCodec.IsWellFormed(t)));
        Assert.All(tokens, t => Assert.Equal(43, t.Length));
        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(RefreshTokenCodec.Hash(tokens[0]), RefreshTokenCodec.Hash(tokens[0]));
        Assert.Equal(64, RefreshTokenCodec.Hash(tokens[0]).Length);
        Assert.NotEqual(tokens[0], RefreshTokenCodec.Hash(tokens[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]   // '+' is not base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]  // 44 characters
    public void Codec_rejects_malformed_tokens(string? token)
    {
        Assert.False(RefreshTokenCodec.IsWellFormed(token));
        if (token is not null)
            Assert.Throws<ArgumentException>(() => RefreshTokenCodec.Hash(token));
    }

    [Fact]
    public async Task Login_without_refresh_tokens_enabled_issues_access_token_only()
    {
        var fx = new Fixture(enabled: false);

        var response = await fx.Service.LoginAsync(new LoginRequest { Email = fx.User.Email, Password = Password }, default);

        Assert.NotNull(response);
        Assert.Null(response!.RefreshToken);
        Assert.Empty(fx.Refresh.Created);
        Assert.Null(await fx.Service.RefreshAsync(RefreshTokenCodec.Generate(), default));
        Assert.Equal(0, fx.Refresh.Rotations);
    }

    [Fact]
    public async Task Login_with_refresh_tokens_stores_only_the_hash_with_bounded_lifetimes()
    {
        var fx = new Fixture(enabled: true);

        var response = await fx.Service.LoginAsync(new LoginRequest { Email = fx.User.Email, Password = Password }, default);

        var stored = Assert.Single(fx.Refresh.Created);
        Assert.Equal(RefreshTokenCodec.Hash(response!.RefreshToken!), stored.TokenHash);
        Assert.Equal(fx.User.Id, stored.UserId);
        Assert.Equal(Now.AddDays(7), stored.ExpiresAtUtc);
        Assert.Equal(Now.AddDays(30), stored.AbsoluteExpiresAtUtc);
        Assert.Equal(stored.ExpiresAtUtc, response.RefreshTokenExpiresAtUtc);
    }

    [Fact]
    public async Task Failed_login_creates_no_session()
    {
        var fx = new Fixture(enabled: true);

        Assert.Null(await fx.Service.LoginAsync(new LoginRequest { Email = fx.User.Email, Password = "wrong-password-123" }, default));
        Assert.Empty(fx.Refresh.Created);
    }

    [Theory]
    [InlineData(RefreshRotationStatus.Invalid)]
    [InlineData(RefreshRotationStatus.Expired)]
    [InlineData(RefreshRotationStatus.ReplayDetected)]
    public async Task Non_rotated_tokens_never_produce_credentials(RefreshRotationStatus status)
    {
        var fx = new Fixture(enabled: true) { };
        fx.Refresh.NextResult = new RefreshRotationResult(status, fx.User.Id);

        Assert.Null(await fx.Service.RefreshAsync(RefreshTokenCodec.Generate(), default));
        Assert.Equal(0, fx.Tokens.Issued);
    }

    [Fact]
    public async Task Rotation_for_an_inactive_account_revokes_every_session_and_fails()
    {
        var fx = new Fixture(enabled: true, active: false);
        fx.Refresh.NextResult = new RefreshRotationResult(RefreshRotationStatus.Rotated, fx.User.Id, Now.AddDays(7));

        Assert.Null(await fx.Service.RefreshAsync(RefreshTokenCodec.Generate(), default));
        Assert.Equal([(fx.User.Id, SessionService.ReasonInactive)], fx.Sessions.Revocations);
        Assert.Equal(0, fx.Tokens.Issued);
    }

    [Fact]
    public async Task Successful_rotation_returns_a_new_refresh_token_and_access_token()
    {
        var fx = new Fixture(enabled: true);
        fx.Refresh.NextResult = new RefreshRotationResult(RefreshRotationStatus.Rotated, fx.User.Id, Now.AddDays(7));
        var presented = RefreshTokenCodec.Generate();

        var response = await fx.Service.RefreshAsync(presented, default);

        Assert.NotNull(response);
        Assert.NotEqual(presented, response!.RefreshToken);
        Assert.Equal(RefreshTokenCodec.Hash(presented), fx.Refresh.LastPresentedHash);
        Assert.Equal(RefreshTokenCodec.Hash(response.RefreshToken!), fx.Refresh.LastReplacementHash);
        Assert.Equal(1, fx.Tokens.Issued);
    }

    [Fact]
    public async Task Logout_all_revokes_through_the_atomic_store()
    {
        var fx = new Fixture(enabled: true);

        Assert.True(await fx.Service.LogoutAllAsync(fx.User.Id, default));
        Assert.False(await fx.Service.LogoutAllAsync(Guid.Empty, default));
        Assert.Equal([(fx.User.Id, SessionService.ReasonLogoutAll)], fx.Sessions.Revocations);
    }

    [Fact]
    public void Lifetime_options_are_validated()
    {
        Assert.Throws<InvalidOperationException>(() => new RefreshTokenOptions { SlidingLifetime = TimeSpan.Zero }.Validate());
        Assert.Throws<InvalidOperationException>(() => new RefreshTokenOptions { SlidingLifetime = TimeSpan.FromDays(10), AbsoluteLifetime = TimeSpan.FromDays(5) }.Validate());
        Assert.Throws<InvalidOperationException>(() => new RefreshTokenOptions { AbsoluteLifetime = TimeSpan.FromDays(365) }.Validate());
        new RefreshTokenOptions().Validate();
    }

    private sealed class Fixture
    {
        public Fixture(bool enabled, bool active = true)
        {
            User = global::BuildMind.Domain.Entities.User.Restore(Guid.NewGuid(), "dana@example.invalid", "DANA@EXAMPLE.INVALID",
                "hash:" + Password, null, null, active, Now);
            Users = new Users(User);
            Service = new SessionService(
                new AuthenticationService(Users, new Passwords(), Tokens),
                Users, Tokens, Refresh, Sessions, new RefreshTokenOptions { Enabled = enabled }, new FixedTime());
        }

        public User User { get; }
        public Users Users { get; }
        public Tokens Tokens { get; } = new();
        public RefreshStore Refresh { get; } = new();
        public SessionStore Sessions { get; } = new();
        public SessionService Service { get; }
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Users(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(id == user.Id ? user : null);
        public Task<User?> GetByEmailAsync(string e, CancellationToken ct) => Task.FromResult(e == user.NormalizedEmail ? user : null);
        public Task<bool> ExistsByEmailAsync(string e, CancellationToken ct) => Task.FromResult(e == user.NormalizedEmail);
        public Task AddAsync(User u, CancellationToken ct) => throw new NotSupportedException();
        public Task UpdateAsync(User u, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Passwords : IPasswordHashService
    {
        public string HashPassword(string password) => "hash:" + password;
        public bool VerifyPassword(string password, string passwordHash) => passwordHash == "hash:" + password;
    }

    private sealed class Tokens : IAccessTokenIssuer
    {
        public int Issued { get; private set; }
        public AccessTokenResult Issue(User user)
        {
            Issued++;
            return new AccessTokenResult("access-" + Issued, Now.AddMinutes(15));
        }
    }

    private sealed class RefreshStore : IRefreshTokenStore
    {
        public List<NewRefreshToken> Created { get; } = [];
        public RefreshRotationResult NextResult { get; set; } = new(RefreshRotationStatus.Invalid);
        public int Rotations { get; private set; }
        public string? LastPresentedHash { get; private set; }
        public string? LastReplacementHash { get; private set; }

        public Task CreateAsync(NewRefreshToken token, CancellationToken ct)
        {
            Created.Add(token);
            return Task.CompletedTask;
        }

        public Task<RefreshRotationResult> RotateAsync(string presented, Guid id, string replacement, DateTimeOffset now, TimeSpan sliding, CancellationToken ct)
        {
            Rotations++;
            LastPresentedHash = presented;
            LastReplacementHash = replacement;
            return Task.FromResult(NextResult);
        }

        public Task<bool> RevokeFamilyAsync(string presented, Guid userId, DateTimeOffset now, string reason, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class SessionStore : IUserSessionStore
    {
        public List<(Guid, string)> Revocations { get; } = [];
        public Task<UserSecurityState?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult<UserSecurityState?>(null);
        public Task<bool> RevokeAllSessionsAsync(Guid userId, DateTimeOffset now, string reason, CancellationToken ct)
        {
            Revocations.Add((userId, reason));
            return Task.FromResult(true);
        }

        public Task<bool> DisableAsync(Guid userId, DateTimeOffset now, CancellationToken ct) => Task.FromResult(true);
    }
}

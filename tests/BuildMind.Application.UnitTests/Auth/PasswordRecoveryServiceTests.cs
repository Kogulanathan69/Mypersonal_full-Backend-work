using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Services;
using BuildMind.Domain.Entities;
using Xunit;

namespace BuildMind.Application.UnitTests.Auth;

public sealed class PasswordRecoveryServiceTests
{
    private const string RawToken =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private const string NewPassword = "NewSecurePassword@123";

    private readonly UserStore _users = new();
    private readonly ResetStore _resets = new();
    private readonly EmailSender _emails = new();

    private readonly TestClock _clock = new(
        new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));

    private PasswordRecoveryService CreateService()
        => new(
            _users,
            _resets,
            new TokenProvider(),
            new PasswordHasher(),
            _emails,
            _clock);

    private static ForgotPasswordRequest Forgot(
        string email = "customer@example.com")
        => new() { Email = email };

    private static ResetPasswordRequest Reset(
        string token = RawToken,
        string email = "customer@example.com")
        => new()
        {
            Email = email,
            Token = token,
            NewPassword = NewPassword,
            ConfirmPassword = NewPassword
        };

    [Fact]
    public async Task KnownAccount_ShouldCreateResetToken()
    {
        var result = await CreateService()
            .ForgotPasswordAsync(Forgot(), default);

        Assert.Equal(
            PasswordRecoveryService.GenericResponse, result);

        Assert.NotNull(_resets.Token);
        Assert.Equal(_users.Account.Id, _resets.Token!.UserId);
        Assert.Equal(1, _emails.SentCount);
    }

    [Fact]
    public async Task UnknownAccount_ShouldNotRevealExistence()
    {
        var result = await CreateService()
            .ForgotPasswordAsync(
                Forgot("unknown@example.com"), default);

        Assert.Equal(
            PasswordRecoveryService.GenericResponse, result);

        Assert.Null(_resets.Token);
        Assert.Equal(0, _emails.SentCount);
    }

    [Fact]
    public async Task InactiveAccount_ShouldReturnSameResponseAsUnknownAccount()
    {
        _users.DeactivateAccount();
        var service = CreateService();

        var inactive = await service.ForgotPasswordAsync(Forgot(), default);
        var unknown = await service.ForgotPasswordAsync(
            Forgot("unknown@example.com"), default);

        Assert.Equal(PasswordRecoveryService.GenericResponse, unknown);
        Assert.Equal(unknown, inactive);
        Assert.Null(_resets.Token);
        Assert.Equal(0, _emails.SentCount);
    }

    [Fact]
    public async Task InvalidEmail_ShouldReturnGenericResponse()
    {
        var result = await CreateService()
            .ForgotPasswordAsync(Forgot("invalid"), default);

        Assert.Equal(
            PasswordRecoveryService.GenericResponse, result);

        Assert.Null(_resets.Token);
    }

    [Fact]
    public async Task StoredToken_ShouldBeHashed()
    {
        await CreateService().ForgotPasswordAsync(
            Forgot(), default);

        Assert.NotEqual(RawToken, _resets.Token!.TokenHash);
        Assert.Equal("HASH:" + RawToken, _resets.Token.TokenHash);
    }

    [Fact]
    public async Task Token_ShouldExpireAfter15Minutes()
    {
        await CreateService().ForgotPasswordAsync(
            Forgot(), default);

        Assert.Equal(
            TimeSpan.FromMinutes(15),
            _resets.Token!.ExpiresAtUtc -
            _resets.Token.CreatedAtUtc);
    }

    [Fact]
    public async Task ValidReset_ShouldSucceed()
    {
        var service = CreateService();

        await service.ForgotPasswordAsync(Forgot(), default);

        Assert.True(await service.ResetPasswordAsync(
            Reset(), default));

        Assert.Equal(
            "HASH:" + NewPassword,
            _resets.UpdatedPasswordHash);

        Assert.Null(_resets.Token);
    }

    [Fact]
    public async Task InvalidToken_ShouldFail()
    {
        var service = CreateService();

        await service.ForgotPasswordAsync(Forgot(), default);

        Assert.False(await service.ResetPasswordAsync(
            Reset(new string('B', 64)), default));

        Assert.Null(_resets.UpdatedPasswordHash);
    }

    [Theory]
    [InlineData('A', 63)]
    [InlineData('A', 65)]
    [InlineData('G', 64)]
    public async Task MalformedToken_IsRejectedBeforeAttemptingReset(char character, int length)
    {
        var service = CreateService();
        await service.ForgotPasswordAsync(Forgot(), default);
        var issuedToken = Assert.IsType<PasswordResetTokenRecord>(_resets.Token);

        Assert.False(await service.ResetPasswordAsync(Reset(new string(character, length)), default));
        Assert.Equal(0, _resets.ResetAttempts);
        Assert.Same(issuedToken, _resets.Token);
        Assert.Null(_resets.UpdatedPasswordHash);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public async Task TokenAtOrPastExpiration_ShouldFail(int elapsedMinutes)
    {
        var service = CreateService();

        await service.ForgotPasswordAsync(Forgot(), default);

        _clock.Now = _clock.Now.AddMinutes(elapsedMinutes);

        Assert.False(await service.ResetPasswordAsync(
            Reset(), default));

        Assert.Null(_resets.UpdatedPasswordHash);
    }

    [Fact]
    public async Task TokenReplay_ShouldFail()
    {
        var service = CreateService();

        await service.ForgotPasswordAsync(Forgot(), default);

        Assert.True(await service.ResetPasswordAsync(
            Reset(), default));

        Assert.False(await service.ResetPasswordAsync(
            Reset(), default));
    }

    [Fact]
    public async Task UnknownAccountReset_ShouldFail()
    {
        Assert.False(await CreateService()
            .ResetPasswordAsync(
                Reset(email: "unknown@example.com"), default));
    }

    [Fact]
    public async Task AccountDeactivatedAfterIssuance_ShouldRejectMatchingToken()
    {
        var service = CreateService();
        await service.ForgotPasswordAsync(Forgot(), default);
        var issuedToken = Assert.IsType<PasswordResetTokenRecord>(_resets.Token);
        _users.DeactivateAccount();

        Assert.False(await service.ResetPasswordAsync(Reset(), default));
        Assert.Null(_resets.UpdatedPasswordHash);
        Assert.Same(issuedToken, _resets.Token);
    }

    [Fact]
    public async Task StorageFailure_ShouldNotSendEmail()
    {
        _resets.RejectSave = true;

        var result = await CreateService()
            .ForgotPasswordAsync(Forgot(), default);

        Assert.Equal(
            PasswordRecoveryService.GenericResponse, result);

        Assert.Equal(0, _emails.SentCount);
    }

    [Fact]
    public async Task EmailDeliveryFailure_ShouldReturnGenericResponse()
    {
        _emails.RejectDelivery = true;

        var result = await CreateService()
            .ForgotPasswordAsync(Forgot(), default);

        Assert.Equal(
            PasswordRecoveryService.GenericResponse, result);
    }

    private sealed class TestClock(DateTimeOffset now)
        : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TokenProvider
        : IPasswordResetTokenProvider
    {
        public string Generate() => RawToken;

        public string Hash(string token) => "HASH:" + token;

        public bool Verify(string? token, string? storedHash)
            => token is not null && Hash(token) == storedHash;
    }

    private sealed class PasswordHasher : IPasswordHashService
    {
        public string HashPassword(string password)
            => "HASH:" + password;

        public bool VerifyPassword(
            string password, string passwordHash)
            => HashPassword(password) == passwordHash;
    }

    private sealed class EmailSender
        : IPasswordResetEmailSender
    {
        public int SentCount { get; private set; }

        public bool RejectDelivery { get; set; }

        public Task<bool> SendAsync(
            string email,
            string token,
            DateTimeOffset expiresAtUtc,
            CancellationToken cancellationToken)
        {
            SentCount++;

            return Task.FromResult(!RejectDelivery);
        }
    }

    private sealed class ResetStore
        : IPasswordResetRepository
    {
        public PasswordResetTokenRecord? Token { get; private set; }

        public string? UpdatedPasswordHash { get; private set; }
        public int ResetAttempts { get; private set; }

        public bool RejectSave { get; set; }

        public Task<bool> ReplaceTokenAsync(
            PasswordResetTokenRecord token,
            CancellationToken cancellationToken)
        {
            if (RejectSave)
                return Task.FromResult(false);

            Token = token;

            return Task.FromResult(true);
        }

        public Task<bool> ResetPasswordAtomicallyAsync(
            Guid userId,
            string tokenHash,
            string newPasswordHash,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken)
        {
            ResetAttempts++;
            if (Token is null ||
                Token.UserId != userId ||
                Token.TokenHash != tokenHash ||
                Token.ExpiresAtUtc <= nowUtc)
            {
                return Task.FromResult(false);
            }

            UpdatedPasswordHash = newPasswordHash;
            Token = null;

            return Task.FromResult(true);
        }
    }

    private sealed class UserStore : IUserRepository
    {
        public User Account { get; private set; } = new()
        {
            Email = "customer@example.com",
            NormalizedEmail = "CUSTOMER@EXAMPLE.COM",
            PasswordHash = "synthetic-test-hash"
        };

        public void DeactivateAccount()
        {
            Account = User.Restore(
                Account.Id,
                Account.Email,
                Account.NormalizedEmail,
                Account.PasswordHash,
                Account.FullName,
                Account.PhoneNumber,
                isActive: false,
                createdAtUtc: Account.CreatedAtUtc);
        }

        public Task<User?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken)
            => Task.FromResult<User?>(
                Account.Id == id ? Account : null);

        public Task<User?> GetByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
            => Task.FromResult<User?>(
                Account.NormalizedEmail == normalizedEmail
                    ? Account : null);

        public Task<bool> ExistsByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
            => Task.FromResult(
                Account.NormalizedEmail == normalizedEmail);

        public Task AddAsync(
            User user, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpdateAsync(
            User user, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}

using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Services;
using BuildMind.Domain.Entities;
using Xunit;

namespace BuildMind.Application.UnitTests.Auth;

public sealed class AuthenticationServiceTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakePasswordService _passwords = new();
    private readonly FakeTokenIssuer _tokens = new();

    private const string TestPassword = "SyntheticPassword123!";

    private AuthenticationService CreateService()
        => new(_users, _passwords, _tokens);

    private static RegisterRequest Registration(
        string email = "customer@example.com",
        string password = TestPassword)
        => new()
        {
            Email = email,
            Password = password
        };

    private static LoginRequest Login(
        string email = "customer@example.com",
        string password = TestPassword)
        => new()
        {
            Email = email,
            Password = password
        };

    [Fact]
    public async Task ValidRegistration_ShouldSucceed()
    {
        var result = await CreateService().RegisterAsync(
            Registration(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("customer@example.com", result!.Email);
        Assert.True(result.IsActive);
        Assert.Single(_users.Users);
    }

    [Fact]
    public async Task Registration_ShouldNormalizeEmail()
    {
        await CreateService().RegisterAsync(
            Registration("Customer@Example.com"),
            CancellationToken.None);

        Assert.Equal(
            "CUSTOMER@EXAMPLE.COM",
            Assert.Single(_users.Users).NormalizedEmail);
    }

    [Fact]
    public async Task Registration_ShouldHashPassword()
    {
        await CreateService().RegisterAsync(
            Registration(), CancellationToken.None);

        var user = Assert.Single(_users.Users);

        Assert.NotEqual(TestPassword, user.PasswordHash);

        Assert.True(_passwords.VerifyPassword(
            TestPassword, user.PasswordHash));
    }

    [Fact]
    public async Task DuplicateEmail_ShouldFail()
    {
        var service = CreateService();

        await service.RegisterAsync(
            Registration(), CancellationToken.None);

        var result = await service.RegisterAsync(
            Registration("CUSTOMER@example.com"),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Single(_users.Users);
    }

    [Theory]
    [InlineData("", "SyntheticPassword123!")]
    [InlineData("invalid-email", "SyntheticPassword123!")]
    [InlineData("customer@example.com", "short")]
    public async Task InvalidRegistration_ShouldFail(
        string email, string password)
    {
        var result = await CreateService().RegisterAsync(
            Registration(email, password),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(_users.Users);
    }

    [Fact]
    public async Task ValidLogin_ShouldIssueToken()
    {
        var service = CreateService();

        await service.RegisterAsync(
            Registration(), CancellationToken.None);

        var result = await service.LoginAsync(
            Login(), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Bearer", result!.TokenType);
        Assert.Equal("synthetic-token", result.AccessToken);
        Assert.Equal(1, _tokens.IssuedCount);
    }

    [Fact]
    public async Task WrongPassword_ShouldFail()
    {
        var service = CreateService();

        await service.RegisterAsync(
            Registration(), CancellationToken.None);

        var result = await service.LoginAsync(
            Login(password: "WrongPassword"),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, _tokens.IssuedCount);
    }

    [Fact]
    public async Task UnknownAccount_ShouldFail()
    {
        var result = await CreateService().LoginAsync(
            Login(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, _tokens.IssuedCount);
    }

    [Fact]
    public async Task InactiveAccount_WithCorrectPassword_DoesNotReceiveToken()
    {
        var service = CreateService();
        await service.RegisterAsync(Registration(), CancellationToken.None);
        var user = Assert.Single(_users.Users);
        _users.Users[0] = User.Restore(
            user.Id, user.Email, user.NormalizedEmail, user.PasswordHash,
            user.FullName, user.PhoneNumber, isActive: false, createdAtUtc: user.CreatedAtUtc);

        var result = await service.LoginAsync(Login(), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, _tokens.IssuedCount);
    }

    [Fact]
    public async Task Login_ShouldIgnoreEmailCase()
    {
        var service = CreateService();

        await service.RegisterAsync(
            Registration(), CancellationToken.None);

        var result = await service.LoginAsync(
            Login("CUSTOMER@EXAMPLE.COM"),
            CancellationToken.None);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvalidLogin_ShouldFail()
    {
        var result = await CreateService().LoginAsync(
            Login(email: ""), CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, _tokens.IssuedCount);
    }
    [Fact]
    public async Task DuplicateEmailRace_ShouldFailSafely()
    {
        _users.SimulateDuplicateEmailRace = true;

        var result = await CreateService().RegisterAsync(
            Registration(),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(_users.Users);
        Assert.Equal(0, _tokens.IssuedCount);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public List<User> Users { get; } = new();
        public bool SimulateDuplicateEmailRace { get; set; }
        public Task<User?> GetByIdAsync(
            Guid id, CancellationToken cancellationToken)
            => Task.FromResult(
                Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
            => Task.FromResult(Users.FirstOrDefault(
                u => u.NormalizedEmail == normalizedEmail));

        public Task<bool> ExistsByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken)
            => Task.FromResult(Users.Any(
                u => u.NormalizedEmail == normalizedEmail));

        public Task AddAsync(
     User user,
     CancellationToken cancellationToken)
        {
            if (SimulateDuplicateEmailRace)
            {
                throw new DuplicateEmailException();
            }

            Users.Add(user);

            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            User user, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class FakePasswordService
        : IPasswordHashService
    {
        public string HashPassword(string password)
            => "test-only:" + password;

        public bool VerifyPassword(
            string password, string passwordHash)
            => HashPassword(password) == passwordHash;
    }

    private sealed class FakeTokenIssuer
        : IAccessTokenIssuer
    {
        public int IssuedCount { get; private set; }

        public AccessTokenResult Issue(User user)
        {
            IssuedCount++;

            return new AccessTokenResult(
                "synthetic-token",
                DateTimeOffset.UtcNow.AddMinutes(15));
        }
    }
}

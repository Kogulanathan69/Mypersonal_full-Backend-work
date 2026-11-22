using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Identity;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Tests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class AuthenticationStartupTests
{
    private const string Register = "/api/v1/auth/register";
    private const string Login = "/api/v1/auth/login";
    private const string Email = "startup@example.test";
    private const string Password = "Synthetic startup password";

    [Fact]
    public async Task RegisterThenLogin_ReturnsUserAndValidToken()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var registration = await client.PostAsJsonAsync(Register, new { email = Email, password = Password });

        AssertStatus(registration, HttpStatusCode.Created);
        var user = Assert.IsType<UserResponse>(await registration.Content.ReadFromJsonAsync<UserResponse>());
        Assert.Equal(Email, user.Email);
        Assert.True(user.IsActive);
        Assert.NotEqual(Guid.Empty, user.Id);
        var saved = Assert.IsType<User>(await factory.Users.GetByEmailAsync(Email.ToUpperInvariant(), default));
        Assert.Equal(user.Id, saved.Id);
        Assert.NotEqual(Password, saved.PasswordHash);

        using var login = await client.PostAsJsonAsync(Login, new { email = Email.ToUpperInvariant(), password = Password });

        AssertStatus(login, HttpStatusCode.OK);
        var session = Assert.IsType<AuthResponse>(await login.Content.ReadFromJsonAsync<AuthResponse>());
        Assert.Equal(user.Id, session.User.Id);
        Assert.Equal("Bearer", session.TokenType);
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(
            session.AccessToken,
            JwtValidationParametersFactory.Create(factory.Services.GetRequiredService<JwtOptions>()),
            out _);
        Assert.Equal(user.Id.ToString("D"), principal.FindFirst("sub")?.Value);
        await AssertNoSecretsAsync(registration, saved.PasswordHash);
        await AssertNoSecretsAsync(login, saved.PasswordHash);
    }

    [Fact]
    public async Task DuplicateAndInvalidRegistration_ReturnGenericBadRequest()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var registration = await client.PostAsJsonAsync(Register, new { email = Email, password = Password });
        AssertStatus(registration, HttpStatusCode.Created);

        using var duplicate = await client.PostAsJsonAsync(Register, new { email = Email.ToUpperInvariant(), password = Password });
        using var invalid = await client.PostAsJsonAsync(Register, new { email = "invalid-email", password = Password });

        const string detail = "Registration could not be completed.";
        await AssertProblemAsync(duplicate, HttpStatusCode.BadRequest, detail);
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, detail);
        Assert.Equal(1, factory.Users.Count);
    }

    [Fact]
    public async Task InvalidCredentialsAndInactiveAccount_ReturnSameUnauthorizedProblem()
    {
        await using var factory = new AuthFactory();
        using var client = factory.CreateClient();
        using var registration = await client.PostAsJsonAsync(Register, new { email = Email, password = Password });
        AssertStatus(registration, HttpStatusCode.Created);

        using var wrong = await client.PostAsJsonAsync(Login, new { email = Email, password = "Wrong synthetic password" });
        using var unknown = await client.PostAsJsonAsync(Login, new { email = "unknown@example.test", password = Password });
        var active = Assert.IsType<User>(await factory.Users.GetByEmailAsync(Email.ToUpperInvariant(), default));
        await factory.Users.UpdateAsync(User.Restore(
            active.Id, active.Email, active.NormalizedEmail, active.PasswordHash,
            active.FullName, active.PhoneNumber, isActive: false, createdAtUtc: active.CreatedAtUtc), default);
        using var inactive = await client.PostAsJsonAsync(Login, new { email = Email, password = Password });

        foreach (var response in new[] { wrong, unknown, inactive })
        {
            await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Authentication failed.");
            await AssertNoSecretsAsync(response, active.PasswordHash);
        }
    }

    private static void AssertStatus(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string detail)
    {
        AssertStatus(response, status);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = problem.RootElement;
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(detail, body.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("code").GetString()));
        Assert.False(body.TryGetProperty("accessToken", out _));
        Assert.False(body.TryGetProperty("stackTrace", out _));
        Assert.DoesNotContain(Password, body.GetRawText());
        Assert.DoesNotContain("Wrong synthetic password", body.GetRawText());
    }

    private static async Task AssertNoSecretsAsync(HttpResponseMessage response, string hash)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Password, body);
        Assert.DoesNotContain(hash, body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class AuthFactory : WebApplicationFactory<Program>
    {
        public MemoryUsers Users { get; } = new();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Minimal hosting reads the activation flags before web-host configuration callbacks run.
            builder.ConfigureHostConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Enabled"] = "true",
                    ["Database:Enabled"] = "true",
                    ["ConnectionStrings:BuildMind"] = DatabaseOptionsTests.ValidConnection,
                    ["Jwt:Issuer"] = "M1StartupTest",
                    ["Jwt:Audience"] = "M1StartupTestClient",
                    ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Services:Python:Enabled"] = "false",
                    ["Services:Rag:Enabled"] = "false"
                }));
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(Users);
                // M1-02 per-request account-state check reads the same in-memory users (no database).
                services.RemoveAll<BuildMind.Application.Features.Auth.Abstractions.IUserSecurityStateReader>();
                services.RemoveAll<BuildMind.Application.Features.Auth.Abstractions.IUserSessionStore>();
                services.RemoveAll<BuildMind.Application.Features.Auth.Abstractions.IRefreshTokenStore>();
                services.AddSingleton<BuildMind.Application.Features.Auth.Abstractions.IUserSecurityStateReader>(Users);
                services.AddSingleton<BuildMind.Application.Features.Auth.Abstractions.IUserSessionStore>(Users);
                services.AddSingleton<BuildMind.Application.Features.Auth.Abstractions.IRefreshTokenStore, UnusedRefreshTokens>();
                services.RemoveAll<BuildMindDbContext>();
                services.AddScoped<BuildMindDbContext>(_ =>
                    throw new InvalidOperationException("Database access is forbidden in authentication startup tests."));
            });
        }
    }

    /// <summary>Refresh tokens are disabled in these hosts; any use is a test failure.</summary>
    internal sealed class UnusedRefreshTokens : BuildMind.Application.Features.Auth.Abstractions.IRefreshTokenStore
    {
        public Task CreateAsync(BuildMind.Application.Features.Auth.Abstractions.NewRefreshToken token, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Refresh tokens are disabled in this host.");

        public Task<BuildMind.Application.Features.Auth.Abstractions.RefreshRotationResult> RotateAsync(string presentedTokenHash, Guid replacementId, string replacementTokenHash,
            DateTimeOffset nowUtc, TimeSpan slidingLifetime, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Refresh tokens are disabled in this host.");

        public Task<bool> RevokeFamilyAsync(string presentedTokenHash, Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Refresh tokens are disabled in this host.");
    }

    internal sealed class MemoryUsers : IUserRepository, BuildMind.Application.Features.Auth.Abstractions.IUserSessionStore
    {
        public Task<BuildMind.Application.Features.Auth.Abstractions.UserSecurityState?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_users.Values.SingleOrDefault(user => user.Id == userId) is { } user
                ? new BuildMind.Application.Features.Auth.Abstractions.UserSecurityState(user.IsActive, user.TokenVersion)
                : null);

        public Task<bool> RevokeAllSessionsAsync(Guid userId, DateTimeOffset nowUtc, string reason, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> DisableAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        private readonly ConcurrentDictionary<string, User> _users = new(StringComparer.Ordinal);

        public int Count => _users.Count;

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_users.Values.SingleOrDefault(user => user.Id == id));

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(_users.GetValueOrDefault(normalizedEmail));

        public Task<bool> ExistsByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(_users.ContainsKey(normalizedEmail));

        public Task AddAsync(User user, CancellationToken cancellationToken)
        {
            if (!_users.TryAdd(user.NormalizedEmail, user))
                throw new DuplicateEmailException();

            return Task.CompletedTask;
        }

        public Task UpdateAsync(User user, CancellationToken cancellationToken)
        {
            _users[user.NormalizedEmail] = user;
            return Task.CompletedTask;
        }
    }
}

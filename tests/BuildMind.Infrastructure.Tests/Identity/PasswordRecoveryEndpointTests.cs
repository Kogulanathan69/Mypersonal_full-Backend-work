using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildMind.API;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.Services;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class PasswordRecoveryEndpointTests
{
    private const string Forgot = "/api/v1/auth/forgot-password";
    private const string Reset = "/api/v1/auth/reset-password";
    private const string Email = "recovery@example.test";
    private const string Password = "Synthetic recovery password";

    [Fact]
    public async Task Forgot_ReturnsIdenticalAcknowledgementsForKnownUnknownAndInvalidEmail()
    {
        var state = new RecoveryState();
        await using var app = await StartAsync(state);
        using var client = app.GetTestClient();
        using var known = await client.PostAsJsonAsync(Forgot, new { email = Email });
        using var unknown = await client.PostAsJsonAsync(Forgot, new { email = "unknown@example.test" });
        using var invalid = await client.PostAsJsonAsync(Forgot, new { email = "invalid-email" });

        var body = await known.Content.ReadAsStringAsync();
        foreach (var response in new[] { known, unknown, invalid })
        {
            AssertStatus(response, HttpStatusCode.Accepted);
            Assert.Equal(body, await response.Content.ReadAsStringAsync());
        }
        using var json = JsonDocument.Parse(body);
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal(PasswordRecoveryService.GenericResponse, json.RootElement.GetProperty("message").GetString());
        var token = Assert.Single(state.Sender.Tokens);
        var stored = Assert.IsType<PasswordResetTokenRecord>(state.Token);
        Assert.Equal(PasswordResetTokenService.Hash(token), stored.TokenHash);
        Assert.DoesNotContain(token, body);
        Assert.DoesNotContain(Email, body);
    }

    [Fact]
    public async Task Reset_ConsumesValidTokenAndRejectsReplay()
    {
        var state = new RecoveryState();
        await using var app = await StartAsync(state);
        using var client = app.GetTestClient();
        var token = await IssueAsync(client, state);
        using var success = await client.PostAsJsonAsync(Reset, ResetBody(token));
        AssertStatus(success, HttpStatusCode.NoContent);
        Assert.Empty(await success.Content.ReadAsStringAsync());
        Assert.Null(state.Token);
        Assert.NotEqual(Password, state.Account.PasswordHash);
        Assert.True(new PasswordHashService().VerifyPassword(Password, state.Account.PasswordHash));

        var savedHash = state.Account.PasswordHash;
        using var replay = await client.PostAsJsonAsync(Reset, ResetBody(token, password: "Different synthetic password"));
        await AssertRejectedAsync(replay, token, savedHash, "Different synthetic password");
        Assert.Equal(savedHash, state.Account.PasswordHash);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("expired")]
    [InlineData("malformed")]
    [InlineData("unknown-account")]
    public async Task Reset_RejectionsHaveSamePublicErrorAndDoNotChangePassword(string scenario)
    {
        var state = new RecoveryState();
        await using var app = await StartAsync(state);
        using var client = app.GetTestClient();
        var issued = await IssueAsync(client, state);
        var before = state.Account.PasswordHash;
        var record = state.Token;
        var token = scenario switch
        {
            "wrong" => (issued[0] == 'A' ? "B" : "A") + issued[1..],
            "malformed" => "synthetic-non-hex-token",
            _ => issued
        };
        if (scenario == "expired")
            state.Clock.Now = Assert.IsType<PasswordResetTokenRecord>(record).ExpiresAtUtc;

        var email = scenario == "unknown-account" ? "unknown@example.test" : Email;
        using var response = await client.PostAsJsonAsync(Reset, ResetBody(token, email));
        await AssertRejectedAsync(response, token, before);
        Assert.Equal(before, state.Account.PasswordHash);
        Assert.Same(record, state.Token);
    }

    [Theory]
    [InlineData(Forgot)]
    [InlineData(Reset)]
    public async Task MalformedJson_ReturnsSafeBadRequest(string route)
    {
        const string marker = "synthetic-invalid-json-value";
        var state = new RecoveryState();
        await using var app = await StartAsync(state);
        using var client = app.GetTestClient();
        using var content = new StringContent("{\"email\":\"" + marker, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(marker, await response.Content.ReadAsStringAsync());
        Assert.Empty(state.Sender.Tokens);
        Assert.Null(state.Token);
    }

    private static async Task<string> IssueAsync(HttpClient client, RecoveryState state)
    {
        using var response = await client.PostAsJsonAsync(Forgot, new { email = Email });
        AssertStatus(response, HttpStatusCode.Accepted);
        return Assert.Single(state.Sender.Tokens);
    }

    private static object ResetBody(string token, string email = Email, string password = Password) =>
        new { email, token, newPassword = password, confirmPassword = password };

    private static void AssertStatus(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response, string token, string hash, string password = Password)
    {
        AssertStatus(response, HttpStatusCode.BadRequest);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Password reset could not be completed.", json.RootElement.GetProperty("detail").GetString());
        Assert.DoesNotContain(token, body);
        Assert.DoesNotContain(hash, body);
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain("example.test", body);
        Assert.False(json.RootElement.TryGetProperty("stackTrace", out _));
    }

    private static async Task<WebApplication> StartAsync(RecoveryState state)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddBuildMindApiInfrastructure(builder.Configuration);
        builder.Services.AddSingleton<IApiEndpointModule, ProvisionalPasswordRecoveryEndpointModule>();
        builder.Services.AddSingleton<IUserRepository>(state.Users);
        builder.Services.AddSingleton<IPasswordResetRepository>(state);
        builder.Services.AddSingleton<IPasswordResetEmailSender>(state.Sender);
        builder.Services.AddSingleton<TimeProvider>(state.Clock);
        builder.Services.AddSingleton<IPasswordResetTokenProvider, PasswordResetTokenProvider>();
        builder.Services.AddSingleton<IPasswordHashService, PasswordHashService>();
        builder.Services.AddScoped<PasswordRecoveryService>();

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapBuildMindApiV1();
        await app.StartAsync();
        return app;
    }

    private sealed class RecoveryState : IPasswordResetRepository
    {
        private readonly object _gate = new();
        public User Account { get; } = new()
        {
            Email = PasswordRecoveryEndpointTests.Email,
            NormalizedEmail = PasswordRecoveryEndpointTests.Email.ToUpperInvariant(),
            PasswordHash = "synthetic-original-hash"
        };
        public AuthenticationStartupTests.MemoryUsers Users { get; } = new();
        public FakeSender Sender { get; } = new();
        public TestClock Clock { get; } = new();
        public PasswordResetTokenRecord? Token { get; private set; }

        public RecoveryState() => Users.AddAsync(Account, default).GetAwaiter().GetResult();

        public Task<bool> ReplaceTokenAsync(PasswordResetTokenRecord token, CancellationToken cancellationToken)
        {
            lock (_gate)
                Token = token;
            return Task.FromResult(true);
        }

        public Task<bool> ResetPasswordAtomicallyAsync(
            Guid userId, string tokenHash, string newPasswordHash, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (Token is null || Token.UserId != userId || Token.TokenHash != tokenHash || Token.ExpiresAtUtc <= nowUtc)
                    return Task.FromResult(false);

                Account.PasswordHash = newPasswordHash;
                Token = null;
                return Task.FromResult(true);
            }
        }
    }

    private sealed class FakeSender : IPasswordResetEmailSender
    {
        public List<string> Tokens { get; } = [];
        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
        {
            Tokens.Add(token);
            return Task.FromResult(true);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

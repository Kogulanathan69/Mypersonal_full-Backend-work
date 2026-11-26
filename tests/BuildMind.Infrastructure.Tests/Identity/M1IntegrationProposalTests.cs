using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildMind.API.Security;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

/// <summary>
/// STEP 2 integration proposal (develop ee44506 + M1 71bc50c): the real composition root with M1 authentication,
/// M4 S4-B as the only rate limiter, and the documented middleware order.
/// </summary>
public sealed class M1IntegrationProposalTests
{
    private const string Login = "/api/v1/auth/login";
    private const string Forgot = "/api/v1/auth/forgot-password";

    [Fact]
    public void Sensitive_auth_endpoints_use_m4_auth_strict_and_no_m1_policy_remains()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store(), RecoveryOn);
        _ = factory.CreateClient();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        foreach (var route in new[] { "/api/v1/auth/register", Login, Forgot, "/api/v1/auth/reset-password" })
        {
            var endpoint = Assert.Single(endpoints, e => e.RoutePattern.RawText == route);
            var policy = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            Assert.NotNull(policy);
            Assert.Equal(RateLimitingConfiguration.AuthStrictPolicy, policy.PolicyName);
        }

        Assert.DoesNotContain(endpoints, e => e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == "M1Authentication");
    }

    [Fact]
    public async Task Login_is_throttled_by_auth_strict_after_ten_attempts_per_peer()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 10; attempt++)
        {
            using var response = await client.PostAsJsonAsync(Login, new { email = "nobody@example.invalid", password = "wrong-password-wrong-password" });
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(Login, new { email = "nobody@example.invalid", password = "wrong-password-wrong-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        var body = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("RATE_LIMITED", body.GetProperty("code").GetString());
        Assert.True(rejected.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task Authentication_runs_before_the_rate_limiter_so_users_get_their_own_partition()
    {
        // Anonymous callers from one peer are capped at 120/min. A signed-in user from the same peer is partitioned by
        // user id (300/min); that only happens when UseAuthentication runs before UseRateLimiter.
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await M1ApiIntegrationTests.RegisterAndLoginAsync(client, "order@example.invalid"));

        for (var request = 1; request <= 130; request++)
        {
            using var response = await client.GetAsync("/api/v1/profile");
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"request {request} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Disabled_account_is_rejected_on_the_next_request()
    {
        var store = new InMemoryM1Store();
        using var factory = M1ApiIntegrationTests.CreateFactory(store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await M1ApiIntegrationTests.RegisterAndLoginAsync(client, "disabled@example.invalid"));
        using (var before = await client.GetAsync("/api/v1/profile"))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        var user = await store.GetByEmailAsync("DISABLED@EXAMPLE.INVALID", CancellationToken.None);
        store.SetActive(user!.Id, false);

        using var after = await client.GetAsync("/api/v1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Password_recovery_and_refresh_routes_are_not_mapped_by_default()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();

        foreach (var route in new[] { Forgot, "/api/v1/auth/reset-password", "/api/v1/auth/refresh", "/api/v1/auth/logout-all" })
        {
            using var response = await client.PostAsJsonAsync(route, new { email = "a@example.invalid" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task Recovery_proposal_persists_only_a_hash_and_never_delivers_the_token()
    {
        var store = new InMemoryM1Store();
        var resets = new CapturingResetRepository();
        using var factory = M1ApiIntegrationTests.CreateFactory(store, RecoveryOn, services =>
        {
            services.RemoveAll<IPasswordResetRepository>();
            services.AddSingleton<IPasswordResetRepository>(resets);
        });
        using var client = factory.CreateClient();
        await M1ApiIntegrationTests.RegisterAndLoginAsync(client, "reset@example.invalid");

        using var known = await client.PostAsJsonAsync(Forgot, new { email = "reset@example.invalid" });
        using var unknown = await client.PostAsJsonAsync(Forgot, new { email = "nobody@example.invalid" });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var record = Assert.Single(resets.Records);
        Assert.Matches("^[0-9a-f]{64}$", record.TokenHash.ToLowerInvariant());
        Assert.Equal(TimeSpan.FromMinutes(15), record.ExpiresAtUtc - record.CreatedAtUtc);
        Assert.IsType<NonDeliveringPasswordResetEmailSender>(factory.Services.GetRequiredService<IPasswordResetEmailSender>());
    }

    [Fact]
    public async Task M7_retrieval_stays_unregistered_with_authentication_enabled()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await M1ApiIntegrationTests.RegisterAndLoginAsync(client, "m7@example.invalid"));

        using var response = await client.PostAsJsonAsync("/api/v1/regulations/retrieve", new { query = "setback" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void RecoveryOn(Dictionary<string, string?> settings) =>
        settings[M1AuthenticationExtensions.PasswordRecoveryEnabledKey] = "true";

    private sealed class CapturingResetRepository : IPasswordResetRepository
    {
        private readonly List<PasswordResetTokenRecord> _records = [];

        public IReadOnlyList<PasswordResetTokenRecord> Records
        {
            get { lock (_records) { return _records.ToArray(); } }
        }

        public Task<bool> ReplaceTokenAsync(PasswordResetTokenRecord token, CancellationToken cancellationToken)
        {
            lock (_records) { _records.Add(token); }
            return Task.FromResult(true);
        }

        public Task<bool> ResetPasswordAtomicallyAsync(Guid userId, string tokenHash, string newPasswordHash, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}

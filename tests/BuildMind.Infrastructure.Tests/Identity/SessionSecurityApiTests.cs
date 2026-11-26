using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static BuildMind.Infrastructure.Tests.Identity.M1ApiIntegrationTests;

namespace BuildMind.Infrastructure.Tests.Identity;

/// <summary>
/// STEP 05 decisions 1-3 at the HTTP boundary: disabled accounts, token-version revocation, refresh-token rotation
/// and replay. In-memory stores mirror the MySQL semantics; atomic SQL behaviour is covered by the live suite.
/// </summary>
public sealed class SessionSecurityApiTests
{
    private const string Email = "carol@example.invalid";

    [Fact]
    public async Task AUTH_G02_Disable_after_password_verification_rejects_login()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store, s => s["Authentication:RefreshTokens:Enabled"] = "true");
        using var client = factory.CreateClient();
        using var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var id = await UserIdAsync(store);
        var attempts = 0;
        store.BeforeCreateRefresh = token =>
        {
            Assert.Equal(id, token.UserId);
            attempts++;
            store.SetActive(id, false);
        };
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
        Assert.Equal(1, attempts);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        var body = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", body);
        Assert.DoesNotContain("refreshToken", body);
        Assert.Empty(store.RefreshTokens);
    }

    [Fact]
    public async Task AUTH_G05_Revocation_before_signing_rejects_rotated_bearer_at_profile()
    {
        var store = new InMemoryM1Store();
        Action<User>? beforeSigning = null;
        await using var factory = CreateFactory(store, s => s["Authentication:RefreshTokens:Enabled"] = "true",
            services => services.AddScoped<IAccessTokenIssuer>(sp => new HookedIssuer(
                ActivatorUtilities.CreateInstance<JwtAccessTokenIssuer>(sp), user => beforeSigning?.Invoke(user))));
        using var client = factory.CreateClient();
        using var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var first = await LoginBodyAsync(client);
        var id = await UserIdAsync(store);
        var version = (await store.GetAsync(id, default))!.TokenVersion;
        var calls = 0;
        beforeSigning = user =>
        {
            calls++;
            Assert.Equal(id, user.Id);
            Assert.Equal(version, user.TokenVersion);
            Assert.Equal(2, store.RefreshTokens.Count);
            Assert.True(store.RevokeAllSessionsAsync(id, DateTimeOffset.UtcNow, "test", default).GetAwaiter().GetResult());
        };
        using var rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = first.GetProperty("refreshToken").GetString() });
        beforeSigning = null;
        Assert.Equal(1, calls);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.Equal(version + 1, (await store.GetAsync(id, default))!.TokenVersion);
        var body = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        var jwt = body.GetProperty("accessToken").GetString()!;
        var claim = new JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Single(c => c.Type == "tv");
        Assert.Equal(version.ToString(System.Globalization.CultureInfo.InvariantCulture), claim.Value);
        Authorize(client, jwt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
        Authorize(client, await LoginAsync(client, Email));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profile")).StatusCode);
    }

    private sealed class HookedIssuer(JwtAccessTokenIssuer issuer, Action<User> beforeSigning) : IAccessTokenIssuer
    {
        public AccessTokenResult Issue(User user)
        {
            beforeSigning(user);
            return issuer.Issue(user);
        }
    }

    [Fact]
    public async Task A_disabled_account_loses_access_immediately_with_an_unexpired_token()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        Authorize(client, await RegisterAndLoginAsync(client, Email));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profile")).StatusCode);

        store.SetActive(await UserIdAsync(store), false);   // token version deliberately unchanged

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/projects")).StatusCode);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Revoking_all_sessions_invalidates_issued_access_tokens()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var oldToken = await RegisterAndLoginAsync(client, Email);

        await store.RevokeAllSessionsAsync(await UserIdAsync(store), DateTimeOffset.UtcNow, "test", default);

        Authorize(client, oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
        Authorize(client, await LoginAsync(client, Email));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profile")).StatusCode);
    }

    [Fact]
    public async Task Tokens_without_a_valid_session_version_are_rejected_even_when_correctly_signed()
    {
        var store = new InMemoryM1Store();
        var settings = AuthSettings();
        await using var factory = CreateFactory(store, s => s["Jwt:SigningKey"] = settings["Jwt:SigningKey"]);
        using var client = factory.CreateClient();
        await RegisterAndLoginAsync(client, Email);
        var userId = await UserIdAsync(store);
        var key = Convert.FromBase64String(settings["Jwt:SigningKey"]!);

        foreach (var claims in new[]
                 {
                     new[] { new Claim("sub", userId.ToString("D")) },                                  // no tv
                     [new Claim("sub", userId.ToString("D")), new Claim("tv", "0")],                   // invalid tv
                     [new Claim("sub", userId.ToString("D")), new Claim("tv", "2")],                   // future tv
                     [new Claim("sub", userId.ToString("D")), new Claim("tv", "1"), new Claim("tv", "1")], // duplicate
                     [new Claim("sub", Guid.NewGuid().ToString("D")), new Claim("tv", "1")],           // unknown user
                 })
        {
            Authorize(client, Sign(key, claims));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
        }

        Authorize(client, Sign(key, [new Claim("sub", userId.ToString("D")), new Claim("tv", "1")]));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profile")).StatusCode);
    }

    [Fact]
    public async Task An_account_state_outage_never_serves_the_protected_resource()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        Authorize(client, await RegisterAndLoginAsync(client, Email));

        store.StateReadFailure = new InvalidOperationException("synthetic outage");
        using var response = await client.GetAsync("/api/v1/profile");

        Assert.True((int)response.StatusCode >= 500, $"expected a server error, got {(int)response.StatusCode}");
        Assert.DoesNotContain("synthetic outage", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_bearer_header_does_not_block_anonymous_login()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();
        var stale = await RegisterAndLoginAsync(client, Email);
        await store.RevokeAllSessionsAsync(await UserIdAsync(store), DateTimeOffset.UtcNow, "test", default);

        Authorize(client, stale);
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Session_endpoints_and_refresh_tokens_are_off_by_default()
    {
        await using var factory = CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();
        await RegisterAndLoginAsync(client, Email);

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("refreshToken", out _));
        foreach (var route in new[] { "/api/v1/auth/refresh", "/api/v1/auth/logout", "/api/v1/auth/logout-all" })
        {
            using var response = await client.PostAsJsonAsync(route, new { refreshToken = "x" });
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, route);
        }
    }

    [Fact]
    public async Task Refresh_rotates_and_a_replayed_token_revokes_the_whole_session()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store, s => s["Authentication:RefreshTokens:Enabled"] = "true");
        using var client = factory.CreateClient();
        using (await client.PostAsJsonAsync("/api/v1/auth/register", new { email = Email, password = Password })) { }
        var first = await LoginBodyAsync(client);
        var firstRefresh = first.GetProperty("refreshToken").GetString()!;
        Assert.Equal(43, firstRefresh.Length);
        Assert.DoesNotContain(store.RefreshTokens, t => t.Token.TokenHash == firstRefresh); // only hashes stored

        using var rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = firstRefresh });
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = await rotated.Content.ReadFromJsonAsync<JsonElement>();
        var secondRefresh = second.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(firstRefresh, secondRefresh);
        Authorize(client, second.GetProperty("accessToken").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/profile")).StatusCode);

        // Replay of the consumed token: rejected, family revoked, every access token invalidated.
        using var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = firstRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var afterReplay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = secondRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReplay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
        Assert.All(store.RefreshTokens, t => Assert.NotNull(t.RevokedAtUtc));
    }

    [Fact]
    public async Task Logout_ends_one_session_and_logout_all_ends_every_session()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store, s => s["Authentication:RefreshTokens:Enabled"] = "true");
        using var client = factory.CreateClient();
        using (await client.PostAsJsonAsync("/api/v1/auth/register", new { email = Email, password = Password })) { }
        var laptop = await LoginBodyAsync(client);
        var phone = await LoginBodyAsync(client);

        Authorize(client, laptop.GetProperty("accessToken").GetString()!);
        using (var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = laptop.GetProperty("refreshToken").GetString() }))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using (var laptopRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = laptop.GetProperty("refreshToken").GetString() }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, laptopRefresh.StatusCode);
        }

        using var phoneRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = phone.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, phoneRefresh.StatusCode);
        var phoneNow = await phoneRefresh.Content.ReadFromJsonAsync<JsonElement>();

        Authorize(client, phoneNow.GetProperty("accessToken").GetString()!);
        using (var all = await client.PostAsync("/api/v1/auth/logout-all", null))
        {
            Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
        using var phoneAfter = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = phoneNow.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.Unauthorized, phoneAfter.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // well-formed but unknown
    public async Task Unknown_or_malformed_refresh_tokens_get_the_same_401(string token)
    {
        await using var factory = CreateFactory(new InMemoryM1Store(), s => s["Authentication:RefreshTokens:Enabled"] = "true");
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<JsonElement> LoginBodyAsync(HttpClient client)
    {
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return await login.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> UserIdAsync(InMemoryM1Store store) =>
        (await store.GetByEmailAsync(Email.ToUpperInvariant(), default))!.Id;

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static string Sign(byte[] key, Claim[] claims)
    {
        var token = new JwtSecurityToken("buildmind-tests", "buildmind-tests-api", claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

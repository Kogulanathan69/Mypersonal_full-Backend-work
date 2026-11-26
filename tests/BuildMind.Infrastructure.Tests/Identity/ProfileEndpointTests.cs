using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Application.Features.Users.Services;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class ProfileEndpointTests
{
    private const string Route = "/api/v1/profile";
    internal static readonly User Owner = NewUser("owner@example.test");
    internal static readonly User Other = NewUser("other@example.test");
    private static readonly User Missing = NewUser("missing@example.test");
    private static readonly User Inactive = NewUser("inactive@example.test");
    private static readonly JwtOptions Options = new()
    {
        Issuer = "ProfileTest",
        Audience = "ProfileTestClient",
        SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    };

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AnonymousOrExpiredToken_IsUnauthorized(bool update, bool expired)
    {
        var store = new ProfileStore();
        await using var app = await StartAsync(store);
        using var client = expired
            ? CreateClient(app, Owner, DateTimeOffset.UtcNow.AddHours(-1))
            : app.GetTestClient();
        using var response = update
            ? await client.PutAsJsonAsync(Route, new { fullName = "Rejected edit" })
            : await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task MalformedBearerToken_IsRejectedWithoutExposingTokenOrParserError()
    {
        const string malformed = "synthetic-malformed-bearer";
        var store = new ProfileStore();
        await using var app = await StartAsync(store);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", malformed);

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Null(challenge.Parameter);
        Assert.DoesNotContain(malformed, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task Get_ReturnsOnlyAuthenticatedProfileWithoutPasswordHash()
    {
        await using var app = await StartAsync(new ProfileStore());
        using var client = CreateClient(app, Owner);
        using var response = await client.GetAsync(Route);

        AssertStatus(response, HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Owner.Id, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(Owner.Email, json.RootElement.GetProperty("email").GetString());
        Assert.False(json.RootElement.TryGetProperty("passwordHash", out _));
        Assert.DoesNotContain(Owner.PasswordHash, json.RootElement.GetRawText());
    }

    [Fact]
    public async Task Update_TargetsAuthenticatedUserEvenWithAnotherUserInQuery()
    {
        await using var app = await StartAsync(new ProfileStore());
        using var client = CreateClient(app, Owner);
        using var response = await client.PutAsJsonAsync(
            $"{Route}?userId={Other.Id}", new { fullName = "  Updated owner  ", phoneNumber = "+94770000000" });

        AssertStatus(response, HttpStatusCode.OK);
        var updated = Assert.IsType<ProfileResponse>(await response.Content.ReadFromJsonAsync<ProfileResponse>());
        Assert.Equal(Owner.Id, updated.Id);
        Assert.Equal("Updated owner", updated.FullName);
        Assert.Equal("+94770000000", updated.PhoneNumber);
        using var ownerRead = await client.GetAsync(Route);
        AssertStatus(ownerRead, HttpStatusCode.OK);
        Assert.Equal(updated.FullName, (await ownerRead.Content.ReadFromJsonAsync<ProfileResponse>())?.FullName);
        using var otherClient = CreateClient(app, Other);
        using var otherRead = await otherClient.GetAsync(Route);
        AssertStatus(otherRead, HttpStatusCode.OK);
        var other = Assert.IsType<ProfileResponse>(await otherRead.Content.ReadFromJsonAsync<ProfileResponse>());
        Assert.Equal(Other.Id, other.Id);
        Assert.Equal("Original profile", other.FullName);
        Assert.Null(other.PhoneNumber);
    }

    [Theory]
    [InlineData("unexpectedField", "synthetic-extra")]
    [InlineData("id", null)]
    [InlineData("userId", null)]
    [InlineData("ownerId", null)]
    [InlineData("role", "synthetic-admin")]
    [InlineData("roles", "synthetic-admin")]
    [InlineData("password", "synthetic-new-password")]
    [InlineData("newPassword", "synthetic-new-password")]
    [InlineData("passwordHash", "synthetic-replacement-hash")]
    [InlineData("email", "replacement@example.test")]
    [InlineData("isActive", "false")]
    public async Task Update_ExtraJsonFieldsDoNotChangeProtectedProfileData(string field, string? value)
    {
        var store = new ProfileStore();
        await using var app = await StartAsync(store);
        using var client = CreateClient(app, Owner);
        var body = new Dictionary<string, object?>
        {
            ["fullName"] = "Updated owner",
            ["phoneNumber"] = "+94770000000",
            [field] = field switch
            {
                "roles" => new[] { value },
                "isActive" => false,
                _ => value ?? Other.Id.ToString()
            }
        };

        using var response = await client.PutAsJsonAsync(Route, body);

        // Characterize the current binder behavior until strict rejection is agreed.
        AssertStatus(response, HttpStatusCode.OK);
        Assert.Equal(1, store.Calls);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var updated = json.RootElement;
        Assert.Equal(Owner.Id, updated.GetProperty("id").GetGuid());
        Assert.Equal(Owner.Email, updated.GetProperty("email").GetString());
        Assert.True(updated.GetProperty("isActive").GetBoolean());
        Assert.Equal("Updated owner", updated.GetProperty("fullName").GetString());
        Assert.Equal("+94770000000", updated.GetProperty("phoneNumber").GetString());
        if (field is not ("id" or "email" or "isActive"))
            Assert.False(updated.TryGetProperty(field, out _));
        if (value is not null && field != "isActive")
            Assert.DoesNotContain(value, updated.GetRawText());

        using var otherClient = CreateClient(app, Other);
        using var otherRead = await otherClient.GetAsync(Route);
        AssertStatus(otherRead, HttpStatusCode.OK);
        var other = Assert.IsType<ProfileResponse>(await otherRead.Content.ReadFromJsonAsync<ProfileResponse>());
        Assert.Equal(Other.Id, other.Id);
        Assert.Equal("Original profile", other.FullName);
        Assert.Null(other.PhoneNumber);
    }

    [Theory]
    [InlineData(" ", null)]
    [InlineData("Valid name", "invalid")]
    public async Task InvalidUpdate_ReturnsSafeBadRequestWithoutAccessingStore(string name, string? phone)
    {
        var store = new ProfileStore();
        await using var app = await StartAsync(store);
        using var client = CreateClient(app, Owner);
        using var response = await client.PutAsJsonAsync(Route, new { fullName = name, phoneNumber = phone });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "Invalid profile request.");
        Assert.Equal(0, store.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAndInactiveProfiles_ReturnSameNotFound(bool update)
    {
        await using var app = await StartAsync(new ProfileStore());
        foreach (var user in new[] { Missing, Inactive })
        {
            using var client = CreateClient(app, user);
            using var response = update
                ? await client.PutAsJsonAsync(Route, new { fullName = "Rejected edit" })
                : await client.GetAsync(Route);

            await AssertProblemAsync(response, HttpStatusCode.NotFound, "Profile not found.");
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
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)status, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(detail, json.RootElement.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("code").GetString()));
    }

    private static User NewUser(string email) =>
        new() { Email = email, NormalizedEmail = email.ToUpperInvariant(), PasswordHash = "synthetic-profile-hash" };

    private static HttpClient CreateClient(WebApplication app, User user, DateTimeOffset? issuedAt = null)
    {
        var issuer = new JwtAccessTokenIssuer(Options, new FixedClock(issuedAt ?? DateTimeOffset.UtcNow));
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issuer.Issue(user).AccessToken);
        return client;
    }

    private static async Task<WebApplication> StartAsync(ProfileStore store)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt =>
        {
            jwt.MapInboundClaims = false;
            jwt.IncludeErrorDetails = false;
            jwt.TokenValidationParameters = JwtValidationParametersFactory.Create(Options);
        });
        builder.Services.AddAuthorization();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        builder.Services.AddScoped<ProfileService>();
        builder.Services.AddSingleton<IUserProfileRepository>(store);
        builder.Services.AddSingleton<IApiEndpointModule, ProfileEndpointModule>();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapBuildMindApiV1();
        await app.StartAsync();
        return app;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal sealed class ProfileStore : IUserProfileRepository
    {
        private readonly Dictionary<Guid, ProfileResponse> _profiles = new()
        {
            [Owner.Id] = MakeProfile(Owner),
            [Other.Id] = MakeProfile(Other),
            [Inactive.Id] = MakeProfile(Inactive, active: false)
        };

        public int Calls { get; private set; }

        public Task<ProfileResponse?> GetAsync(Guid userId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(FindActive(userId));
        }

        public Task<ProfileResponse?> UpdateAsync(
            Guid userId, string fullName, string? phoneNumber, CancellationToken cancellationToken)
        {
            Calls++;
            var profile = FindActive(userId);
            if (profile is null)
                return Task.FromResult<ProfileResponse?>(null);

            var updated = new ProfileResponse
            {
                Id = profile.Id,
                Email = profile.Email,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                IsActive = profile.IsActive,
                CreatedAtUtc = profile.CreatedAtUtc
            };
            _profiles[userId] = updated;
            return Task.FromResult<ProfileResponse?>(updated);
        }

        private ProfileResponse? FindActive(Guid id) =>
            _profiles.TryGetValue(id, out var profile) && profile.IsActive ? profile : null;

        private static ProfileResponse MakeProfile(User user, bool active = true) => new()
        {
            Id = user.Id,
            Email = user.Email,
            FullName = "Original profile",
            IsActive = active,
            CreatedAtUtc = user.CreatedAtUtc
        };
    }
}

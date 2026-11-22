using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

/// <summary>
/// STEP 05 Batch 2A: the real composition root with M1 JWT authentication enabled (Development), hosting the first
/// product API milestone. Repositories are in-memory stand-ins; MySQL behaviour is covered by the live suite.
/// </summary>
public sealed class M1ApiIntegrationTests
{
    internal const string Password = "correct-horse-battery-staple-2026";

    internal static readonly string[] ExpectedProductRoutes =
    [
        "POST /api/v1/auth/register",
        "POST /api/v1/auth/login",
        "GET /api/v1/profile",
        "PUT /api/v1/profile",
        "POST /api/v1/projects",
        "GET /api/v1/projects",
        "GET /api/v1/projects/{id}",
        "PATCH /api/v1/projects/{id}",
        "GET /api/v1/dashboard/summary",
    ];

    internal static Dictionary<string, string?> AuthSettings() => new()
    {
        ["Authentication:Enabled"] = "true",
        ["Database:Enabled"] = "true",
        ["ConnectionStrings:BuildMind"] =
            "Server=127.0.0.1;Port=1;Database=buildmind_synthetic;User ID=bm_synthetic;SslMode=Required;Password=synthetic-not-a-secret",
        ["Jwt:Issuer"] = "buildmind-tests",
        ["Jwt:Audience"] = "buildmind-tests-api",
        ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    internal static StartupSettingsApiFactory CreateFactory(
        InMemoryM1Store store, Action<Dictionary<string, string?>>? settings = null, Action<IServiceCollection>? extra = null) =>
        new("Development", With(AuthSettings(), settings), services =>
        {
            services.RemoveAll<IUserRepository>();
            services.RemoveAll<IUserProfileRepository>();
            services.RemoveAll<IProjectRepository>();
            services.RemoveAll<IUserSessionStore>();
            services.RemoveAll<IUserSecurityStateReader>();
            services.RemoveAll<IRefreshTokenStore>();
            services.AddSingleton<IUserRepository>(store);
            services.AddSingleton<IUserProfileRepository>(store);
            services.AddSingleton<IProjectRepository>(store);
            services.AddSingleton<IUserSessionStore>(store);
            services.AddSingleton<IUserSecurityStateReader>(store);
            services.AddSingleton<IRefreshTokenStore>(store);
            extra?.Invoke(services);
        });

    private static Dictionary<string, string?> With(Dictionary<string, string?> values, Action<Dictionary<string, string?>>? change)
    {
        change?.Invoke(values);
        return values;
    }

    [Fact]
    public async Task Without_authentication_no_product_modules_are_registered()
    {
        await using var factory = new BuildMindApiFactory("Development");

        Assert.DoesNotContain(factory.Services.GetServices<IApiEndpointModule>(),
            m => m.GetType().Namespace == "BuildMind.API.Routing");
    }

    [Fact]
    public async Task Authentication_requires_development_and_database()
    {
        var production = AuthSettings();
        var noDatabase = AuthSettings();
        noDatabase["Database:Enabled"] = "false";

        await using var prodFactory = new StartupSettingsApiFactory("Production", production);
        await using var noDbFactory = new StartupSettingsApiFactory("Development", noDatabase);

        Assert.Contains("Authentication activation is not approved",
            Assert.ThrowsAny<Exception>(() => prodFactory.CreateClient()).ToString(), StringComparison.Ordinal);
        Assert.Contains("Authentication requires database access",
            Assert.ThrowsAny<Exception>(() => noDbFactory.CreateClient()).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Swagger_lists_the_milestone_routes_with_bearer_only_where_required()
    {
        await using var factory = CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var root = document.RootElement;
        var operations = root.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Select(op => (Key: op.Name.ToUpperInvariant() + " " + path.Name, Op: op.Value)))
            .Where(x => x.Key.Contains("/api/v1/", StringComparison.Ordinal))
            .ToDictionary(x => x.Key, x => x.Op);

        Assert.Equal(ExpectedProductRoutes.Order(), operations.Keys.Order());
        Assert.Equal("bearer", root.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("Bearer").GetProperty("scheme").GetString());
        foreach (var (key, op) in operations)
        {
            var anonymous = key.Contains("/auth/", StringComparison.Ordinal);
            Assert.Equal(!anonymous, op.TryGetProperty("security", out _));
        }

        Assert.DoesNotContain("/requirements", string.Join(" ", operations.Keys), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customer_journey_register_login_profile_projects_dashboard()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store);
        using var client = factory.CreateClient();

        var token = await RegisterAndLoginAsync(client, "alice@example.invalid");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using (var profile = await client.PutAsJsonAsync("/api/v1/profile", new { fullName = "Alice Example", phoneNumber = "+94771234567" }))
        {
            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        }

        var profileBody = await client.GetFromJsonAsync<JsonElement>("/api/v1/profile");
        Assert.Equal("Alice Example", profileBody.GetProperty("fullName").GetString());

        using var created = await client.PostAsJsonAsync("/api/v1/projects", new { name = "Kandy house", projectType = "Residential" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = project.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/projects/{id}", created.Headers.Location!.OriginalString);

        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/projects")).EnumerateArray());
        using (var get = await client.GetAsync($"/api/v1/projects/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        }

        using var patch = await client.PatchAsJsonAsync($"/api/v1/projects/{id}", new { name = "Kandy house v2", revision = 1 });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        using var stale = await client.PatchAsJsonAsync($"/api/v1/projects/{id}", new { name = "stale", revision = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var dashboard = await client.GetAsync("/api/v1/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Equal("no-store", dashboard.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Another_users_project_is_not_found_and_cannot_be_modified()
    {
        var store = new InMemoryM1Store();
        await using var factory = CreateFactory(store);
        using var alice = factory.CreateClient();
        using var mallory = factory.CreateClient();
        alice.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLoginAsync(alice, "alice@example.invalid"));
        mallory.DefaultRequestHeaders.Authorization = new("Bearer", await RegisterAndLoginAsync(mallory, "mallory@example.invalid"));

        using var created = await alice.PostAsJsonAsync("/api/v1/projects", new { name = "Private", projectType = "Residential" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var read = await mallory.GetAsync($"/api/v1/projects/{id}");
        using var write = await mallory.PatchAsJsonAsync($"/api/v1/projects/{id}", new { name = "hijack", revision = 1 });
        var list = await mallory.GetFromJsonAsync<JsonElement>("/api/v1/projects");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, write.StatusCode);
        Assert.Empty(list.EnumerateArray());
        Assert.Equal("Private", (await store.GetByIdAndOwnerAsync(id, (await store.GetByEmailAsync("ALICE@EXAMPLE.INVALID", default))!.Id, default))!.Name);
    }

    [Theory]
    [InlineData("GET", "/api/v1/profile")]
    [InlineData("GET", "/api/v1/projects")]
    [InlineData("POST", "/api/v1/projects")]
    [InlineData("GET", "/api/v1/dashboard/summary")]
    public async Task Protected_routes_reject_missing_and_forged_tokens(string method, string route)
    {
        await using var factory = CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();

        using var missing = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));
        var forged = new HttpRequestMessage(new HttpMethod(method), route);
        forged.Headers.Authorization = new("Bearer", ForgedToken());
        using var forgedResponse = await client.SendAsync(forged);

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
    }

    [Fact]
    public async Task Login_failures_are_indistinguishable()
    {
        await using var factory = CreateFactory(new InMemoryM1Store());
        using var client = factory.CreateClient();
        await RegisterAndLoginAsync(client, "bob@example.invalid");

        using var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "bob@example.invalid", password = "wrong-password-wrong-password" });
        using var unknownUser = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.invalid", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal(await wrongPassword.Content.ReadAsStringAsync().ContinueWith(t => Strip(t.Result)),
            Strip(await unknownUser.Content.ReadAsStringAsync()));
    }

    internal static async Task<string> RegisterAndLoginAsync(HttpClient client, string email)
    {
        using var register = await client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return await LoginAsync(client, email);
    }

    internal static async Task<string> LoginAsync(HttpClient client, string email)
    {
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static string ForgedToken()
    {
        // Structurally valid JWT signed with an unrelated key.
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "buildmind-tests", audience: "buildmind-tests-api",
            claims: [new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString("D"))],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256));
        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string Strip(string problem) =>
        System.Text.RegularExpressions.Regex.Replace(problem, "\"traceId\":\"[^\"]*\"", string.Empty);
}

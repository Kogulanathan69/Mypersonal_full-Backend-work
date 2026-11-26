using System.Net;
using System.Text.Json;
using BuildMind.API.Health;
using BuildMind.Infrastructure.Tests.Persistence;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// BE-M4-D04 shared security infrastructure: readiness (C11b), safe response headers and credential-free logs.
/// No database is contacted: the "enabled" cases point at port 1 on loopback, which refuses connections.
/// </summary>
public sealed class SecurityInfrastructureTests
{
    private const string Secret = SyntheticTestEndpoints.SyntheticSecret;

    private static readonly Dictionary<string, string?> UnreachableDatabase = new()
    {
        ["Database:Enabled"] = "true",
        ["ConnectionStrings:BuildMind"] = DatabaseOptionsTests.ValidConnection, // 127.0.0.1:1, VerifyFull, synthetic password
    };

    [Fact]
    public async Task Readiness_is_503_when_the_mandatory_database_is_not_configured()
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);
        var (status, checks) = await ReadReadinessAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Unhealthy", status);
        Assert.Equal(("Unhealthy", "not_configured"), checks["mysql"]);
        Assert.Equal(("Degraded", "not_configured"), checks["python"]);
        Assert.Equal(("Degraded", "not_configured"), checks["rag"]);
    }

    [Fact]
    public async Task Readiness_reports_an_unreachable_database_without_connection_details()
    {
        await using var factory = new BuildMindApiFactory("Production", UnreachableDatabase);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);
        var body = await response.Content.ReadAsStringAsync();
        var (_, checks) = await ReadReadinessAsync(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(("Unhealthy", "unreachable"), checks["mysql"]);
        foreach (var forbidden in new[] { Secret, "127.0.0.1", "bm_synthetic", "buildmind_synthetic", "Exception" })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        }

        // STEP 07-B regression: EF's ConnectionError event used to log the database name and server host on every
        // failed probe. No credential, host, user or database name may reach any log.
        Assert.All(factory.Logs.Entries, e =>
        {
            foreach (var forbidden in new[] { Secret, "127.0.0.1", "bm_synthetic", "buildmind_synthetic" })
            {
                Assert.DoesNotContain(forbidden, e.AllText, StringComparison.Ordinal);
            }
        });
    }

    [Fact]
    public async Task Repeated_readiness_probes_against_a_down_database_stay_unhealthy_and_quiet()
    {
        await using var factory = new BuildMindApiFactory("Production", UnreachableDatabase);
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync(Readiness.Path);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        Assert.DoesNotContain(factory.Logs.Entries, e => e.Category.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            && e.AllText.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    /// <summary>
    /// D04 originally asserted Python "unverified" (no client existed). Since D05, Python has a real check against
    /// its confirmed /health contract, so a configured but failing Python is "unreachable"; still Degraded, never
    /// Healthy. Since the M7 MVP, RAG also has a real check (contract v1): a configured but failing RAG is
    /// "unreachable" too (formerly "unverified" because no service existed).
    /// </summary>
    [Fact]
    public async Task Unverified_or_failing_internal_services_are_never_reported_healthy()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?>
            {
                ["Services:Python:Enabled"] = "true",
                ["Services:Python:BaseUrl"] = "https://python-services.internal:8000",
                ["Services:Rag:Enabled"] = "true",
                ["Services:Rag:BaseUrl"] = "https://rag.internal:8001",
                ["Services:Rag:AuthToken"] = "synthetic-internal-token-0123456789abcdef",
            });
        await using var host = factory.WithPythonTransport(StubHttpHandler.Throws(new HttpRequestException("synthetic connection refused")))
            .WithRagTransport(StubHttpHandler.Throws(new HttpRequestException("synthetic connection refused")));
        using var client = host.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);
        var (_, checks) = await ReadReadinessAsync(response);

        Assert.Equal(("Degraded", "unreachable"), checks["python"]);
        Assert.Equal(("Degraded", "unreachable"), checks["rag"]);
    }

    [Fact]
    public async Task Readiness_is_never_cached()
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);

        Assert.True(response.Headers.CacheControl?.NoStore == true || response.Headers.CacheControl?.NoCache == true);
    }

    [Fact]
    public async Task Liveness_stays_200_and_independent_of_readiness()
    {
        await using var factory = new BuildMindApiFactory("Production", UnreachableDatabase);
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health");
        using var ready = await client.GetAsync(Readiness.Path);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("{\"service\":\"buildmind-api\",\"status\":\"ok\"}", await live.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Theory]
    [InlineData("/health", false)]                                         // success, outside /api
    [InlineData("/api/v1/does-not-exist", true)]                           // status-code page (404 problem)
    [InlineData(SyntheticTestEndpoints.Prefix + "/throw", true)]           // exception handler (500 problem)
    [InlineData(SyntheticTestEndpoints.Prefix + "/current-user", true)]    // success under /api
    public async Task Safe_headers_are_present_on_success_and_error_responses(string path, bool apiPath)
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
        if (apiPath)
        {
            Assert.True(response.Headers.CacheControl?.NoStore, "API responses must not be stored.");
        }
    }

    [Fact]
    public async Task Server_header_is_disabled()
    {
        await using var factory = new BuildMindApiFactory("Production");
        _ = factory.Services;

        Assert.False(factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Tokens_cookies_and_query_credentials_never_reach_the_logs(string environment)
    {
        await using var factory = new BuildMindApiFactory(environment);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get, SyntheticTestEndpoints.Prefix + "/throw-with-segment/x?access_token=" + Secret);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Secret);
        request.Headers.TryAddWithoutValidation("Cookie", "session=" + Secret);
        request.Headers.TryAddWithoutValidation("X-Api-Key", Secret);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEmpty(factory.Logs.Entries); // the failure itself is logged (non-vacuous)
        Assert.All(factory.Logs.Entries, e => Assert.DoesNotContain(Secret, e.AllText, StringComparison.Ordinal));
        Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static string Single(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? Assert.Single(values) : throw new Xunit.Sdk.XunitException($"Missing {header}");

    private static async Task<(string Status, Dictionary<string, (string Status, string Reason)> Checks)> ReadReadinessAsync(HttpResponseMessage response) =>
        await ReadReadinessAsync(await response.Content.ReadAsStringAsync());

    private static Task<(string Status, Dictionary<string, (string Status, string Reason)> Checks)> ReadReadinessAsync(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var checks = root.GetProperty("checks").EnumerateArray().ToDictionary(
            c => c.GetProperty("name").GetString()!,
            c => (c.GetProperty("status").GetString()!, c.GetProperty("reason").GetString()!));
        return Task.FromResult((root.GetProperty("status").GetString()!, checks));
    }
}

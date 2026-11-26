using System.Net;
using BuildMind.API.Configuration;
using BuildMind.Infrastructure.Tests.Support;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// STEP 05 hosting decisions H1–H4 and H8: default AllowedHosts list, start-up validation outside Development,
/// no wildcards outside Development, Development-only "*" override, standard framework rejection response.
/// </summary>
public sealed class HostFilteringTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5080")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:8097")]   // CI full-stack smoke
    [InlineData("[::1]")]
    [InlineData("[::1]:5080")]
    public async Task Default_list_accepts_loopback_hosts_in_production(string host)
    {
        await using var factory = new ProductionApiFactory();

        using var response = await SendAsync(factory, "/health", host);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("localhost.evil.example")]
    [InlineData("127.0.0.2")]
    [InlineData("api.example.test")]
    public async Task Default_list_rejects_other_hosts_with_the_standard_minimal_response(string host)
    {
        await using var factory = new ProductionApiFactory();

        using var response = await SendAsync(factory, "/health", host);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(host, body, StringComparison.OrdinalIgnoreCase);            // host is not reflected
        Assert.DoesNotContain("buildmind", body, StringComparison.OrdinalIgnoreCase);     // nothing about the app
        // H8: the framework's fixed page (observed 2026-11-21: 334-byte static HTML "Bad Request - Invalid Hostname",
        // written before BuildMind's middleware, so it carries no BuildMind security headers). Static, no reflected
        // input and no application detail, so it is kept.
        Assert.Contains("Invalid Hostname", body, StringComparison.Ordinal);
        Assert.True(body.Length < 1024, "rejection body should stay a short static page");
        Assert.False(response.Headers.Contains("Server"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Rejected_host_never_reaches_cors_or_the_application()
    {
        await using var factory = new StartupSettingsApiFactory("Development", new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "localhost",
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
        });
        using var client = factory.CreateClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/projects");
        preflight.Headers.Host = "evil.example";
        preflight.Headers.Add("Origin", "http://localhost:4200");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        using var rejected = await client.SendAsync(preflight);

        using var swagger = await SendAsync(factory, "/swagger/v1/swagger.json", "evil.example");

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.False(rejected.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.BadRequest, swagger.StatusCode);
    }

    [Fact]
    public async Task Production_with_an_explicit_host_accepts_only_that_host()
    {
        await using var factory = new StartupSettingsApiFactory("Production",
            new Dictionary<string, string?> { ["AllowedHosts"] = "api.example.test" });

        using var listed = await SendAsync(factory, "/health", "api.example.test");
        using var upper = await SendAsync(factory, "/health", "API.EXAMPLE.TEST");
        using var loopback = await SendAsync(factory, "/health", "localhost");
        using var sub = await SendAsync(factory, "/health", "x.api.example.test");

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, upper.StatusCode);         // host names are case-insensitive
        Assert.Equal(HttpStatusCode.BadRequest, loopback.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sub.StatusCode);
    }

    [Theory]
    [InlineData("Production", "*", "unrestricted")]
    [InlineData("Production", "", "missing or empty")]
    [InlineData("Production", "   ", "missing or empty")]
    [InlineData("Production", "*.example.test", "wildcard")]
    [InlineData("Production", "localhost;*.example.test", "wildcard")]
    [InlineData("Production", "https://api.example.test", "host name only")]
    [InlineData("Production", "api.example.test:443", "port")]
    [InlineData("Production", "api.example.test/path", "host name only")]
    [InlineData("Production", "user@api.example.test", "host name only")]
    [InlineData("Production", "::1", "port")]
    [InlineData("Production", "[not-an-ip]", "bracketed IPv6")]
    [InlineData("Production", "bad_host name", "not a valid host")]
    [InlineData("Staging", "*", "unrestricted")]
    [InlineData("Testing", "*", "unrestricted")]
    public async Task Non_development_start_up_refuses_unsafe_allowed_hosts(string environment, string value, string expected)
    {
        await using var factory = new StartupSettingsApiFactory(environment,
            new Dictionary<string, string?> { ["AllowedHosts"] = value });

        var thrown = Record.Exception(() => factory.CreateClient());

        // Uses the repository's start-up race helper (see ExceptionChains): the original failure must be the
        // HostFilteringOptions validation, either thrown directly or recorded by the host.
        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(thrown, factory.Logs);
        Assert.Equal(typeof(Microsoft.AspNetCore.HostFiltering.HostFilteringOptions), validation.OptionsType);
        Assert.Contains(validation.Failures, f => f.Contains("AllowedHosts", StringComparison.Ordinal)
            && f.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_allows_an_explicit_wildcard_override()
    {
        await using var factory = new StartupSettingsApiFactory("Development",
            new Dictionary<string, string?> { ["AllowedHosts"] = "*" });

        using var any = await SendAsync(factory, "/health", "anything.example");

        Assert.Equal(HttpStatusCode.OK, any.StatusCode);
    }

    [Fact]
    public async Task Development_default_still_uses_the_loopback_list()
    {
        await using var factory = new BuildMindApiFactory("Development");

        using var local = await SendAsync(factory, "/health", "localhost");
        using var foreign = await SendAsync(factory, "/health", "evil.example");

        Assert.Equal(HttpStatusCode.OK, local.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("api.example.test", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("[::1]", true)]
    [InlineData("[2001:db8::1]", true)]
    [InlineData("*", false)]
    [InlineData("*.example.test", false)]
    [InlineData("api.*.test", false)]
    [InlineData("api.example.test:8080", false)]
    [InlineData("[::1]:80", false)]
    [InlineData("http://localhost", false)]
    [InlineData("", false)]
    public void Entry_rules(string entry, bool valid) =>
        Assert.Equal(valid, AllowedHostsValidation.IsExactHost(entry, out _));

    private static async Task<HttpResponseMessage> SendAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, string path, string host)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        return await client.SendAsync(request);
    }
}

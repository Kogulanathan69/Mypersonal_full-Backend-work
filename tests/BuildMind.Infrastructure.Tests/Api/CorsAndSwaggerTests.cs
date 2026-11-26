using System.Net;
using BuildMind.API.Security;
using BuildMind.Infrastructure.Tests.Support;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>STEP 05 Batch 2A (M4): exact-origin CORS allowlist and Development-only Swagger.</summary>
public sealed class CorsAndSwaggerTests
{
    private const string AllowedOrigin = "http://localhost:4200";

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    [InlineData("http://localhost:4200/")]
    [InlineData("http://localhost:4200/app")]
    [InlineData("http://localhost:4200?x=1")]
    [InlineData("http://user@localhost:4200")]
    [InlineData("localhost:4200")]
    [InlineData("")]
    [InlineData("ftp://localhost")]
    public void Invalid_origins_are_rejected(string origin)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => CorsConfiguration.NormalizeOrigins([origin], allowHttp: true));
        Assert.Contains(CorsConfiguration.AllowedOriginsKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Http_origins_are_rejected_outside_development()
    {
        Assert.Throws<InvalidOperationException>(
            () => CorsConfiguration.NormalizeOrigins([AllowedOrigin], allowHttp: false));
        Assert.Equal(["https://app.example.com"],
            CorsConfiguration.NormalizeOrigins(["https://app.example.com"], allowHttp: false));
    }

    [Fact]
    public void Origins_are_normalized_and_deduplicated()
    {
        Assert.Equal([AllowedOrigin, "https://app.example.com"],
            CorsConfiguration.NormalizeOrigins(
                [" http://LOCALHOST:4200 ", AllowedOrigin, "https://app.example.com:443"], allowHttp: true));
    }

    [Fact]
    public async Task Preflight_from_the_allowed_origin_is_answered_without_credentials()
    {
        await using var factory = CorsFactory();
        using var client = factory.CreateClient();

        using var request = Preflight(AllowedOrigin, "PATCH", "authorization,content-type");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, Single(response, "Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Contains("PATCH", Single(response, "Access-Control-Allow-Methods"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("http://localhost:4201")]
    public async Task Other_origins_receive_no_cors_grant(string origin)
    {
        await using var factory = CorsFactory();
        using var client = factory.CreateClient();

        using var preflight = await client.SendAsync(Preflight(origin, "GET", "authorization"));
        using var simple = new HttpRequestMessage(HttpMethod.Get, "/health");
        simple.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(simple);

        Assert.False(preflight.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Disallowed_methods_and_headers_are_not_granted()
    {
        await using var factory = CorsFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Preflight(AllowedOrigin, "DELETE", "x-custom"));

        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods")
            && Single(response, "Access-Control-Allow-Methods").Contains("DELETE", StringComparison.Ordinal));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Headers")
            && Single(response, "Access-Control-Allow-Headers").Contains("x-custom", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Without_configured_origins_no_cors_policy_exists(string environment)
    {
        await using var factory = new BuildMindApiFactory(environment);
        using var client = factory.CreateClient();

        using var simple = new HttpRequestMessage(HttpMethod.Get, "/health");
        simple.Headers.Add("Origin", AllowedOrigin);
        using var response = await client.SendAsync(simple);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Production_rejects_an_http_origin_at_start_up()
    {
        await using var factory = new StartupSettingsApiFactory("Production",
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = AllowedOrigin });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("must use https outside Development", error.ToString(), StringComparison.Ordinal);
    }

    private static StartupSettingsApiFactory CorsFactory() =>
        new("Development", new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = AllowedOrigin });

    [Fact]
    public async Task Invalid_configured_origin_fails_start_up()
    {
        await using var factory = new StartupSettingsApiFactory("Development",
            new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "*" });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Cors:AllowedOrigins", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Swagger_document_is_served_in_development_only()
    {
        await using var development = CorsFactory();
        await using var production = new ProductionApiFactory();

        using var devResponse = await development.CreateClient().GetAsync("/swagger/v1/swagger.json");
        using var prodResponse = await production.CreateClient().GetAsync("/swagger/v1/swagger.json");
        using var prodUi = await production.CreateClient().GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, devResponse.StatusCode);
        var document = await devResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"/health\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("securitySchemes", document, StringComparison.Ordinal); // auth disabled => no scheme
        Assert.Equal(HttpStatusCode.NotFound, prodResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, prodUi.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_served_in_development_with_the_security_headers()
    {
        await using var development = CorsFactory();

        using var ui = await development.CreateClient().GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Equal("text/html", ui.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Single(ui, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(ui, "X-Frame-Options"));
        Assert.DoesNotContain("api/v1", await ui.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static HttpRequestMessage Preflight(string origin, string method, string headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/projects");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", headers);
        return request;
    }

    private static string Single(HttpResponseMessage response, string header) =>
        string.Join(",", response.Headers.GetValues(header));
}

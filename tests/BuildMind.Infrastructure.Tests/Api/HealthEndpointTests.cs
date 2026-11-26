using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// Liveness contract: GET /health reports only that the API process is running.
/// It must not depend on MySQL, Python or RAG availability.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Api_starts_and_serves_requests_in_test_host()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.NotNull(_factory.Services);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Get_health_returns_200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_health_returns_liveness_contract()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        var propertyNames = root.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(new[] { "service", "status" }, propertyNames);
        Assert.Equal("buildmind-api", root.GetProperty("service").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
    }
}

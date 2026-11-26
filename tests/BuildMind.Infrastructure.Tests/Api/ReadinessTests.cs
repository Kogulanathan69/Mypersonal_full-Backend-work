using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// Readiness must be truthful. With the shipped configuration the mandatory database is not configured, so the
/// API must not report itself ready.
/// </summary>
/// <remarks>
/// BE-M4-D04 implemented readiness (<c>/health/ready</c>, design C11b). These original assertions still hold
/// (not successful while the database is not configured; liveness independent). The dependency-driven cases
/// (statuses, reasons, 503, no connection details) live in <c>SecurityInfrastructureTests</c>.
/// </remarks>
public sealed class ReadinessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReadinessTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Readiness_is_not_reported_while_dependency_checks_are_unimplemented()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.False(
            response.IsSuccessStatusCode,
            $"GET /health/ready returned {(int)response.StatusCode} but no dependency readiness checks exist yet.");
    }

    [Fact]
    public async Task Liveness_success_is_independent_of_readiness()
    {
        using var client = _factory.CreateClient();

        using var live = await client.GetAsync("/health");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.NotEqual(live.StatusCode, ready.StatusCode);
    }
}

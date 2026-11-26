using System.Net;
using BuildMind.API.Routing;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>Public API versioning foundation (decision C1).</summary>
public sealed class ApiVersioningTests
{
    [Fact]
    public void Approved_public_prefix_is_api_v1()
    {
        Assert.Equal("/api/v1", ApiRouteExtensions.ApiV1Prefix);
    }

    [Fact]
    public async Task Registered_modules_are_mapped_under_api_v1_only()
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.CreateClient();

        using var versioned = await client.GetAsync("/api/v1/__synthetic-tests/status/409");
        using var unversioned = await client.GetAsync("/__synthetic-tests/status/409");

        Assert.Equal(HttpStatusCode.Conflict, versioned.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unversioned.StatusCode);
    }

    [Fact]
    public async Task Health_remains_unversioned()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var root = await client.GetAsync("/health");
        using var versioned = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, root.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, versioned.StatusCode);
    }

    [Fact]
    public async Task No_product_endpoint_modules_are_registered_yet()
    {
        await using var factory = new WebApplicationFactory<Program>();

        Assert.Empty(factory.Services.GetServices<IApiEndpointModule>());
    }
}

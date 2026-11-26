using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Security.Claims;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Features.Dashboard.Abstractions;
using BuildMind.Application.Features.Dashboard.Contracts;
using BuildMind.Application.Features.Dashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildMind.Infrastructure.Tests;

public sealed class DashboardEndpointTests
{
    private const string Route = "/api/v1/dashboard/summary";

    private sealed class TestDashboardService(
        DashboardSummaryResult result)
        : IDashboardApplicationService
    {
        public int Calls { get; private set; }

        public Task<DashboardSummaryResult> GetSummaryAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options, logger, encoder)
    {
        protected override Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            if (Request.Headers["X-Dashboard-Test-Auth"] != "yes")
                return Task.FromResult(
                    AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity(
                new[]
                {
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        Guid.NewGuid().ToString())
                },
                Scheme.Name);

            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                Scheme.Name);

            return Task.FromResult(
                AuthenticateResult.Success(ticket));
        }
    }

    private static async Task<WebApplication> CreateAppAsync(
        TestDashboardService service)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "Testing"
            });

        builder.WebHost.UseTestServer();

        builder.Services
            .AddAuthentication("DashboardTest")
            .AddScheme<
                AuthenticationSchemeOptions,
                TestAuthHandler>(
                    "DashboardTest", _ => { });

        builder.Services.AddAuthorization();

        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails =
                ProblemDetailsDefaults.Apply);

        builder.Services
            .AddSingleton<IDashboardApplicationService>(service);

        var app = builder.Build();

        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        new DashboardEndpointModule()
            .MapEndpoints(app.MapGroup("/api/v1"));

        await app.StartAsync();

        return app;
    }

    private static DashboardSummaryResult CreateSuccess()
    {
        return DashboardSummaryResult.Available(
            new DashboardSummaryDto(
                new[]
                {
                    new DashboardProjectDto(
                        Guid.NewGuid(),
                        "Synthetic Project",
                        "Created")
                }));
    }

    [Fact]
    public async Task ReturnsDashboardForAuthenticatedCaller()
    {
        var service = new TestDashboardService(
            CreateSuccess());

        await using var app = await CreateAppAsync(service);
        using var client = app.GetTestClient();

        client.DefaultRequestHeaders.Add(
            "X-Dashboard-Test-Auth", "yes");

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(1, service.Calls);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            1,
            json.RootElement
                .GetProperty("totalProjects")
                .GetInt32());

        Assert.Single(
            json.RootElement
                .GetProperty("projects")
                .EnumerateArray());
    }

    [Fact]
    public async Task RejectsUnauthenticatedCaller()
    {
        var service = new TestDashboardService(
            CreateSuccess());

        await using var app = await CreateAppAsync(service);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync(Route);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task ReturnsEmptyDashboard()
    {
        var service = new TestDashboardService(
            DashboardSummaryResult.Available(
                new DashboardSummaryDto(
                    Array.Empty<DashboardProjectDto>())));

        await using var app = await CreateAppAsync(service);
        using var client = app.GetTestClient();

        client.DefaultRequestHeaders.Add(
            "X-Dashboard-Test-Auth", "yes");

        using var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            0,
            json.RootElement
                .GetProperty("totalProjects")
                .GetInt32());
    }

    [Theory]
    [InlineData(DashboardSummaryStatus.Unauthenticated, 401)]
    [InlineData(DashboardSummaryStatus.DependencyUnavailable, 503)]
    [InlineData(DashboardSummaryStatus.DependencyTimeout, 504)]
    [InlineData(DashboardSummaryStatus.InvalidDependencyResponse, 500)]
    public async Task MapsFailureToSafeHttpResponse(
        DashboardSummaryStatus status,
        int expectedStatus)
    {
        var service = new TestDashboardService(
            DashboardSummaryResult.Failure(status));

        await using var app = await CreateAppAsync(service);
        using var client = app.GetTestClient();

        client.DefaultRequestHeaders.Add(
            "X-Dashboard-Test-Auth", "yes");

        using var response = await client.GetAsync(Route);

        Assert.Equal(
            expectedStatus,
            (int)response.StatusCode);

        Assert.True(response.Headers.CacheControl?.NoStore);

        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(
            expectedStatus,
            json.RootElement
                .GetProperty("status")
                .GetInt32());

        Assert.Equal(
            ProblemDetailsDefaults.GetDefaultCode(expectedStatus),
            json.RootElement
                .GetProperty("code")
                .GetString());

        Assert.False(
            json.RootElement.TryGetProperty(
                "projects", out _));

        Assert.False(
            json.RootElement.TryGetProperty(
                "stackTrace", out _));
    }

    [Fact]
    public void RegistersDashboardDependencies()
    {
        var services = new ServiceCollection();

        services.AddDashboardFeature();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType ==
                typeof(IDashboardProjectSource) &&
            descriptor.ImplementationType ==
                typeof(DashboardProjectSource) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType ==
                typeof(IDashboardApplicationService) &&
            descriptor.ImplementationType ==
                typeof(DashboardApplicationService) &&
            descriptor.Lifetime == ServiceLifetime.Scoped);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType ==
                typeof(IApiEndpointModule) &&
            descriptor.ImplementationType ==
                typeof(DashboardEndpointModule) &&
            descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
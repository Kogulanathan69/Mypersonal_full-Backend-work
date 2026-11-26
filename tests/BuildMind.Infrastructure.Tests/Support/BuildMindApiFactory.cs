using BuildMind.API.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>
/// Hosts the real API with test-only synthetic endpoints, captured logs, an explicit environment and
/// optional in-memory configuration overrides.
/// </summary>
public class BuildMindApiFactory : WebApplicationFactory<Program>
{
    private readonly string _environment;
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public BuildMindApiFactory()
        : this("Development")
    {
    }

    public BuildMindApiFactory(string environment, IReadOnlyDictionary<string, string?>? settings = null)
    {
        _environment = environment;
        _settings = settings ?? new Dictionary<string, string?>();
    }

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        if (_settings.Count > 0)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
            services.AddSingleton<IApiEndpointModule, SyntheticTestEndpoints>());
    }
}

/// <summary>Shared Production-environment fixture.</summary>
public sealed class ProductionApiFactory() : BuildMindApiFactory("Production");

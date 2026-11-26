using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>
/// Hosts the real API with settings applied through <c>UseSetting</c>, so they are visible to the composition root
/// while it reads <c>builder.Configuration</c> before <c>Build()</c> (feature switches such as
/// <c>Authentication:Enabled</c> and <c>Cors:AllowedOrigins</c>). <see cref="BuildMindApiFactory"/> applies its
/// settings through <c>ConfigureAppConfiguration</c>, which minimal hosting only applies after those reads.
/// </summary>
public sealed class StartupSettingsApiFactory(
    string environment,
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? configureTestServices = null)
    : WebApplicationFactory<Program>
{
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        if (configureTestServices is not null)
        {
            builder.ConfigureTestServices(configureTestServices);
        }
    }
}

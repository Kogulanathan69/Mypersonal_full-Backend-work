using BuildMind.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildMind.Infrastructure;

/// <summary>
/// Infrastructure-layer service registration (adapters implementing Application ports),
/// called once by the API composition root.
/// </summary>
/// <remarks>
/// Owned by M4. Persistence (BE-M4-D02), internal service clients (BE-M4-D05) and storage adapters
/// are added in later approved slices. Configuration option types live in
/// <c>BuildMind.Infrastructure.Configuration</c>; their binding and start-up validation are wired in the
/// API composition root because this project does not reference the options-binding packages.
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Shared clock (BCL). Services take TimeProvider so tests can supply deterministic time.
        services.TryAddSingleton(TimeProvider.System);

        // BE-M4-D02a: DbContext + transaction helper. Disabled by default (Database:Enabled = false);
        // registration never connects, creates a database or applies migrations.
        services.AddBuildMindPersistence();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Application;

/// <summary>
/// Application-layer service registration, called once by the API composition root.
/// </summary>
/// <remarks>
/// Owned by M4 (shared composition). Feature owners add their use-case registrations through
/// this entry point in coordination with M4; no feature handlers exist yet (BE-M4-D01a).
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}

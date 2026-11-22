using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Projects.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildMind.Infrastructure.Persistence.Repositories.Projects;

public static class ProjectServiceRegistration
{
    public static IServiceCollection AddM1Projects(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TimeProvider>(
            TimeProvider.System);

        services.TryAddScoped<
            IProjectRepository,
            ProjectRepository>();

        services.TryAddScoped<ProjectService>();

        // STEP 05 decision 4: the atomic version writer is available to future authorization-changing operations.
        // ProjectOwnerAuthorizationPort (M3's IProjectAuthorizationPort) is intentionally NOT registered yet.
        services.TryAddScoped<
            Application.Features.Projects.Abstractions.IProjectAuthorizationVersionStore,
            ProjectAuthorizationVersionStore>();

        return services;
    }
}
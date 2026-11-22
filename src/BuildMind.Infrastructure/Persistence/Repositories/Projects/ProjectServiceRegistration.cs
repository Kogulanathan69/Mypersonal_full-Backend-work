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

        return services;
    }
}
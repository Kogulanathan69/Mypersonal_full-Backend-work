using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Application.Features.Users.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildMind.Infrastructure.Persistence.Repositories.Identity;

public static class ProfileServiceRegistration
{
    /// <summary>M1 profile feature: application service and its MySQL repository.</summary>
    public static IServiceCollection AddM1Profiles(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<
            IUserProfileRepository,
            UserProfileRepository>();

        services.TryAddScoped<ProfileService>();

        return services;
    }
}

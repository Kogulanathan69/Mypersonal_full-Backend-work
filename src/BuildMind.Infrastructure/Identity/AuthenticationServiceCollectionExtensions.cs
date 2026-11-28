using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.Services;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildMind.Infrastructure.Identity;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddM1Authentication(
        this IServiceCollection services,
        JwtOptions options,
        RefreshTokenOptions? refreshTokenOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();
        refreshTokenOptions ??= new RefreshTokenOptions();
        refreshTokenOptions.Validate();

        services.TryAddSingleton<JwtOptions>(options);
        services.TryAddSingleton(refreshTokenOptions);

        services.TryAddSingleton<TimeProvider>(
            TimeProvider.System);

        services.TryAddSingleton<
            IPasswordHashService,
            PasswordHashService>();

        services.TryAddSingleton<
            IAccessTokenIssuer,
            JwtAccessTokenIssuer>();

        services.TryAddScoped<
            IUserRepository,
            UserRepository>();

        // STEP 05 decisions 1-3: per-request account state, token versions and refresh-token sessions.
        services.TryAddScoped<IUserSessionStore, UserSessionStore>();
        services.TryAddScoped<IUserSecurityStateReader>(
            provider => provider.GetRequiredService<IUserSessionStore>());
        services.TryAddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.TryAddScoped<M1MutationSessionGuard>();

        // STEP 05 decision 7: reset-token persistence is ready, but PasswordRecoveryService and its endpoints stay
        // unregistered until an approved IPasswordResetEmailSender exists.
        services.TryAddScoped<IPasswordResetRepository, PasswordResetRepository>();
        services.TryAddSingleton<IPasswordResetTokenProvider, PasswordResetTokenProvider>();

        services.TryAddScoped<AuthenticationService>();
        services.TryAddScoped<SessionService>();

        return services;
    }
}

using BuildMind.API.Routing;
using BuildMind.Application.Features.Auth.Services;
using BuildMind.Infrastructure.Identity;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace BuildMind.API.Security;

public static class M1AuthenticationExtensions
{
    public static bool AddM1AuthenticationApi(
        this WebApplicationBuilder builder)
    {
        var enabled = builder.Configuration
            .GetValue<bool>("Authentication:Enabled");

        if (!enabled)
            return false;

        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Authentication activation is not approved.");
        }

        if (!builder.Configuration.GetValue<bool>(
            "Database:Enabled"))
        {
            throw new InvalidOperationException(
                "Authentication requires database access.");
        }

        var options = builder.Configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                "JWT configuration is missing.");

        options.Validate();

        // STEP 05 decision 3: refresh tokens stay off unless Authentication:RefreshTokens:Enabled is true.
        var refreshTokenOptions = builder.Configuration
            .GetSection(RefreshTokenOptions.SectionName)
            .Get<RefreshTokenOptions>() ?? new RefreshTokenOptions();
        refreshTokenOptions.Validate();

        builder.Services.AddM1Authentication(options, refreshTokenOptions);

        builder.Services
            .AddAuthentication(
                JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.SaveToken = false;
                jwt.IncludeErrorDetails = false;

                jwt.TokenValidationParameters =
                    JwtValidationParametersFactory.Create(options);

                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!JwtSubjectValidator.IsValid(
                            context.Principal))
                        {
                            context.Fail(
                                "Invalid authentication identity.");
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddAuthorization();

        // Rate limiting is owned by M4 (S4-B, RateLimitingConfiguration): the single global limiter plus the
        // auth-strict policy. M1 adds no limiter, policy or UseRateLimiter() of its own.

        // Profile and project features need the authenticated user, so they are registered only with JWT.
        builder.Services.AddM1Profiles();
        builder.Services.AddM1Projects();

        builder.Services.AddSingleton<
            IApiEndpointModule,
            AuthenticationEndpointModule>();

        builder.Services.AddSingleton<
            IApiEndpointModule,
            ProfileEndpointModule>();

        builder.Services.AddSingleton<
            IApiEndpointModule,
            ProjectEndpointModule>();

        // Session-management endpoints (refresh, logout, logout-all) are not part of the first milestone and stay
        // unregistered until Authentication:RefreshTokens:Enabled is approved. Token-version revocation itself is
        // always active (password reset and replay detection use it).
        if (refreshTokenOptions.Enabled)
        {
            builder.Services.AddSingleton<
                IApiEndpointModule,
                RefreshTokenEndpointModule>();

            builder.Services.AddSingleton<
                IApiEndpointModule,
                LogoutAllEndpointModule>();
        }

        // PROPOSAL (STEP 05 decision 7, not approved for activation): password recovery endpoints. Off unless
        // Authentication:PasswordRecovery:Enabled; refused outside Development because no email delivery is approved.
        // The sender never delivers and never records the token, so a Development run cannot leak reset tokens.
        if (builder.Configuration.GetValue<bool>(PasswordRecoveryEnabledKey))
        {
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Password recovery activation is not approved: no email delivery has been approved.");
            }

            builder.Services.AddScoped<PasswordRecoveryService>();
            builder.Services.AddSingleton<
                BuildMind.Application.Features.Auth.Abstractions.IPasswordResetEmailSender,
                NonDeliveringPasswordResetEmailSender>();
            builder.Services.AddSingleton<
                IApiEndpointModule,
                ProvisionalPasswordRecoveryEndpointModule>();
        }

        return true;
    }

    /// <summary>Proposal flag for the provisional recovery endpoints (default off; Development only).</summary>
    public const string PasswordRecoveryEnabledKey = "Authentication:PasswordRecovery:Enabled";
}
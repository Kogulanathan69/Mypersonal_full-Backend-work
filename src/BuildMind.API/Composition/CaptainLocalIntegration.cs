using BuildMind.API.Endpoints.PlanEditor;
using BuildMind.API.Endpoints.Requirements;
using BuildMind.API.Endpoints.Planning;
using BuildMind.API.Endpoints.Survey;
using BuildMind.Application.Features.Planning;
using BuildMind.API.Routing;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Services;
using BuildMind.Infrastructure.CaptainIntegration;

namespace BuildMind.API.Composition;

/// <summary>
/// Captain local-integration profile: a Development-only composition that registers implemented features whose
/// normal-host activation is still pending approval. Production activation is NOT approved.
/// </summary>
/// <remarks>
/// Guards (all fail closed at start-up):
/// <list type="bullet">
/// <item>off unless <see cref="EnabledKey"/> is true;</item>
/// <item>refused outside the Development environment;</item>
/// <item>refused unless M1 JWT authentication is active (which in turn requires the database);</item>
/// <item>every protected write uses the ratified project-row lock with an owner and authorization-version
/// re-check.</item>
/// </list>
/// The existing per-feature gates (for example <c>AddIsolatedRequirementsFeature</c>, which is Testing-only) are
/// unchanged and not used by this profile.
/// </remarks>
public static class CaptainLocalIntegration
{
    public const string EnabledKey = "CaptainIntegration:Enabled";

    /// <returns>True when the profile was registered.</returns>
    public static bool AddCaptainLocalIntegration(this WebApplicationBuilder builder, bool authenticationEnabled)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Configuration.GetValue<bool>(EnabledKey))
            return false;

        if (!builder.Environment.IsDevelopment())
            throw new InvalidOperationException(
                "The Captain local-integration profile is restricted to Development; production activation is not approved.");
        if (!authenticationEnabled)
            throw new InvalidOperationException(
                $"{EnabledKey} requires end-user authentication (Authentication:Enabled); protected features are never anonymous.");

        var services = builder.Services;
        services.AddCaptainIntegrationInfrastructure();

        // Requirements (M5 services, unchanged) over the protected repository adapter.
        services.AddScoped<RequirementRevisionReader>();
        services.AddScoped<ValidatedRequirementRevisionWriter>();
        services.AddScoped<IRequirementsApplicationService, RequirementsApplicationService>();
        services.AddScoped<IRequirementsSectionApplicationService, RequirementsSectionApplicationService>();
        services.AddSingleton<IApiEndpointModule, RequirementsEndpointModule>();
        services.AddSingleton<IApiEndpointModule, RequirementsSectionsEndpointModule>();

        // M2 survey upload and status (existing services and module).
        services.AddSurveyFeature();

        // Captain planning: land boundary, generation jobs, saving a generated candidate as a plan.
        services.AddScoped<PlanningAccess>();
        services.AddScoped<LandService>();
        services.AddScoped<GenerationService>();
        services.AddScoped<PlanSaveService>();
        services.AddSingleton<IRegulatoryEvidenceProvider, PolicyRegulatoryEvidenceProvider>();
        services.AddSingleton<IApiEndpointModule, PlanningEndpointModule>();

        // M3 revision history: the two read routes only.
        services.AddSingleton<IApiEndpointModule, PlanRevisionHistoryEndpointModule>();

        return true;
    }
}

/// <summary>Maps only the existing revision-history GET handlers of the M3 module.</summary>
public sealed class PlanRevisionHistoryEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        PlanEditorEndpointModule.MapRevisionHistoryEndpoints(apiV1);
    }
}

using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Infrastructure.Persistence.Repositories.PlanEditor;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Infrastructure adapters for the Captain local-integration profile. The API composition root calls this only
/// after the Development-only guard has passed. Production composition never calls it.
/// </summary>
public static class CaptainIntegrationInfrastructure
{
    public static IServiceCollection AddCaptainIntegrationInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Shared fresh owner-scoped authorization read (Option A, C-01 A1/A2/A8).
        services.TryAddScoped<IProjectAuthorizationPort, ProjectOwnerAuthorizationPort>();

        // M3 revision history read side (existing MySQL adapter).
        services.TryAddScoped<IPlanRevisionHistoryQueryPort, MySqlPlanRevisionHistoryQuery>();
        services.TryAddScoped<IPlanRevisionHistoryService, PlanRevisionHistoryService>();

        // Requirements: every write uses the protected atomic path; the policy comes from an operator-supplied file.
        services.TryAddScoped<RequirementRevisionRepository>();
        services.Replace(ServiceDescriptor.Scoped<IRequirementRevisionRepository, OwnerProtectedRequirementRevisionRepository>());
        services.TryAddSingleton<IRequirementCatalogueProvider, ConfiguredRequirementCatalogueProvider>();

        return services;
    }
}

using BuildMind.API.Routing;
using BuildMind.Application.Features.Survey;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.API.Endpoints.Survey;

public static class SurveyFeatureRegistration
{
    // Register only after production storage, repository, and ownership dependencies are ready.
    public static IServiceCollection AddSurveyFeature(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSurveyApplication();

        services.AddSingleton<
            IApiEndpointModule,
            SurveyEndpointModule>();

        return services;
    }
}

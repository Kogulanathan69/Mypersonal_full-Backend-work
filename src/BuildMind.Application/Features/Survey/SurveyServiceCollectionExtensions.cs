using BuildMind.Application.Features.Survey.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Application.Features.Survey;

public static class SurveyServiceCollectionExtensions
{
    public static IServiceCollection AddSurveyApplication(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<SurveyUploadValidator>();
        services.AddScoped<SurveyUploadService>();
        services.AddScoped<SurveyStatusService>();

        return services;
    }
}

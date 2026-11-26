using BuildMind.API.Errors;
using BuildMind.Application.Features.Dashboard.Abstractions;
using BuildMind.Application.Features.Dashboard.Contracts;
using BuildMind.Application.Features.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildMind.API.Routing;

public sealed class DashboardEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);

        apiV1.MapGet("/dashboard/summary", GetSummaryAsync)
            .WithName("GetCustomerDashboardSummary")
            .WithTags("Dashboard")
            .RequireAuthorization();
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        [FromServices] IDashboardApplicationService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        var result = await service
            .GetSummaryAsync(cancellationToken);

        return result.Status switch
        {
            DashboardSummaryStatus.Available =>
                Results.Ok(result.Summary),

            DashboardSummaryStatus.Unauthenticated =>
                CreateProblem(401, "Authentication is required."),

            DashboardSummaryStatus.DependencyUnavailable =>
                CreateProblem(503, "Dashboard service is unavailable."),

            DashboardSummaryStatus.DependencyTimeout =>
                CreateProblem(504, "Dashboard request timed out."),

            _ => CreateProblem(500,
                ProblemDetailsDefaults.InternalErrorDetail)
        };
    }

    private static IResult CreateProblem(
        int status, string detail)
    {
        var code = ProblemDetailsDefaults.GetDefaultCode(status)!;

        return Results.Problem(
            statusCode: status,
            detail: detail,
            type: ProblemDetailsDefaults.GetTypeForCode(code),
            extensions: new Dictionary<string, object?>
            {
                [ProblemDetailsDefaults.CodeExtension] = code
            });
    }
}

public static class DashboardServiceRegistration
{
    // Register only after production dependencies are ready.
    public static IServiceCollection AddDashboardFeature(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<
            IDashboardProjectSource,
            DashboardProjectSource>();

        services.AddScoped<
            IDashboardApplicationService,
            DashboardApplicationService>();

        services.AddSingleton<
            IApiEndpointModule,
            DashboardEndpointModule>();

        return services;
    }
}
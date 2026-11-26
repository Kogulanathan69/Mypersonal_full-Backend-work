using System.Text.Json;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Services;

namespace BuildMind.API.Endpoints.Requirements;

/// <summary>Isolated D01-D08 module. Never registered by the production composition root.</summary>
public sealed class RequirementsEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var group = apiV1.MapGroup("/projects/{id}/requirements").WithTags("Requirements").RequireAuthorization();
        group.MapGet("", GetAsync).WithName("GetProjectRequirements").Produces(200).Produces(204);
        group.MapGet("/operations/{operationId}", RequirementMutationHttp.ObserveAsync)
            .WithName("ObserveRequirementMutation").Produces(200).ProducesProblem(404).ProducesProblem(503);
        group.MapPut("", PutAsync).WithName("ReplaceProjectRequirements").Accepts<PutRequirementsHttpRequest>("application/json").Produces(200);
    }

    private static async Task<IResult> GetAsync(string id, HttpContext context, IRequirementsApplicationService service, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(id, out var project) || project == Guid.Empty) return InvalidProject();
        return Map(await service.GetAsync(project, cancellationToken), project);
    }

    private static async Task<IResult> PutAsync(string id, HttpContext context, IRequirementsApplicationService service, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(id, out var project) || project == Guid.Empty) return InvalidProject();
        if (!context.Request.HasJsonContentType()) return Problem(415);
        if (context.Request.Headers.ContainsKey("Idempotency-Key"))
            return await RequirementMutationHttp.PutAsync<PutRequirementsHttpRequest>(project, context, cancellationToken);
        PutRequirementsHttpRequest? request;
        try { request = await context.Request.ReadFromJsonAsync<PutRequirementsHttpRequest>(cancellationToken); }
        catch (JsonException) { return ApiProblems.ValidationFailed(new Dictionary<string, string[]> { ["request"] = ["A complete valid JSON snapshot is required."] }); }
        if (request is null) return ApiProblems.ValidationFailed(new Dictionary<string, string[]> { ["request"] = ["A complete snapshot is required."] });
        var errors = request.GetStructuralErrors();
        if (errors.Count > 0) return ApiProblems.ValidationFailed(errors);
        return Map(await service.SaveAsync(project, request.ToApplicationRequest(), cancellationToken), project, save: true);
    }

    internal static IResult InvalidProject() => ApiProblems.ValidationFailed(new Dictionary<string, string[]> { ["id"] = ["A nonempty project identity is required."] });

    internal static IResult Map(RequirementsOperationResult result, Guid projectId, bool save = false)
    {
        if (result.Status == RequirementsOperationStatus.Success && result.Revision!.ProjectId != projectId)
            return Problem(500);
        if (!save && result.Status is RequirementsOperationStatus.RevisionConflict
                or RequirementsOperationStatus.CatalogueConflict or RequirementsOperationStatus.ValidationFailed)
            return Problem(500);
        return result.Status switch
        {
            RequirementsOperationStatus.Success => Results.Ok(result.Revision),
            RequirementsOperationStatus.NoSavedRevision => save ? Problem(500) : Results.NoContent(),
            RequirementsOperationStatus.Unauthenticated => Problem(401),
            RequirementsOperationStatus.NotFound => Problem(404),
            RequirementsOperationStatus.RevisionConflict => Problem(409, "REVISION_CONFLICT"),
            RequirementsOperationStatus.CatalogueConflict => Problem(409),
            RequirementsOperationStatus.ValidationFailed => ApiProblems.DomainRuleViolation("VALIDATION_FAILED", "Requirements violate the supplied policy.",
                new Dictionary<string, object?> { ["errors"] = result.Issues.GroupBy(item => item.Field)
                    .ToDictionary(group => group.Key, group => group.Select(item => new { item.Code, item.Message }).ToArray()) }),
            RequirementsOperationStatus.DependencyUnavailable => Problem(503),
            RequirementsOperationStatus.DependencyTimeout => Problem(504),
            _ => Problem(500)
        };
    }

    internal static IResult Problem(int status, string? code = null)
    {
        code ??= ProblemDetailsDefaults.GetDefaultCode(status)!;
        return Results.Problem(statusCode: status, type: ProblemDetailsDefaults.GetTypeForCode(code),
            detail: status == 500 ? ProblemDetailsDefaults.InternalErrorDetail : null,
            extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
    }
}

public static class RequirementsFeatureRegistration
{
    /// <summary>Fail-closed opt-in for Testing hosts only. No production dependency implementation is supplied.</summary>
    public static IServiceCollection AddIsolatedRequirementsFeature(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        if (!environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Requirements registration is restricted to isolated Testing hosts pending atomic ownership verification.");
        services.AddScoped<RequirementRevisionReader>();
        services.AddScoped<ValidatedRequirementRevisionWriter>();
        services.AddScoped<IRequirementsApplicationService, RequirementsApplicationService>();
        services.AddScoped<IRequirementsSectionApplicationService, RequirementsSectionApplicationService>();
        services.AddSingleton<IApiEndpointModule, RequirementsEndpointModule>();
        services.AddSingleton<IApiEndpointModule, RequirementsSectionsEndpointModule>();
        return services;
    }
}

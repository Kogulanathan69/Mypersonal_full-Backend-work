using System.Text.Json;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.API.Endpoints.Requirements;

/// <summary>Provisional isolated Day 04 routes; production activation remains prohibited.</summary>
public sealed class RequirementsSectionsEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var group = apiV1.MapGroup("/projects/{id}/requirements").WithTags("Requirements").RequireAuthorization();
        group.MapGet("/spaces", GetAsync).WithName("GetRequirementSpaces").Produces(200).Produces(204);
        group.MapGet("/relationships", GetAsync).WithName("GetRequirementRelationships").Produces(200).Produces(204);
        // Provisional summary is the complete persisted DTO, not a readiness certification.
        group.MapGet("/summary", GetAsync).WithName("GetRequirementsSummary").Produces(200).Produces(204);
        apiV1.MapPut("/projects/{id}/design-method", PutDesignMethodAsync)
            .WithTags("Requirements").RequireAuthorization().WithName("ReplaceRequirementDesignMethod").Accepts<PutDesignMethodHttpRequest>("application/json").Produces(200);
        group.MapPut("/spaces", PutSpacesAsync).WithName("ReplaceRequirementSpaces").Accepts<PutAdditionalSpacesHttpRequest>("application/json").Produces(200);
        group.MapPut("/relationships", PutRelationshipsAsync).WithName("ReplaceRequirementRelationships").Accepts<PutRoomRelationshipsHttpRequest>("application/json").Produces(200);
    }

    private static async Task<IResult> GetAsync(string id, HttpContext context, IRequirementsSectionApplicationService service, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(id, out var project) || project == Guid.Empty) return RequirementsEndpointModule.InvalidProject();
        return RequirementsEndpointModule.Map(await service.GetAsync(project, cancellationToken), project);
    }

    private static Task<IResult> PutDesignMethodAsync(string id, HttpContext context, IRequirementsSectionApplicationService service, CancellationToken token) =>
        PutAsync<PutDesignMethodHttpRequest>(id, context,
            (project, request, cancellation) => service.SaveDesignMethodAsync(project, request.ToApplicationRequest(), cancellation), token);

    private static Task<IResult> PutSpacesAsync(string id, HttpContext context, IRequirementsSectionApplicationService service, CancellationToken token) =>
        PutAsync<PutAdditionalSpacesHttpRequest>(id, context,
            (project, request, cancellation) => service.SaveSpacesAsync(project, request.ToApplicationRequest(), cancellation), token);

    private static Task<IResult> PutRelationshipsAsync(string id, HttpContext context, IRequirementsSectionApplicationService service, CancellationToken token) =>
        PutAsync<PutRoomRelationshipsHttpRequest>(id, context,
            (project, request, cancellation) => service.SaveRelationshipsAsync(project, request.ToApplicationRequest(), cancellation), token);

    private static async Task<IResult> PutAsync<T>(string id, HttpContext context,
        Func<Guid, T, CancellationToken, Task<RequirementsOperationResult>> save, CancellationToken token) where T : PutRequirementsSectionHttpRequest
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(id, out var project) || project == Guid.Empty) return RequirementsEndpointModule.InvalidProject();
        if (!context.Request.HasJsonContentType()) return RequirementsEndpointModule.Problem(415);
        if (context.Request.Headers.ContainsKey("Idempotency-Key"))
            return await RequirementMutationHttp.PutAsync<T>(project, context, token);
        T? request;
        try { request = await context.Request.ReadFromJsonAsync<T>(token); }
        catch (JsonException) { return InvalidBody(); }
        if (request is null) return InvalidBody();
        var errors = request.GetStructuralErrors();
        if (errors.Count > 0) return ApiProblems.ValidationFailed(errors);
        return RequirementsEndpointModule.Map(await save(project, request, token), project, save: true);
    }

    private static IResult InvalidBody() => ApiProblems.ValidationFailed(
        new Dictionary<string, string[]> { ["request"] = ["An explicitly supplied valid section snapshot is required."] });
}

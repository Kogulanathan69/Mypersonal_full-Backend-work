using BuildMind.API.Errors;
using BuildMind.Application.Features.Projects.DTOs;
using BuildMind.Application.Features.Projects.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildMind.API.Routing;

public sealed class ProjectEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        var projects = apiV1.MapGroup("/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        projects.MapPost("", CreateAsync);
        projects.MapGet("", ListAsync);
        projects.MapGet("/{id:guid}", GetAsync);
        projects.MapPatch("/{id:guid}", UpdateAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        HttpContext context,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        var result = await service.CreateAsync(
            request, cancellationToken);

        if (result.Status == ProjectOperationStatus.Success)
        {
            return Results.Created(
                $"/api/v1/projects/{result.Value!.Id}",
                result.Value);
        }

        return MapResult(result);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        var result = await service.ListAsync(
            cancellationToken);

        return MapResult(result);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        HttpContext context,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        var result = await service.GetAsync(
            id, cancellationToken);

        return MapResult(result);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateProjectRequest request,
        HttpContext context,
        [FromServices] ProjectService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        var result = await service.UpdateAsync(
            id, request, cancellationToken);

        return MapResult(result);
    }

    private static IResult MapResult<T>(
        ProjectOperationResult<T> result)
        where T : class
    {
        return result.Status switch
        {
            ProjectOperationStatus.Success =>
                Results.Ok(result.Value),

            ProjectOperationStatus.Unauthenticated =>
                CreateProblem(401, "Authentication is required."),

            ProjectOperationStatus.ValidationFailed =>
                CreateProblem(400, "Invalid project request."),

            ProjectOperationStatus.NotFound =>
                CreateProblem(404, "Project not found."),

            ProjectOperationStatus.RevisionConflict =>
                CreateProblem(409, "Project revision conflict."),

            _ => CreateProblem(500, "An internal error occurred.")
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
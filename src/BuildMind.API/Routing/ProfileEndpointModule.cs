using BuildMind.API.Errors;
using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Application.Features.Users.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildMind.API.Routing;

public sealed class ProfileEndpointModule
    : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        var profile = apiV1.MapGroup("/profile")
            .WithTags("Profile")
            .RequireAuthorization();

        profile.MapGet("", GetAsync);
        profile.MapPut("", UpdateAsync);
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        [FromServices] ProfileService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        return MapResult(await service.GetAsync(
            cancellationToken));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateProfileRequest request,
        HttpContext context,
        [FromServices] ProfileService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        return MapResult(await service.UpdateAsync(
            request, cancellationToken));
    }

    private static IResult MapResult(
        ProfileOperationResult result)
    {
        return result.Status switch
        {
            ProfileOperationStatus.Success =>
                Results.Ok(result.Value),

            ProfileOperationStatus.Unauthenticated =>
                CreateProblem(401, "Authentication is required."),

            ProfileOperationStatus.ValidationFailed =>
                CreateProblem(400, "Invalid profile request."),

            ProfileOperationStatus.NotFound =>
                CreateProblem(404, "Profile not found."),

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
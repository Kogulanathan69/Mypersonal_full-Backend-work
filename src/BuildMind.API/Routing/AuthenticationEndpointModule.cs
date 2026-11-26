using BuildMind.API.Errors;
using BuildMind.API.Security;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildMind.API.Routing;

public sealed class AuthenticationEndpointModule
    : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        var auth = apiV1.MapGroup("/auth")
            .WithTags("Authentication");

        auth.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingConfiguration.AuthStrictPolicy).Produces(201);

        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingConfiguration.AuthStrictPolicy);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext context,
        [FromServices] AuthenticationService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        var result = await service.RegisterAsync(
            request, cancellationToken);

        return result is null
            ? CreateProblem(400, "Registration could not be completed.")
            : Results.Json(result, statusCode: 201);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        [FromServices] SessionService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";

        var result = await service.LoginAsync(
            request, cancellationToken);

        return result is null
            ? CreateProblem(401, "Authentication failed.")
            : Results.Ok(result);
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
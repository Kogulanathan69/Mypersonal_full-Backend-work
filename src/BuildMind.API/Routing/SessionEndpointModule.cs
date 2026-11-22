using BuildMind.API.Errors;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildMind.API.Routing;

/// <summary>Logout-all (always with authentication): revokes every access and refresh token of the caller.</summary>
public sealed class LogoutAllEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        apiV1.MapPost("/auth/logout-all", LogoutAllAsync)
            .WithTags("Authentication")
            .RequireAuthorization();
    }

    private static async Task<IResult> LogoutAllAsync(
        HttpContext context,
        [FromServices] ICurrentUser currentUser,
        [FromServices] SessionService sessions,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        await sessions.LogoutAllAsync(userId, cancellationToken);
        return Results.NoContent();
    }
}

/// <summary>
/// Refresh-token endpoints. Registered only when <c>Authentication:RefreshTokens:Enabled</c> is true
/// (STEP 05 decision 3: not activated until approved).
/// </summary>
public sealed class RefreshTokenEndpointModule : IApiEndpointModule
{

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        var auth = apiV1.MapGroup("/auth").WithTags("Authentication");

        auth.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous();

        auth.MapPost("/logout", LogoutAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> RefreshAsync(
        RefreshTokenRequest request,
        HttpContext context,
        [FromServices] SessionService sessions,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        var result = await sessions.RefreshAsync(request.RefreshToken, cancellationToken);
        return result is null
            ? Problem(401, "The session could not be refreshed.")
            : Results.Ok(result);
    }

    private static async Task<IResult> LogoutAsync(
        RefreshTokenRequest request,
        HttpContext context,
        [FromServices] ICurrentUser currentUser,
        [FromServices] SessionService sessions,
        CancellationToken cancellationToken)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return Results.Unauthorized();

        // Same response whether or not the token existed or belonged to the caller.
        await sessions.LogoutAsync(userId, request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static IResult Problem(int status, string detail)
    {
        var code = ProblemDetailsDefaults.GetDefaultCode(status)!;
        return Results.Problem(statusCode: status, detail: detail, type: ProblemDetailsDefaults.GetTypeForCode(code),
            extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
    }
}

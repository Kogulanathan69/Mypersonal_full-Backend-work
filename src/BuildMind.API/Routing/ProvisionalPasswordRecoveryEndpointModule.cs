using BuildMind.API.Errors;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Services;
using Microsoft.AspNetCore.Mvc;

namespace BuildMind.API.Routing;

/// <summary>
/// M1 recovery HTTP draft. Register only in isolated tests until the response contract,
/// rate limiting, reset persistence and email delivery are agreed.
/// </summary>
public sealed class ProvisionalPasswordRecoveryEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        var recovery = apiV1.MapGroup("/auth");
        recovery.MapPost("/forgot-password", ForgotAsync).AllowAnonymous()
            .RequireRateLimiting(BuildMind.API.Security.RateLimitingConfiguration.AuthStrictPolicy).Produces(202);
        recovery.MapPost("/reset-password", ResetAsync).AllowAnonymous()
            .RequireRateLimiting(BuildMind.API.Security.RateLimitingConfiguration.AuthStrictPolicy)
            .Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> ForgotAsync(
        ForgotPasswordRequest request,
        HttpContext context,
        [FromServices] PasswordRecoveryService recovery,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var message = await recovery.ForgotPasswordAsync(request, cancellationToken);
        return Results.Json(new { message }, statusCode: StatusCodes.Status202Accepted);
    }

    private static async Task<IResult> ResetAsync(
        ResetPasswordRequest request,
        HttpContext context,
        [FromServices] PasswordRecoveryService recovery,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (await recovery.ResetPasswordAsync(request, cancellationToken))
            return Results.NoContent();

        const int status = StatusCodes.Status400BadRequest;
        var code = ProblemDetailsDefaults.GetDefaultCode(status)!;
        return Results.Problem(
            statusCode: status,
            detail: "Password reset could not be completed.",
            type: ProblemDetailsDefaults.GetTypeForCode(code),
            extensions: new Dictionary<string, object?> { [ProblemDetailsDefaults.CodeExtension] = code });
    }
}

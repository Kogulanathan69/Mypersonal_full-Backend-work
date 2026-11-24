using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Administration;
using A = BuildMind.API.Endpoints.Administration.AdministrationEndpointModule;
using P = BuildMind.API.Endpoints.Planning.PlanningEndpointModule;

namespace BuildMind.API.Endpoints.Administration;

/// <summary>
/// The caller's own account security: password change (revokes every session), email verification (single-use emailed
/// code, recorded but not enforced for login until a policy is approved) and security-event history.
/// </summary>
public sealed class AccountSecurityEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);
        var me = apiV1.MapGroup("/users/me").WithTags("Account security").RequireAuthorization();
        me.MapPost("/password", ChangePasswordAsync).RequireRateLimiting(RateLimitingConfiguration.AuthStrictPolicy);
        me.MapGet("/security", async (HttpContext c, ICurrentUser u, IAccountSecurityStore s, CancellationToken t) =>
        {
            c.Response.Headers.CacheControl = "no-store";
            return u.UserId is { } id ? Results.Ok(await s.GetStatusAsync(id, t)) : A.Problem(401, "UNAUTHENTICATED");
        });
        me.MapPost("/email-verification", RequestVerificationAsync).RequireRateLimiting(RateLimitingConfiguration.AuthStrictPolicy);
        me.MapPost("/email-verification/confirm", ConfirmVerificationAsync).RequireRateLimiting(RateLimitingConfiguration.AuthStrictPolicy);
        me.MapGet("/security-events", async (HttpContext c, ICurrentUser u, IAccountSecurityStore s, int? page, int? pageSize, CancellationToken t) =>
        {
            c.Response.Headers.CacheControl = "no-store";
            if (u.UserId is not { } id) return A.Problem(401, "UNAUTHENTICATED");
            var result = await s.ListEventsAsync(id, PageRequest.Normalize(page, pageSize, null, null, null), t);
            return Results.Ok(new { items = result.Items, page = result.Page, pageSize = result.PageSize, total = result.Total });
        });
    }

    private static IResult Map(AccountSecurityOutcome outcome, int success) => outcome switch
    {
        AccountSecurityOutcome.Done => Results.StatusCode(success),
        AccountSecurityOutcome.Unauthenticated => A.Problem(401, "UNAUTHENTICATED"),
        AccountSecurityOutcome.CurrentPasswordIncorrect => A.Problem(422, "CURRENT_PASSWORD_INCORRECT"),
        AccountSecurityOutcome.Conflict => A.Problem(409, "PASSWORD_CHANGED_CONCURRENTLY"),
        AccountSecurityOutcome.InvalidCode => A.Problem(422, "VERIFICATION_CODE_INVALID"),
        AccountSecurityOutcome.AlreadyVerified => A.Problem(409, "EMAIL_ALREADY_VERIFIED"),
        AccountSecurityOutcome.EmailUnavailable => A.Problem(503, "EMAIL_DELIVERY_UNAVAILABLE"),
        _ => A.Problem(400, "VALIDATION_FAILED"),
    };

    private static async Task<IResult> ChangePasswordAsync(HttpContext c, AccountSecurityService service, CancellationToken t)
    {
        c.Response.Headers.CacheControl = "no-store";
        var body = await P.ReadAsync<ChangePasswordRequest>(c, t);
        if (body.Failure is not null) return body.Failure;
        var r = body.Value!;
        return Map(await service.ChangePasswordAsync(r.CurrentPassword, r.NewPassword, r.ConfirmPassword, t), 204);
    }

    private static async Task<IResult> RequestVerificationAsync(HttpContext c, AccountSecurityService service, CancellationToken t)
    {
        c.Response.Headers.CacheControl = "no-store";
        return Map(await service.RequestVerificationAsync(t), 202);
    }

    private static async Task<IResult> ConfirmVerificationAsync(HttpContext c, AccountSecurityService service, CancellationToken t)
    {
        c.Response.Headers.CacheControl = "no-store";
        var body = await P.ReadAsync<VerificationCodeRequest>(c, t);
        if (body.Failure is not null) return body.Failure;
        return Map(await service.ConfirmVerificationAsync(body.Value!.Code, t), 204);
    }
}

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword, string? ConfirmPassword);
public sealed record VerificationCodeRequest(string? Code);

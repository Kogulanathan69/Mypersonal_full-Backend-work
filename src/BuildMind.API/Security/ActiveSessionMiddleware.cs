using System.Globalization;
using System.Security.Claims;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Infrastructure.Identity;

namespace BuildMind.API.Security;

/// <summary>
/// STEP 05 decisions 1 and 2: after JWT validation, every authenticated request is checked against the account's
/// current state with one primary-key lookup. A disabled account, a missing account, or a token whose version no
/// longer matches (password reset, logout-all, replay, disabling) is treated as unauthenticated, so protected
/// endpoints answer 401 and anonymous endpoints are unaffected.
/// </summary>
/// <remarks>
/// Dependency failures are not converted into 401: the exception reaches the global handler, which answers
/// 503/504 for database unavailability. Either way the protected resource is never served.
/// </remarks>
internal sealed class ActiveSessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUserSecurityStateReader accounts)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated == true)
        {
            if (!TryReadSession(principal, out var userId, out var tokenVersion))
            {
                Reject(context);
            }
            else
            {
                var state = await accounts.GetAsync(userId, context.RequestAborted);
                if (state is null || !state.IsActive || state.TokenVersion != tokenVersion)
                {
                    Reject(context);
                }
            }
        }

        await next(context);
    }

    internal static bool TryReadSession(ClaimsPrincipal principal, out Guid userId, out long tokenVersion)
    {
        userId = Guid.Empty;
        tokenVersion = 0;

        var versions = principal.FindAll(JwtAccessTokenIssuer.TokenVersionClaim).ToArray();
        return JwtSubjectValidator.IsValid(principal)
            && Guid.TryParse(principal.FindFirst("sub")!.Value, out userId)
            && versions.Length == 1
            && long.TryParse(versions[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out tokenVersion)
            && tokenVersion >= 1;
    }

    private static void Reject(HttpContext context) =>
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
}

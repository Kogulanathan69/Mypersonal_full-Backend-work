using Microsoft.Net.Http.Headers;

namespace BuildMind.API.Security;

/// <summary>
/// Safe response defaults for a JSON API (BE-M4-D04). Registered first in the pipeline, so the headers are also
/// present on Problem Details error responses and status-code pages. CORS is NOT configured here (C6 pending).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers[HeaderNames.XContentTypeOptions] = "nosniff";
            headers[HeaderNames.XFrameOptions] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            // Owner-scoped API data must not be stored by browsers or shared caches unless an endpoint opts in.
            if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && !headers.ContainsKey(HeaderNames.CacheControl))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}

using System.Diagnostics;

namespace BuildMind.API.Errors;

/// <summary>Resolves the trace identifier shared by error responses and operational logs.</summary>
/// <remarks>
/// Uses the W3C trace ID of the current <see cref="Activity"/> when one exists (propagated from an
/// incoming <c>traceparent</c> header), otherwise the request's <see cref="HttpContext.TraceIdentifier"/>.
/// A trace ID is a correlation value only — never authentication or authorization evidence.
/// </remarks>
public static class TraceIdentifiers
{
    public static string Get(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var activity = Activity.Current;
        if (activity is not null && activity.IdFormat == ActivityIdFormat.W3C && activity.TraceId != default)
        {
            return activity.TraceId.ToHexString();
        }

        return httpContext.TraceIdentifier;
    }
}

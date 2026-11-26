using BuildMind.Application.Abstractions.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BuildMind.API.Errors;

/// <summary>
/// BuildMind RFC 9457 Problem Details conventions (decisions C3/C5): every problem response carries
/// <c>type</c>, <c>title</c>, <c>status</c>, <c>instance</c>, <c>code</c> and <c>traceId</c>.
/// </summary>
public static class ProblemDetailsDefaults
{
    public const string TypePrefix = "urn:buildmind:problem:";
    public const string CodeExtension = "code";
    public const string TraceIdExtension = "traceId";

    private static readonly string[] UnsafeExtensionKeys = ["exception", "stackTrace", "stack", "innerException"];

    private static readonly Dictionary<int, string> CodesByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = SharedErrorCodes.MalformedRequest,
        [StatusCodes.Status401Unauthorized] = SharedErrorCodes.Unauthenticated,
        [StatusCodes.Status403Forbidden] = SharedErrorCodes.Forbidden,
        [StatusCodes.Status404NotFound] = SharedErrorCodes.NotFound,
        [StatusCodes.Status409Conflict] = SharedErrorCodes.Conflict,
        [StatusCodes.Status413PayloadTooLarge] = SharedErrorCodes.PayloadTooLarge,
        [StatusCodes.Status415UnsupportedMediaType] = SharedErrorCodes.UnsupportedMediaType,
        [StatusCodes.Status422UnprocessableEntity] = SharedErrorCodes.ValidationFailed,
        [StatusCodes.Status429TooManyRequests] = SharedErrorCodes.RateLimited,
        [StatusCodes.Status500InternalServerError] = SharedErrorCodes.InternalError,
        [StatusCodes.Status503ServiceUnavailable] = SharedErrorCodes.DependencyUnavailable,
        [StatusCodes.Status504GatewayTimeout] = SharedErrorCodes.DependencyTimeout,
    };

    /// <summary>Shared default code for a status, or <c>null</c> when no shared code is approved for it.</summary>
    public static string? GetDefaultCode(int statusCode) =>
        CodesByStatus.TryGetValue(statusCode, out var code) ? code : null;

    /// <summary>Problem type URN for a code, e.g. <c>urn:buildmind:problem:internal-error</c>.</summary>
    public static string GetTypeForCode(string code) =>
        TypePrefix + code.ToLowerInvariant().Replace('_', '-');

    /// <summary>Generic public detail for unexpected server errors.</summary>
    public const string InternalErrorDetail = "An unexpected error occurred.";

    /// <summary>Applied by <c>AddProblemDetails</c> to every problem response written by the framework.</summary>
    /// <remarks>
    /// Removing a few known-unsafe extension names is a backstop, not a guarantee: endpoints remain
    /// responsible for never placing sensitive data in <c>detail</c> or custom extensions. For HTTP 500 the
    /// response is reduced to generic fields regardless of what the endpoint supplied.
    /// </remarks>
    public static void Apply(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = context.ProblemDetails;
        var httpContext = context.HttpContext;
        var status = problem.Status ?? httpContext.Response.StatusCode;
        problem.Status = status;

        foreach (var key in UnsafeExtensionKeys)
        {
            problem.Extensions.Remove(key);
        }

        if (status == StatusCodes.Status500InternalServerError)
        {
            ReduceToGenericInternalError(problem);
        }

        if (!problem.Extensions.TryGetValue(CodeExtension, out var existing) || existing is not string { Length: > 0 })
        {
            var code = status == StatusCodes.Status400BadRequest
                && problem is HttpValidationProblemDetails { Errors.Count: > 0 }
                    ? SharedErrorCodes.ValidationFailed
                    : GetDefaultCode(status);

            if (code is not null)
            {
                problem.Extensions[CodeExtension] = code;
            }
            else
            {
                problem.Extensions.Remove(CodeExtension);
            }
        }

        if (IsFrameworkDefaultType(problem.Type))
        {
            problem.Type = problem.Extensions.TryGetValue(CodeExtension, out var c) && c is string codeValue
                ? GetTypeForCode(codeValue)
                : "about:blank";
        }

        problem.Title ??= ReasonPhrases.GetReasonPhrase(status);
        problem.Instance ??= httpContext.Request.PathBase.Add(httpContext.Request.Path).ToUriComponent();
        problem.Extensions[TraceIdExtension] = TraceIdentifiers.Get(httpContext);
    }

    private static void ReduceToGenericInternalError(ProblemDetails problem)
    {
        var code = problem.Extensions.TryGetValue(CodeExtension, out var value)
            && value is string candidate
            && IsWellFormedCode(candidate)
                ? candidate
                : SharedErrorCodes.InternalError;

        problem.Extensions.Clear();
        problem.Extensions[CodeExtension] = code;
        problem.Title = ReasonPhrases.GetReasonPhrase(StatusCodes.Status500InternalServerError);
        problem.Detail = InternalErrorDetail;
        problem.Type = GetTypeForCode(code);
    }

    private static bool IsWellFormedCode(string code) =>
        code.Length is > 0 and <= 64
        && code.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
        && code[0] is >= 'A' and <= 'Z'
        && !code.EndsWith('_')
        && !code.Contains("__", StringComparison.Ordinal);

    private static bool IsFrameworkDefaultType(string? type) =>
        string.IsNullOrEmpty(type)
        || type.StartsWith("https://tools.ietf.org/", StringComparison.OrdinalIgnoreCase)
        || type.StartsWith("https://datatracker.ietf.org/", StringComparison.OrdinalIgnoreCase);
}

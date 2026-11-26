using System.Text.RegularExpressions;
using BuildMind.Application.Abstractions.Errors;

namespace BuildMind.API.Errors;

/// <summary>
/// Shared validation error results for endpoints (decision C4): 400 for structurally invalid requests,
/// 422 for valid requests that violate a domain rule.
/// </summary>
/// <remarks>
/// Feature-specific diagnostic payloads remain owned by their modules and are passed through as
/// caller-named extensions; this helper deliberately does not fix a diagnostic schema (pending C4 detail).
/// </remarks>
public static partial class ApiProblems
{
    /// <summary>HTTP 400 <c>VALIDATION_FAILED</c> with field errors keyed by request member path.</summary>
    public static IResult ValidationFailed(IDictionary<string, string[]> errors, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return TypedResults.ValidationProblem(
            errors,
            detail: detail ?? "One or more fields are invalid.",
            type: ProblemDetailsDefaults.GetTypeForCode(SharedErrorCodes.ValidationFailed),
            extensions: new Dictionary<string, object?>
            {
                [ProblemDetailsDefaults.CodeExtension] = SharedErrorCodes.ValidationFailed,
            });
    }

    /// <summary>HTTP 422 for a domain-rule failure identified by a feature-owned UPPER_SNAKE_CASE code.</summary>
    public static IResult DomainRuleViolation(
        string code,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        if (string.IsNullOrWhiteSpace(code) || !ErrorCodePattern().IsMatch(code))
        {
            throw new ArgumentException("Error codes must be UPPER_SNAKE_CASE.", nameof(code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        var allExtensions = new Dictionary<string, object?>();
        if (extensions is not null)
        {
            foreach (var (key, value) in extensions)
            {
                if (key is ProblemDetailsDefaults.CodeExtension or ProblemDetailsDefaults.TraceIdExtension)
                {
                    throw new ArgumentException($"Extension '{key}' is reserved.", nameof(extensions));
                }

                allExtensions[key] = value;
            }
        }

        allExtensions[ProblemDetailsDefaults.CodeExtension] = code;

        return TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            type: ProblemDetailsDefaults.GetTypeForCode(code),
            extensions: allExtensions);
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$")]
    private static partial Regex ErrorCodePattern();
}

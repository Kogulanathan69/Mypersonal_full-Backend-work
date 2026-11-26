namespace BuildMind.Application.Abstractions.Errors;

/// <summary>
/// Shared, HTTP-level error codes approved in the lead decision record of 2026-11-18
/// (17-SHARED-CONVENTIONS-PROPOSAL §20, decisions C3 and C5).
/// </summary>
/// <remarks>
/// Values are stable public identifiers: never rename or reuse them. Feature modules keep their own
/// feature-owned codes (for example the M3 plan-editor codes); this class does not replace them.
/// </remarks>
public static class SharedErrorCodes
{
    public const string MalformedRequest = "MALFORMED_REQUEST";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string RevisionConflict = "REVISION_CONFLICT";
    public const string DuplicateRequest = "DUPLICATE_REQUEST";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string RateLimited = "RATE_LIMITED";
    public const string DependencyUnavailable = "DEPENDENCY_UNAVAILABLE";
    public const string DependencyTimeout = "DEPENDENCY_TIMEOUT";
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>All shared codes, in declaration order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        MalformedRequest,
        ValidationFailed,
        Unauthenticated,
        Forbidden,
        NotFound,
        Conflict,
        RevisionConflict,
        DuplicateRequest,
        PayloadTooLarge,
        UnsupportedMediaType,
        RateLimited,
        DependencyUnavailable,
        DependencyTimeout,
        InternalError,
    ];
}

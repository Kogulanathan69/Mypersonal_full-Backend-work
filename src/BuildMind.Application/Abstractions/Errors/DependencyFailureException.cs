namespace BuildMind.Application.Abstractions.Errors;

/// <summary>How a private internal dependency (Python FastAPI, RAG) failed (BE-M4-D05).</summary>
public enum DependencyFailureKind
{
    /// <summary>The integration is disabled or not configured.</summary>
    Disabled,

    /// <summary>Connection refused/failed, or the dependency answered 502/503.</summary>
    Unavailable,

    /// <summary>No response within the configured timeout, or the dependency answered 504.</summary>
    Timeout,

    /// <summary>A status code or redirect the caller did not expect.</summary>
    UnexpectedResponse,

    /// <summary>Not JSON, invalid JSON, or JSON failing the caller's validation.</summary>
    MalformedResponse,

    /// <summary>The response exceeded the client's size limit.</summary>
    ResponseTooLarge,
}

/// <summary>
/// A private internal dependency failed. Public translation: <see cref="DependencyFailureKind.Timeout"/> →
/// 504 <c>DEPENDENCY_TIMEOUT</c>; every other kind → 503 <c>DEPENDENCY_UNAVAILABLE</c>. The message never contains
/// URLs, hosts, payloads or credentials; <see cref="Dependency"/> is a fixed code-defined name such as "python".
/// </summary>
/// <remarks>Feature code may catch it to return its own port status (for example M3's DependencyTimeout).</remarks>
public sealed class DependencyFailureException : Exception
{
    public DependencyFailureException(string dependency, DependencyFailureKind kind, int? statusCode = null)
        : base($"Internal dependency '{dependency}' failed: {kind}.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dependency);
        Dependency = dependency;
        Kind = kind;
        StatusCode = statusCode;
    }

    public string Dependency { get; }

    public DependencyFailureKind Kind { get; }

    /// <summary>HTTP status returned by the dependency, when there was one.</summary>
    public int? StatusCode { get; }

    public string Code => Kind == DependencyFailureKind.Timeout
        ? SharedErrorCodes.DependencyTimeout
        : SharedErrorCodes.DependencyUnavailable;
}

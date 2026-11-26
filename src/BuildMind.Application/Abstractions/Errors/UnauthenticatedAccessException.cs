namespace BuildMind.Application.Abstractions.Errors;

/// <summary>
/// Raised when an operation requires an authenticated user but no valid authenticated identity exists.
/// The API maps it to HTTP 401 with code <see cref="SharedErrorCodes.Unauthenticated"/>.
/// </summary>
/// <remarks>The message is fixed and never contains identity or claim values.</remarks>
public sealed class UnauthenticatedAccessException : Exception
{
    public UnauthenticatedAccessException()
        : base("Authentication is required.")
    {
    }
}

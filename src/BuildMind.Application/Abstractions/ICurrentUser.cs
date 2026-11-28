using BuildMind.Application.Abstractions.Errors;

namespace BuildMind.Application.Abstractions;

/// <summary>
/// The authenticated caller of the current request, derived only from a validated identity.
/// </summary>
/// <remarks>
/// Foundation abstraction (BE-M4-D01c). Production authentication (JWT validation, claim naming and
/// role contracts) is pending M1/M4 integration; until then every request is unauthenticated.
/// Never derive a user identifier from request bodies, query strings or arbitrary headers.
/// </remarks>
public interface ICurrentUser
{
    /// <summary>True only when an authenticated identity carries exactly one valid, non-empty user identifier.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The authenticated user identifier, or <c>null</c>. Never <see cref="Guid.Empty"/>.</summary>
    Guid? UserId { get; }

    /// <summary>Roles of the authenticated identity; empty when unauthenticated. No role is ever assumed.</summary>
    IReadOnlySet<string> Roles { get; }

    /// <summary>Server-issued token version claim (<c>tv</c>) when authenticated by JWT, or <c>null</c>.</summary>
    long? TokenVersion => null;

    /// <summary>Returns the authenticated user identifier or throws <see cref="UnauthenticatedAccessException"/>.</summary>
    Guid GetRequiredUserId();
}

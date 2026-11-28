using System.Globalization;
using System.Security.Claims;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Identity;

namespace BuildMind.API.Security;

/// <summary>
/// <see cref="ICurrentUser"/> backed by the request's authenticated <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// Reads only claims of identities that the authentication pipeline marked as authenticated; request
/// headers, bodies and unvalidated tokens are never inspected. No authentication handler is registered
/// yet (JWT pending M1/M4, A1/A2), so every request is currently unauthenticated.
/// Identifier claim names (<c>sub</c> or <see cref="ClaimTypes.NameIdentifier"/>) and role claim
/// names are provisional pending M1 coordination. Missing, malformed, empty or conflicting identifiers
/// fail closed (unauthenticated), as does more than one authenticated identity carrying an identifier;
/// roles come only from the identity that carries the identifier; no role is ever assumed.
/// </remarks>
public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private static readonly string[] SubjectClaimTypes = ["sub", ClaimTypes.NameIdentifier];
    private static readonly string[] RoleClaimTypes = ["role", ClaimTypes.Role];
    private static readonly IReadOnlySet<string> NoRoles = new HashSet<string>(StringComparer.Ordinal);

    private ResolvedIdentity? _resolved;

    public bool IsAuthenticated => Resolve().UserId is not null;

    public Guid? UserId => Resolve().UserId;

    public IReadOnlySet<string> Roles => Resolve().Roles;

    public long? TokenVersion => Resolve().TokenVersion;

    public Guid GetRequiredUserId() => Resolve().UserId ?? throw new UnauthenticatedAccessException();

    private ResolvedIdentity Resolve() => _resolved ??= ResolveFrom(httpContextAccessor.HttpContext?.User);

    internal static ResolvedIdentity ResolveFrom(ClaimsPrincipal? principal)
    {
        // Exactly one authenticated identity may carry the user identifier; identifier and roles are both
        // taken from that single identity so claims of unrelated identities are never combined.
        var identitiesWithSubject = (principal?.Identities ?? [])
            .Where(identity => identity.IsAuthenticated && identity.Claims.Any(IsSubjectClaim))
            .ToArray();

        if (identitiesWithSubject.Length != 1)
        {
            return ResolvedIdentity.Anonymous;
        }

        var identity = identitiesWithSubject[0];

        var subjects = identity.Claims
            .Where(IsSubjectClaim)
            .Select(claim => claim.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (subjects.Length != 1
            || !Guid.TryParse(subjects[0], out var userId)
            || userId == Guid.Empty)
        {
            return ResolvedIdentity.Anonymous;
        }

        var roles = identity.Claims
            .Where(claim =>
                RoleClaimTypes.Contains(claim.Type, StringComparer.Ordinal)
                || string.Equals(claim.Type, identity.RoleClaimType, StringComparison.Ordinal))
            .Select(claim => claim.Value.Trim())
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var tokenVersions = identity.FindAll(JwtAccessTokenIssuer.TokenVersionClaim).ToArray();
        long? tokenVersion = tokenVersions.Length == 1
            && long.TryParse(tokenVersions[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedVersion)
            && parsedVersion >= 1
            ? parsedVersion
            : null;

        return new ResolvedIdentity(userId, roles, tokenVersion);
    }

    private static bool IsSubjectClaim(Claim claim) =>
        SubjectClaimTypes.Contains(claim.Type, StringComparer.Ordinal);

    internal sealed record ResolvedIdentity(Guid? UserId, IReadOnlySet<string> Roles, long? TokenVersion)
    {
        public static ResolvedIdentity Anonymous { get; } = new(null, NoRoles, null);
    }
}

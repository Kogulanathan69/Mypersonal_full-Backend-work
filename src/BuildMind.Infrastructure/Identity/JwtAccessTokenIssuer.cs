using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace BuildMind.Infrastructure.Identity;

public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    /// <summary>Claim carrying <see cref="User.TokenVersion"/>.</summary>
    public const string TokenVersionClaim = "tv";

    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;

    public JwtAccessTokenIssuer(
        JwtOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        options.Validate();

        _options = options;
        _timeProvider = timeProvider;
    }

    public AccessTokenResult Issue(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (!user.IsActive || user.Id == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Token issuance rejected.");
        }

        var now = _timeProvider.GetUtcNow();

        var expiresAt = now.AddMinutes(
            JwtOptions.AccessTokenMinutes);

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString("D")),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString("N")),

            new Claim(
                JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),

            // Session generation; the token is rejected once the user's token version changes.
            new Claim(
                TokenVersionClaim,
                user.TokenVersion.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64)
        };

        var signingKey = new SymmetricSecurityKey(
            _options.GetSigningKey());

        var credentials = new SigningCredentials(
            signingKey,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var accessToken =
            new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessTokenResult(
            accessToken,
            expiresAt);
    }
}
using Microsoft.IdentityModel.Tokens;

namespace BuildMind.Infrastructure.Identity;

public static class JwtValidationParametersFactory
{
    public static TokenValidationParameters Create(
        JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        return new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,

            ValidateAudience = true,
            ValidAudience = options.Audience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                options.GetSigningKey()),

            ValidateLifetime = true,

            RequireExpirationTime = true,
            RequireSignedTokens = true,
            RequireAudience = true,

            ValidAlgorithms = new[]
            {
                SecurityAlgorithms.HmacSha256
            },

            ClockSkew = TimeSpan.FromSeconds(
                JwtOptions.ClockSkewSeconds),

            NameClaimType = "sub",
            RoleClaimType = "role",

            SaveSigninToken = false
        };
    }
}
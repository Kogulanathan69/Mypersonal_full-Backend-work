using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Identity;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class JwtSecurityTests
{
    private static JwtOptions CreateOptions()
    {
        return new JwtOptions
        {
            Issuer = "BuildMind-Test",
            Audience = "BuildMind-Test-Client",
            SigningKey = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32))
        };
    }

    private static User CreateUser()
    {
        return new User
        {
            Email = "test@example.com",
            NormalizedEmail = "TEST@EXAMPLE.COM",
            PasswordHash = "synthetic-test-only"
        };
    }

    private static string Issue(JwtOptions options)
    {
        return new JwtAccessTokenIssuer(
            options, TimeProvider.System)
            .Issue(CreateUser()).AccessToken;
    }

    private static void Validate(
        string token, JwtOptions options)
    {
        new JwtSecurityTokenHandler().ValidateToken(
            token,
            JwtValidationParametersFactory.Create(options),
            out _);
    }

    [Fact]
    public void ValidToken_ShouldPass()
    {
        var options = CreateOptions();
        Validate(Issue(options), options);
    }

    [Fact]
    public void Token_ShouldContainUserId()
    {
        var options = CreateOptions();
        var user = CreateUser();

        var result = new JwtAccessTokenIssuer(
            options, TimeProvider.System).Issue(user);

        var token = new JwtSecurityTokenHandler()
            .ReadJwtToken(result.AccessToken);

        Assert.Equal(user.Id.ToString("D"), token.Subject);
    }

    [Fact]
    public void Token_ShouldUseHS256()
    {
        var token = new JwtSecurityTokenHandler()
            .ReadJwtToken(Issue(CreateOptions()));

        Assert.Equal("HS256", token.Header.Alg);
    }

    [Fact]
    public void Token_ShouldExpireAfter15Minutes()
    {
        var token = new JwtSecurityTokenHandler()
            .ReadJwtToken(Issue(CreateOptions()));

        Assert.Equal(
            TimeSpan.FromMinutes(15),
            token.ValidTo - token.ValidFrom);
    }

    [Fact]
    public void WrongSigningKey_ShouldFail()
    {
        var options = CreateOptions();
        var token = Issue(options);

        options.SigningKey = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32));

        Assert.ThrowsAny<SecurityTokenException>(
            () => Validate(token, options));
    }

    [Fact]
    public void WrongIssuer_ShouldFail()
    {
        var options = CreateOptions();
        var token = Issue(options);

        options.Issuer = "Invalid-Issuer";

        Assert.ThrowsAny<SecurityTokenException>(
            () => Validate(token, options));
    }

    [Fact]
    public void WrongAudience_ShouldFail()
    {
        var options = CreateOptions();
        var token = Issue(options);

        options.Audience = "Invalid-Audience";

        Assert.ThrowsAny<SecurityTokenException>(
            () => Validate(token, options));
    }

    [Fact]
    public void TamperedToken_ShouldFail()
    {
        var options = CreateOptions();
        var parts = Issue(options).Split('.');

        parts[2] =
            (parts[2][0] == 'A' ? "B" : "A")
            + parts[2].Substring(1);

        var tampered = string.Join(".", parts);

        Assert.ThrowsAny<SecurityTokenException>(
            () => Validate(tampered, options));
    }

    [Fact]
    public void ExpiredToken_ShouldFail()
    {
        var options = CreateOptions();

        var token = new JwtAccessTokenIssuer(
            options,
            new FixedTimeProvider(
                DateTimeOffset.UtcNow.AddHours(-1)))
            .Issue(CreateUser()).AccessToken;

        Assert.Throws<SecurityTokenExpiredException>(
            () => Validate(token, options));
    }

    [Fact]
    public void ShortSigningKey_ShouldFail()
    {
        var options = CreateOptions();

        options.SigningKey = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(16));

        Assert.Throws<InvalidOperationException>(
            () => options.Validate());
    }

    [Fact]
    public void Token_ShouldNotContainPasswordHash()
    {
        var token = new JwtSecurityTokenHandler()
            .ReadJwtToken(Issue(CreateOptions()));

        Assert.DoesNotContain(
            token.Claims,
            c => c.Value == "synthetic-test-only");
    }

    [Fact]
    public void ClockSkew_ShouldBe30Seconds()
    {
        var parameters =
            JwtValidationParametersFactory.Create(
                CreateOptions());

        Assert.Equal(
            TimeSpan.FromSeconds(30),
            parameters.ClockSkew);
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => now;
    }
}
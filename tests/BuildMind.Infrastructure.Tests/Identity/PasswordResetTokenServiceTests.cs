using BuildMind.Infrastructure.Identity;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class PasswordResetTokenServiceTests
{
    [Fact]
    public void GeneratedToken_ShouldHave256Bits()
    {
        var token = PasswordResetTokenService.Generate();

        Assert.Equal(32, Convert.FromHexString(token).Length);
    }

    [Fact]
    public void GeneratedTokens_ShouldBeUnique()
    {
        var tokens = Enumerable.Range(0, 100)
            .Select(_ => PasswordResetTokenService.Generate())
            .ToArray();

        Assert.Equal(100, tokens.Distinct().Count());
    }

    [Fact]
    public void Hash_ShouldNotEqualRawToken()
    {
        var token = PasswordResetTokenService.Generate();

        Assert.NotEqual(
            token, PasswordResetTokenService.Hash(token));
    }

    [Fact]
    public void ValidToken_ShouldPass()
    {
        var token = PasswordResetTokenService.Generate();

        Assert.True(PasswordResetTokenService.Verify(
            token, PasswordResetTokenService.Hash(token)));
    }

    [Fact]
    public void WrongToken_ShouldFail()
    {
        var token = PasswordResetTokenService.Generate();

        Assert.False(PasswordResetTokenService.Verify(
            PasswordResetTokenService.Generate(),
            PasswordResetTokenService.Hash(token)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void InvalidToken_ShouldFail(string? token)
    {
        var hash = PasswordResetTokenService.Hash(
            PasswordResetTokenService.Generate());

        Assert.False(
            PasswordResetTokenService.Verify(token, hash));
    }

    [Fact]
    public void InvalidHash_ShouldFail()
    {
        Assert.False(PasswordResetTokenService.Verify(
            PasswordResetTokenService.Generate(),
            new string('Z', 64)));
    }

    [Fact]
    public void ExpirationPolicy_ShouldBe15Minutes()
    {
        Assert.Equal(
            15, PasswordResetTokenService.ExpirationMinutes);
    }
}
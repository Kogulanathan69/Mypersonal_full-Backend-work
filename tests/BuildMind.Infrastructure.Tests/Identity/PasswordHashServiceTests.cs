using BuildMind.Infrastructure.Identity;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class PasswordHashServiceTests
{
    private readonly PasswordHashService _service = new();

    private const string Password = "SyntheticTestPassword123!";

    [Fact]
    public void HashPassword_ShouldNotReturnPlainText()
    {
        var hash = _service.HashPassword(Password);

        Assert.NotEqual(Password, hash);
        Assert.DoesNotContain(Password, hash);
    }

    [Fact]
    public void CorrectPassword_ShouldPass()
    {
        var hash = _service.HashPassword(Password);

        Assert.True(
            _service.VerifyPassword(Password, hash));
    }

    [Fact]
    public void WrongPassword_ShouldFail()
    {
        var hash = _service.HashPassword(Password);

        Assert.False(
            _service.VerifyPassword("WrongPassword", hash));
    }

    [Fact]
    public void SamePassword_ShouldGenerateDifferentHashes()
    {
        var first = _service.HashPassword(Password);
        var second = _service.HashPassword(Password);

        Assert.NotEqual(first, second);
        Assert.True(_service.VerifyPassword(Password, first));
        Assert.True(_service.VerifyPassword(Password, second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("PBKDF2-SHA256-V1$600000$invalid$invalid")]
    [InlineData("PBKDF2-SHA256-V1$1$AAAA$AAAA")]
    public void InvalidHash_ShouldFail(string hash)
    {
        Assert.False(
            _service.VerifyPassword(Password, hash));
    }

    [Fact]
    public void EmptyPassword_ShouldBeRejected()
    {
        Assert.Throws<ArgumentException>(
            () => _service.HashPassword(""));
    }

    [Fact]
    public void EmptyPasswordVerification_ShouldFail()
    {
        var hash = _service.HashPassword(Password);

        Assert.False(
            _service.VerifyPassword("", hash));
    }

    [Fact]
    public void TamperedHash_ShouldFail()
    {
        var hash = _service.HashPassword(Password);
        var parts = hash.Split('$');

        var bytes = Convert.FromBase64String(parts[3]);
        bytes[0] ^= 1;

        parts[3] = Convert.ToBase64String(bytes);

        var tampered = string.Join("$", parts);

        Assert.False(
            _service.VerifyPassword(Password, tampered));
    }
}
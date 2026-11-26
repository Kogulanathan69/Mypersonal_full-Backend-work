using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Validators;

namespace BuildMind.Application.UnitTests.Auth;

public sealed class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var request = new LoginRequest
        {
            Email = "customer@example.com",
            Password = "StrongPassword123!"
        };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NullRequest_ShouldFail()
    {
        var result = _validator.Validate(null!);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Request" &&
                 e.Code == "REQUIRED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingEmail_ShouldFail(string email)
    {
        var request = new LoginRequest
        {
            Email = email,
            Password = "StrongPassword123!"
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Email" &&
                 e.Code == "REQUIRED");
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("customer@")]
    [InlineData("@example.com")]
    public void InvalidEmail_ShouldFail(string email)
    {
        var request = new LoginRequest
        {
            Email = email,
            Password = "StrongPassword123!"
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Email" &&
                 e.Code == "INVALID_EMAIL");
    }

    [Fact]
    public void EmailTooLong_ShouldFail()
    {
        var request = new LoginRequest
        {
            Email = new string('a', 255) + "@example.com",
            Password = "StrongPassword123!"
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Email" &&
                 e.Code == "INVALID_EMAIL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPassword_ShouldFail(string password)
    {
        var request = new LoginRequest
        {
            Email = "customer@example.com",
            Password = password
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Password" &&
                 e.Code == "REQUIRED");
    }

    [Fact]
    public void PasswordTooLong_ShouldFail()
    {
        var request = new LoginRequest
        {
            Email = "customer@example.com",
            Password = new string('a', 129)
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Password" &&
                 e.Code == "INVALID_LENGTH");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(128)]
    public void AllowedPasswordLength_ShouldPass(int length)
    {
        var request = new LoginRequest
        {
            Email = "customer@example.com",
            Password = new string('a', length)
        };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
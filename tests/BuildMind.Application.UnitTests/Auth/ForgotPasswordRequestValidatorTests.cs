using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Validators;

namespace BuildMind.Application.UnitTests.Auth;

public sealed class ForgotPasswordRequestValidatorTests
{
    private readonly ForgotPasswordRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var request = new ForgotPasswordRequest
        {
            Email = "customer@example.com"
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
    [InlineData(null)]
    public void MissingEmail_ShouldFail(string? email)
    {
        var request = new ForgotPasswordRequest
        {
            Email = email!
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
        var request = new ForgotPasswordRequest
        {
            Email = email
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
        var request = new ForgotPasswordRequest
        {
            Email = new string('a', 255) + "@example.com"
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Email" &&
                 e.Code == "INVALID_EMAIL");
    }
}
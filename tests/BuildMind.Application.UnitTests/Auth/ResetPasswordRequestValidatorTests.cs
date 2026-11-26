using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Validators;

namespace BuildMind.Application.UnitTests.Auth;

public sealed class ResetPasswordRequestValidatorTests
{
    private readonly ResetPasswordRequestValidator _validator = new();

    private static ResetPasswordRequest CreateRequest(
        string email = "customer@example.com",
        string token = "sample-reset-token",
        string password = "StrongPassword123!",
        string confirmPassword = "StrongPassword123!")
    {
        return new ResetPasswordRequest
        {
            Email = email,
            Token = token,
            NewPassword = password,
            ConfirmPassword = confirmPassword
        };
    }

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var result = _validator.Validate(CreateRequest());

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
        var result = _validator.Validate(
            CreateRequest(email: email!));

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
        var result = _validator.Validate(
            CreateRequest(email: email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Email" &&
                 e.Code == "INVALID_EMAIL");
    }

    [Fact]
    public void EmailTooLong_ShouldFail()
    {
        var result = _validator.Validate(
            CreateRequest(email: new string('a', 255) + "@example.com"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Email" &&
                 e.Code == "INVALID_EMAIL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingToken_ShouldFail(string? token)
    {
        var result = _validator.Validate(
            CreateRequest(token: token!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Token" &&
                 e.Code == "REQUIRED");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingNewPassword_ShouldFail(string? password)
    {
        var result = _validator.Validate(
            CreateRequest(password: password!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "NewPassword" &&
                 e.Code == "REQUIRED");
    }

    [Theory]
    [InlineData(14)]
    [InlineData(129)]
    public void InvalidPasswordLength_ShouldFail(int length)
    {
        var password = new string('a', length);

        var result = _validator.Validate(
            CreateRequest(
                password: password,
                confirmPassword: password));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "NewPassword" &&
                 e.Code == "INVALID_LENGTH");
    }

    [Theory]
    [InlineData(15)]
    [InlineData(128)]
    public void ValidPasswordLength_ShouldPass(int length)
    {
        var password = new string('a', length);

        var result = _validator.Validate(
            CreateRequest(
                password: password,
                confirmPassword: password));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingConfirmPassword_ShouldFail(string? confirmPassword)
    {
        var result = _validator.Validate(
            CreateRequest(confirmPassword: confirmPassword!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "ConfirmPassword" &&
                 e.Code == "REQUIRED");
    }

    [Fact]
    public void PasswordMismatch_ShouldFail()
    {
        var result = _validator.Validate(
            CreateRequest(
                confirmPassword: "DifferentPassword123!"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "ConfirmPassword" &&
                 e.Code == "PASSWORD_MISMATCH");
    }
}
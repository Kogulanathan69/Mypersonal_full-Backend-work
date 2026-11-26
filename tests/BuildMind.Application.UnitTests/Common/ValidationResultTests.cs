using BuildMind.Application.Common.Validation;

namespace BuildMind.Application.UnitTests.Common;

public sealed class ValidationResultTests
{
    [Fact]
    public void NewResult_ShouldBeValid()
    {
        var result = new ValidationResult();

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AddError_ShouldMakeResultInvalid()
    {
        var result = new ValidationResult();

        result.AddError(
            "Email",
            "REQUIRED",
            "Email is required.");

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void AddError_ShouldPreserveErrorDetails()
    {
        var result = new ValidationResult();

        result.AddError(
            "Email",
            "REQUIRED",
            "Email is required.");

        var error = Assert.Single(result.Errors);

        Assert.Equal("Email", error.Field);
        Assert.Equal("REQUIRED", error.Code);
        Assert.Equal("Email is required.", error.Message);
    }

    [Fact]
    public void AddMultipleErrors_ShouldPreserveAllErrors()
    {
        var result = new ValidationResult();

        result.AddError(
            "Email",
            "REQUIRED",
            "Email is required.");

        result.AddError(
            "Password",
            "REQUIRED",
            "Password is required.");

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }
}
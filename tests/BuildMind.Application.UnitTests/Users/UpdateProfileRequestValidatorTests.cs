using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Application.Features.Users.Validators;

namespace BuildMind.Application.UnitTests.Users;

public sealed class UpdateProfileRequestValidatorTests
{
    private readonly UpdateProfileRequestValidator _validator = new();

    private static UpdateProfileRequest CreateRequest(
        string fullName = "Sample Customer",
        string? phoneNumber = null)
    {
        return new UpdateProfileRequest
        {
            FullName = fullName,
            PhoneNumber = phoneNumber
        };
    }

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(phoneNumber: "0771234567"));

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
    public void MissingFullName_ShouldFail(string? fullName)
    {
        var result = _validator.Validate(
            CreateRequest(fullName: fullName!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "FullName" &&
                 e.Code == "REQUIRED");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(101)]
    public void InvalidNameLength_ShouldFail(int length)
    {
        var result = _validator.Validate(
            CreateRequest(fullName: new string('a', length)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "FullName" &&
                 e.Code == "INVALID_LENGTH");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(100)]
    public void ValidNameLength_ShouldPass(int length)
    {
        var result = _validator.Validate(
            CreateRequest(fullName: new string('a', length)));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("குமார்")]
    [InlineData("කුමාර")]
    [InlineData("Sample Customer")]
    public void UnicodeAndEnglishNames_ShouldPass(string fullName)
    {
        var result = _validator.Validate(
            CreateRequest(fullName: fullName));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void OptionalPhoneNumber_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(phoneNumber: null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("0771234567")]
    [InlineData("+94771234567")]
    [InlineData("123456789")]
    [InlineData("+123456789012345")]
    public void ValidPhoneNumber_ShouldPass(string phoneNumber)
    {
        var result = _validator.Validate(
            CreateRequest(phoneNumber: phoneNumber));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc123")]
    [InlineData("12345678")]
    [InlineData("1234567890123456")]
    [InlineData("++94771234567")]
    [InlineData("077-123-4567")]
    public void InvalidPhoneNumber_ShouldFail(string phoneNumber)
    {
        var result = _validator.Validate(
            CreateRequest(phoneNumber: phoneNumber));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "PhoneNumber" &&
                 e.Code == "INVALID_PHONE_NUMBER");
    }
}
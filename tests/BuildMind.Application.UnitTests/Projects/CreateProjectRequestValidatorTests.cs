using BuildMind.Application.Features.Projects.DTOs;
using BuildMind.Application.Features.Projects.Validators;

namespace BuildMind.Application.UnitTests.Projects;

public sealed class CreateProjectRequestValidatorTests
{
    private readonly CreateProjectRequestValidator _validator = new();

    private static CreateProjectRequest CreateRequest(
        string name = "My Dream House",
        string projectType = "Residential",
        string? locationReference = null)
    {
        return new CreateProjectRequest
        {
            Name = name,
            ProjectType = projectType,
            LocationReference = locationReference
        };
    }

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(locationReference: "Jaffna"));

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
    public void MissingProjectName_ShouldFail(string? name)
    {
        var result = _validator.Validate(
            CreateRequest(name: name!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Name" &&
                 e.Code == "REQUIRED");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(151)]
    public void InvalidProjectNameLength_ShouldFail(int length)
    {
        var result = _validator.Validate(
            CreateRequest(name: new string('a', length)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Name" &&
                 e.Code == "INVALID_LENGTH");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(150)]
    public void ValidProjectNameLength_ShouldPass(int length)
    {
        var result = _validator.Validate(
            CreateRequest(name: new string('a', length)));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingProjectType_ShouldFail(string? projectType)
    {
        var result = _validator.Validate(
            CreateRequest(projectType: projectType!));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "ProjectType" &&
                 e.Code == "REQUIRED");
    }

    [Fact]
    public void ProjectTypeTooLong_ShouldFail()
    {
        var result = _validator.Validate(
            CreateRequest(projectType: new string('a', 101)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "ProjectType" &&
                 e.Code == "INVALID_LENGTH");
    }

    [Fact]
    public void MaximumProjectTypeLength_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(projectType: new string('a', 100)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void OptionalLocationReference_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(locationReference: null));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void LocationReferenceTooLong_ShouldFail()
    {
        var result = _validator.Validate(
            CreateRequest(
                locationReference: new string('a', 501)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "LocationReference" &&
                 e.Code == "INVALID_LENGTH");
    }

    [Fact]
    public void MaximumLocationReferenceLength_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(
                locationReference: new string('a', 500)));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void MultipleInvalidFields_ShouldReturnMultipleErrors()
    {
        var result = _validator.Validate(
            CreateRequest(
                name: "",
                projectType: "",
                locationReference: new string('a', 501)));

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }
}
using BuildMind.Application.Features.Projects.DTOs;
using BuildMind.Application.Features.Projects.Validators;

namespace BuildMind.Application.UnitTests.Projects;

public sealed class UpdateProjectRequestValidatorTests
{
    private readonly UpdateProjectRequestValidator _validator = new();

    private static UpdateProjectRequest CreateRequest(
        string? name = null,
        string? projectType = null,
        string? locationReference = null,
        long revision = 1)
    {
        return new UpdateProjectRequest
        {
            Name = name,
            ProjectType = projectType,
            LocationReference = locationReference,
            Revision = revision
        };
    }

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(
                name: "Updated Dream House",
                projectType: "Residential",
                locationReference: "Jaffna",
                revision: 2));

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
    [InlineData("a")]
    public void InvalidProjectName_ShouldFail(string name)
    {
        var result = _validator.Validate(
            CreateRequest(name: name));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Name" &&
                 e.Code == "INVALID_NAME");
    }

    [Fact]
    public void ProjectNameTooLong_ShouldFail()
    {
        var result = _validator.Validate(
            CreateRequest(name: new string('a', 151)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Name" &&
                 e.Code == "INVALID_NAME");
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
    public void InvalidProjectType_ShouldFail(string projectType)
    {
        var result = _validator.Validate(
            CreateRequest(projectType: projectType));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "ProjectType" &&
                 e.Code == "INVALID_PROJECT_TYPE");
    }

    [Fact]
    public void ProjectTypeTooLong_ShouldFail()
    {
        var result = _validator.Validate(
            CreateRequest(projectType: new string('a', 101)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "ProjectType" &&
                 e.Code == "INVALID_PROJECT_TYPE");
    }

    [Fact]
    public void MaximumProjectTypeLength_ShouldPass()
    {
        var result = _validator.Validate(
            CreateRequest(projectType: new string('a', 100)));

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

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void InvalidRevision_ShouldFail(long revision)
    {
        var result = _validator.Validate(
            CreateRequest(
                name: "Updated House",
                revision: revision));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Revision" &&
                 e.Code == "INVALID_REVISION");
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(100L)]
    public void ValidRevision_ShouldPass(long revision)
    {
        var result = _validator.Validate(
            CreateRequest(
                name: "Updated House",
                revision: revision));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NoChanges_ShouldFail()
    {
        var result = _validator.Validate(CreateRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors,
            e => e.Field == "Request" &&
                 e.Code == "NO_CHANGES");
    }

    [Fact]
    public void MultipleInvalidFields_ShouldReturnMultipleErrors()
    {
        var result = _validator.Validate(
            CreateRequest(
                name: "",
                projectType: "",
                locationReference: new string('a', 501),
                revision: 0));

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Errors.Count);
    }
}
using BuildMind.Application.Features.Requirements.Contracts;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class RequirementsOperationResultTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UndefinedStatus_IsRejected(int status) => Assert.Throws<ArgumentOutOfRangeException>(() => new RequirementsOperationResult((RequirementsOperationStatus)status));

    [Fact]
    public void Success_RequiresRevisionAndRejectsIssues()
    {
        Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(RequirementsOperationStatus.Success));
        var dto = new RequirementRevisionDto(F.Revision());
        Assert.Same(dto, new RequirementsOperationResult(RequirementsOperationStatus.Success, dto).Revision);
        Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(RequirementsOperationStatus.Success, dto, [Issue()]));
    }

    public static IEnumerable<object[]> Failures => Enum.GetValues<RequirementsOperationStatus>()
        .Where(status => status != RequirementsOperationStatus.Success).Select(status => new object[] { status });

    [Theory]
    [MemberData(nameof(Failures))]
    public void Failure_CannotExposeRevision(RequirementsOperationStatus status) =>
        Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(status, new RequirementRevisionDto(F.Revision())));

    [Theory]
    [InlineData(RequirementsOperationStatus.ValidationFailed)]
    [InlineData(RequirementsOperationStatus.CatalogueConflict)]
    public void DiagnosticResult_RequiresIssuesAndCopiesCollection(RequirementsOperationStatus status)
    {
        Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(status));
        var issues = new[] { Issue() };
        var result = new RequirementsOperationResult(status, issues: issues);
        issues[0] = new("OTHER", "other", "Synthetic change");
        Assert.Equal("CATALOGUE_VERSION_MISMATCH", result.Issues[0].Code);
        F.AssertReadOnly(result.Issues);
    }

    [Fact]
    public void UnrelatedFailure_CannotExposeIssues() => Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(RequirementsOperationStatus.NotFound, issues: [Issue()]));

    [Fact]
    public void CatalogueConflict_CannotUseUnrelatedDiagnostic() => Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(RequirementsOperationStatus.CatalogueConflict, issues: [new("OTHER", "field", "Synthetic message")]));

    [Fact]
    public void DiagnosticCannotBeNullOrIncomplete()
    {
        Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(RequirementsOperationStatus.ValidationFailed, issues: [null!]));
        Assert.Throws<ArgumentException>(() => new RequirementsOperationResult(RequirementsOperationStatus.ValidationFailed, issues: [new("", "field", "Synthetic message")]));
    }

    private static RequirementValidationIssue Issue() => new("CATALOGUE_VERSION_MISMATCH", "catalogueVersion", "Synthetic version conflict");
}

using BuildMind.Application.Features.Requirements.Abstractions;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Abstractions;

public sealed class RequirementSaveResultTests
{
    [Fact]
    public void Constructor_AcceptsSavedWithRevision()
    {
        var revision = F.Revision();
        var result = new RequirementSaveResult(RequirementSaveStatus.Saved, revision);
        Assert.Equal(RequirementSaveStatus.Saved, result.Status);
        Assert.Same(revision, result.SavedRevision);
    }

    [Fact]
    public void Constructor_RejectsSavedWithoutRevision() =>
        Assert.Throws<ArgumentNullException>("savedRevision", () => new RequirementSaveResult(RequirementSaveStatus.Saved));

    [Theory]
    [InlineData(RequirementSaveStatus.RevisionConflict)]
    [InlineData(RequirementSaveStatus.ProjectNotFound)]
    public void Constructor_AcceptsFailureWithoutRevision(RequirementSaveStatus status)
    {
        var result = new RequirementSaveResult(status);
        Assert.Equal(status, result.Status);
        Assert.Null(result.SavedRevision);
    }

    [Theory]
    [InlineData(RequirementSaveStatus.RevisionConflict)]
    [InlineData(RequirementSaveStatus.ProjectNotFound)]
    public void Constructor_RejectsFailureWithRevision(RequirementSaveStatus status) =>
        Assert.Throws<ArgumentException>("savedRevision", () => new RequirementSaveResult(status, F.Revision()));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(-1, true)]
    [InlineData(999, false)]
    [InlineData(999, true)]
    public void Constructor_RejectsUndefinedSaveStatus(int status, bool hasRevision) =>
        Assert.Throws<ArgumentOutOfRangeException>("status", () => new RequirementSaveResult(
            (RequirementSaveStatus)status, hasRevision ? F.Revision() : null));

    [Theory]
    [InlineData(RequirementSaveStatus.Saved)]
    [InlineData(RequirementSaveStatus.RevisionConflict)]
    [InlineData(RequirementSaveStatus.ProjectNotFound)]
    public void RecordCopy_PreservesConsistentState(RequirementSaveStatus status)
    {
        var original = new RequirementSaveResult(status, status == RequirementSaveStatus.Saved ? F.Revision() : null);
        var copy = original with { };
        Assert.Equal(original.Status, copy.Status);
        Assert.Same(original.SavedRevision, copy.SavedRevision);
        Assert.Equal(status == RequirementSaveStatus.Saved, copy.SavedRevision is not null);
    }
}

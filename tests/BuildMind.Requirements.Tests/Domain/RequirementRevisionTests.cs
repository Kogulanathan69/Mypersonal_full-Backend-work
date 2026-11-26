using BuildMind.Domain.Entities;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Domain;

public sealed class RequirementRevisionTests
{
    [Fact]
    public void Constructor_PreservesSnapshotMetadataAndNormalizesTimestamp()
    {
        var content = F.Content();
        var revision = new RequirementRevision(F.RevisionId, F.ProjectId, 1, F.Schema, F.Version, content, F.Timestamp);
        Assert.Equal(F.RevisionId, revision.Id);
        Assert.Equal(F.ProjectId, revision.ProjectId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(F.Schema, revision.SchemaVersion);
        Assert.Equal(F.Version, revision.CatalogueVersion);
        Assert.Same(content, revision.Content);
        Assert.Equal(F.Timestamp.UtcDateTime, revision.CreatedAtUtc.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, revision.CreatedAtUtc.Offset);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_RejectsEmptyIdentity(bool revisionId) =>
        Assert.Throws<ArgumentException>(revisionId ? "id" : "projectId", () => new RequirementRevision(
            revisionId ? Guid.Empty : F.RevisionId, revisionId ? F.ProjectId : Guid.Empty,
            1, F.Schema, F.Version, F.Content(), F.Timestamp));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void Constructor_RejectsNonPositiveRevisionNumber(long number) =>
        Assert.Throws<ArgumentOutOfRangeException>("revisionNumber", () => new RequirementRevision(
            F.RevisionId, F.ProjectId, number, F.Schema, F.Version, F.Content(), F.Timestamp));

    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void Constructor_AcceptsPositiveRevisionNumber(long number) =>
        Assert.Equal(number, new RequirementRevision(F.RevisionId, F.ProjectId, number,
            F.Schema, F.Version, F.Content(), F.Timestamp).RevisionNumber);

    [Theory]
    [InlineData(null, "catalogue")]
    [InlineData("", "catalogue")]
    [InlineData(" ", "catalogue")]
    [InlineData("schema", null)]
    [InlineData("schema", "")]
    [InlineData("schema", " ")]
    public void Constructor_RejectsBlankVersions(string? schema, string? catalogue) =>
        Assert.ThrowsAny<ArgumentException>(() => new RequirementRevision(F.RevisionId, F.ProjectId,
            1, schema!, catalogue!, F.Content(), F.Timestamp));

    [Fact]
    public void Constructor_RejectsNullContent() =>
        Assert.Throws<ArgumentNullException>("content", () => new RequirementRevision(
            F.RevisionId, F.ProjectId, 1, F.Schema, F.Version, null!, F.Timestamp));

    [Fact]
    public void CreatingLaterRevision_DoesNotChangeEarlierRevision()
    {
        var first = F.Revision();
        var next = new RequirementRevision(Guid.Parse("10000000-0000-0000-0000-000000000003"), F.ProjectId,
            2, F.Schema, F.Version, new(2, [new("ROOM_A", 2)]), F.Timestamp.AddMinutes(1));
        Assert.Equal(1, first.RevisionNumber);
        Assert.Equal(1, first.Content.FloorCount);
        Assert.Equal(1, first.Content.GroundFloorRooms[0].Count);
        Assert.Equal(2, next.Content.FloorCount);
        Assert.NotSame(first.Content, next.Content);
    }
}

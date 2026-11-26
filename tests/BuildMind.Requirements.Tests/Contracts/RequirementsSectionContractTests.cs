using BuildMind.Application.Features.Requirements.Contracts;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class RequirementsSectionContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyRevision_IsRejected(bool spaces)
    {
        Assert.Throws<ArgumentException>(() => Create(spaces, Guid.Empty, F.Version));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, " ")]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, " ")]
    public void MissingCatalogueVersion_IsRejected(bool spaces, string? version) =>
        Assert.ThrowsAny<ArgumentException>(() => Create(spaces, F.RevisionId, version!));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullCollection_IsRejected(bool spaces)
    {
        if (spaces) Assert.Throws<ArgumentNullException>(() => new SaveAdditionalSpacesRequest(F.RevisionId, F.Version, null!));
        else Assert.Throws<ArgumentNullException>(() => new SaveRoomRelationshipsRequest(F.RevisionId, F.Version, null!));
    }

    [Fact]
    public void Spaces_AreDefensivelyCopiedAndEmptyIsExplicitlySupported()
    {
        AdditionalSpaceInput?[] input = [new("SPACE_A", null)];
        var request = new SaveAdditionalSpacesRequest(F.RevisionId, F.Version, input);
        input[0] = null;
        Assert.Equal("SPACE_A", request.AdditionalSpaces[0]!.Option);
        F.AssertReadOnly(request.AdditionalSpaces);
        Assert.Empty(new SaveAdditionalSpacesRequest(F.RevisionId, F.Version, []).AdditionalSpaces);
    }

    [Fact]
    public void Relationships_AreDefensivelyCopiedAndEmptyIsExplicitlySupported()
    {
        RoomRelationshipInput?[] input = [new("ROOM_A", "ROOM_B", "NEAR")];
        var request = new SaveRoomRelationshipsRequest(F.RevisionId, F.Version, input);
        input[0] = null;
        Assert.Equal("NEAR", request.Relationships[0]!.Kind);
        F.AssertReadOnly(request.Relationships);
        Assert.Empty(new SaveRoomRelationshipsRequest(F.RevisionId, F.Version, []).Relationships);
    }

    private static object Create(bool spaces, Guid expected, string version) => spaces
        ? new SaveAdditionalSpacesRequest(expected, version, []) : new SaveRoomRelationshipsRequest(expected, version, []);
}

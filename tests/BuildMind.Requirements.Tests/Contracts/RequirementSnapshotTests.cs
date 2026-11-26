using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.ValueObjects;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class RequirementSnapshotTests
{
    [Fact]
    public void Content_CopiesCallerCollections()
    {
        var rooms = F.Content().GroundFloorRooms.ToList();
        var spaces = F.Content().AdditionalSpaces.ToList();
        var relationships = F.Content().Relationships.ToList();
        var content = new RequirementContent(1, rooms, spaces, relationships);
        rooms[0] = new("CHANGED", 99);
        spaces.Clear();
        relationships.Clear();
        Assert.Equal(F.Content().GroundFloorRooms, content.GroundFloorRooms);
        Assert.Equal(F.Content().AdditionalSpaces, content.AdditionalSpaces);
        Assert.Equal(F.Content().Relationships, content.Relationships);
    }

    [Fact]
    public void Content_CollectionsRejectMutation()
    {
        var content = F.Content();
        F.AssertReadOnly(content.GroundFloorRooms);
        F.AssertReadOnly(content.AdditionalSpaces);
        F.AssertReadOnly(content.Relationships);
    }

    [Fact]
    public void Request_CopiesCallerCollections()
    {
        var rooms = F.Request().GroundFloorRooms!.ToList();
        var spaces = new List<AdditionalSpaceInput?> { new("SPACE_TEXT", "text") };
        var relationships = new List<RoomRelationshipInput?> { new("ROOM_A", "ROOM_B", "NEAR") };
        var request = new SaveRequirementsRequest(null, F.Version, 1, rooms, spaces, relationships);
        rooms[0] = new("CHANGED", 99);
        spaces.Clear();
        relationships.Clear();
        Assert.Equal(F.Request().GroundFloorRooms, request.GroundFloorRooms);
        Assert.Equal(new AdditionalSpaceInput("SPACE_TEXT", "text"), Assert.Single(request.AdditionalSpaces));
        Assert.Equal(new RoomRelationshipInput("ROOM_A", "ROOM_B", "NEAR"), Assert.Single(request.Relationships));
        F.AssertReadOnly(request.GroundFloorRooms!);
        F.AssertReadOnly(request.AdditionalSpaces);
        F.AssertReadOnly(request.Relationships);
    }

    [Fact]
    public void Request_PreservesMissingRoomsAndNormalizesOptionalLists()
    {
        var request = new SaveRequirementsRequest(null, F.Version, 1, null);
        Assert.Null(request.GroundFloorRooms);
        Assert.Empty(request.AdditionalSpaces);
        Assert.Empty(request.Relationships);
        F.AssertReadOnly(request.AdditionalSpaces);
        F.AssertReadOnly(request.Relationships);
    }

    [Fact]
    public void Catalogue_CopiesCallerCollections()
    {
        var rooms = F.Rooms().ToList();
        var spaces = F.Spaces().ToList();
        var relationships = F.Relationships().ToList();
        var methods = new List<string> { "METHOD_A" };
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), rooms, spaces, relationships, methods, false);
        rooms[0] = new("CHANGED", new(0, 99), false);
        spaces.Clear();
        relationships.Clear();
        methods[0] = "CHANGED";
        Assert.Equal(F.Rooms(), catalogue.Rooms);
        Assert.Equal(F.Spaces(), catalogue.AdditionalSpaces);
        Assert.Equal(F.Relationships(), catalogue.Relationships);
        Assert.Equal("METHOD_A", Assert.Single(catalogue.DesignMethods));
    }

    [Fact]
    public void Catalogue_CollectionsRejectMutation()
    {
        var catalogue = F.Catalogue();
        F.AssertReadOnly(catalogue.Rooms);
        F.AssertReadOnly(catalogue.AdditionalSpaces);
        F.AssertReadOnly(catalogue.Relationships);
        F.AssertReadOnly(catalogue.DesignMethods);
    }

    [Fact]
    public void ValidatedSnapshot_SurvivesOriginalInputMutation()
    {
        var rooms = F.Request().GroundFloorRooms!.ToList();
        var policies = F.Rooms().ToList();
        var request = new SaveRequirementsRequest(null, F.Version, 1, rooms);
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), policies, [], [], [], false);
        var result = RequirementContractValidator.Validate(request, catalogue);
        rooms.Clear();
        policies.Clear();
        F.AssertValid(result);
        Assert.Equal(new RoomCount("ROOM_A", 1), result.Content!.GroundFloorRooms[0]);
        Assert.Equal(2, result.Content.GroundFloorRooms.Count);
        F.AssertReadOnly(result.Content.GroundFloorRooms);
    }

    [Fact]
    public void ValidationIssues_RejectMutation()
    {
        var result = F.Validate(F.Request(floors: null));
        F.AssertIssue(result, "REQUIRED", "floorCount");
        F.AssertReadOnly(result.Issues);
    }

    [Fact]
    public void ReadDto_MapsSavedSnapshotAndExposesReadOnlyCollections()
    {
        var revision = F.Revision();
        var dto = new RequirementRevisionDto(revision);
        Assert.Equal(revision.Id, dto.RevisionId);
        Assert.Equal(revision.ProjectId, dto.ProjectId);
        Assert.Equal(revision.RevisionNumber, dto.RevisionNumber);
        Assert.Equal(revision.SchemaVersion, dto.SchemaVersion);
        Assert.Equal(revision.CatalogueVersion, dto.CatalogueVersion);
        Assert.Equal(revision.CreatedAtUtc, dto.CreatedAtUtc);
        Assert.Equal(revision.Content.FloorCount, dto.FloorCount);
        Assert.Equal(revision.Content.DesignMethod, dto.DesignMethod);
        Assert.Equal(revision.Content.GroundFloorRooms.Select(x => new RoomCountInput(x.RoomType, x.Count)), dto.GroundFloorRooms);
        Assert.Equal(new AdditionalSpaceInput("SPACE_TEXT", "text"), Assert.Single(dto.AdditionalSpaces));
        Assert.Equal(new RoomRelationshipInput("ROOM_A", "ROOM_B", "NEAR"), Assert.Single(dto.Relationships));
        F.AssertReadOnly(dto.GroundFloorRooms);
        F.AssertReadOnly(dto.AdditionalSpaces);
        F.AssertReadOnly(dto.Relationships);
    }

    [Fact]
    public void ReadDto_RejectsNullRevision() =>
        Assert.Throws<ArgumentNullException>("revision", () => new RequirementRevisionDto(null!));
}

using BuildMind.Domain.ValueObjects;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Domain;

public sealed class RequirementContentTests
{
    [Fact]
    public void Constructor_AcceptsStructurallyValidContent()
    {
        var content = F.Content();
        Assert.Equal(1, content.FloorCount);
        Assert.Equal(new RoomCount("ROOM_A", 1), content.GroundFloorRooms[0]);
        Assert.Equal(new AdditionalSpaceSelection("SPACE_TEXT", "text"), Assert.Single(content.AdditionalSpaces));
        Assert.Equal(new RoomRelationshipPreference("ROOM_A", "ROOM_B", "NEAR"), Assert.Single(content.Relationships));
        Assert.Equal("METHOD_A", content.DesignMethod);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_RejectsNonPositiveFloorCount(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>("floorCount", () => new RequirementContent(count, []));

    [Fact]
    public void Constructor_DoesNotInventFloorMaximum() =>
        Assert.Equal(int.MaxValue, new RequirementContent(int.MaxValue, []).FloorCount);

    [Fact]
    public void Constructor_RejectsNullRoomCollection() =>
        Assert.Throws<ArgumentNullException>("groundFloorRooms", () => new RequirementContent(1, null!));

    [Theory]
    [InlineData("rooms")]
    [InlineData("spaces")]
    [InlineData("relationships")]
    public void Constructor_RejectsNullCollectionEntries(string section) =>
        Assert.Throws<ArgumentException>(() => new RequirementContent(1,
            section == "rooms" ? [null!] : [new("ROOM_A", 1)],
            section == "spaces" ? [null!] : [], section == "relationships" ? [null!] : []));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void Constructor_RejectsBlankRoomType(string? code) =>
        Assert.ThrowsAny<ArgumentException>(() => new RequirementContent(1, [new(code!, 1)]));

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_RejectsNegativeRoomCount(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>("groundFloorRooms", () => new RequirementContent(1, [new("ROOM_A", count)]));

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void Constructor_AcceptsStructurallyNonnegativeCountWithoutPolicyMaximum(int count) =>
        Assert.Equal(count, Assert.Single(new RequirementContent(1, [new("ROOM_A", count)]).GroundFloorRooms).Count);

    [Fact]
    public void Constructor_RejectsDuplicateRoomTypes() =>
        Assert.Throws<ArgumentException>(() => new RequirementContent(1, [new("ROOM_A", 1), new("ROOM_A", 2)]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_RejectsBlankSpaceOption(string? code) =>
        Assert.ThrowsAny<ArgumentException>(() => new RequirementContent(1, [], [new(code!, null)]));

    [Fact]
    public void Constructor_RejectsDuplicateSpaceOptions() =>
        Assert.Throws<ArgumentException>(() => new RequirementContent(1, [], [new("SPACE_A", null), new("SPACE_A", "x")]));

    [Theory]
    [InlineData(null, "ROOM_B", "NEAR")]
    [InlineData("", "ROOM_B", "NEAR")]
    [InlineData(" ", "ROOM_B", "NEAR")]
    [InlineData("ROOM_A", null, "NEAR")]
    [InlineData("ROOM_A", "", "NEAR")]
    [InlineData("ROOM_A", " ", "NEAR")]
    [InlineData("ROOM_A", "ROOM_B", null)]
    [InlineData("ROOM_A", "ROOM_B", "")]
    [InlineData("ROOM_A", "ROOM_B", " ")]
    public void Constructor_RejectsBlankRelationshipFields(string? source, string? target, string? kind) =>
        Assert.ThrowsAny<ArgumentException>(() => new RequirementContent(1, F.Content().GroundFloorRooms,
            relationships: [new(source!, target!, kind!)]));

    [Theory]
    [InlineData("UNKNOWN", "ROOM_B")]
    [InlineData("ROOM_A", "UNKNOWN")]
    public void Constructor_RejectsUndeclaredRelationshipReference(string source, string target) =>
        Assert.Throws<ArgumentException>(() => new RequirementContent(1, F.Content().GroundFloorRooms,
            relationships: [new(source, target, "NEAR")]));

    [Fact]
    public void Constructor_RejectsExactDuplicateRelationships() =>
        Assert.Throws<ArgumentException>(() => new RequirementContent(1, F.Content().GroundFloorRooms,
            relationships: [new("ROOM_A", "ROOM_B", "NEAR"), new("ROOM_A", "ROOM_B", "NEAR")]));

    [Fact]
    public void Constructor_LeavesDirectionAndSelfRulesToPolicy()
    {
        var content = new RequirementContent(1, F.Content().GroundFloorRooms, relationships:
            [new("ROOM_A", "ROOM_B", "NEAR"), new("ROOM_B", "ROOM_A", "NEAR"), new("ROOM_A", "ROOM_A", "NEAR")]);
        Assert.Equal(3, content.Relationships.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void Constructor_RejectsBlankPresentDesignMethod(string method) =>
        Assert.Throws<ArgumentException>(() => new RequirementContent(1, [], designMethod: method));

    [Fact]
    public void Constructor_NormalizesNullOptionalCollectionsAndAllowsEmptyRooms()
    {
        var content = new RequirementContent(1, []);
        Assert.Empty(content.GroundFloorRooms);
        Assert.Empty(content.AdditionalSpaces);
        Assert.Empty(content.Relationships);
        Assert.Null(content.DesignMethod);
    }
}

using BuildMind.Application.Features.Requirements.Contracts;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class RequirementCatalogueTests
{
    [Fact]
    public void Constructor_AcceptsValidSyntheticCatalogue()
    {
        var catalogue = F.Catalogue();
        Assert.Equal(F.Version, catalogue.Version);
        Assert.Equal(F.Schema, catalogue.SchemaVersion);
        Assert.Equal(new RequirementCountBounds(1, 3), catalogue.FloorCountBounds);
        Assert.Equal(F.Rooms(), catalogue.Rooms);
        Assert.Equal(F.Spaces(), catalogue.AdditionalSpaces);
        Assert.Equal(F.Relationships(), catalogue.Relationships);
        Assert.Equal("METHOD_A", Assert.Single(catalogue.DesignMethods));
        Assert.False(catalogue.DesignMethodRequired);
    }

    [Theory]
    [InlineData(null, "schema")]
    [InlineData("", "schema")]
    [InlineData(" ", "schema")]
    [InlineData("version", null)]
    [InlineData("version", "")]
    [InlineData("version", " ")]
    public void Constructor_RejectsBlankVersions(string? version, string? schema) =>
        Assert.ThrowsAny<ArgumentException>(() => F.Catalogue(version: version!, schema: schema!));

    [Theory]
    [InlineData("floor")]
    [InlineData("rooms")]
    [InlineData("spaces")]
    [InlineData("relationships")]
    public void Constructor_RejectsNullRequiredPolicyInputs(string section) =>
        Assert.Throws<ArgumentNullException>(() => new RequirementCatalogue(F.Version, F.Schema,
            section == "floor" ? null! : new(1, 3), section == "rooms" ? null! : F.Rooms(),
            section == "spaces" ? null! : F.Spaces(), section == "relationships" ? null! : F.Relationships(), [], false));

    [Theory]
    [InlineData("rooms")]
    [InlineData("spaces")]
    [InlineData("relationships")]
    public void Constructor_RejectsNullPolicyEntries(string section) =>
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema, new(1, 3),
            section == "rooms" ? [null!] : F.Rooms(), section == "spaces" ? [null!] : F.Spaces(),
            section == "relationships" ? [null!] : F.Relationships(), [], false));

    [Theory]
    [InlineData("rooms", null)]
    [InlineData("rooms", "")]
    [InlineData("rooms", " ")]
    [InlineData("spaces", null)]
    [InlineData("spaces", "")]
    [InlineData("spaces", " ")]
    [InlineData("relationships", null)]
    [InlineData("relationships", "")]
    [InlineData("relationships", " ")]
    [InlineData("methods", null)]
    [InlineData("methods", "")]
    [InlineData("methods", " ")]
    public void Constructor_RejectsBlankPolicyCodes(string section, string? code) =>
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema, new(1, 3),
            section == "rooms" ? [new(code!, new(0, 1), false)] : F.Rooms(),
            section == "spaces" ? [new(code!, false, null)] : F.Spaces(),
            section == "relationships" ? [new(code!, true, false)] : F.Relationships(),
            section == "methods" ? [code!] : [], false));

    [Theory]
    [InlineData("rooms")]
    [InlineData("spaces")]
    [InlineData("relationships")]
    [InlineData("methods")]
    public void Constructor_RejectsDuplicatePolicyCodes(string section) =>
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema, new(1, 3),
            section == "rooms" ? [F.Rooms()[0], F.Rooms()[0]] : F.Rooms(),
            section == "spaces" ? [F.Spaces()[0], F.Spaces()[0]] : F.Spaces(),
            section == "relationships" ? [F.Relationships()[0], F.Relationships()[0]] : F.Relationships(),
            section == "methods" ? ["METHOD_A", "METHOD_A"] : [], false));

    [Fact]
    public void Constructor_UsesOrdinalCodeUniqueness()
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3),
            [new("ROOM_A", new(0, 1), false), new("room_a", new(0, 1), false)],
            [new("SPACE_A", false, null), new("space_a", false, null)],
            [new("NEAR", false, false), new("near", false, false)], ["METHOD_A", "method_a"], false);
        Assert.Equal(2, catalogue.Rooms.Count);
        Assert.Equal(2, catalogue.AdditionalSpaces.Count);
        Assert.Equal(2, catalogue.Relationships.Count);
        Assert.Equal(2, catalogue.DesignMethods.Count);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(2, 1)]
    public void Constructor_RejectsNegativeOrReversedCountBounds(int minimum, int maximum)
    {
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema,
            new(minimum, maximum), [], [], [], [], false));
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema,
            new(1, 3), [new("ROOM_A", new(minimum, maximum), false)], [], [], [], false));
    }

    [Fact]
    public void Constructor_RejectsNullRoomBounds() =>
        Assert.Throws<ArgumentNullException>(() => new RequirementCatalogue(F.Version, F.Schema,
            new(1, 3), [new("ROOM_A", null!, false)], [], [], [], false));

    [Fact]
    public void Constructor_RejectsZeroFloorMinimum() =>
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema,
            new(0, 3), [], [], [], [], false));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Constructor_AcceptsEqualBounds(int roomCount)
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 1),
            [new("ROOM_A", new(roomCount, roomCount), false)], [], [], [], false);
        Assert.Equal(new RequirementCountBounds(roomCount, roomCount), Assert.Single(catalogue.Rooms).Bounds);
    }

    [Fact]
    public void Constructor_RequiresLimitWhenTextAllowed() =>
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema, new(1, 3),
            F.Rooms(), [new("SPACE_TEXT", true, null)], [], [], false));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Constructor_RejectsNegativeTextLimit(bool allowed) =>
        Assert.Throws<ArgumentException>(() => new RequirementCatalogue(F.Version, F.Schema, new(1, 3),
            F.Rooms(), [new("SPACE_TEXT", allowed, -1)], [], [], false));

    [Fact]
    public void Constructor_AcceptsZeroTextLimitAndAbsentLimitWhenForbidden()
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), F.Rooms(),
            [new("SPACE_TEXT", true, 0), new("SPACE_A", false, null)], [], [], false);
        Assert.Equal(0, catalogue.AdditionalSpaces[0].OtherTextMaximumLength);
        Assert.Null(catalogue.AdditionalSpaces[1].OtherTextMaximumLength);
    }

    [Fact]
    public void Constructor_AcceptsConfiguredEmptyCollections()
    {
        var catalogue = new RequirementCatalogue(F.Version, F.Schema, new(1, 3), [], [], [], [], false);
        Assert.Empty(catalogue.Rooms);
        Assert.Empty(catalogue.AdditionalSpaces);
        Assert.Empty(catalogue.Relationships);
        Assert.Empty(catalogue.DesignMethods);
        Assert.True(catalogue.DesignMethodPolicyAvailable);
    }
}

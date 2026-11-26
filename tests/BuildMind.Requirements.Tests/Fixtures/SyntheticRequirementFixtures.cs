using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Requirements.Tests.Fixtures;

/// <summary>All catalogue codes, limits and identities here are synthetic, not production rules.</summary>
internal static class SyntheticRequirementFixtures
{
    public const string Version = "test-catalogue-v1";
    public const string Schema = "test-schema-v1";
    public static readonly Guid RevisionId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid ProjectId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly DateTimeOffset Timestamp = new(2026, 11, 17, 12, 0, 0, TimeSpan.FromMinutes(330));

    public static RoomCountPolicy[] Rooms() =>
        [new("ROOM_A", new(1, 4), true), new("ROOM_B", new(0, 2), false)];
    public static AdditionalSpacePolicy[] Spaces() =>
        [new("SPACE_A", false, null), new("SPACE_TEXT", true, 5)];
    public static RoomRelationshipPolicy[] Relationships() =>
        [new("NEAR", false, false), new("FLOWS_TO", true, false), new("SELF_OK", true, true)];

    public static RequirementCatalogue Catalogue(
        bool required = false, string version = Version, string schema = Schema) =>
        new(version, schema, new(1, 3), Rooms(), Spaces(), Relationships(), ["METHOD_A"], required);

    public static RequirementCatalogue WithMethods(IEnumerable<string>? methods, bool required = false) =>
        new(Version, Schema, new(1, 3), Rooms(), Spaces(), Relationships(), methods, required);

    // Null rooms deliberately remains distinguishable from the baseline factory.
    public static SaveRequirementsRequest Request(
        string? method = null, int? floors = 1, string? version = Version, Guid? expected = null) =>
        new(expected, version, floors, [new("ROOM_A", 1), new("ROOM_B", 1)], designMethod: method);

    public static SaveRequirementsRequest WithRooms(params RoomCountInput?[] rooms) =>
        new(null, Version, 1, rooms);
    public static SaveRequirementsRequest WithSpaces(params AdditionalSpaceInput?[] spaces) =>
        new(null, Version, 1, Request().GroundFloorRooms, spaces);
    public static SaveRequirementsRequest WithRelationships(params RoomRelationshipInput?[] relationships) =>
        new(null, Version, 1, Request().GroundFloorRooms, relationships: relationships);

    public static RequirementContent Content() => new(1,
        [new("ROOM_A", 1), new("ROOM_B", 1)],
        [new("SPACE_TEXT", "text")], [new("ROOM_A", "ROOM_B", "NEAR")], "METHOD_A");

    public static RequirementRevision Revision() =>
        new(RevisionId, ProjectId, 1, Schema, Version, Content(), Timestamp);

    public static RequirementValidationResult Validate(SaveRequirementsRequest? request) =>
        RequirementContractValidator.Validate(request, Catalogue());

    public static void AssertValid(RequirementValidationResult result)
    {
        Assert.Equal(RequirementValidationStatus.Valid, result.Status);
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Content);
        Assert.Equal(Schema, result.SchemaVersion);
        Assert.Equal(Version, result.CatalogueVersion);
    }

    public static void AssertIssue(RequirementValidationResult result, string code, string field,
        RequirementValidationStatus status = RequirementValidationStatus.Invalid)
    {
        Assert.Equal(status, result.Status);
        Assert.Contains(result.Issues, issue => issue.Code == code && issue.Field == field);
        Assert.Null(result.Content);
        Assert.Null(result.SchemaVersion);
        Assert.Null(result.CatalogueVersion);
    }

    public static void AssertReadOnly<T>(IReadOnlyList<T> items)
    {
        var list = Assert.IsAssignableFrom<IList<T>>(items);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(default!));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        if (list.Count > 0)
            Assert.Throws<NotSupportedException>(() => list[0] = default!);
    }
}

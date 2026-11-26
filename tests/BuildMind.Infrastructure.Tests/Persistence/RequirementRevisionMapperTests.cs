using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.Persistence.Mapping;
using BuildMind.Infrastructure.Persistence.Records;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>Explicit domain ↔ persistence mapping for M5 requirement revisions (BE-M4-D03). No database.</summary>
public sealed class RequirementRevisionMapperTests
{
    private static readonly Guid ProjectId = Guid.Parse("8b1f6a52-2c4e-4d7a-9e31-5a0c7d2b4e61");
    private static readonly Guid RevisionId = Guid.Parse("c3d9e0a1-7f24-4b8e-a6d5-1e2f3a4b5c6d");
    private static readonly DateTimeOffset CreatedAt =
        new DateTimeOffset(2026, 11, 18, 10, 20, 30, TimeSpan.Zero).AddTicks(1_234_560);

    internal static RequirementRevision SampleRevision(Guid? id = null, Guid? projectId = null, long number = 1) => new(
        id ?? RevisionId,
        projectId ?? ProjectId,
        number,
        "requirements-v1",
        "catalogue-2026.11",
        new RequirementContent(
            2,
            [new RoomCount("bedroom", 3), new RoomCount("kitchen", 1)],
            [new AdditionalSpaceSelection("store", null), new AdditionalSpaceSelection("other", "prayer room")],
            [new RoomRelationshipPreference("kitchen", "bedroom", "not-adjacent")],
            "synthetic-method"),
        CreatedAt);

    [Fact]
    public void Round_trip_preserves_every_field_and_value_object()
    {
        var original = SampleRevision();

        var restored = RequirementRevisionMapper.ToDomain(RequirementRevisionMapper.ToRecord(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.ProjectId, restored.ProjectId);
        Assert.Equal(original.RevisionNumber, restored.RevisionNumber);
        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.CatalogueVersion, restored.CatalogueVersion);
        Assert.Equal(original.CreatedAtUtc, restored.CreatedAtUtc);
        Assert.Equal(original.Content.FloorCount, restored.Content.FloorCount);
        Assert.Equal(original.Content.GroundFloorRooms, restored.Content.GroundFloorRooms);
        Assert.Equal(original.Content.AdditionalSpaces, restored.Content.AdditionalSpaces);
        Assert.Equal(original.Content.Relationships, restored.Content.Relationships);
        Assert.Equal(original.Content.DesignMethod, restored.Content.DesignMethod);
    }

    [Fact]
    public void Optional_content_round_trips_as_empty_and_null()
    {
        var content = new RequirementContent(1, [new RoomCount("hall", 1)]);

        var restored = RequirementRevisionMapper.DeserializeContent(RequirementRevisionMapper.SerializeContent(content));

        Assert.Empty(restored.AdditionalSpaces);
        Assert.Empty(restored.Relationships);
        Assert.Null(restored.DesignMethod);
        Assert.Null(restored.AdditionalSpaces.FirstOrDefault()?.OtherText);
    }

    [Fact]
    public void Serialisation_is_deterministic_and_independent_of_domain_shape()
    {
        var first = RequirementRevisionMapper.SerializeContent(SampleRevision().Content);
        var second = RequirementRevisionMapper.SerializeContent(SampleRevision().Content);

        Assert.Equal(first, second);
        Assert.StartsWith("{\"floorCount\":2,\"groundFloorRooms\":[{\"roomType\":\"bedroom\",\"count\":3}", first, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_keeps_the_utc_instant()
    {
        var record = RequirementRevisionMapper.ToRecord(SampleRevision());

        Assert.Equal(TimeSpan.Zero, record.CreatedAtUtc.Offset);
        Assert.Equal(CreatedAt.UtcTicks, record.CreatedAtUtc.UtcTicks);
    }

    [Theory]
    [InlineData("{\"floorCount\":0,\"groundFloorRooms\":[]}")]                                                  // floor count must be > 0
    [InlineData("{\"floorCount\":1,\"groundFloorRooms\":[{\"roomType\":\"a\",\"count\":1},{\"roomType\":\"a\",\"count\":2}]}")] // duplicate room type
    [InlineData("{\"floorCount\":1}")]                                                                          // rooms missing
    [InlineData("not json SYNTHETIC-SECRET-7f3a9c41")]
    public void Stored_data_violating_the_domain_contract_is_reported_as_corrupt_without_echo(string json)
    {
        var record = RequirementRevisionMapper.ToRecord(SampleRevision());
        record.ContentJson = json;

        var exception = Assert.Throws<InvalidDataException>(() => RequirementRevisionMapper.ToDomain(record));

        Assert.DoesNotContain("SYNTHETIC-SECRET", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_stored_identity_is_rejected_by_the_domain_constructor()
    {
        var record = RequirementRevisionMapper.ToRecord(SampleRevision());
        record.ProjectId = Guid.Empty;

        Assert.Throws<InvalidDataException>(() => RequirementRevisionMapper.ToDomain(record));
    }

    [Fact]
    public void Versions_longer_than_the_column_are_rejected_before_reaching_mysql()
    {
        var revision = new RequirementRevision(
            RevisionId, ProjectId, 1, new string('v', 65), "catalogue", SampleRevision().Content, CreatedAt);

        var exception = Assert.Throws<ArgumentException>(() => RequirementRevisionMapper.ToRecord(revision));

        Assert.DoesNotContain(new string('v', 65), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Version_columns_have_an_explicit_storage_limit()
    {
        Assert.Equal(64, RequirementRevisionRecord.VersionMaxLength);
    }
}

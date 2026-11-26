using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using BuildMind.Infrastructure.Persistence.Records;

namespace BuildMind.Infrastructure.Persistence.Mapping;

/// <summary>
/// Explicit mapping between M5 <see cref="RequirementRevision"/> and <see cref="RequirementRevisionRecord"/>.
/// Reconstruction goes through the domain constructors, so every domain invariant is re-checked on read;
/// stored data that violates them is reported as corrupt rather than silently accepted.
/// </summary>
internal static class RequirementRevisionMapper
{
    // Fixed, explicit contract for the stored document: property names, order and representation never
    // depend on the domain type's shape. MySQL's native json may reorder keys, so this is NOT used for hashing.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static RequirementRevisionRecord ToRecord(RequirementRevision revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        if (revision.SchemaVersion.Length > RequirementRevisionRecord.VersionMaxLength
            || revision.CatalogueVersion.Length > RequirementRevisionRecord.VersionMaxLength)
        {
            // Fail clearly before MySQL's "data too long" error; the value itself is not echoed.
            throw new ArgumentException(
                $"Schema and catalogue versions are limited to {RequirementRevisionRecord.VersionMaxLength} characters.",
                nameof(revision));
        }

        return new RequirementRevisionRecord
        {
            Id = revision.Id,
            ProjectId = revision.ProjectId,
            RevisionNumber = revision.RevisionNumber,
            SchemaVersion = revision.SchemaVersion,
            CatalogueVersion = revision.CatalogueVersion,
            ContentJson = SerializeContent(revision.Content),
            CreatedAtUtc = revision.CreatedAtUtc,
        };
    }

    public static RequirementRevision ToDomain(RequirementRevisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            return new RequirementRevision(
                record.Id,
                record.ProjectId,
                record.RevisionNumber,
                record.SchemaVersion,
                record.CatalogueVersion,
                DeserializeContent(record.ContentJson),
                record.CreatedAtUtc);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidDataException)
        {
            // Never echo stored content: it is customer data.
            throw new InvalidDataException("A stored requirement revision violates the domain contract.", exception);
        }
    }

    public static string SerializeContent(RequirementContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var document = new ContentDocument(
            content.FloorCount,
            content.GroundFloorRooms.Select(r => new RoomCountDocument(r.RoomType, r.Count)).ToArray(),
            content.AdditionalSpaces.Select(s => new AdditionalSpaceDocument(s.Option, s.OtherText)).ToArray(),
            content.Relationships.Select(r => new RelationshipDocument(r.SourceRoomType, r.TargetRoomType, r.Kind)).ToArray(),
            content.DesignMethod);

        return JsonSerializer.Serialize(document, Options);
    }

    public static RequirementContent DeserializeContent(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var document = JsonSerializer.Deserialize<ContentDocument>(json, Options)
            ?? throw new InvalidDataException("Stored requirement content is empty.");

        return new RequirementContent(
            document.FloorCount,
            (document.GroundFloorRooms ?? throw new InvalidDataException("Stored rooms are missing."))
                .Select(r => new RoomCount(r.RoomType, r.Count)),
            (document.AdditionalSpaces ?? []).Select(s => new AdditionalSpaceSelection(s.Option, s.OtherText)),
            (document.Relationships ?? []).Select(r => new RoomRelationshipPreference(r.SourceRoomType, r.TargetRoomType, r.Kind)),
            document.DesignMethod);
    }

    private sealed record ContentDocument(
        int FloorCount,
        RoomCountDocument[]? GroundFloorRooms,
        AdditionalSpaceDocument[]? AdditionalSpaces,
        RelationshipDocument[]? Relationships,
        string? DesignMethod);

    private sealed record RoomCountDocument(string RoomType, int Count);

    private sealed record AdditionalSpaceDocument(string Option, string? OtherText);

    private sealed record RelationshipDocument(string SourceRoomType, string TargetRoomType, string Kind);
}

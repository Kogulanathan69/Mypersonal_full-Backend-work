namespace BuildMind.Domain.ValueObjects;

public sealed record RoomCount(string RoomType, int Count);

public sealed record AdditionalSpaceSelection(string Option, string? OtherText);

/// <summary>References requirement room types, not M3 geometry element IDs.</summary>
public sealed record RoomRelationshipPreference(string SourceRoomType, string TargetRoomType, string Kind);

/// <summary>
/// Immutable customer intent. Business validity is checked against the exact approved catalogue
/// by the application boundary; Domain enforces structure without a production catalogue.
/// </summary>
public sealed class RequirementContent
{
    public int FloorCount { get; }
    public IReadOnlyList<RoomCount> GroundFloorRooms { get; }
    public IReadOnlyList<AdditionalSpaceSelection> AdditionalSpaces { get; }
    public IReadOnlyList<RoomRelationshipPreference> Relationships { get; }
    public string? DesignMethod { get; }

    public RequirementContent(
        int floorCount,
        IEnumerable<RoomCount> groundFloorRooms,
        IEnumerable<AdditionalSpaceSelection>? additionalSpaces = null,
        IEnumerable<RoomRelationshipPreference>? relationships = null,
        string? designMethod = null)
    {
        ArgumentNullException.ThrowIfNull(groundFloorRooms);
        if (floorCount <= 0) throw new ArgumentOutOfRangeException(nameof(floorCount));
        FloorCount = floorCount;
        GroundFloorRooms = Copy(groundFloorRooms);
        AdditionalSpaces = Copy(additionalSpaces ?? []);
        Relationships = Copy(relationships ?? []);
        if (designMethod is not null) ArgumentException.ThrowIfNullOrWhiteSpace(designMethod);
        DesignMethod = designMethod;

        var roomTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in GroundFloorRooms)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(room.RoomType);
            if (room.Count < 0) throw new ArgumentOutOfRangeException(nameof(groundFloorRooms));
            if (!roomTypes.Add(room.RoomType))
                throw new ArgumentException("Room types must be unique.", nameof(groundFloorRooms));
        }
        var spaceOptions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var space in AdditionalSpaces)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(space.Option);
            if (!spaceOptions.Add(space.Option))
                throw new ArgumentException("Additional space options must be unique.", nameof(additionalSpaces));
        }
        var relationshipKeys = new HashSet<(string Source, string Target, string Kind)>();
        foreach (var relationship in Relationships)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(relationship.SourceRoomType);
            ArgumentException.ThrowIfNullOrWhiteSpace(relationship.TargetRoomType);
            ArgumentException.ThrowIfNullOrWhiteSpace(relationship.Kind);
            if (!roomTypes.Contains(relationship.SourceRoomType) || !roomTypes.Contains(relationship.TargetRoomType))
                throw new ArgumentException("Relationships must reference declared room types.", nameof(relationships));
            if (!relationshipKeys.Add((relationship.SourceRoomType, relationship.TargetRoomType, relationship.Kind)))
                throw new ArgumentException("Relationships must be unique.", nameof(relationships));
        }
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) where T : class
    {
        var items = source.ToArray();
        if (items.Any(item => item is null))
            throw new ArgumentException("Requirement collections cannot contain null entries.", nameof(source));
        return Array.AsReadOnly(items);
    }
}

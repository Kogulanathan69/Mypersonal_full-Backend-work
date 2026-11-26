namespace BuildMind.Application.Features.Requirements.Contracts;

/// <summary>Inclusive approved bounds, with no built-in business values.</summary>
public sealed record RequirementCountBounds(int Minimum, int Maximum);
public sealed record RoomCountPolicy(string RoomType, RequirementCountBounds Bounds, bool Required);
public sealed record AdditionalSpacePolicy(string Option, bool AllowsOtherText, int? OtherTextMaximumLength);

/// <summary>Direction and self-reference behavior must be supplied, not inferred.</summary>
public sealed record RoomRelationshipPolicy(string Kind, bool Directed, bool AllowsSelfReference);

/// <summary>
/// Caller-supplied approved policy snapshot. Construction does not certify business approval.
/// Empty optional catalogues allow no selections of that kind. There are no production defaults.
/// </summary>
public sealed class RequirementCatalogue
{
    public string Version { get; }
    public string SchemaVersion { get; }
    public RequirementCountBounds FloorCountBounds { get; }
    public IReadOnlyList<RoomCountPolicy> Rooms { get; }
    public IReadOnlyList<AdditionalSpacePolicy> AdditionalSpaces { get; }
    public IReadOnlyList<RoomRelationshipPolicy> Relationships { get; }
    public IReadOnlyList<string> DesignMethods { get; }
    /// <summary>Null constructor input means unavailable; an empty collection is configured and allows no choices.</summary>
    public bool DesignMethodPolicyAvailable { get; }
    public bool DesignMethodRequired { get; }

    public RequirementCatalogue(
        string version,
        string schemaVersion,
        RequirementCountBounds floorCountBounds,
        IEnumerable<RoomCountPolicy> rooms,
        IEnumerable<AdditionalSpacePolicy> additionalSpaces,
        IEnumerable<RoomRelationshipPolicy> relationships,
        IEnumerable<string>? designMethods,
        bool designMethodRequired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ValidateBounds(floorCountBounds);
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(additionalSpaces);
        ArgumentNullException.ThrowIfNull(relationships);
        if (floorCountBounds.Minimum <= 0)
            throw new ArgumentException("Floor count bounds must be positive.", nameof(floorCountBounds));

        var roomArray = rooms.ToArray();
        var spaceArray = additionalSpaces.ToArray();
        var relationshipArray = relationships.ToArray();
        var methodArray = designMethods?.ToArray() ?? [];
        if (roomArray.Any(item => item is null)
            || spaceArray.Any(item => item is null)
            || relationshipArray.Any(item => item is null))
            throw new ArgumentException("Policy entries cannot be null.");
        ValidateCodes(roomArray.Select(item => item.RoomType));
        ValidateCodes(spaceArray.Select(item => item.Option));
        ValidateCodes(relationshipArray.Select(item => item.Kind));
        ValidateCodes(methodArray);
        foreach (var room in roomArray) ValidateBounds(room.Bounds);
        foreach (var space in spaceArray)
        {
            if (space.AllowsOtherText && space.OtherTextMaximumLength is null)
                throw new ArgumentException("An approved text limit is required when other text is allowed.");
            if (space.OtherTextMaximumLength is < 0)
                throw new ArgumentException("Text length configuration cannot be negative.");
        }

        Version = version;
        SchemaVersion = schemaVersion;
        FloorCountBounds = floorCountBounds;
        Rooms = Array.AsReadOnly(roomArray);
        AdditionalSpaces = Array.AsReadOnly(spaceArray);
        Relationships = Array.AsReadOnly(relationshipArray);
        DesignMethods = Array.AsReadOnly(methodArray);
        DesignMethodPolicyAvailable = designMethods is not null;
        DesignMethodRequired = designMethodRequired;
    }

    private static void ValidateBounds(RequirementCountBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (bounds.Minimum < 0 || bounds.Minimum > bounds.Maximum)
            throw new ArgumentException("Approved bounds must be nonnegative and minimum cannot exceed maximum.", nameof(bounds));
    }

    private static void ValidateCodes(IEnumerable<string> codes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            if (string.IsNullOrWhiteSpace(code) || !seen.Add(code))
                throw new ArgumentException("Policy codes must be nonblank and unique (ordinal comparison).");
        }
    }
}

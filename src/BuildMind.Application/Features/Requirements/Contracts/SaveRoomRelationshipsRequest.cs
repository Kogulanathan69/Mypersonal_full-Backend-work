namespace BuildMind.Application.Features.Requirements.Contracts;

/// <summary>Replaces relationship preferences only; existing catalogue rules remain authoritative.</summary>
public sealed class SaveRoomRelationshipsRequest
{
    public Guid ExpectedRevisionId { get; }
    public string CatalogueVersion { get; }
    public IReadOnlyList<RoomRelationshipInput?> Relationships { get; }

    public SaveRoomRelationshipsRequest(Guid expectedRevisionId, string catalogueVersion, IEnumerable<RoomRelationshipInput?> relationships)
    {
        if (expectedRevisionId == Guid.Empty) throw new ArgumentException("An existing revision identity is required.", nameof(expectedRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueVersion);
        ArgumentNullException.ThrowIfNull(relationships);
        ExpectedRevisionId = expectedRevisionId;
        CatalogueVersion = catalogueVersion;
        Relationships = Array.AsReadOnly(relationships.ToArray());
    }
}

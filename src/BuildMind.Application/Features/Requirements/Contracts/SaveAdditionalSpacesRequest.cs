namespace BuildMind.Application.Features.Requirements.Contracts;

/// <summary>Isolated Day 04 section replacement against an existing immutable requirements revision.</summary>
public sealed class SaveAdditionalSpacesRequest
{
    public Guid ExpectedRevisionId { get; }
    public string CatalogueVersion { get; }
    public IReadOnlyList<AdditionalSpaceInput?> AdditionalSpaces { get; }

    public SaveAdditionalSpacesRequest(Guid expectedRevisionId, string catalogueVersion, IEnumerable<AdditionalSpaceInput?> additionalSpaces)
    {
        if (expectedRevisionId == Guid.Empty) throw new ArgumentException("An existing revision identity is required.", nameof(expectedRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueVersion);
        ArgumentNullException.ThrowIfNull(additionalSpaces);
        ExpectedRevisionId = expectedRevisionId;
        CatalogueVersion = catalogueVersion;
        AdditionalSpaces = Array.AsReadOnly(additionalSpaces.ToArray());
    }
}

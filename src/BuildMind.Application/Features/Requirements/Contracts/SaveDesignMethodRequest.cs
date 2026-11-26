namespace BuildMind.Application.Features.Requirements.Contracts;

/// <summary>Provisional isolated method replacement; null is validated by authoritative policy.</summary>
public sealed class SaveDesignMethodRequest
{
    public Guid ExpectedRevisionId { get; }
    public string CatalogueVersion { get; }
    public string? DesignMethod { get; }

    public SaveDesignMethodRequest(Guid expectedRevisionId, string catalogueVersion, string? designMethod)
    {
        if (expectedRevisionId == Guid.Empty) throw new ArgumentException("An existing revision is required.", nameof(expectedRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueVersion);
        ExpectedRevisionId = expectedRevisionId;
        CatalogueVersion = catalogueVersion;
        DesignMethod = designMethod;
    }
}

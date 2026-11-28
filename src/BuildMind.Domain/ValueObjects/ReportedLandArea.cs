namespace BuildMind.Domain.ValueObjects;

public enum ReportedAreaUnit
{
    M2,
    FT2,
    Unknown
}

public enum ReportedAreaReviewStatus
{
    Unreviewed,
    ManualReviewRequired,
    ManuallyReviewed
}

/// <summary>
/// Identifies the survey document from which a reported area was extracted.
/// This reference does not establish that the document is legally verified.
/// </summary>
public sealed class LandAreaSourceReference
{
    public Guid FileId { get; }
    public int? PageNumber { get; }
    public string? ReferenceText { get; }

    public LandAreaSourceReference(
        Guid fileId,
        int? pageNumber = null,
        string? referenceText = null)
    {
        if (fileId == Guid.Empty)
            throw new ArgumentException("Source file identity is required.", nameof(fileId));

        if (pageNumber is <= 0)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        if (referenceText is { Length: > 500 })
            throw new ArgumentException("Source reference text cannot exceed 500 characters.", nameof(referenceText));

        FileId = fileId;
        PageNumber = pageNumber;
        ReferenceText = referenceText;
    }
}

/// <summary>
/// Preserves the area reported by a survey document separately from
/// the area calculated from boundary geometry.
/// </summary>
public sealed class ReportedLandArea
{
    private const double SquareFeetToSquareMetres = 0.09290304;

    public double OriginalValue { get; }
    public ReportedAreaUnit Unit { get; }
    public double? AreaM2 { get; }
    public LandAreaSourceReference Source { get; }
    public ReportedAreaReviewStatus ReviewStatus { get; }

    public ReportedLandArea(
        double originalValue,
        ReportedAreaUnit unit,
        LandAreaSourceReference source,
        ReportedAreaReviewStatus reviewStatus = ReportedAreaReviewStatus.Unreviewed)
    {
        if (!double.IsFinite(originalValue) || originalValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(originalValue));

        if (!Enum.IsDefined(unit))
            throw new ArgumentOutOfRangeException(nameof(unit));

        if (!Enum.IsDefined(reviewStatus))
            throw new ArgumentOutOfRangeException(nameof(reviewStatus));

        ArgumentNullException.ThrowIfNull(source);

        OriginalValue = originalValue;
        Unit = unit;
        Source = source;
        ReviewStatus = reviewStatus;

        AreaM2 = unit switch
        {
            ReportedAreaUnit.M2 => originalValue,
            ReportedAreaUnit.FT2 => originalValue * SquareFeetToSquareMetres,
            ReportedAreaUnit.Unknown => null,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
    }
}

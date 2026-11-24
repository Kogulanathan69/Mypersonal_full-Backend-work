using System.Text.Json;

namespace BuildMind.Application.Features.Planning;

/// <summary>One customer decision about one extracted measurement (index into the stored extraction).</summary>
public sealed record MeasurementDecision(int MeasurementIndex, string Action, double? CorrectedValue, string? CorrectedUnit);

/// <summary>Normalised review entry: metres only when the value is known; nothing here becomes geometry.</summary>
public sealed record ReviewedMeasurement(int MeasurementIndex, string MeasurementName, string Action, double? ValueMetres, string Basis);

public sealed record SurveyReviewRevision(Guid ReviewId, Guid ExtractionId, Guid SurveyId, int Version, Guid ReviewedBy,
    DateTimeOffset CreatedAtUtc, IReadOnlyList<ReviewedMeasurement> Measurements, string? Note);

public interface ISurveyReviewStore
{
    Task<IReadOnlyList<SurveyReviewRevision>> ListAsync(Guid projectId, Guid surveyId, CancellationToken token);

    /// <summary>Protected append (project lock + owner + authorization version); version must be latest + 1.</summary>
    Task<ProtectedWriteStatus> AppendAsync(SurveyReviewRevision revision, Guid projectId, long authorizationVersion, CancellationToken token);
}

/// <summary>
/// Customer review/correction of extracted survey measurements, versioned and immutable. Reviewed values remain
/// customer-asserted evidence: they never confirm land and are never converted into coordinates, bearings or polygons.
/// </summary>
public sealed class SurveyReviewService(PlanningAccess access, ISurveyExtractionReader extractions, ISurveyReviewStore reviews, TimeProvider time)
{
    public const double FeetToMetres = 0.3048;
    public const int MaxDecisions = 200;

    public async Task<PlanningResult<SurveyReviewRevision>> ReviewAsync(Guid projectId, Guid surveyId, Guid extractionId, int expectedVersion,
        IReadOnlyList<MeasurementDecision>? decisions, string? note, CancellationToken token)
    {
        var (status, actor, version) = await access.AuthorizeAsync(projectId, token).ConfigureAwait(false);
        if (status != PlanningStatus.Success) return new(status);
        if (decisions is null || decisions.Count is 0 or > MaxDecisions || note is { Length: > 1000 } || expectedVersion < 0)
            return new(PlanningStatus.ValidationFailed, Code: "REVIEW_INVALID");
        var extraction = await extractions.GetAsync(projectId, surveyId, token).ConfigureAwait(false);
        if (extraction is null) return new(PlanningStatus.NotFound);
        if (extraction.ExtractionId != extractionId) return new(PlanningStatus.Conflict, Code: "STALE_EXTRACTION");

        using var document = JsonDocument.Parse(extraction.ExtractionJson);
        var measurements = document.RootElement.TryGetProperty("measurements", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().ToArray() : [];
        if (decisions.Select(d => d.MeasurementIndex).Distinct().Count() != decisions.Count)
            return new(PlanningStatus.ValidationFailed, Code: "DUPLICATE_DECISION");
        var reviewed = new List<ReviewedMeasurement>();
        foreach (var d in decisions.OrderBy(d => d.MeasurementIndex))
        {
            if (d.MeasurementIndex < 0 || d.MeasurementIndex >= measurements.Length)
                return new(PlanningStatus.ValidationFailed, Code: "MEASUREMENT_INDEX_INVALID");
            var source = measurements[d.MeasurementIndex];
            var name = source.TryGetProperty("measurement_name", out var n) ? n.GetString() ?? "" : "";
            switch (d.Action)
            {
                case "CONFIRM":
                    if (d.CorrectedValue is not null || d.CorrectedUnit is not null) return new(PlanningStatus.ValidationFailed, Code: "CONFIRM_TAKES_NO_VALUE");
                    var value = source.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (double?)null;
                    var unit = source.TryGetProperty("unit", out var u) ? u.GetString() : null;
                    if (value is null || Metres(value.Value, unit) is not { } confirmed)
                        return new(PlanningStatus.ValidationFailed, Code: "NOTHING_TO_CONFIRM");
                    reviewed.Add(new(d.MeasurementIndex, name, "CONFIRMED", confirmed, "EXTRACTED_VALUE_CONFIRMED_BY_CUSTOMER"));
                    break;
                case "CORRECT":
                    if (d.CorrectedValue is not { } corrected || !double.IsFinite(corrected) || corrected <= 0 || corrected > 100_000
                        || Metres(corrected, d.CorrectedUnit) is not { } metres)
                        return new(PlanningStatus.ValidationFailed, Code: "CORRECTION_INVALID");
                    reviewed.Add(new(d.MeasurementIndex, name, "CORRECTED", metres, "CUSTOMER_ENTERED_VALUE"));
                    break;
                case "REJECT":
                    if (d.CorrectedValue is not null || d.CorrectedUnit is not null) return new(PlanningStatus.ValidationFailed, Code: "REJECT_TAKES_NO_VALUE");
                    reviewed.Add(new(d.MeasurementIndex, name, "REJECTED", null, "CUSTOMER_REJECTED"));
                    break;
                default:
                    return new(PlanningStatus.ValidationFailed, Code: "ACTION_INVALID");
            }
        }
        var revision = new SurveyReviewRevision(Guid.NewGuid(), extractionId, surveyId, checked(expectedVersion + 1), actor, PlanningClock.Now(time),
            reviewed, note?.Trim());
        return await reviews.AppendAsync(revision, projectId, version, token).ConfigureAwait(false) switch
        {
            ProtectedWriteStatus.Written => PlanningResult<SurveyReviewRevision>.Ok(revision),
            ProtectedWriteStatus.Conflict => new(PlanningStatus.Conflict, Code: "STALE_REVIEW_VERSION"),
            _ => new(PlanningStatus.NotFound),
        };
    }

    /// <summary>Unit normalisation; "unknown" units are never guessed.</summary>
    public static double? Metres(double value, string? unit) => unit switch
    {
        "m" => Math.Round(value, 4),
        "ft" => Math.Round(value * FeetToMetres, 4),
        _ => null,
    };
}

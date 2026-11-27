using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMind.Application.Features.Land.Abstractions;
using BuildMind.Application.Features.Land.Contracts;

namespace BuildMind.Infrastructure.Integration.Land;

public sealed class PythonLandGeometryValidator(
    PythonServiceClient pythonClient) : ILandGeometryValidator
{
    public async Task<LandGeometryValidationResult> ValidateAsync(
        LandGeometryValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wireRequest = new ValidationRequest(
            request.SourceCrs,
            request.CalculationCrs,
            request.Exterior,
            request.Holes);

        var response = await pythonClient.PostJsonAsync<
            ValidationRequest,
            ValidationResponse>(
                "geometry/land/validate",
                wireRequest,
                response => IsRecognizedResponse(response, request),
                cancellationToken);

        bool isValid = response.ValidationStatus == "VALID";

        return new LandGeometryValidationResult(
            isValid,
            response.SourceCrs,
            response.CalculationCrs,
            response.GeometryCrs,
            response.Measurements is null
                ? null
                : new LandGeometryMeasurements(
                    response.Measurements.AreaM2,
                    response.Measurements.PerimeterM),
            response.NormalizedMetricExterior,
            response.NormalizedMetricHoles,
            response.DiagnosticCode,
            response.GeoJson?.GetRawText());
    }

    private static bool IsRecognizedResponse(ValidationResponse response, LandGeometryValidationRequest request)
    {
        if (response.LegalBoundaryVerified != false ||
            !CrsMatches(response.SourceCrs, request.SourceCrs) ||
            !CrsMatches(response.CalculationCrs, request.CalculationCrs) ||
            !CrsMatches(response.GeometryCrs, "EPSG:4326"))
        {
            return false;
        }

        if (response.ValidationStatus == "INVALID")
            return !string.IsNullOrWhiteSpace(response.DiagnosticCode);

        return response.ValidationStatus == "VALID" &&
            response.Measurements is not null &&
            IsValidRing(response.NormalizedMetricExterior) &&
            response.NormalizedMetricHoles is not null &&
            response.NormalizedMetricHoles.All(IsValidRing) &&
            double.IsFinite(response.Measurements.AreaM2) &&
            response.Measurements.AreaM2 > 0 &&
            double.IsFinite(response.Measurements.PerimeterM) &&
            response.Measurements.PerimeterM > 0;
    }

    private static bool CrsMatches(string? actual, string? expected)
    {
        var normalizedActual = NormalizeCrs(actual);
        var normalizedExpected = NormalizeCrs(expected);
        return normalizedActual is not null &&
               normalizedExpected is not null &&
               string.Equals(normalizedActual, normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeCrs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (trimmed.All(char.IsAsciiDigit))
            return $"EPSG:{trimmed}";

        if (trimmed.StartsWith("EPSG::", StringComparison.OrdinalIgnoreCase))
        {
            var code = trimmed[6..];
            if (code.Length > 0 && code.All(char.IsAsciiDigit))
                return $"EPSG:{code}";
        }

        if (trimmed.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase))
        {
            var code = trimmed[5..];
            if (code.Length > 0 && code.All(char.IsAsciiDigit))
                return $"EPSG:{code}";
        }

        const string urnPrefix = "urn:ogc:def:crs:EPSG::";
        if (trimmed.StartsWith(urnPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = trimmed[urnPrefix.Length..];
            if (code.Length > 0 && code.All(char.IsAsciiDigit))
                return $"EPSG:{code}";
        }

        if (string.Equals(trimmed, "WGS84", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "WGS 84", StringComparison.OrdinalIgnoreCase))
        {
            return "EPSG:4326";
        }

        return trimmed;
    }

    private static bool IsValidRing(IReadOnlyList<IReadOnlyList<double>>? ring) =>
        ring is { Count: >= 4 } &&
        ring.All(point => point is { Count: 2 } && point.All(double.IsFinite)) &&
        ring[0].SequenceEqual(ring[^1]);

    private sealed record ValidationRequest(
        [property: JsonPropertyName("source_crs")] string SourceCrs,
        [property: JsonPropertyName("calculation_crs")] string CalculationCrs,
        [property: JsonPropertyName("exterior")] IReadOnlyList<IReadOnlyList<double>> Exterior,
        [property: JsonPropertyName("holes")] IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> Holes);

    private sealed record Measurements(
        [property: JsonPropertyName("area_m2")] double AreaM2,
        [property: JsonPropertyName("perimeter_m")] double PerimeterM);

    private sealed record ValidationResponse(
        [property: JsonPropertyName("validation_status")] string ValidationStatus,
        [property: JsonPropertyName("source_crs")] string SourceCrs,
        [property: JsonPropertyName("calculation_crs")] string CalculationCrs,
        [property: JsonPropertyName("geometry_crs")] string GeometryCrs,
        [property: JsonPropertyName("measurements")] Measurements? Measurements,
        [property: JsonPropertyName("normalized_metric_exterior")] IReadOnlyList<IReadOnlyList<double>>? NormalizedMetricExterior,
        [property: JsonPropertyName("normalized_metric_holes")] IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>>? NormalizedMetricHoles,
        [property: JsonPropertyName("geojson")] JsonElement? GeoJson,
        [property: JsonPropertyName("diagnostic_code")] string? DiagnosticCode,
        [property: JsonPropertyName("legal_boundary_verified")] bool? LegalBoundaryVerified);
}

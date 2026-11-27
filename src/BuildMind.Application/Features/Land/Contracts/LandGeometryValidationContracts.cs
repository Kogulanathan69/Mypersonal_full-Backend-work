namespace BuildMind.Application.Features.Land.Contracts;

public sealed record LandGeometryValidationRequest(
    string SourceCrs,
    string CalculationCrs,
    IReadOnlyList<IReadOnlyList<double>> Exterior,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>> Holes);

public sealed record LandGeometryMeasurements(
    double AreaM2,
    double PerimeterM);

public sealed record LandGeometryValidationResult(
    bool IsValid,
    string SourceCrs,
    string CalculationCrs,
    string GeometryCrs,
    LandGeometryMeasurements? Measurements,
    IReadOnlyList<IReadOnlyList<double>>? NormalizedMetricExterior,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<double>>>? NormalizedMetricHoles,
    string? DiagnosticCode,
    string? GeoJsonJson = null);

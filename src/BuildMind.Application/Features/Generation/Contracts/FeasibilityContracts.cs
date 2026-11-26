// BuildMind Member 6 | BE-M6-D01-E | Provisional JSON DTOs

using System.Text.Json.Serialization;
using BuildMind.Application.Features.PlanEditor.Contracts;

namespace BuildMind.Application.Features.Generation.Contracts;

public static class FeasibilitySchemas
{
    public const string MetricLand = "metric-land/0.1-draft";
    public const string Result = "m6-feasibility-result/0.1-draft";
}

public static class MetricLandCoordinateValues
{
    public const string Type = "localCartesian";
    public const string Unit = "m";
    public const string XDirection = "right";
    public const string YDirection = "up";
}

public static class FeasibilityStatuses
{
    public const string Feasible = "FEASIBLE";
    public const string Infeasible = "INFEASIBLE";
    public const string NeedsReview = "NEEDS_REVIEW";
    public const string Unsupported = "UNSUPPORTED";
}

public static class RegulatoryEvidenceStatuses
{
    public const string Missing = "MISSING";
    public const string Unverified = "UNVERIFIED";
    public const string Conflicting = "CONFLICTING";
    public const string Reviewed = "REVIEWED";
}

public static class ConstraintApplicabilityStatuses
{
    public const string Applicable = "APPLICABLE";
    public const string NotApplicable = "NOT_APPLICABLE";
    public const string Unknown = "UNKNOWN";
}

public static class GenerationConstraintTypes
{
    public const string Setback = "setback";
}

public sealed class MetricLandDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("coordinateSystem")]
    public required CoordinateSystemDto CoordinateSystem { get; init; }

    [JsonPropertyName("boundary")]
    public required PolygonBoundaryDto Boundary { get; init; }
}

public sealed class PolygonBoundaryDto
{
    [JsonPropertyName("exterior")]
    public required IReadOnlyList<CoordinatePointDto> Exterior { get; init; }

    [JsonPropertyName("holes")]
    public IReadOnlyList<IReadOnlyList<CoordinatePointDto>> Holes { get; init; } = [];
}

/// <summary>Supplied review and provenance metadata require separate authentication.</summary>
public sealed class RegulatoryEvidenceDto
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("ruleSetVersion")]
    public required string? RuleSetVersion { get; init; }

    [JsonPropertyName("constraints")]
    public required IReadOnlyList<ReviewedConstraintDto> Constraints { get; init; }

    [JsonPropertyName("gaps")]
    public required IReadOnlyList<EvidenceGapDto> Gaps { get; init; }
}

public sealed class ReviewedConstraintDto
{
    [JsonPropertyName("ruleId")]
    public required string RuleId { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("source")]
    public required SourceProvenanceDto Source { get; init; }

    [JsonPropertyName("parcelEdgeIndex")]
    public required int ParcelEdgeIndex { get; init; }

    [JsonPropertyName("distanceM")]
    public required double DistanceM { get; init; }

    [JsonPropertyName("applicability")]
    public required ConstraintApplicabilityDto Applicability { get; init; }

    [JsonPropertyName("reviewedBy")]
    public required Guid ReviewedBy { get; init; }

    [JsonPropertyName("reviewReference")]
    public required string ReviewReference { get; init; }
}

public sealed class SourceProvenanceDto
{
    [JsonPropertyName("sourceVersion")]
    public required string SourceVersion { get; init; }

    [JsonPropertyName("sourceLocator")]
    public required string SourceLocator { get; init; }

    [JsonPropertyName("jurisdiction")]
    public required string Jurisdiction { get; init; }
}

public sealed class ConstraintApplicabilityDto
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("basis")]
    public required string Basis { get; init; }
}

public sealed class EvidenceGapDto
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }
}

/// <summary>Null areas are unchecked. A calculated empty area is zero.</summary>
public sealed class FeasibilityResultDto : RevisionBindingDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("buildableFootprint")]
    public required MetricLandDto? BuildableFootprint { get; init; }

    [JsonPropertyName("landAreaM2")]
    public required double? LandAreaM2 { get; init; }

    [JsonPropertyName("buildableAreaM2")]
    public required double? BuildableAreaM2 { get; init; }

    [JsonPropertyName("requiredAreaM2")]
    public required double? RequiredAreaM2 { get; init; }

    [JsonPropertyName("regulatoryEvidenceStatus")]
    public required string RegulatoryEvidenceStatus { get; init; }

    [JsonPropertyName("ruleSetVersion")]
    public required string? RuleSetVersion { get; init; }

    [JsonPropertyName("feasibilityPolicyVersion")]
    public required string FeasibilityPolicyVersion { get; init; }

    [JsonPropertyName("diagnostics")]
    public required IReadOnlyList<DiagnosticDto> Diagnostics { get; init; }
}

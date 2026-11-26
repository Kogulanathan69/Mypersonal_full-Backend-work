// BuildMind Member 6 | BE-M6-D01-E | Provisional JSON DTOs

using System.Text.Json.Serialization;
using BuildMind.Application.Features.PlanEditor.Contracts;

namespace BuildMind.Application.Features.Generation.Contracts;

public static class GenerationSchemas
{
    public const string Input = "m6-generation-input/0.1-draft";
    public const string Result = "m6-generation-result/0.1-draft";
    public const string RoomCatalogue = "synthetic-fixture-v1";
}

public static class GenerationOutcomes
{
    public const string Success = "SUCCESS";
    public const string Unavailable = "UNAVAILABLE";
    public const string Infeasible = "INFEASIBLE";
    public const string NeedsReview = "NEEDS_REVIEW";
    public const string Unsupported = "UNSUPPORTED";
    public const string Failed = "FAILED";
}

public static class GenerationRoomTypes
{
    public const string Bedroom = "bedroom";
    public const string LivingRoom = "livingRoom";
    public const string Kitchen = "kitchen";
    public const string Bathroom = "bathroom";
}

public static class GenerationAdjacencyRelationships
{
    public const string Adjacent = "adjacent";
    public const string Separated = "separated";
}

public sealed class GenerationInputDto : RevisionBindingDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("land")]
    public required MetricLandDto Land { get; init; }

    [JsonPropertyName("roomProgram")]
    public required RoomProgramDto RoomProgram { get; init; }

    [JsonPropertyName("adjacencyPreferences")]
    public required IReadOnlyList<AdjacencyPreferenceDto> AdjacencyPreferences { get; init; }

    [JsonPropertyName("regulatoryEvidence")]
    public required RegulatoryEvidenceDto RegulatoryEvidence { get; init; }

    [JsonPropertyName("seed")]
    public required int Seed { get; init; }
}

public sealed class RoomProgramDto
{
    [JsonPropertyName("level")]
    public required int Level { get; init; }

    [JsonPropertyName("catalogueVersion")]
    public required string CatalogueVersion { get; init; }

    [JsonPropertyName("rooms")]
    public required IReadOnlyList<RoomSlotDto> Rooms { get; init; }
}

public sealed class RoomSlotDto
{
    [JsonPropertyName("slotId")]
    public required Guid SlotId { get; init; }

    [JsonPropertyName("roomType")]
    public required string RoomType { get; init; }

    [JsonPropertyName("dimensions")]
    public required RoomDimensionsDto Dimensions { get; init; }
}

public sealed class RoomDimensionsDto
{
    [JsonPropertyName("minWidthM")]
    public required double MinWidthM { get; init; }

    [JsonPropertyName("minDepthM")]
    public required double MinDepthM { get; init; }

    [JsonPropertyName("maxWidthM")]
    public double? MaxWidthM { get; init; }

    [JsonPropertyName("maxDepthM")]
    public double? MaxDepthM { get; init; }
}

public sealed class AdjacencyPreferenceDto
{
    [JsonPropertyName("fromSlotId")]
    public required Guid FromSlotId { get; init; }

    [JsonPropertyName("toSlotId")]
    public required Guid ToSlotId { get; init; }

    [JsonPropertyName("relationship")]
    public required string Relationship { get; init; }
}

public sealed class GenerationResultDto : RevisionBindingDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("reasonCode")]
    public required string? ReasonCode { get; init; }

    [JsonPropertyName("algorithmVersion")]
    public required string? AlgorithmVersion { get; init; }

    [JsonPropertyName("seed")]
    public required int Seed { get; init; }

    [JsonPropertyName("alternatives")]
    public required IReadOnlyList<GenerationAlternativeDto> Alternatives { get; init; }

    [JsonPropertyName("diagnostics")]
    public required IReadOnlyList<DiagnosticDto> Diagnostics { get; init; }
}

/// <summary>Reuses the existing M3 draft geometry for fully populated floor payloads.</summary>
public sealed class GenerationAlternativeDto : RevisionBindingDto
{
    [JsonPropertyName("alternativeId")]
    public required Guid AlternativeId { get; init; }

    [JsonPropertyName("geometry")]
    public required PlanGeometryDto Geometry { get; init; }

    [JsonPropertyName("metrics")]
    public required AlternativeMetricsDto Metrics { get; init; }

    [JsonPropertyName("validation")]
    public required ValidationResultDto Validation { get; init; }

    [JsonPropertyName("diversitySignature")]
    public required DiversitySignatureDto DiversitySignature { get; init; }

    [JsonPropertyName("algorithmVersion")]
    public required string AlgorithmVersion { get; init; }
}

public sealed class AlternativeMetricsDto
{
    [JsonPropertyName("floorAreaM2")]
    public required double FloorAreaM2 { get; init; }

    [JsonPropertyName("footprintAreaM2")]
    public required double FootprintAreaM2 { get; init; }

    [JsonPropertyName("roomCount")]
    public required int RoomCount { get; init; }
}

public sealed class DiversitySignatureDto
{
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

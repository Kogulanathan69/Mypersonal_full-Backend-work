// BuildMind - Member 3
// Task: BE-M3-D01
// Status: PROVISIONAL CONTRACT
// Canonical schema approval is pending.
// These DTOs are not an authoritative geometry validator.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BuildMind.Application.Features.PlanEditor.Contracts;

public static class GeometrySchemaInfo
{
    public const string DraftVersion = "0.1-draft";
}

public sealed class CoordinatePointDto
{
    [JsonPropertyName("x")]
    public required double X { get; init; }

    [JsonPropertyName("y")]
    public required double Y { get; init; }
}

public sealed class CoordinateSystemDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("unit")]
    public required string Unit { get; init; }

    [JsonPropertyName("origin")]
    public required CoordinatePointDto Origin { get; init; }

    [JsonPropertyName("xDirection")]
    public required string XDirection { get; init; }

    [JsonPropertyName("yDirection")]
    public required string YDirection { get; init; }
}

public sealed class RoomDto
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("polygon")]
    public required IReadOnlyList<CoordinatePointDto> Polygon { get; init; }

    [JsonPropertyName("wallIds")]
    public required IReadOnlyList<Guid> WallIds { get; init; }
}

public sealed class WallDto
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("start")]
    public required CoordinatePointDto Start { get; init; }

    [JsonPropertyName("end")]
    public required CoordinatePointDto End { get; init; }

    [JsonPropertyName("thickness")]
    public required double Thickness { get; init; }
}

public sealed class OpeningDto
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("wallId")]
    public required Guid WallId { get; init; }

    [JsonPropertyName("offset")]
    public required double Offset { get; init; }

    [JsonPropertyName("width")]
    public required double Width { get; init; }
}

public sealed class FloorDto
{
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    [JsonPropertyName("level")]
    public required int Level { get; init; }

    [JsonPropertyName("rooms")]
    public required IReadOnlyList<RoomDto> Rooms { get; init; }

    [JsonPropertyName("walls")]
    public required IReadOnlyList<WallDto> Walls { get; init; }

    [JsonPropertyName("openings")]
    public required IReadOnlyList<OpeningDto> Openings { get; init; }
}

public sealed class PlanGeometryDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("projectId")]
    public required Guid ProjectId { get; init; }

    [JsonPropertyName("planId")]
    public required Guid PlanId { get; init; }

    [JsonPropertyName("landRevisionId")]
    public required Guid LandRevisionId { get; init; }

    [JsonPropertyName("requirementsRevisionId")]
    public required Guid RequirementsRevisionId { get; init; }

    [JsonPropertyName("coordinateSystem")]
    public required CoordinateSystemDto CoordinateSystem { get; init; }

    [JsonPropertyName("floors")]
    public required IReadOnlyList<FloorDto> Floors { get; init; }
}

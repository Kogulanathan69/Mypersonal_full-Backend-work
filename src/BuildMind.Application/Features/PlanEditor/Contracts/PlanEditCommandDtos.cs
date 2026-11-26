// BuildMind - Member 3
// Task: BE-M3-D01
// Status: PROVISIONAL CONTRACT

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildMind.Application.Features.PlanEditor.Contracts;

public static class PlanEditCommandTypes
{
    public const string AddRoom = "addRoom";
    public const string RemoveRoom = "removeRoom";
    public const string MoveRoom = "moveRoom";
    public const string ResizeRoom = "resizeRoom";
    public const string UpdateWall = "updateWall";
    public const string AddOpening = "addOpening";
    public const string MoveOpening = "moveOpening";
    public const string RemoveOpening = "removeOpening";
}

public sealed class PlanEditCommandDto
{
    [JsonPropertyName("commandId")]
    public required Guid CommandId { get; init; }

    [JsonPropertyName("commandType")]
    public required string CommandType { get; init; }

    [JsonPropertyName("floorId")]
    public required Guid FloorId { get; init; }

    [JsonPropertyName("targetId")]
    public Guid? TargetId { get; init; }

    [JsonPropertyName("payload")]
    public required JsonElement Payload { get; init; }
}

public sealed class PlanEditRequestDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("projectId")]
    public required Guid ProjectId { get; init; }

    [JsonPropertyName("planId")]
    public required Guid PlanId { get; init; }

    [JsonPropertyName("baseRevisionId")]
    public required Guid BaseRevisionId { get; init; }

    [JsonPropertyName("command")]
    public required PlanEditCommandDto Command { get; init; }
}

// BuildMind Member 6 | BE-M6-D01-E | Provisional JSON DTOs

using System.Text.Json.Serialization;

namespace BuildMind.Application.Features.Generation.Contracts;

public static class GenerationJobWire
{
    public const string SchemaVersion = "m6-generation-job/0.1-draft";
    public const string Operation = "GENERATE_PLAN";
}

public static class GenerationJobStates
{
    public const string Queued = "QUEUED";
    public const string Running = "RUNNING";
    public const string Succeeded = "SUCCEEDED";
    public const string Infeasible = "INFEASIBLE";
    public const string Failed = "FAILED";
}

/// <summary>Codes classified CRITICAL by the provisional Python job registry.</summary>
public static class RegisteredJobDiagnosticCodes
{
    public const string InvalidIdentifier = "INVALID_IDENTIFIER";
    public const string SchemaVersionUnsupported = "SCHEMA_VERSION_UNSUPPORTED";
    public const string MetricUnitsRequired = "METRIC_UNITS_REQUIRED";
    public const string CoordinateSystemUnsupported = "COORDINATE_SYSTEM_UNSUPPORTED";
    public const string InputRevisionMismatch = "INPUT_REVISION_MISMATCH";
    public const string PolygonInvalid = "POLYGON_INVALID";
    public const string RoomOverlap = "ROOM_OVERLAP";
    public const string OutsideBuildableFootprint = "OUTSIDE_BUILDABLE_FOOTPRINT";
    public const string WallTopologyInvalid = "WALL_TOPOLOGY_INVALID";
    public const string OpeningWallReferenceInvalid = "OPENING_WALL_REFERENCE_INVALID";
    public const string OpeningOutsideWall = "OPENING_OUTSIDE_WALL";
    public const string OpeningCollision = "OPENING_COLLISION";
    public const string RoomInaccessible = "ROOM_INACCESSIBLE";
    public const string RoomRequirementUnmet = "ROOM_REQUIREMENT_UNMET";
    public const string SetbackEvidenceMissing = "SETBACK_EVIDENCE_MISSING";
    public const string RegulatoryEvidenceConflict = "REGULATORY_EVIDENCE_CONFLICT";
    public const string RequiredAreaPolicyUnavailable = "REQUIRED_AREA_POLICY_UNAVAILABLE";
    public const string ValidationEvidenceStale = "VALIDATION_EVIDENCE_STALE";
    public const string ValidationUnavailable = "VALIDATION_UNAVAILABLE";
    public const string GenerationUnavailable = "GENERATION_UNAVAILABLE";
    public const string SearchExhausted = "SEARCH_EXHAUSTED";
    public const string InsufficientDistinctAlternatives = "INSUFFICIENT_DISTINCT_ALTERNATIVES";
    public const string BuildableAreaInsufficient = "BUILDABLE_AREA_INSUFFICIENT";
    public const string GenerationTimeout = "GENERATION_TIMEOUT";
    public const string GenerationFailed = "GENERATION_FAILED";
}

/// <summary>Provisional snapshot with optional inline result or failure details.</summary>
public sealed class GenerationJobDto : RevisionBindingDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("jobId")]
    public required Guid JobId { get; init; }

    [JsonPropertyName("correlationId")]
    public required Guid CorrelationId { get; init; }

    [JsonPropertyName("seed")]
    public required int Seed { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("idempotency")]
    public required JobIdempotencyDto Idempotency { get; init; }

    [JsonPropertyName("inputFingerprint")]
    public required string InputFingerprint { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public required DateTimeOffset CreatedAtUtc { get; init; }

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset? StartedAtUtc { get; init; }

    [JsonPropertyName("completedAtUtc")]
    public DateTimeOffset? CompletedAtUtc { get; init; }

    [JsonPropertyName("result")]
    public GenerationResultDto? Result { get; init; }

    [JsonPropertyName("failure")]
    public JobFailureDto? Failure { get; init; }
}

/// <summary>Carries project-scoped replay metadata for future durable enforcement.</summary>
public sealed class JobIdempotencyDto
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("projectId")]
    public required Guid ProjectId { get; init; }

    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("inputFingerprint")]
    public required string InputFingerprint { get; init; }

    [JsonPropertyName("seed")]
    public required int Seed { get; init; }
}

public sealed class JobFailureDto
{
    [JsonPropertyName("reasonCode")]
    public required string ReasonCode { get; init; }

    [JsonPropertyName("diagnostics")]
    public required IReadOnlyList<RegisteredDiagnosticDto> Diagnostics { get; init; }
}

/// <summary>Job failure diagnostic with a registered code and CRITICAL severity in the Python contract.</summary>
public sealed class RegisteredDiagnosticDto : DiagnosticDto
{
}

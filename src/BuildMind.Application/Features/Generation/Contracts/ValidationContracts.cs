// BuildMind Member 6 | BE-M6-D01-E | Provisional JSON DTOs

using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildMind.Application.Features.Generation.Contracts;

/// <summary>Use these options at the provisional M6 JSON boundary.</summary>
public static class GenerationContractJson
{
    public static JsonSerializerOptions StrictOptions { get; } = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        RespectNullableAnnotations = true
    };
}

public static class DiagnosticSeverities
{
    public const string Critical = "CRITICAL";
    public const string Warning = "WARNING";
}

/// <summary>Identifies the exact input snapshot named by a provisional M6 contract.</summary>
public abstract class RevisionBindingDto
{
    [JsonPropertyName("projectId")]
    public required Guid ProjectId { get; init; }

    [JsonPropertyName("landRevisionId")]
    public required Guid LandRevisionId { get; init; }

    [JsonPropertyName("requirementsRevisionId")]
    public required Guid RequirementsRevisionId { get; init; }
}

/// <summary>Public diagnostic metadata. Producers supply sanitized messages.</summary>
public class DiagnosticDto
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("relatedEntityIds")]
    public IReadOnlyList<Guid> RelatedEntityIds { get; init; } = [];

    [JsonPropertyName("sourceReferenceIds")]
    public IReadOnlyList<string> SourceReferenceIds { get; init; } = [];
}

/// <summary>Supplied hash and review metadata describe the reported snapshot.</summary>
public sealed class ValidationResultDto : RevisionBindingDto
{
    [JsonPropertyName("validationId")]
    public required Guid ValidationId { get; init; }

    [JsonPropertyName("candidateId")]
    public required Guid CandidateId { get; init; }

    [JsonPropertyName("geometryHash")]
    public required string GeometryHash { get; init; }

    [JsonPropertyName("geometryValid")]
    public required bool? GeometryValid { get; init; }

    [JsonPropertyName("requirementsMatch")]
    public required bool? RequirementsMatch { get; init; }

    [JsonPropertyName("regulatoryEvidenceStatus")]
    public required string RegulatoryEvidenceStatus { get; init; }

    [JsonPropertyName("hasBlockingIssues")]
    public required bool HasBlockingIssues { get; init; }

    [JsonPropertyName("validatorVersion")]
    public required string ValidatorVersion { get; init; }

    [JsonPropertyName("ruleSetVersion")]
    public required string? RuleSetVersion { get; init; }

    [JsonPropertyName("validatedAtUtc")]
    public required DateTimeOffset ValidatedAtUtc { get; init; }

    [JsonPropertyName("diagnostics")]
    public required IReadOnlyList<DiagnosticDto> Diagnostics { get; init; }
}

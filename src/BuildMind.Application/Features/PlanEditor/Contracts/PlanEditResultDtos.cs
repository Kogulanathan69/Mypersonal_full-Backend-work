// BuildMind - Member 3
// Task: BE-M3-D01
// Status: PROVISIONAL CONTRACT
// Validation authority belongs to M6.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BuildMind.Application.Features.PlanEditor.Contracts;

public static class PlanEditStatus
{
    public const string Accepted = "accepted";
    public const string AcceptedWithWarnings = "acceptedWithWarnings";
    public const string Rejected = "rejected";
    public const string RevisionConflict = "revisionConflict";
    public const string ValidationUnavailable = "validationUnavailable";
}

public sealed class PlanEditDiagnosticDto
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("entityId")]
    public Guid? EntityId { get; init; }
}

public sealed class PlanValidationReferenceDto
{
    [JsonPropertyName("validationId")]
    public required Guid ValidationId { get; init; }

    [JsonPropertyName("validatorVersion")]
    public required string ValidatorVersion { get; init; }

    [JsonPropertyName("geometryHash")]
    public required string GeometryHash { get; init; }

    [JsonPropertyName("geometryValid")]
    public required bool GeometryValid { get; init; }

    [JsonPropertyName("requirementsMatch")]
    public required bool? RequirementsMatch { get; init; }

    [JsonPropertyName("regulatoryEvidenceStatus")]
    public required string RegulatoryEvidenceStatus { get; init; }

    [JsonPropertyName("hasBlockingIssues")]
    public required bool HasBlockingIssues { get; init; }

    [JsonPropertyName("validatedAtUtc")]
    public required DateTimeOffset ValidatedAtUtc { get; init; }
}

public sealed class PlanEditResultDto
{
    [JsonPropertyName("schemaVersion")]
    public required string SchemaVersion { get; init; }

    [JsonPropertyName("commandId")]
    public required Guid CommandId { get; init; }

    [JsonPropertyName("baseRevisionId")]
    public required Guid BaseRevisionId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("candidateGeometry")]
    public PlanGeometryDto? CandidateGeometry { get; init; }

    [JsonPropertyName("validation")]
    public PlanValidationReferenceDto? Validation { get; init; }

    [JsonPropertyName("diagnostics")]
    public required IReadOnlyList<PlanEditDiagnosticDto> Diagnostics { get; init; }

    [JsonPropertyName("changedEntityIds")]
    public required IReadOnlyList<Guid> ChangedEntityIds { get; init; }
}

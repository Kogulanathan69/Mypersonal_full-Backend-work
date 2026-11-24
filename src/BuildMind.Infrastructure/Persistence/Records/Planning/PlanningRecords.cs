namespace BuildMind.Infrastructure.Persistence.Records.Planning;

/// <summary>Immutable user-confirmed land boundary revision (metric-land/0.1-draft JSON).</summary>
internal sealed class LandRevisionRecord
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public long RevisionNumber { get; init; }
    public required string LandJson { get; init; }
    public double AreaM2 { get; init; }
    public int EdgeCount { get; init; }
    public Guid? SurveyId { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

internal sealed class GenerationJobRecord
{
    public const int KeyMaxLength = 64;
    public const int CodeMaxLength = 80;
    public const int LabelMaxLength = 64;

    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid RequestedBy { get; init; }
    public required string IdempotencyKey { get; init; }
    public Guid LandRevisionId { get; init; }
    public Guid RequirementsRevisionId { get; init; }
    public required string Status { get; set; }
    public string? Outcome { get; set; }
    public string? ReasonCode { get; set; }
    public string? AlgorithmVersion { get; set; }
    public required string EvidenceMode { get; init; }
    public int Seed { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public required string DiagnosticsJson { get; set; }
}

internal sealed class GenerationCandidateRecord
{
    public Guid Id { get; init; }
    public Guid JobId { get; init; }
    public Guid ProjectId { get; init; }
    public int Ordinal { get; init; }
    public required string GeometryJson { get; init; }
    public required string GeometryHash { get; init; }
    public required string MetricsJson { get; init; }
    public required string ValidationJson { get; init; }
    public required string DiversitySignature { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>Server-issued plan validation evidence ledger; plan revisions may cite only these rows.</summary>
internal sealed class PlanValidationEvidenceRecord
{
    public Guid ValidationId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid PlanId { get; init; }
    public required string ValidatorVersion { get; init; }
    public required string GeometryHash { get; init; }
    public bool GeometryValid { get; init; }
    public bool? RequirementsMatch { get; init; }
    public required string RegulatoryEvidenceStatus { get; init; }
    public bool HasBlockingIssues { get; init; }
    public DateTimeOffset ValidatedAtUtc { get; init; }
    public required string DiagnosticsJson { get; init; }
}

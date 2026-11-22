namespace BuildMind.Infrastructure.Persistence.Records.PlanEditor;

internal sealed class PlanRecord
{
    public Guid ProjectId { get; init; }
    public Guid PlanId { get; init; }
    public Guid? HeadRevisionId { get; set; }
}

// Revisions are inserted as complete snapshots. There is no revision write API in this slice.
internal sealed class PlanRevisionRecord
{
    public const int ProvisionalGeometryMaxBytes = 256 * 1024;
    public const int VersionLabelMaxLength = 64;
    public const int EvidenceStatusMaxLength = 64;
    public const int ChangeSummaryMaxLength = 1024;

    public Guid ProjectId { get; init; }
    public Guid PlanId { get; init; }
    public Guid RevisionId { get; init; }
    public Guid? ParentRevisionId { get; init; }
    public long VersionNumber { get; init; }
    public required string GeometrySchemaVersion { get; init; }
    public required string GeometryJson { get; init; }
    public required string GeometryHash { get; init; }
    public Guid LandRevisionId { get; init; }
    public Guid RequirementsRevisionId { get; init; }
    public Guid? EditCommandId { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string? ChangeSummary { get; init; }

    public Guid ValidationId { get; init; }
    public required string ValidatorVersion { get; init; }
    public required string ValidationGeometryHash { get; init; }
    public bool GeometryValid { get; init; }
    public bool? RequirementsMatch { get; init; }
    public required string RegulatoryEvidenceStatus { get; init; }
    public bool HasBlockingIssues { get; init; }
    public DateTimeOffset ValidatedAtUtc { get; init; }
}

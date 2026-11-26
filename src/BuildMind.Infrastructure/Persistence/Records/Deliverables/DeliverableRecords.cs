namespace BuildMind.Infrastructure.Persistence.Records.Deliverables;

/// <summary>
/// Report metadata. The snapshot is NOT copied: it is rehydrated from the immutable sources it references
/// (requirement revision, plan revision, confirmation, validation evidence) plus the captured time and disclaimer.
/// Bytes live in private storage under the opaque <see cref="ArtifactId"/>; no path or URL is stored.
/// </summary>
internal sealed class ReportRecord
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid RequestedBy { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; }
    public required string SnapshotSchemaVersion { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public Guid RequirementsRevisionId { get; init; }
    public Guid PlanId { get; init; }
    public Guid PlanRevisionId { get; init; }
    public Guid ConfirmationId { get; init; }
    public required string DisclaimerVersion { get; init; }
    public required string DisclaimerText { get; init; }
    public long StateVersion { get; set; }
    public required string Status { get; set; }
    public Guid? ArtifactId { get; set; }
    public string? ArtifactMediaType { get; set; }
    public long? ArtifactByteLength { get; set; }
    public string? ArtifactContentHash { get; set; }
    public DateTimeOffset? ArtifactCreatedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public DateTimeOffset? FailedAtUtc { get; set; }
}

/// <summary>Architect request with its explicit share consent (one row; consent is immutable).</summary>
internal sealed class ArchitectRequestRecord
{
    public Guid RequestId { get; init; }
    public Guid ConsentId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid GrantedBy { get; init; }
    public Guid RecipientId { get; init; }
    public Guid? ReportId { get; init; }
    public required string DocumentIdsJson { get; init; }
    public required string PolicyVersion { get; init; }
    public DateTimeOffset GrantedAtUtc { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; }
    public long StateVersion { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
}

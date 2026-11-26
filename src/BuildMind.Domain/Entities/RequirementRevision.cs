using BuildMind.Domain.ValueObjects;

namespace BuildMind.Domain.Entities;

/// <summary>An immutable requirements snapshot. Saving never mutates an earlier revision.</summary>
public sealed class RequirementRevision
{
    public Guid Id { get; }
    public Guid ProjectId { get; }
    public long RevisionNumber { get; }
    public string SchemaVersion { get; }
    public string CatalogueVersion { get; }
    public RequirementContent Content { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Constructs a structurally valid snapshot, including for persistence rehydration.
    /// New Application revisions must use RequirementValidationResult.CreateRevision to bind policy provenance.
    /// This constructor does not certify catalogue approval.
    /// </summary>
    public RequirementRevision(
        Guid id,
        Guid projectId,
        long revisionNumber,
        string schemaVersion,
        string catalogueVersion,
        RequirementContent content,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(id));
        if (projectId == Guid.Empty) throw new ArgumentException("Project identity is required.", nameof(projectId));
        if (revisionNumber <= 0) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogueVersion);
        ArgumentNullException.ThrowIfNull(content);

        Id = id;
        ProjectId = projectId;
        RevisionNumber = revisionNumber;
        SchemaVersion = schemaVersion;
        CatalogueVersion = catalogueVersion;
        Content = content;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }
}

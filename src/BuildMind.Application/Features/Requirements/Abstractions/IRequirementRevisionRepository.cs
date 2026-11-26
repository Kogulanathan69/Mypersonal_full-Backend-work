using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Requirements.Abstractions;

public enum RequirementSaveStatus { Saved, RevisionConflict, ProjectNotFound }

public sealed record RequirementSaveResult
{
    public RequirementSaveStatus Status { get; }
    public RequirementRevision? SavedRevision { get; }

    public RequirementSaveResult(RequirementSaveStatus status, RequirementRevision? savedRevision = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == RequirementSaveStatus.Saved && savedRevision is null)
            throw new ArgumentNullException(nameof(savedRevision), "A successful save requires a revision.");
        if (status != RequirementSaveStatus.Saved && savedRevision is not null)
            throw new ArgumentException("A failed save cannot contain a revision.", nameof(savedRevision));
        Status = status;
        SavedRevision = savedRevision;
    }
}

/// <summary>
/// Persistence port, not an authorization guard. The caller must establish authenticated project
/// ownership using the agreed M1/M4 boundary before invoking any operation. Implementations must
/// filter every lookup/write by project and honor cancellation without partial writes.
/// </summary>
public interface IRequirementRevisionRepository
{
    Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken);

    Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken);

    /// <summary>
    /// In one transaction, compare the project's current revision with expectedRevisionId, insert
    /// the immutable snapshot and update the current pointer. Null expects no current revision;
    /// empty GUID is invalid. A mismatch returns RevisionConflict and changes nothing. Validate
    /// project existence, revision identity uniqueness and consecutive revision numbering (first
    /// is 1); never overwrite history. Only Saved may include SavedRevision. Infrastructure/M4
    /// owns the transaction/concurrency implementation; no external calls inside the transaction.
    /// </summary>
    Task<RequirementSaveResult> TrySaveAtomicallyAsync(
        Guid? expectedRevisionId,
        RequirementRevision proposedRevision,
        CancellationToken cancellationToken);
}

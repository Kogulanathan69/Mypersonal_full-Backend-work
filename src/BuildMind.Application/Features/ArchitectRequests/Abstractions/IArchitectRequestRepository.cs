using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.ArchitectRequests.Abstractions;

public enum ArchitectRequestWriteStatus
{
    Created, Withdrawn, AlreadyExists, NotFound, StateConflict, SourceConflict, PolicyUnavailable, NotEligible
}

public sealed class ArchitectRequestWriteResult
{
    public ArchitectRequestWriteStatus Status { get; }
    public ArchitectRequest? SavedRequest { get; }

    public ArchitectRequestWriteResult(ArchitectRequestWriteStatus status, ArchitectRequest? savedRequest = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status is ArchitectRequestWriteStatus.Created or ArchitectRequestWriteStatus.Withdrawn)
        {
            ArgumentNullException.ThrowIfNull(savedRequest);
            if ((status == ArchitectRequestWriteStatus.Created && savedRequest.Status != ArchitectRequestStatus.Pending)
                || (status == ArchitectRequestWriteStatus.Withdrawn && savedRequest.Status != ArchitectRequestStatus.Withdrawn))
                throw new ArgumentException("Saved request conflicts with operation status.", nameof(savedRequest));
        }
        else if (savedRequest is not null)
            throw new ArgumentException("Unsuccessful writes cannot carry a saved request.", nameof(savedRequest));
        Status = status;
        SavedRequest = savedRequest;
    }
}

/// <summary>Provisional persistence boundary, not an authorization guard or sharing service.
/// All operations must be authorized through the M1/M4 owner boundary before invocation.</summary>
public interface IArchitectRequestRepository
{
    Task<ArchitectRequest?> GetAsync(Guid projectId, Guid requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArchitectRequest>> ListAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Atomically verify exact project, consent, selected document/report identities, owner,
    /// recipient eligibility and approved policy through agreed trusted sources, then persist Pending
    /// request and consent together. Missing policy/source or conflict must not write anything.
    /// No external dispatch implied. G12 and M4 transaction integration remain pending.</summary>
    Task<ArchitectRequestWriteResult> TryCreatePendingAtomicallyAsync(ArchitectRequest pendingRequest, CancellationToken cancellationToken);

    /// <summary>Atomically compare project/request identity and expected version, enforce agreed
    /// withdrawal policy, preserve original consent and immutable fields, and persist the terminal state.
    /// A withdrawal does not prove previously delivered copies were deleted. No partial writes.</summary>
    Task<ArchitectRequestWriteResult> TryWithdrawAtomicallyAsync(long expectedStateVersion,
        ArchitectRequest proposedState, CancellationToken cancellationToken);
}

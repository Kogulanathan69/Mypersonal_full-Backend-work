namespace BuildMind.Application.Features.Requirements.Integration;

/// <summary>
/// Isolated M5 proposal only. Not registered for production use.
/// Authentication, ownership and concurrency authority remain with the agreed M1/M4 boundary.
/// </summary>
public sealed record IsolatedRequirementMutationScope
{
    public Guid ProjectId { get; }
    public Guid TrustedActorId { get; }
    public Guid OperationId { get; }
    public Guid? ExpectedRequirementRevisionId { get; }

    public IsolatedRequirementMutationScope(
        Guid projectId, Guid trustedActorId, Guid operationId,
        Guid? expectedRequirementRevisionId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("Project ID is required.", nameof(projectId));
        if (trustedActorId == Guid.Empty) throw new ArgumentException("Trusted actor ID is required.", nameof(trustedActorId));
        if (operationId == Guid.Empty) throw new ArgumentException("Operation ID is required.", nameof(operationId));
        if (expectedRequirementRevisionId == Guid.Empty)
            throw new ArgumentException("Expected revision cannot be an empty GUID.", nameof(expectedRequirementRevisionId));

        ProjectId = projectId;
        TrustedActorId = trustedActorId;
        OperationId = operationId;
        ExpectedRequirementRevisionId = expectedRequirementRevisionId;
    }
}

/// <summary>
/// Provisional classifications; mapping to HTTP status codes and shared error contracts is NOT approved.
/// The port must never treat dependency failure or an unknown commit as successful authorization.
/// </summary>
public enum IsolatedRequirementMutationStatus
{
    Saved,
    AccessDenied,
    AuthorizationChanged,
    RequirementRevisionConflict,
    ProjectNotFound,
    DependencyUnavailable,
    DependencyTimeout,
    CommitOutcomeUnknown
}

public sealed record IsolatedRequirementMutationOutcome
{
    public IsolatedRequirementMutationStatus Status { get; }
    public Guid? SavedRequirementRevisionId { get; }

    public IsolatedRequirementMutationOutcome(
        IsolatedRequirementMutationStatus status, Guid? savedRequirementRevisionId = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == IsolatedRequirementMutationStatus.Saved)
        {
            if (savedRequirementRevisionId is null || savedRequirementRevisionId == Guid.Empty)
                throw new ArgumentException("A saved outcome requires a nonempty revision ID.", nameof(savedRequirementRevisionId));
        }
        else if (savedRequirementRevisionId is not null)
        {
            throw new ArgumentException("A non-success outcome must not expose a saved revision ID.", nameof(savedRequirementRevisionId));
        }

        Status = status;
        SavedRequirementRevisionId = savedRequirementRevisionId;
    }
}

/// <summary>
/// PROPOSAL ONLY: an M4-backed implementation must authenticate the trusted server-side actor,
/// establish current ownership, enforce authorization-version consistency, and compare/update the
/// requirement revision within an agreed concurrency/transaction protocol. Ownership revocation
/// must participate in the same protocol. No external calls should run inside the DB transaction.
/// Never retry an uncertain commit without reconciliation. Cancellation is propagated, not treated
/// as AccessDenied. This port does not provide or implement an ownership authority.
/// </summary>
public interface IIsolatedAtomicRequirementMutationPort
{
    Task<IsolatedRequirementMutationOutcome> ExecuteAsync(
        IsolatedRequirementMutationScope scope,
        CancellationToken cancellationToken);
}

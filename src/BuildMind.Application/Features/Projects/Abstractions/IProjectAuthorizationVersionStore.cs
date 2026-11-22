namespace BuildMind.Application.Features.Projects.Abstractions;

/// <summary>
/// STEP 05 decision 4: the only write path for <c>projects.authorization_version</c>. Callers are operations that
/// change who may act on a project (ownership transfer, sharing/architect consent, access-relevant status changes);
/// metadata edits must not call it.
/// </summary>
public interface IProjectAuthorizationVersionStore
{
    /// <summary>
    /// Atomic compare-and-set: increments the version only when it still equals <paramref name="expectedVersion"/>.
    /// Returns the new version, or null when the project does not exist or the version has already moved.
    /// Callers should invoke it inside the same transaction as the authorization change it records.
    /// </summary>
    Task<long?> TryIncrementAsync(Guid projectId, long expectedVersion, CancellationToken cancellationToken);
}

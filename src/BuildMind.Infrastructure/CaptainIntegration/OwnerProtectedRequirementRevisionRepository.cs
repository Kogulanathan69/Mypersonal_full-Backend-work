using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// Captain local-integration adapter for the unchanged <see cref="IRequirementRevisionRepository"/> port.
/// Every save goes through the ratified A1–A8 protected-write path:
/// <list type="number">
/// <item>a fresh owner-scoped authorization read supplies the server-side authorization version for the trusted
/// actor (never a client value);</item>
/// <item>inside the save transaction, the project row is locked <c>FOR UPDATE</c> and owner and version are
/// re-checked before the requirement head is observed or changed.</item>
/// </list>
/// An authorization change between the two steps (transfer, revocation, version bump) denies the write with the
/// existing no-payload <see cref="RequirementSaveStatus.ProjectNotFound"/> shape.
/// </summary>
/// <remarks>Reads stay owner-agnostic, as in the port contract: callers establish ownership before reading.</remarks>
internal sealed class OwnerProtectedRequirementRevisionRepository(
    RequirementRevisionRepository inner,
    ICurrentUser currentUser,
    IProjectAuthorizationPort authorization) : IRequirementRevisionRepository
{
    public Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken) =>
        inner.GetCurrentAsync(projectId, cancellationToken);

    public Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken) =>
        inner.GetAsync(projectId, revisionId, cancellationToken);

    public async Task<RequirementSaveResult> TrySaveAtomicallyAsync(
        Guid? expectedRevisionId,
        RequirementRevision proposedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposedRevision);
        cancellationToken.ThrowIfCancellationRequested();

        if (currentUser.UserId is not { } actor || actor == Guid.Empty)
            return new RequirementSaveResult(RequirementSaveStatus.ProjectNotFound);

        var access = await authorization
            .AuthorizeOwnerAsync(proposedRevision.ProjectId, actor, cancellationToken)
            .ConfigureAwait(false);
        if (access.Status != ProjectAuthorizationStatus.Authorized || access.Snapshot is not { } snapshot
            || snapshot.ProjectId != proposedRevision.ProjectId || snapshot.ActorId != actor)
            return new RequirementSaveResult(RequirementSaveStatus.ProjectNotFound);

        return await inner.TrySaveAuthorizedAtomicallyAsync(
            actor, snapshot.Version.Value, expectedRevisionId, proposedRevision, cancellationToken)
            .ConfigureAwait(false);
    }
}

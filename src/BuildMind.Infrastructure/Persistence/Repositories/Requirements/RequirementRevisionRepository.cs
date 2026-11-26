using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Mapping;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Requirements;

/// <summary>
/// MySQL implementation of the M5 <see cref="IRequirementRevisionRepository"/> port (BE-M4-D03).
/// </summary>
/// <remarks>
/// Not an authorization guard (per the port): callers establish authenticated project ownership first.
/// Every lookup and write is filtered by project. A save runs in one <see cref="TransactionRunner"/>
/// transaction: verify the project, insert the immutable revision, then compare-and-set the project's
/// head pointer. A stale or concurrent save returns <see cref="RequirementSaveStatus.RevisionConflict"/> and
/// changes nothing; a database failure is reported as a conflict only when the head verifiably moved
/// (<see cref="ResolveSaveFailure"/>). No external calls, no retries; an unknown commit outcome, an unconfirmed
/// rollback, a lock timeout or unrelated contention propagates as an exception.
/// <para>PROJECT FILTERING IS NOT USER AUTHORIZATION. This repository never checks who the caller is; it must be
/// reached only through an Application service that has verified project ownership (for example M3's
/// <c>ProjectOwnershipGate</c>). No HTTP endpoint exposes it as of BE-M4-D03.</para>
/// </remarks>
internal sealed class RequirementRevisionRepository(BuildMindDbContext context, TransactionRunner transactions)
    : IRequirementRevisionRepository
{
    public async Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken)
    {
        RequireIdentity(projectId, nameof(projectId));

        var record = await (
                from head in context.Set<RequirementHeadRecord>().AsNoTracking()
                where head.ProjectId == projectId
                join revision in context.Set<RequirementRevisionRecord>().AsNoTracking()
                    on new { head.ProjectId, Id = head.CurrentRevisionId } equals new { revision.ProjectId, revision.Id }
                select revision)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : RequirementRevisionMapper.ToDomain(record);
    }

    public async Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken)
    {
        RequireIdentity(projectId, nameof(projectId));
        RequireIdentity(revisionId, nameof(revisionId));

        var record = await context.Set<RequirementRevisionRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ProjectId == projectId && r.Id == revisionId, cancellationToken)
            .ConfigureAwait(false);

        return record is null ? null : RequirementRevisionMapper.ToDomain(record);
    }

    public Task<RequirementSaveResult> TrySaveAtomicallyAsync(
        Guid? expectedRevisionId,
        RequirementRevision proposedRevision,
        CancellationToken cancellationToken) =>
        SaveCoreAsync(expectedRevisionId, proposedRevision, null, cancellationToken);

    // Isolated entry point. Callers must supply server-derived actor and version evidence.
    // The existing application port remains unchanged until the cross-owner write contract is approved.
    internal Task<RequirementSaveResult> TrySaveAuthorizedAtomicallyAsync(
        Guid trustedActorId, long expectedAuthorizationVersion, Guid? expectedRevisionId,
        RequirementRevision proposedRevision, CancellationToken cancellationToken) =>
        SaveCoreAsync(expectedRevisionId, proposedRevision, (trustedActorId, expectedAuthorizationVersion), cancellationToken);

    private async Task<RequirementSaveResult> SaveCoreAsync(
        Guid? expectedRevisionId, RequirementRevision proposedRevision,
        (Guid Actor, long Version)? authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposedRevision);
        if (expectedRevisionId == Guid.Empty)
        {
            throw new ArgumentException("An empty expected revision identity is invalid; use null for 'no current revision'.", nameof(expectedRevisionId));
        }

        if (expectedRevisionId is null && proposedRevision.RevisionNumber != 1)
        {
            throw new ArgumentException("The first revision of a project must be number 1.", nameof(proposedRevision));
        }

        var projectId = proposedRevision.ProjectId;
        var record = RequirementRevisionMapper.ToRecord(proposedRevision);

        try
        {
            return await transactions.ExecuteAsync(
                async token =>
                {
                    // In the protected path the project row is locked before observing or changing the head.
                    var projectAccessible = authorization is { } access
                        ? await new ProjectMutationAuthorization(context)
                            .LockAndCheckAsync(projectId, access.Actor, access.Version, token).ConfigureAwait(false)
                        : await context.Set<ProjectRecord>().AsNoTracking()
                            .AnyAsync(p => p.Id == projectId, token).ConfigureAwait(false);
                    if (!projectAccessible)
                    {
                        throw new SaveRejected(RequirementSaveStatus.ProjectNotFound);
                    }

                    return await WriteRevisionAsync(expectedRevisionId, proposedRevision, record, token).ConfigureAwait(false);
                },
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SaveRejected rejected)
        {
            return new RequirementSaveResult(rejected.Status);
        }
        catch (Exception exception) when (exception is DbUpdateException or MySql.Data.MySqlClient.MySqlException)
        {
            var failure = PersistenceErrorClassifier.Classify(exception);

            // The transaction was rolled back by TransactionRunner (an unconfirmed rollback would have surfaced as
            // TransactionRollbackUncertainException instead). Only report a conflict if the head REALLY moved.
            // The protected path has released its project lock. Never inspect a new owner's head to
            // classify the prior writer's failure. Only the legacy path keeps its existing recheck.
            var headMoved = authorization is null &&
                failure.Kind is (PersistenceFailureKind.DuplicateKey or PersistenceFailureKind.Deadlock)
                && !await HeadIsAsync(projectId, expectedRevisionId, cancellationToken).ConfigureAwait(false);

            return ResolveSaveFailure(failure, headMoved) switch
            {
                SaveFailureResolution.RevisionConflict => new RequirementSaveResult(RequirementSaveStatus.RevisionConflict),
                SaveFailureResolution.ProjectNotFound => new RequirementSaveResult(RequirementSaveStatus.ProjectNotFound),
                SaveFailureResolution.RevisionIdentityReused => throw new ArgumentException(
                    "The proposed revision identity already exists.", nameof(proposedRevision), exception),
                _ => throw exception.Rethrow(),
            };
        }
    }

    internal async Task<RequirementSaveResult> WriteRevisionAsync(Guid? expectedRevisionId, RequirementRevision proposedRevision,
        RequirementRevisionRecord record, CancellationToken token)
    {
        var projectId = proposedRevision.ProjectId;
        var head = await context.Set<RequirementHeadRecord>().AsNoTracking()
            .Where(h => h.ProjectId == projectId)
            .Select(h => new { h.CurrentRevisionId, h.CurrentRevisionNumber })
            .SingleOrDefaultAsync(token)
            .ConfigureAwait(false);

        if (expectedRevisionId is null ? head is not null : head is null || head.CurrentRevisionId != expectedRevisionId)
        {
            throw new SaveRejected(RequirementSaveStatus.RevisionConflict);
        }

        if (head is not null && proposedRevision.RevisionNumber != checked(head.CurrentRevisionNumber + 1))
        {
            throw new ArgumentException("Revision numbers must be consecutive.", nameof(proposedRevision));
        }

        context.Add(record);
        await context.SaveChangesAsync(token).ConfigureAwait(false);

        if (head is null)
        {
            // A concurrent first save collides on the head primary key (duplicate → conflict).
            context.Add(new RequirementHeadRecord
            {
                ProjectId = projectId,
                CurrentRevisionId = proposedRevision.Id,
                CurrentRevisionNumber = proposedRevision.RevisionNumber,
            });
            await context.SaveChangesAsync(token).ConfigureAwait(false);
        }
        else
        {
            var moved = await context.Set<RequirementHeadRecord>()
                .Where(h => h.ProjectId == projectId && h.CurrentRevisionId == expectedRevisionId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(h => h.CurrentRevisionId, proposedRevision.Id)
                        .SetProperty(h => h.CurrentRevisionNumber, proposedRevision.RevisionNumber),
                    token)
                .ConfigureAwait(false);
            if (moved != 1)
            {
                throw new SaveRejected(RequirementSaveStatus.RevisionConflict);
            }
        }

        context.ChangeTracker.Clear();
        return new RequirementSaveResult(RequirementSaveStatus.Saved, proposedRevision);
    }

    /// <summary>
    /// Decides how a database failure during a save is reported. Pure function (no I/O) so the policy is testable.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Duplicate key or deadlock AND the head is no longer what the caller expected → a concurrent save won:
    /// <see cref="SaveFailureResolution.RevisionConflict"/>.</item>
    /// <item>Duplicate on the revision primary key while the head is unchanged → the caller reused an existing
    /// revision identity (a caller error, not a stale revision).</item>
    /// <item>Deadlock while the head is unchanged → unrelated lock contention (for example InnoDB gap locks from
    /// another project's save). Not a conflict: rethrown as the transient database failure it is.</item>
    /// <item>FK violation on a project reference → the project vanished: <see cref="SaveFailureResolution.ProjectNotFound"/>.
    /// Any other FK or failure (including 1205 lock timeouts) → rethrown.</item>
    /// </list>
    /// </remarks>
    internal static SaveFailureResolution ResolveSaveFailure(PersistenceFailure failure, bool headMoved)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.Kind switch
        {
            PersistenceFailureKind.DuplicateKey or PersistenceFailureKind.Deadlock when headMoved
                => SaveFailureResolution.RevisionConflict,
            // MySQL names every primary key PRIMARY; the table tells which one was violated.
            PersistenceFailureKind.DuplicateKey when failure.ConstraintName == "PRIMARY"
                && failure.ConstraintTable == "requirement_revisions"
                => SaveFailureResolution.RevisionIdentityReused,
            PersistenceFailureKind.ForeignKeyViolation when failure.ConstraintName is
                "fk_requirement_revisions_projects_project_id" or "fk_project_requirement_heads_projects_project_id"
                => SaveFailureResolution.ProjectNotFound,
            _ => SaveFailureResolution.Rethrow,
        };
    }

    /// <summary>True when the project's head is exactly what the caller expected (null = no head).</summary>
    private async Task<bool> HeadIsAsync(Guid projectId, Guid? expectedRevisionId, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var current = await context.Set<RequirementHeadRecord>().AsNoTracking()
            .Where(h => h.ProjectId == projectId)
            .Select(h => (Guid?)h.CurrentRevisionId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return current == expectedRevisionId;
    }

    private static void RequireIdentity(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A nonempty identity is required.", name);
        }
    }

    /// <summary>Internal control flow: abort the transaction (rolled back) and report a port status.</summary>
    internal sealed class SaveRejected(RequirementSaveStatus status) : Exception
    {
        public RequirementSaveStatus Status { get; } = status;
    }
}

internal enum SaveFailureResolution
{
    RevisionConflict,
    ProjectNotFound,
    RevisionIdentityReused,
    Rethrow,
}

internal static class ExceptionRethrowExtensions
{
    /// <summary>Rethrows preserving the original stack trace (used inside switch expressions).</summary>
    public static Exception Rethrow(this Exception exception)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        return exception; // unreachable
    }
}

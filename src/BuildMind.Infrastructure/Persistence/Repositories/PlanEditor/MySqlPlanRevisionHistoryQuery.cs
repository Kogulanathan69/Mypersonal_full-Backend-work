using System.Data;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Mapping;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.PlanEditor;

internal sealed class MySqlPlanRevisionHistoryQuery(BuildMindDbContext context) : IPlanRevisionHistoryQueryPort
{
    public Task<PlanRevisionHistorySourcePage> ReadPageAsync(
        Guid projectId, Guid planId, PlanRevisionHistoryCursor? cursor, int pageSize,
        CancellationToken cancellationToken) => PlanHistoryDatabaseRead.RunAsync(
            () => ReadPageCoreAsync(projectId, planId, cursor, pageSize, cancellationToken), cancellationToken);

    private async Task<PlanRevisionHistorySourcePage> ReadPageCoreAsync(
        Guid projectId, Guid planId, PlanRevisionHistoryCursor? cursor, int pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pageSize is < 1 or > PlanRevisionHistoryService.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (cursor is { } token && (!token.IsValid || token.ProjectId != projectId || token.PlanId != planId))
            return new(PlanRevisionHistorySourceStatus.InvalidCursor);

        // InnoDB's consistent read view is established by the first SELECT. Disposal ends it before
        // the caller performs its final authorization read. This transaction performs no writes.
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        var revisions = ScopedRevisions(projectId, planId);
        var plan = await HeadPointerAsync(projectId, planId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
            return new(PlanRevisionHistorySourceStatus.NotFound);
        if (plan.HeadRevisionId is null)
        {
            if (await revisions.AnyAsync(cancellationToken).ConfigureAwait(false))
                throw CorruptHistory();
            return cursor is null
                ? new(PlanRevisionHistorySourceStatus.Succeeded, [])
                : new(PlanRevisionHistorySourceStatus.InvalidCursor);
        }

        var current = await ReadHeadAsync(revisions, plan.HeadRevisionId.Value, cancellationToken).ConfigureAwait(false);
        var traversalHead = current;
        var firstVersion = current.VersionNumber;
        var firstId = current.RevisionId;
        if (cursor is { } continuation)
        {
            if (!await VerifyCursorAsync(revisions, current, continuation, cancellationToken).ConfigureAwait(false))
                return new(PlanRevisionHistorySourceStatus.InvalidCursor);

            traversalHead = new(continuation.HeadVersionNumber, continuation.HeadRevisionId);
            firstVersion = continuation.AfterVersionNumber - 1;
            firstId = continuation.ExpectedNextRevisionId;
        }

        var rows = await revisions.Where(row => row.VersionNumber <= firstVersion)
            .OrderByDescending(row => row.VersionNumber).Take(pageSize + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var hasMore = rows.Count > pageSize;
        var page = new List<PlanRevisionSnapshot>(Math.Min(pageSize, rows.Count));
        var expectedVersion = firstVersion;
        Guid? expectedId = firstId;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.VersionNumber != expectedVersion || row.RevisionId != expectedId)
                throw CorruptHistory();

            if (page.Count < pageSize)
                page.Add(ValidateAndMap(row, projectId, planId, row.RevisionId));

            expectedVersion--;
            expectedId = row.ParentRevisionId;
        }

        if (page.Count == 0 || (!hasMore && expectedVersion != 0))
            throw CorruptHistory();

        return new(PlanRevisionHistorySourceStatus.Succeeded, page, hasMore, traversalHead, cursor is not null);
    }

    public Task<PlanRevisionHistorySourceRevision> ReadRevisionAsync(
        Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken) => PlanHistoryDatabaseRead.RunAsync(
            () => ReadRevisionCoreAsync(projectId, planId, revisionId, cancellationToken), cancellationToken);

    private async Task<PlanRevisionHistorySourceRevision> ReadRevisionCoreAsync(
        Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        var revisions = ScopedRevisions(projectId, planId);
        var row = await revisions.SingleOrDefaultAsync(revision => revision.RevisionId == revisionId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return new(PlanRevisionHistorySourceStatus.NotFound);

        var plan = await HeadPointerAsync(projectId, planId, cancellationToken).ConfigureAwait(false);
        if (plan?.HeadRevisionId is not { } headId)
            throw CorruptHistory();

        var head = await ReadHeadAsync(revisions, headId, cancellationToken).ConfigureAwait(false);
        var snapshot = ValidateAndMap(row, projectId, planId, revisionId);
        // Include the requested revision's parent edge, when present, in the integrity proof.
        var bottom = Math.Max(1, row.VersionNumber - 1);
        if (row.VersionNumber > head.VersionNumber ||
            !await HasContiguousPathAsync(revisions, head.VersionNumber, bottom, cancellationToken).ConfigureAwait(false))
            throw CorruptHistory();

        return new(PlanRevisionHistorySourceStatus.Succeeded, snapshot);
    }

    private IQueryable<PlanRevisionRecord> ScopedRevisions(Guid projectId, Guid planId) =>
        context.Set<PlanRevisionRecord>().AsNoTracking()
            .Where(row => row.ProjectId == projectId && row.PlanId == planId);

    private Task<PlanRecord?> HeadPointerAsync(Guid projectId, Guid planId, CancellationToken cancellationToken) =>
        context.Set<PlanRecord>().AsNoTracking().SingleOrDefaultAsync(
            plan => plan.ProjectId == projectId && plan.PlanId == planId, cancellationToken);

    private static async Task<PlanRevisionHistoryHead> ReadHeadAsync(
        IQueryable<PlanRevisionRecord> revisions, Guid headId, CancellationToken cancellationToken)
    {
        var head = await revisions.Where(row => row.RevisionId == headId)
            .Select(row => new PlanRevisionHistoryHead(row.VersionNumber, row.RevisionId))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return head.IsValid ? head : throw CorruptHistory();
    }

    private static async Task<bool> VerifyCursorAsync(
        IQueryable<PlanRevisionRecord> revisions, PlanRevisionHistoryHead current,
        PlanRevisionHistoryCursor cursor, CancellationToken cancellationToken)
    {
        if (cursor.HeadVersionNumber > current.VersionNumber)
            return false;

        var headMatches = await revisions.AnyAsync(row => row.VersionNumber == cursor.HeadVersionNumber &&
            row.RevisionId == cursor.HeadRevisionId, cancellationToken).ConfigureAwait(false);
        if (!headMatches)
            return false;

        var anchorMatches = await revisions.AnyAsync(row => row.VersionNumber == cursor.AfterVersionNumber &&
            row.RevisionId == cursor.AfterRevisionId && row.ParentRevisionId == cursor.ExpectedNextRevisionId,
            cancellationToken).ConfigureAwait(false);
        return anchorMatches && await HasContiguousPathAsync(
            revisions, current.VersionNumber, cursor.AfterVersionNumber - 1, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> HasContiguousPathAsync(
        IQueryable<PlanRevisionRecord> revisions, long head, long bottom, CancellationToken cancellationToken)
    {
        if (head == bottom)
            return true;

        // The unique scoped version index gives at most one row per version. Counting only exact
        // predecessor links proves that every version in the interval is connected without reading
        // intervening geometry or materializing the full history. SQL work grows with the interval.
        var matchingLinks = from child in revisions
                            where child.VersionNumber > bottom && child.VersionNumber <= head
                            join parent in revisions on child.ParentRevisionId equals (Guid?)parent.RevisionId
                            where parent.VersionNumber == child.VersionNumber - 1
                            select child.VersionNumber;
        return await matchingLinks.LongCountAsync(cancellationToken).ConfigureAwait(false) == head - bottom;
    }

    private static PlanRevisionSnapshot ValidateAndMap(PlanRevisionRecord row, Guid projectId, Guid planId, Guid revisionId)
    {
        var snapshot = PlanRevisionMapper.ToSnapshot(row);
        return PlanRevisionHistoryService.ValidateSnapshot(snapshot, projectId, planId, revisionId)
            ? snapshot
            : throw CorruptHistory();
    }

    private static InvalidDataException CorruptHistory() => new("Persisted plan revision history failed integrity checks.");
}

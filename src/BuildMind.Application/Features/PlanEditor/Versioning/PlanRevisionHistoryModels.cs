namespace BuildMind.Application.Features.PlanEditor.Versioning;

/// <summary>
/// Keyset cursor issued by <see cref="PlanRevisionHistoryService"/>. The persistence adapter must verify that
/// <see cref="AfterRevisionId"/> is the exact revision at <see cref="AfterVersionNumber"/> and query only the
/// immediately older revisions. This is not an offset and must never be implemented by loading full history.
/// </summary>
public readonly record struct PlanRevisionHistoryCursor(
    Guid ProjectId,
    Guid PlanId,
    long HeadVersionNumber,
    Guid HeadRevisionId,
    long AfterVersionNumber,
    Guid AfterRevisionId,
    Guid ExpectedNextRevisionId)
{
    public bool IsValid =>
        ProjectId != Guid.Empty &&
        PlanId != Guid.Empty &&
        HeadVersionNumber >= AfterVersionNumber &&
        HeadVersionNumber > 1 &&
        HeadRevisionId != Guid.Empty &&
        AfterVersionNumber > 1 &&
        AfterRevisionId != Guid.Empty &&
        ExpectedNextRevisionId != Guid.Empty &&
        AfterRevisionId != ExpectedNextRevisionId &&
        (HeadVersionNumber != AfterVersionNumber || HeadRevisionId == AfterRevisionId);
}

public readonly record struct PlanRevisionHistoryHead(long VersionNumber, Guid RevisionId)
{
    public bool IsValid => VersionNumber > 0 && VersionNumber < long.MaxValue && RevisionId != Guid.Empty;
}

public sealed record PlanRevisionHistoryQuery(
    Guid ProjectId,
    Guid PlanId,
    int PageSize,
    PlanRevisionHistoryCursor? Cursor = null);

public enum PlanRevisionHistorySourceStatus
{
    Succeeded,
    NotFound,
    InvalidCursor
}

/// <summary>
/// Bounded persistence response. For a cursorless request, <see cref="TraversalHead"/> is the authoritative
/// current head established in the same consistent database read as the page; a null head proves empty history.
/// For a continuation, it is the original traversal head carried by the cursor. Implementations return at most
/// the requested page size and determine <see cref="HasMore"/> without loading full history (for example, by
/// reading at most page-size-plus-one rows).
/// </summary>
public sealed record PlanRevisionHistorySourcePage(
    PlanRevisionHistorySourceStatus Status,
    IReadOnlyList<PlanRevisionSnapshot>? Revisions = null,
    bool HasMore = false,
    PlanRevisionHistoryHead? TraversalHead = null,
    bool CursorAnchorVerified = false);

public sealed record PlanRevisionHistorySourceRevision(
    PlanRevisionHistorySourceStatus Status,
    PlanRevisionSnapshot? Revision = null);

/// <summary>
/// Isolated M3 read port. It has no production implementation or registration in STEP 11-B.
/// M4's future adapter must enforce project/plan scope in every database predicate and use keyset pagination.
/// A cursorless read starts at the authoritative current head under a consistent read. A continuation verifies
/// the cursor's project, plan, original head, after-version, after-revision and expected-parent identity against
/// immutable stored lineage before setting <see cref="PlanRevisionHistorySourcePage.CursorAnchorVerified"/>.
/// Invalid or unverifiable anchors return <see cref="PlanRevisionHistorySourceStatus.InvalidCursor"/>. This port
/// requirement is not proof that a production database adapter implements it correctly.
/// </summary>
public interface IPlanRevisionHistoryQueryPort
{
    Task<PlanRevisionHistorySourcePage> ReadPageAsync(
        Guid projectId,
        Guid planId,
        PlanRevisionHistoryCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PlanRevisionHistorySourceRevision> ReadRevisionAsync(
        Guid projectId,
        Guid planId,
        Guid revisionId,
        CancellationToken cancellationToken);
}

public enum PlanRevisionHistoryStatus
{
    Succeeded,
    DeniedOrNotFound,
    InvalidRequest,
    InvalidCursor,
    InvalidDependencyResponse,
    DependencyTimeout,
    DependencyUnavailable,
    UnexpectedInternalFailure
}

public sealed record PlanRevisionHistoryPageResult
{
    public PlanRevisionHistoryPageResult(
        PlanRevisionHistoryStatus status,
        IReadOnlyList<PlanRevisionSnapshot>? revisions = null,
        PlanRevisionHistoryCursor? nextCursor = null)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (status == PlanRevisionHistoryStatus.Succeeded && revisions is null)
            throw new ArgumentNullException(nameof(revisions), "Successful history requires a revision collection.");
        if (status != PlanRevisionHistoryStatus.Succeeded && (revisions is not null || nextCursor is not null))
            throw new ArgumentException("Failure results cannot contain protected revision history.");
        if (nextCursor is { } cursor && (revisions is not { Count: > 0 } || !cursor.IsValid))
            throw new ArgumentException("A continuation cursor requires a valid nonempty successful page.", nameof(nextCursor));

        Status = status;
        Revisions = revisions?.ToArray();
        NextCursor = nextCursor;
    }

    public PlanRevisionHistoryStatus Status { get; }
    public IReadOnlyList<PlanRevisionSnapshot>? Revisions { get; }
    public PlanRevisionHistoryCursor? NextCursor { get; }

    public static PlanRevisionHistoryPageResult Failure(PlanRevisionHistoryStatus status)
    {
        if (status == PlanRevisionHistoryStatus.Succeeded)
            throw new ArgumentException("Success is not a failure status.", nameof(status));
        return new(status);
    }
}

public sealed record PlanRevisionHistoryLookupResult
{
    public PlanRevisionHistoryLookupResult(
        PlanRevisionHistoryStatus status,
        PlanRevisionSnapshot? revision = null)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (status == PlanRevisionHistoryStatus.Succeeded && revision is null)
            throw new ArgumentNullException(nameof(revision), "Successful lookup requires one exact revision.");
        if (status != PlanRevisionHistoryStatus.Succeeded && revision is not null)
            throw new ArgumentException("Failure results cannot contain a protected revision.", nameof(revision));

        Status = status;
        Revision = revision;
    }

    public PlanRevisionHistoryStatus Status { get; }
    public PlanRevisionSnapshot? Revision { get; }

    public static PlanRevisionHistoryLookupResult Failure(PlanRevisionHistoryStatus status)
    {
        if (status == PlanRevisionHistoryStatus.Succeeded)
            throw new ArgumentException("Success is not a failure status.", nameof(status));
        return new(status);
    }
}

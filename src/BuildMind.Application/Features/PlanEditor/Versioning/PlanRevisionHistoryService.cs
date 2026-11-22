using System.Text.Json;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;

namespace BuildMind.Application.Features.PlanEditor.Versioning;

public interface IPlanRevisionHistoryService
{
    Task<PlanRevisionHistoryPageResult> ListAsync(
        PlanRevisionHistoryQuery query,
        CancellationToken cancellationToken = default);

    Task<PlanRevisionHistoryLookupResult> GetAsync(
        Guid projectId,
        Guid planId,
        Guid revisionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owner-scoped, read-only revision history boundary. Authentication comes only from <see cref="ICurrentUser"/>.
/// Authorization is rechecked after the bounded read so revoked or changed authority never releases snapshots.
/// Database-level authorization and consistency remain responsibilities of M4's future query adapter.
/// </summary>
public sealed class PlanRevisionHistoryService(
    ICurrentUser currentUser,
    IProjectAuthorizationPort authorization,
    IPlanRevisionHistoryQueryPort history) : IPlanRevisionHistoryService
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;

    // STEP 11-D keeps shared PlanEditorJson behavior unchanged while making this read boundary reject duplicate
    // known properties. Hashing remains over the exact stored UTF-8 JSON, matching PlanRevisionService.
    private static readonly JsonSerializerOptions StrictHistoryJson = new(PlanEditorJson.StrictOptions)
    {
        AllowDuplicateProperties = false
    };

    private readonly ICurrentUser currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    private readonly IProjectAuthorizationPort authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    private readonly IPlanRevisionHistoryQueryPort history = history ?? throw new ArgumentNullException(nameof(history));

    public async Task<PlanRevisionHistoryPageResult> ListAsync(
        PlanRevisionHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (query.ProjectId == Guid.Empty || query.PlanId == Guid.Empty ||
            query.PageSize is < 1 or > MaximumPageSize)
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidRequest);
        if (query.Cursor is { } cursor && !cursor.IsValid)
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidCursor);
        if (query.Cursor is { } scopedCursor &&
            (scopedCursor.ProjectId != query.ProjectId || scopedCursor.PlanId != query.PlanId))
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidCursor);

        var actor = GetActor();
        if (actor is null)
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.DeniedOrNotFound);

        var initial = await AuthorizeAsync(query.ProjectId, actor.Value, cancellationToken).ConfigureAwait(false);
        if (initial.Status != AuthorizationReadStatus.Authorized)
            return PlanRevisionHistoryPageResult.Failure(MapAuthorizationFailure(initial.Status));

        PlanRevisionHistorySourcePage? source;
        try
        {
            source = await history.ReadPageAsync(
                query.ProjectId, query.PlanId, query.Cursor, query.PageSize, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.DependencyTimeout); }
        catch (HttpRequestException)
        { return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.DependencyUnavailable); }
        catch (Exception)
        { return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.UnexpectedInternalFailure); }

        var final = await AuthorizeAsync(query.ProjectId, actor.Value, cancellationToken).ConfigureAwait(false);
        if (final.Status != AuthorizationReadStatus.Authorized)
            return PlanRevisionHistoryPageResult.Failure(MapAuthorizationFailure(final.Status));
        if (initial.Snapshot!.Version != final.Snapshot!.Version)
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.DeniedOrNotFound);

        if (source is null || !Enum.IsDefined(source.Status))
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);
        if (source.Status == PlanRevisionHistorySourceStatus.NotFound)
            return HasNoProtectedPagePayload(source)
                ? PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.DeniedOrNotFound)
                : PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);
        if (source.Status == PlanRevisionHistorySourceStatus.InvalidCursor)
            return HasNoProtectedPagePayload(source)
                ? PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidCursor)
                : PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);
        if (source.Revisions is null || source.Revisions.Count > query.PageSize ||
            source.HasMore && source.Revisions.Count == 0)
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);

        var revisions = source.Revisions.ToArray();
        if (!ValidatePage(
                query, revisions, source.HasMore, source.TraversalHead, source.CursorAnchorVerified))
            return PlanRevisionHistoryPageResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);

        PlanRevisionHistoryCursor? next = null;
        if (source.HasMore)
        {
            var last = revisions[^1];
            next = new PlanRevisionHistoryCursor(
                query.ProjectId,
                query.PlanId,
                source.TraversalHead!.Value.VersionNumber,
                source.TraversalHead.Value.RevisionId,
                last.VersionNumber,
                last.RevisionId,
                last.ParentRevisionId!.Value);
        }

        return new(PlanRevisionHistoryStatus.Succeeded, revisions, next);
    }

    public async Task<PlanRevisionHistoryLookupResult> GetAsync(
        Guid projectId,
        Guid planId,
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty || planId == Guid.Empty || revisionId == Guid.Empty)
            return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.InvalidRequest);

        var actor = GetActor();
        if (actor is null)
            return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.DeniedOrNotFound);

        var initial = await AuthorizeAsync(projectId, actor.Value, cancellationToken).ConfigureAwait(false);
        if (initial.Status != AuthorizationReadStatus.Authorized)
            return PlanRevisionHistoryLookupResult.Failure(MapAuthorizationFailure(initial.Status));

        PlanRevisionHistorySourceRevision? source;
        try
        {
            source = await history.ReadRevisionAsync(
                projectId, planId, revisionId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.DependencyTimeout); }
        catch (HttpRequestException)
        { return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.DependencyUnavailable); }
        catch (Exception)
        { return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.UnexpectedInternalFailure); }

        var final = await AuthorizeAsync(projectId, actor.Value, cancellationToken).ConfigureAwait(false);
        if (final.Status != AuthorizationReadStatus.Authorized)
            return PlanRevisionHistoryLookupResult.Failure(MapAuthorizationFailure(final.Status));
        if (initial.Snapshot!.Version != final.Snapshot!.Version)
            return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.DeniedOrNotFound);

        if (source is null || !Enum.IsDefined(source.Status))
            return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);
        if (source.Status != PlanRevisionHistorySourceStatus.Succeeded)
            return source.Status == PlanRevisionHistorySourceStatus.NotFound && source.Revision is null
                ? PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.DeniedOrNotFound)
                : PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);
        if (!ValidateSnapshot(source.Revision, projectId, planId, revisionId))
            return PlanRevisionHistoryLookupResult.Failure(PlanRevisionHistoryStatus.InvalidDependencyResponse);

        return new(PlanRevisionHistoryStatus.Succeeded, source.Revision);
    }

    private Guid? GetActor()
    {
        try
        {
            var actor = currentUser.GetRequiredUserId();
            return actor == Guid.Empty ? null : actor;
        }
        catch (UnauthenticatedAccessException)
        {
            return null;
        }
    }

    private async Task<AuthorizationReadResult> AuthorizeAsync(
        Guid projectId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        ProjectAuthorizationResult? result;
        try
        {
            result = await authorization.AuthorizeOwnerAsync(projectId, actorId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        { return new(AuthorizationReadStatus.DependencyTimeout); }
        catch (HttpRequestException)
        { return new(AuthorizationReadStatus.DependencyUnavailable); }
        catch (Exception)
        { return new(AuthorizationReadStatus.UnexpectedInternalFailure); }

        if (result is null || !Enum.IsDefined(result.Status))
            return new(AuthorizationReadStatus.InvalidDependencyResponse);
        if (result.Status == ProjectAuthorizationStatus.Denied)
            return result.Snapshot is null
                ? new(AuthorizationReadStatus.Denied)
                : new(AuthorizationReadStatus.InvalidDependencyResponse);
        if (result.Snapshot is null || result.Snapshot.ProjectId != projectId ||
            result.Snapshot.ActorId != actorId || !result.Snapshot.Version.IsValid)
            return new(AuthorizationReadStatus.InvalidDependencyResponse);

        return new(AuthorizationReadStatus.Authorized, result.Snapshot);
    }

    private static PlanRevisionHistoryStatus MapAuthorizationFailure(AuthorizationReadStatus status) => status switch
    {
        AuthorizationReadStatus.Denied => PlanRevisionHistoryStatus.DeniedOrNotFound,
        AuthorizationReadStatus.InvalidDependencyResponse => PlanRevisionHistoryStatus.InvalidDependencyResponse,
        AuthorizationReadStatus.DependencyTimeout => PlanRevisionHistoryStatus.DependencyTimeout,
        AuthorizationReadStatus.DependencyUnavailable => PlanRevisionHistoryStatus.DependencyUnavailable,
        _ => PlanRevisionHistoryStatus.UnexpectedInternalFailure
    };

    private static bool HasNoProtectedPagePayload(PlanRevisionHistorySourcePage source) =>
        source.Revisions is null && !source.HasMore && source.TraversalHead is null &&
        !source.CursorAnchorVerified;

    private static bool ValidatePage(
        PlanRevisionHistoryQuery query,
        IReadOnlyList<PlanRevisionSnapshot> revisions,
        bool hasMore,
        PlanRevisionHistoryHead? traversalHead,
        bool cursorAnchorVerified)
    {
        if (revisions.Count == 0)
            return query.Cursor is null && !hasMore && traversalHead is null && !cursorAnchorVerified;

        if (traversalHead is not { } head || !head.IsValid)
            return false;

        if (query.Cursor is null)
        {
            if (cursorAnchorVerified || revisions[0].VersionNumber != head.VersionNumber ||
                revisions[0].RevisionId != head.RevisionId)
                return false;
        }
        else
        {
            var cursor = query.Cursor.Value;
            if (!cursorAnchorVerified || cursor.HeadVersionNumber != head.VersionNumber ||
                cursor.HeadRevisionId != head.RevisionId ||
                revisions[0].VersionNumber != cursor.AfterVersionNumber - 1 ||
                revisions[0].RevisionId != cursor.ExpectedNextRevisionId)
                return false;
        }

        var revisionIds = new HashSet<Guid>();
        var versionNumbers = new HashSet<long>();
        foreach (var revision in revisions)
        {
            if (!ValidateSnapshot(revision, query.ProjectId, query.PlanId, revision?.RevisionId ?? Guid.Empty) ||
                !revisionIds.Add(revision!.RevisionId) || !versionNumbers.Add(revision.VersionNumber))
                return false;
        }

        for (var index = 0; index < revisions.Count - 1; index++)
        {
            var newer = revisions[index];
            var older = revisions[index + 1];
            if (newer.VersionNumber != older.VersionNumber + 1 || newer.ParentRevisionId != older.RevisionId)
                return false;
        }

        var last = revisions[^1];
        var lineageRequiresMore = last.VersionNumber > 1;
        return hasMore == lineageRequiresMore &&
            (!hasMore || last.ParentRevisionId is { } parent && parent != Guid.Empty);
    }

    private static bool ValidateSnapshot(
        PlanRevisionSnapshot? snapshot,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid expectedRevisionId)
    {
        if (snapshot is null || snapshot.RevisionId == Guid.Empty || snapshot.RevisionId != expectedRevisionId ||
            snapshot.ProjectId != expectedProjectId || snapshot.PlanId != expectedPlanId ||
            snapshot.VersionNumber <= 0 || snapshot.VersionNumber == long.MaxValue ||
            snapshot.GeometrySchemaVersion != GeometrySchemaInfo.DraftVersion ||
            string.IsNullOrWhiteSpace(snapshot.GeometryJson) || string.IsNullOrWhiteSpace(snapshot.GeometryHash) ||
            snapshot.LandRevisionId == Guid.Empty || snapshot.RequirementsRevisionId == Guid.Empty ||
            snapshot.CreatedBy == Guid.Empty || snapshot.CreatedAtUtc == default || snapshot.Validation is null ||
            snapshot.Validation.ValidationId == Guid.Empty || string.IsNullOrWhiteSpace(snapshot.Validation.ValidatorVersion) ||
            string.IsNullOrWhiteSpace(snapshot.Validation.RegulatoryEvidenceStatus) ||
            snapshot.Validation.ValidatedAtUtc == default ||
            snapshot.Validation.ValidatedAtUtc.Offset != TimeSpan.Zero ||
            !snapshot.Validation.GeometryValid || snapshot.Validation.HasBlockingIssues ||
            !string.Equals(snapshot.Validation.GeometryHash, snapshot.GeometryHash, StringComparison.Ordinal))
            return false;

        if (snapshot.VersionNumber == 1 && snapshot.ParentRevisionId is not null ||
            snapshot.VersionNumber > 1 && (snapshot.ParentRevisionId is null ||
                snapshot.ParentRevisionId == Guid.Empty || snapshot.ParentRevisionId == snapshot.RevisionId))
            return false;

        try
        {
            var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(
                snapshot.GeometryJson, StrictHistoryJson);
            if (geometry is null)
                return false;
            PlanEditorContractValidator.Validate(geometry);
            return geometry.ProjectId == snapshot.ProjectId && geometry.PlanId == snapshot.PlanId &&
                geometry.SchemaVersion == snapshot.GeometrySchemaVersion &&
                geometry.LandRevisionId == snapshot.LandRevisionId &&
                geometry.RequirementsRevisionId == snapshot.RequirementsRevisionId &&
                string.Equals(PlanRevisionService.ComputeGeometryHash(snapshot.GeometryJson),
                    snapshot.GeometryHash, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NullReferenceException)
        {
            return false;
        }
    }

    private enum AuthorizationReadStatus
    {
        Authorized,
        Denied,
        InvalidDependencyResponse,
        DependencyTimeout,
        DependencyUnavailable,
        UnexpectedInternalFailure
    }

    private sealed record AuthorizationReadResult(
        AuthorizationReadStatus Status,
        ProjectAuthorizationSnapshot? Snapshot = null);
}

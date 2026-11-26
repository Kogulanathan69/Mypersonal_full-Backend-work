using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Application.Features.Requirements.Services;

/// <summary>Composes a section replacement from an exact base and delegates validation/atomic saving.</summary>
/// <remarks>
/// The existing full service owns authenticated project access, authoritative policy and persistence.
/// Sequential reads and checks are not an atomic ownership protocol. Production registration remains
/// prohibited. The original expected revision is retained across read/save; no rebase or retry occurs.
/// </remarks>
public sealed class RequirementsSectionApplicationService : IRequirementsSectionApplicationService
{
    private readonly IRequirementsApplicationService _requirements;

    public RequirementsSectionApplicationService(IRequirementsApplicationService requirements) =>
        _requirements = requirements ?? throw new ArgumentNullException(nameof(requirements));

    public async Task<RequirementsOperationResult> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty) throw new ArgumentException("A project identity is required.", nameof(projectId));
        var result = await _requirements.GetAsync(projectId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null || result.Status is RequirementsOperationStatus.RevisionConflict
                or RequirementsOperationStatus.CatalogueConflict or RequirementsOperationStatus.ValidationFailed
            || (result.Status == RequirementsOperationStatus.Success && result.Revision!.ProjectId != projectId))
            return new(RequirementsOperationStatus.InvalidDependencyResponse);
        return result;
    }

    public Task<RequirementsOperationResult> SaveSpacesAsync(Guid projectId, SaveAdditionalSpacesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ReplaceAsync(projectId, request.ExpectedRevisionId, revision => new SaveRequirementsRequest(
            request.ExpectedRevisionId, request.CatalogueVersion, revision.FloorCount, revision.GroundFloorRooms,
            request.AdditionalSpaces, revision.Relationships, revision.DesignMethod), cancellationToken);
    }

    public Task<RequirementsOperationResult> SaveRelationshipsAsync(Guid projectId, SaveRoomRelationshipsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ReplaceAsync(projectId, request.ExpectedRevisionId, revision => new SaveRequirementsRequest(
            request.ExpectedRevisionId, request.CatalogueVersion, revision.FloorCount, revision.GroundFloorRooms,
            revision.AdditionalSpaces, request.Relationships, revision.DesignMethod), cancellationToken);
    }

    public Task<RequirementsOperationResult> SaveDesignMethodAsync(Guid projectId, SaveDesignMethodRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ReplaceAsync(projectId, request.ExpectedRevisionId, revision => new SaveRequirementsRequest(
            request.ExpectedRevisionId, request.CatalogueVersion, revision.FloorCount, revision.GroundFloorRooms,
            revision.AdditionalSpaces, revision.Relationships, request.DesignMethod), cancellationToken);
    }

    private async Task<RequirementsOperationResult> ReplaceAsync(Guid projectId, Guid expectedRevisionId,
        Func<RequirementRevisionDto, SaveRequirementsRequest> compose, CancellationToken token)
    {
        var current = await GetAsync(projectId, token).ConfigureAwait(false);
        if (current.Status == RequirementsOperationStatus.NoSavedRevision)
            return new(RequirementsOperationStatus.RevisionConflict); // Provisional: sections cannot create core content.
        if (current.Status != RequirementsOperationStatus.Success) return current;
        if (current.Revision!.RevisionId != expectedRevisionId)
            return new(RequirementsOperationStatus.RevisionConflict);

        var snapshot = compose(current.Revision);
        token.ThrowIfCancellationRequested();
        var saved = await _requirements.SaveAsync(projectId, snapshot, token).ConfigureAwait(false);
        // Do not reclassify confirmed saves as cancellation. Unknown transaction outcomes propagate.
        if (saved is null || saved.Status == RequirementsOperationStatus.NoSavedRevision)
            return new(RequirementsOperationStatus.InvalidDependencyResponse);
        if (saved.Revision is { } revision
            && (revision.ProjectId != projectId
                || revision.RevisionId == expectedRevisionId
                || revision.RevisionNumber - 1 != current.Revision.RevisionNumber
                || !MatchesSubmittedContent(snapshot, revision)))
            return new(RequirementsOperationStatus.InvalidDependencyResponse);
        return saved;
    }

    // A successful receipt must describe the exact submitted section replacement and untouched
    // base content. A mismatched receipt is an unknown persistence outcome, not proof of rollback;
    // never retry or expose a fabricated successful revision.
    private static bool MatchesSubmittedContent(SaveRequirementsRequest submitted, RequirementRevisionDto saved) =>
        saved.CatalogueVersion == submitted.CatalogueVersion
        && saved.FloorCount == submitted.FloorCount
        && saved.DesignMethod == submitted.DesignMethod
        && saved.GroundFloorRooms.Cast<RoomCountInput?>().SequenceEqual(submitted.GroundFloorRooms ?? [])
        && saved.AdditionalSpaces.Cast<AdditionalSpaceInput?>().SequenceEqual(submitted.AdditionalSpaces)
        && saved.Relationships.Cast<RoomRelationshipInput?>().SequenceEqual(submitted.Relationships);
}

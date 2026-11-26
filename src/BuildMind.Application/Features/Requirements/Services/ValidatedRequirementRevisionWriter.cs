using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Requirements.Services;

/// <summary>Prepares a validated immutable revision and delegates its atomic save to the existing port.</summary>
/// <remarks>
/// This is not an authorization boundary or an HTTP application service. A future trusted caller must
/// establish project access and the agreed ownership-consistency guarantee before invoking this writer.
/// Policy selection and approval belong to that caller; successful validation alone proves neither.
/// This component is deliberately not registered in production DI.
/// </remarks>
public sealed class ValidatedRequirementRevisionWriter
{
    private readonly IRequirementRevisionRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ValidatedRequirementRevisionWriter(
        IRequirementRevisionRepository repository,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Null expected revision means initial creation only. The preliminary read does not reserve the
    /// head: TrySaveAtomicallyAsync remains authoritative for concurrent saves. No write is retried.
    /// </summary>
    public async Task<RequirementSaveResult> SaveAsync(
        Guid projectId,
        Guid? expectedRevisionId,
        RequirementValidationResult validation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty)
            throw new ArgumentException("A project identity is required.", nameof(projectId));
        if (expectedRevisionId == Guid.Empty)
            throw new ArgumentException("Use a revision identity or null for an initial save.", nameof(expectedRevisionId));
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.Status != RequirementValidationStatus.Valid)
            throw new ArgumentException("Successful requirements validation is required.", nameof(validation));

        var current = await _repository.GetCurrentAsync(projectId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (current is not null && current.ProjectId != projectId)
            throw new InvalidOperationException("The repository returned an inconsistent project identity.");

        if (current?.Id != expectedRevisionId)
            return new RequirementSaveResult(RequirementSaveStatus.RevisionConflict);

        var number = current is null ? 1L : checked(current.RevisionNumber + 1);
        var proposed = validation.CreateRevision(Guid.NewGuid(), projectId, number, _timeProvider.GetUtcNow());
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _repository.TrySaveAtomicallyAsync(expectedRevisionId, proposed, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
            throw new InvalidOperationException("The repository returned no save result.");
        if (result.Status == RequirementSaveStatus.Saved && !Matches(proposed, result.SavedRevision!))
            throw new InvalidOperationException("The repository returned an inconsistent saved revision.");

        // A response-integrity failure does not prove rollback. Never retry it, or turn a confirmed
        // save into a claimed cancellation/rollback merely because cancellation arrived after commit.
        return result;
    }

    private static bool Matches(RequirementRevision proposed, RequirementRevision saved) =>
        proposed.Id == saved.Id && proposed.ProjectId == saved.ProjectId
        && proposed.RevisionNumber == saved.RevisionNumber
        && proposed.SchemaVersion == saved.SchemaVersion && proposed.CatalogueVersion == saved.CatalogueVersion
        && proposed.CreatedAtUtc == saved.CreatedAtUtc
        && proposed.Content.FloorCount == saved.Content.FloorCount
        && proposed.Content.DesignMethod == saved.Content.DesignMethod
        && proposed.Content.GroundFloorRooms.SequenceEqual(saved.Content.GroundFloorRooms)
        && proposed.Content.AdditionalSpaces.SequenceEqual(saved.Content.AdditionalSpaces)
        && proposed.Content.Relationships.SequenceEqual(saved.Content.Relationships);
}

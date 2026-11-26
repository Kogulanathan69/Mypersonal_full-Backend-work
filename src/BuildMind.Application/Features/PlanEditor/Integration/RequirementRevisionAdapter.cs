using BuildMind.Application.Features.Requirements.Abstractions;

namespace BuildMind.Application.Features.PlanEditor.Integration;

/// <summary>
/// Maps an exact M5 requirement revision into the reference consumed by M3.
/// The caller must establish authenticated project ownership before invoking this adapter;
/// <see cref="IRequirementRevisionRepository"/> is a persistence port, not an authorization guard.
/// </summary>
public sealed class RequirementRevisionAdapter(IRequirementRevisionRepository revisions)
    : IRequirementRevisionPort
{
    private readonly IRequirementRevisionRepository revisions =
        revisions ?? throw new ArgumentNullException(nameof(revisions));

    public async Task<RequirementRevisionReference?> GetAsync(
        Guid projectId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("A project identifier is required.", nameof(projectId));
        if (revisionId == Guid.Empty)
            throw new ArgumentException("A requirement revision identifier is required.", nameof(revisionId));

        var revision = await revisions.GetAsync(projectId, revisionId, cancellationToken);
        if (revision is null)
            return null;

        if (revision.ProjectId != projectId ||
            revision.Id != revisionId ||
            string.IsNullOrWhiteSpace(revision.SchemaVersion))
            throw new InvalidDependencyResponseException(
                "The requirement dependency returned a contradictory revision reference.");

        return new RequirementRevisionReference(
            revision.ProjectId,
            revision.Id,
            revision.SchemaVersion);
    }
}

/// <summary>
/// Identifies a response that was received from an M3 dependency but violated that dependency's
/// response contract. Keeping this type internal prevents it from becoming a shared public contract.
/// </summary>
internal sealed class InvalidDependencyResponseException(string message) : Exception(message);

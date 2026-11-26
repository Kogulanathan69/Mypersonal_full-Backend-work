namespace BuildMind.Application.Features.PlanEditor.Integration;

public enum RequirementRevisionAccessStatus
{
    Found,
    NotFound,
    Unauthorized,
    InvalidDependencyResponse
}

/// <summary>
/// Result of the proposed authorization-aware exact requirement revision boundary.
/// This contract does not claim that the current M1 and M5 repositories are database-atomic.
/// </summary>
public sealed record RequirementRevisionAccessResult
{
    public RequirementRevisionAccessResult(
        RequirementRevisionAccessStatus status,
        RequirementRevisionReference? revision = null,
        ProjectAuthorizationSnapshot? authorization = null)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        switch (status)
        {
            case RequirementRevisionAccessStatus.Found:
                if (revision is null)
                    throw new ArgumentNullException(nameof(revision), "A found result requires an exact revision.");
                if (authorization is null)
                    throw new ArgumentNullException(nameof(authorization), "A found result requires authorization evidence.");
                ValidateRevision(revision);
                if (revision.ProjectId != authorization.ProjectId)
                    throw new ArgumentException(
                        "The requirement revision and authorization snapshot must identify the same project.",
                        nameof(revision));
                break;

            case RequirementRevisionAccessStatus.NotFound:
                if (revision is not null)
                    throw new ArgumentException("A not-found result cannot contain a revision.", nameof(revision));
                if (authorization is null)
                    throw new ArgumentNullException(
                        nameof(authorization), "Authorized absence requires authorization evidence.");
                break;

            case RequirementRevisionAccessStatus.Unauthorized:
                if (revision is not null || authorization is not null)
                    throw new ArgumentException(
                        "Unauthorized access cannot disclose a revision or authorization snapshot.",
                        revision is not null ? nameof(revision) : nameof(authorization));
                break;

            case RequirementRevisionAccessStatus.InvalidDependencyResponse:
                if (revision is not null)
                    throw new ArgumentException(
                        "An invalid dependency response cannot expose a requirement revision.", nameof(revision));
                break;
        }

        Status = status;
        Revision = revision;
        Authorization = authorization;
    }

    public RequirementRevisionAccessStatus Status { get; }
    public RequirementRevisionReference? Revision { get; }
    public ProjectAuthorizationSnapshot? Authorization { get; }

    private static void ValidateRevision(RequirementRevisionReference revision)
    {
        if (revision.ProjectId == Guid.Empty)
            throw new ArgumentException("A requirement project identifier is required.", nameof(revision));
        if (revision.RevisionId == Guid.Empty)
            throw new ArgumentException("A requirement revision identifier is required.", nameof(revision));
        if (string.IsNullOrWhiteSpace(revision.SchemaVersion))
            throw new ArgumentException("A requirement schema version is required.", nameof(revision));
    }
}

/// <summary>
/// Proposed M3 consumer port. Its future production implementation must authorize the actor and retrieve
/// the exact requirement revision under the approved database-atomic protocol. Sequential wrapping of the
/// current repositories is not sufficient to satisfy that requirement.
/// </summary>
public interface IAuthorizedRequirementRevisionPort
{
    Task<RequirementRevisionAccessResult> GetAsync(
        Guid projectId,
        Guid revisionId,
        Guid authenticatedActorId,
        CancellationToken cancellationToken);
}

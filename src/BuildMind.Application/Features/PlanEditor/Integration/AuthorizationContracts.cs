namespace BuildMind.Application.Features.PlanEditor.Integration;

/// <summary>
/// Opaque authorization concurrency evidence supplied by the approved M1/M4 boundary.
/// This feature-local value does not decide whether an existing project revision can serve
/// as the authorization version.
/// </summary>
public readonly record struct AuthorizationVersion
{
    public AuthorizationVersion(long value)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "An authorization version must be positive.");

        Value = value;
    }

    public long Value { get; }

    public bool IsValid => Value > 0;
}

/// <summary>
/// Evidence that an exact authenticated actor was authorized for an exact project.
/// Authentication itself remains the responsibility of the trusted host boundary.
/// </summary>
public sealed record ProjectAuthorizationSnapshot
{
    public ProjectAuthorizationSnapshot(
        Guid projectId,
        Guid actorId,
        AuthorizationVersion version)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("A project identifier is required.", nameof(projectId));
        if (actorId == Guid.Empty)
            throw new ArgumentException("An authenticated actor identifier is required.", nameof(actorId));
        if (!version.IsValid)
            throw new ArgumentException("A valid authorization version is required.", nameof(version));

        ProjectId = projectId;
        ActorId = actorId;
        Version = version;
    }

    public Guid ProjectId { get; }
    public Guid ActorId { get; }
    public AuthorizationVersion Version { get; }
}

public enum ProjectAuthorizationStatus
{
    Authorized,
    Denied
}

/// <summary>
/// Structured authorization outcome. Dependency failures are deliberately not represented as
/// denial statuses: implementations must preserve cancellation and dependency failure distinctions.
/// </summary>
public sealed record ProjectAuthorizationResult
{
    public ProjectAuthorizationResult(
        ProjectAuthorizationStatus status,
        ProjectAuthorizationSnapshot? snapshot = null)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (status == ProjectAuthorizationStatus.Authorized && snapshot is null)
            throw new ArgumentNullException(nameof(snapshot), "Authorized access requires an exact snapshot.");
        if (status == ProjectAuthorizationStatus.Denied && snapshot is not null)
            throw new ArgumentException("Denied access cannot disclose an authorization snapshot.", nameof(snapshot));

        Status = status;
        Snapshot = snapshot;
    }

    public ProjectAuthorizationStatus Status { get; }
    public ProjectAuthorizationSnapshot? Snapshot { get; }
}

/// <summary>
/// Proposed M3 consumer boundary. It remains unregistered until M1 authorization-version semantics
/// and the M4 composition contract are approved.
/// </summary>
public interface IProjectAuthorizationPort
{
    Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
        Guid projectId,
        Guid authenticatedActorId,
        CancellationToken cancellationToken);
}

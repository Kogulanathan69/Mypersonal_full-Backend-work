namespace BuildMind.Domain.Entities;

public static class ProjectAuthorizationPolicy
{
    public const long InitialVersion = 1;

    public static bool IsValid(long version)
        => version > 0;

    public static long NextVersion(long currentVersion)
    {
        if (!IsValid(currentVersion))
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentVersion));
        }

        return checked(currentVersion + 1);
    }

    public static bool Matches(
        long currentVersion,
        long expectedVersion)
    {
        return IsValid(currentVersion) &&
               IsValid(expectedVersion) &&
               currentVersion == expectedVersion;
    }

    public static bool MatchesOwner(
        Guid projectId,
        Guid expectedProjectId,
        Guid ownerId,
        Guid authenticatedActorId)
    {
        return projectId != Guid.Empty &&
               expectedProjectId != Guid.Empty &&
               ownerId != Guid.Empty &&
               authenticatedActorId != Guid.Empty &&
               projectId == expectedProjectId &&
               ownerId == authenticatedActorId;
    }

    public static bool MatchesMutationEvidence(
        Guid projectId,
        Guid expectedProjectId,
        Guid ownerId,
        Guid authenticatedActorId,
        long currentAuthorizationVersion,
        long expectedAuthorizationVersion,
        long currentRevision,
        long expectedRevision)
    {
        return MatchesOwner(
                   projectId,
                   expectedProjectId,
                   ownerId,
                   authenticatedActorId) &&
               Matches(
                   currentAuthorizationVersion,
                   expectedAuthorizationVersion) &&
               currentRevision > 0 &&
               expectedRevision > 0 &&
               currentRevision == expectedRevision;
    }
}
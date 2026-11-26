using BuildMind.Application.Features.Dashboard.Contracts;

namespace BuildMind.Application.Features.Dashboard.Abstractions;

/// <summary>Internal owner binding returned by a trusted adapter; never a customer input/read DTO.</summary>
public sealed class DashboardOwnedProject
{
    public Guid OwnerId { get; }
    public DashboardProjectDto Project { get; }

    public DashboardOwnedProject(Guid ownerId, DashboardProjectDto project)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner identity is required.", nameof(ownerId));
        ArgumentNullException.ThrowIfNull(project);
        OwnerId = ownerId;
        Project = project;
    }
}

public enum DashboardProjectReadStatus { Loaded, Unauthenticated, Unavailable }

/// <summary>Internal source outcome. Loaded-empty is distinct from an unavailable dependency.</summary>
public sealed class DashboardProjectReadResult
{
    public DashboardProjectReadStatus Status { get; }
    public Guid? AuthenticatedOwnerId { get; }
    public IReadOnlyList<DashboardOwnedProject>? Projects { get; }

    public DashboardProjectReadResult(DashboardProjectReadStatus status, Guid? authenticatedOwnerId = null,
        IEnumerable<DashboardOwnedProject>? projects = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == DashboardProjectReadStatus.Loaded)
        {
            if (authenticatedOwnerId is null || authenticatedOwnerId == Guid.Empty)
                throw new ArgumentException("Loaded projects require an authenticated owner binding.", nameof(authenticatedOwnerId));
            ArgumentNullException.ThrowIfNull(projects);
            var rows = projects.ToArray();
            if (rows.Any(row => row is null))
                throw new ArgumentException("Project rows cannot be null.", nameof(projects));
            Projects = Array.AsReadOnly(rows);
        }
        else if (authenticatedOwnerId is not null || projects is not null)
            throw new ArgumentException("Unsuccessful source outcomes cannot carry owner or project data.");
        Status = status;
        AuthenticatedOwnerId = authenticatedOwnerId;
    }
}

/// <summary>
/// M5 consumer port only, not a replacement M1 repository or authentication contract. No production
/// adapter is implemented. The adapter must resolve the current user through verified M4 ICurrentUser,
/// then call M1 IProjectRepository.GetByOwnerIdAsync using that identity. Those remote contracts are
/// not yet integrated into this checkout. Never use an owner ID from request data or arbitrary headers.
/// Map rejection by ICurrentUser to Unauthenticated before any project query is attempted.
/// </summary>
/// <remarks>
/// Return the complete, consistently read owner-scoped set (not a page) and bind every row to its
/// actual persisted owner. StageCode must preserve the agreed authoritative M1 Project.Status;
/// no stage defaults, percentages or inferred lifecycle mapping. If source/policy is unavailable,
/// return Unavailable or throw; never substitute an empty Loaded set. Cancellation must be honored.
/// Construction of this result is not proof of authentication or database isolation.
/// </remarks>
public interface IDashboardProjectSource
{
    Task<DashboardProjectReadResult> GetForCurrentUserAsync(CancellationToken cancellationToken);
}

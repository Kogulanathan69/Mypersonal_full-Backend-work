using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Dashboard.Abstractions;
using BuildMind.Application.Features.Dashboard.Contracts;

namespace BuildMind.Application.Features.Dashboard.Services;

public sealed class DashboardProjectSource : IDashboardProjectSource
{
    private readonly ICurrentUser _currentUser;
    private readonly IProjectRepository _repository;

    public DashboardProjectSource(
        ICurrentUser currentUser,
        IProjectRepository repository)
    {
        _currentUser = currentUser
            ?? throw new ArgumentNullException(nameof(currentUser));

        _repository = repository
            ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<DashboardProjectReadResult> GetForCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Guid ownerId;

        try
        {
            if (!_currentUser.IsAuthenticated)
                return new(DashboardProjectReadStatus.Unauthenticated);

            ownerId = _currentUser.GetRequiredUserId();

            if (ownerId == Guid.Empty ||
                _currentUser.UserId != ownerId)
            {
                return new(DashboardProjectReadStatus.Unauthenticated);
            }
        }
        catch (UnauthenticatedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(DashboardProjectReadStatus.Unauthenticated);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var projects = await _repository
            .GetByOwnerIdAsync(ownerId, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(projects);

        var rows = new List<DashboardOwnedProject>();
        var projectIds = new HashSet<Guid>();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (project is null ||
                project.OwnerId != ownerId ||
                !projectIds.Add(project.Id))
            {
                throw new ArgumentException(
                    "Invalid project repository response.");
            }

            rows.Add(new DashboardOwnedProject(
                project.OwnerId,
                new DashboardProjectDto(
                    project.Id,
                    project.Name,
                    project.Status)));
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new DashboardProjectReadResult(
            DashboardProjectReadStatus.Loaded,
            ownerId,
            rows);
    }
}
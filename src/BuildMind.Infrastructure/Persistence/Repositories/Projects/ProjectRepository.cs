using BuildMind.Application.Abstractions;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Projects;

public sealed class ProjectRepository(
    BuildMindDbContext context) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var row = await context.Set<ProjectRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == id,
                cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task<Project?> GetByIdAndOwnerAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var row = await context.Set<ProjectRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == id && p.OwnerId == ownerId,
                cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<Project>> GetByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var rows = await context.Set<ProjectRecord>()
            .AsNoTracking()
            .Where(p => p.OwnerId == ownerId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToArray();
    }

    public async Task AddAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);

        var row = new ProjectRecord
        {
            Id = project.Id,
            OwnerId = project.OwnerId,
            Name = project.Name,
            ProjectType = project.ProjectType,
            LocationReference = project.LocationReference,
            Status = project.Status,
            Revision = project.Revision,
            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc
        };

        context.Set<ProjectRecord>().Add(row);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> UpdateAsync(
        Project project,
        Guid ownerId,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (ownerId == Guid.Empty ||
            project.OwnerId != ownerId ||
            expectedRevision < 1 ||
            expectedRevision == long.MaxValue ||
            project.Revision != expectedRevision + 1)
        {
            return false;
        }

        var affected = await context.Set<ProjectRecord>()
            .Where(p =>
                p.Id == project.Id &&
                p.OwnerId == ownerId &&
                p.Revision == expectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.Name, project.Name)
                .SetProperty(p => p.ProjectType, project.ProjectType)
                .SetProperty(p => p.LocationReference, project.LocationReference)
                .SetProperty(p => p.Revision, project.Revision)
                .SetProperty(p => p.UpdatedAtUtc, project.UpdatedAtUtc),
                cancellationToken);

        return affected == 1;
    }

    private static Project Map(ProjectRecord row)
    {
        return Project.Restore(
            row.Id,
            row.OwnerId,
            row.Name,
            row.ProjectType,
            row.LocationReference,
            row.Status,
            row.Revision,
            row.CreatedAtUtc,
            row.UpdatedAtUtc);
    }
}
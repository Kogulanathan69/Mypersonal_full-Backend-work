using BuildMind.Domain.Entities;

namespace BuildMind.Application.Abstractions;

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<Project?> GetByIdAndOwnerAsync(
        Guid id,
        Guid ownerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Project>> GetByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken);

    Task AddAsync(
        Project project,
        CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        Project project,
        Guid ownerId,
        long expectedRevision,
        CancellationToken cancellationToken);
}
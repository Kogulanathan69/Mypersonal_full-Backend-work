using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Identity;

public sealed class UserProfileRepository(
    BuildMindDbContext context) : IUserProfileRepository
{
    public Task<ProfileResponse?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return context.Set<UserRecord>()
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new ProfileResponse
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive,
                CreatedAtUtc = u.CreatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ProfileResponse?> UpdateAsync(
        Guid userId,
        string fullName,
        string? phoneNumber,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return null;

        await context.Set<UserRecord>()
            .Where(u => u.Id == userId && u.IsActive)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FullName, fullName)
                .SetProperty(u => u.PhoneNumber, phoneNumber),
                cancellationToken);

        return await GetAsync(
            userId, cancellationToken);
    }
}
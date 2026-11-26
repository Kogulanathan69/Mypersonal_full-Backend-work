using BuildMind.Application.Features.Users.DTOs;

namespace BuildMind.Application.Features.Users.Abstractions;

public interface IUserProfileRepository
{
    Task<ProfileResponse?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<ProfileResponse?> UpdateAsync(
        Guid userId,
        string fullName,
        string? phoneNumber,
        CancellationToken cancellationToken);
}
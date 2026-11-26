namespace BuildMind.Application.Features.Auth.DTOs;

public sealed class UserResponse
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
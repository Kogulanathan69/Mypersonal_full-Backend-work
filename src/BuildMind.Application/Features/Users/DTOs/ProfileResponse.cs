namespace BuildMind.Application.Features.Users.DTOs;

public sealed class ProfileResponse
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public string? FullName { get; init; }

    public string? PhoneNumber { get; init; }

    public bool IsActive { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }
}
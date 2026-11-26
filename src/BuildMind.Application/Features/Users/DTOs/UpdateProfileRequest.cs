namespace BuildMind.Application.Features.Users.DTOs;

public sealed class UpdateProfileRequest
{
    public required string FullName { get; init; }

    public string? PhoneNumber { get; init; }
}
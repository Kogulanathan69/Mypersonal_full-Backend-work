namespace BuildMind.Application.Features.Auth.DTOs;

public sealed class LoginRequest
{
    public required string Email { get; init; }

    public required string Password { get; init; }
}
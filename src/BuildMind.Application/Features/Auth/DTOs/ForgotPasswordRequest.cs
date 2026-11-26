namespace BuildMind.Application.Features.Auth.DTOs;

public sealed class ForgotPasswordRequest
{
    public required string Email { get; init; }
}

namespace BuildMind.Application.Features.Auth.DTOs;

public sealed class ResetPasswordRequest
{
    public required string Email { get; init; }

    public required string Token { get; init; }

    public required string NewPassword { get; init; }

    public required string ConfirmPassword { get; init; }
}
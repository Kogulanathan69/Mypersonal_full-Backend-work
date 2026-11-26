using BuildMind.Application.Features.Auth.Abstractions;

namespace BuildMind.Infrastructure.Identity;

public sealed class PasswordResetTokenProvider
    : IPasswordResetTokenProvider
{
    public string Generate()
        => PasswordResetTokenService.Generate();

    public string Hash(string token)
        => PasswordResetTokenService.Hash(token);

    public bool Verify(string? token, string? storedHash)
        => PasswordResetTokenService.Verify(token, storedHash);
}
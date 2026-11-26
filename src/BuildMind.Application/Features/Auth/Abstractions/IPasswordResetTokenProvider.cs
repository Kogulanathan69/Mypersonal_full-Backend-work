namespace BuildMind.Application.Features.Auth.Abstractions;

public interface IPasswordResetTokenProvider
{
    string Generate();

    string Hash(string token);

    bool Verify(string? token, string? storedHash);
}
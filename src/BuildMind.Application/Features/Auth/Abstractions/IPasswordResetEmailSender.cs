namespace BuildMind.Application.Features.Auth.Abstractions;

public interface IPasswordResetEmailSender
{
    Task<bool> SendAsync(
        string email,
        string token,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken);
}
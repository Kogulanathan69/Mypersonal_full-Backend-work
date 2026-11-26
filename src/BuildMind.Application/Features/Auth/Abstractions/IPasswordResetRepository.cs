namespace BuildMind.Application.Features.Auth.Abstractions;

public sealed record PasswordResetTokenRecord(
    Guid UserId,
    string TokenHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public interface IPasswordResetRepository
{
    Task<bool> ReplaceTokenAsync(
        PasswordResetTokenRecord token,
        CancellationToken cancellationToken);

    Task<bool> ResetPasswordAtomicallyAsync(
        Guid userId,
        string tokenHash,
        string newPasswordHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}
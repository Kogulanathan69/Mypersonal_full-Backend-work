using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Auth.Abstractions;
using BuildMind.Application.Features.Auth.DTOs;
using BuildMind.Application.Features.Auth.Validators;

namespace BuildMind.Application.Features.Auth.Services;

public sealed class PasswordRecoveryService(
    IUserRepository users,
    IPasswordResetRepository resets,
    IPasswordResetTokenProvider tokens,
    IPasswordHashService passwords,
    IPasswordResetEmailSender emailSender,
    TimeProvider timeProvider)
{
    public const int ExpirationMinutes = 15;

    public const string GenericResponse =
        "If an eligible account exists, password reset instructions will be sent.";

    public async Task<string> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!new ForgotPasswordRequestValidator()
            .Validate(request).IsValid)
        {
            return GenericResponse;
        }

        var email = request.Email.Trim().ToUpperInvariant();

        var user = await users.GetByEmailAsync(
            email, cancellationToken);

        if (user is null || !user.IsActive)
            return GenericResponse;

        var token = tokens.Generate();
        var tokenHash = tokens.Hash(token);

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(ExpirationMinutes);

        var record = new PasswordResetTokenRecord(
            user.Id,
            tokenHash,
            now,
            expiresAt);

        var saved = await resets.ReplaceTokenAsync(
            record, cancellationToken);

        if (!saved)
            return GenericResponse;

        _ = await emailSender.SendAsync(
            user.Email,
            token,
            expiresAt,
            cancellationToken);

        return GenericResponse;
    }

    public async Task<bool> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!new ResetPasswordRequestValidator()
            .Validate(request).IsValid)
        {
            return false;
        }

        if (request.Token.Length != 64 ||
            !request.Token.All(Uri.IsHexDigit))
        {
            return false;
        }

        var normalizedEmail =
            request.Email.Trim().ToUpperInvariant();

        var user = await users.GetByEmailAsync(
            normalizedEmail,
            cancellationToken);

        if (user is null || !user.IsActive)
            return false;

        var tokenHash = tokens.Hash(request.Token);

        var newPasswordHash =
            passwords.HashPassword(request.NewPassword);

        // Repository must atomically verify and consume
        // the token, update the password, invalidate
        // previous tokens and persist revocation state.

        return await resets.ResetPasswordAtomicallyAsync(
            user.Id,
            tokenHash,
            newPasswordHash,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
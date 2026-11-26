using System.Security.Cryptography;
using BuildMind.Application.Features.Auth.Abstractions;

namespace BuildMind.Application.Features.Administration;

public sealed record StaffInvitationResult(Guid UserId, bool InvitationSent, DateTimeOffset InvitationExpiresAtUtc);

/// <summary>
/// Staff provisioning without any plaintext or temporary password (09-ADMIN): the account gets an unusable random hash
/// and the invitee sets a password through a single-use, hashed, expiring reset token delivered by email. The token is
/// never returned by the API or logged.
/// </summary>
public sealed class StaffInvitationService(
    IAdministrationStore store, IPasswordHashService passwords, IPasswordResetRepository resets, IPasswordResetTokenProvider tokens,
    ISystemEmailSender email, TimeProvider time)
{
    /// <summary>Captain-local invitation lifetime (proposed; recovery tokens keep their 15-minute lifetime).</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(24);

    public async Task<AdminResult<StaffInvitationResult>> InviteAsync(Guid admin, string email, string? fullName,
        IReadOnlyCollection<string> modules, CancellationToken token)
    {
        var unusable = passwords.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var created = await store.CreateStaffAsync(admin, email, fullName, modules, unusable, token).ConfigureAwait(false);
        if (created.Status != AdminStatus.Success) return new(created.Status, null, created.Code);
        var (sent, expires) = await SendSetPasswordAsync(created.Value, email.Trim(), token).ConfigureAwait(false);
        return AdminResult<StaffInvitationResult>.Ok(new StaffInvitationResult(created.Value, sent, expires));
    }

    /// <summary>Issues a fresh invitation token (replacing any outstanding one) and emails it.</summary>
    public async Task<(bool Sent, DateTimeOffset ExpiresAtUtc)> SendSetPasswordAsync(Guid userId, string recipient, CancellationToken token)
    {
        var now = time.GetUtcNow();
        var expires = now + InvitationLifetime;
        var raw = tokens.Generate();
        if (!await resets.ReplaceTokenAsync(new PasswordResetTokenRecord(userId, tokens.Hash(raw), now, expires), token).ConfigureAwait(false))
            return (false, expires);
        var sent = await email.SendAsync(recipient, "BuildMind staff invitation",
            "You have been invited to BuildMind as a staff member.\n\n" +
            $"To activate your account, set a password using the reset-password form with this code:\n{raw}\n\n" +
            $"The code expires at {expires:u} and can be used once.\n", token).ConfigureAwait(false);
        return (sent, expires);
    }
}

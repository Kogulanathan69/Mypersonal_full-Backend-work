using BuildMind.Application.Features.Auth.Abstractions;

namespace BuildMind.API.Security;

/// <summary>
/// PROPOSAL, Development only: accepts a reset request without delivering anything. The token and e-mail address are
/// discarded and never logged, so no reset secret leaves the process. Replace with an approved sender before any
/// activation outside Development (STEP 05 decision 7).
/// </summary>
internal sealed class NonDeliveringPasswordResetEmailSender : IPasswordResetEmailSender
{
    public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}

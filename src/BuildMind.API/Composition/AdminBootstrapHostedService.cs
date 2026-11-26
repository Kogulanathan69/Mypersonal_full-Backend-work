using BuildMind.Application.Features.Administration;

namespace BuildMind.API.Composition;

/// <summary>
/// Operator-controlled admin bootstrap (no hardcoded credentials): when <c>CaptainIntegration:AdminBootstrap:Email</c>
/// names an existing, active, self-registered account AND no active admin exists, that account is granted ADMIN once
/// (audited as BOOTSTRAP_ADMIN, sessions revoked so the next login is fresh). Otherwise nothing changes. The operator
/// removes the setting after first use; with an admin present it is inert.
/// </summary>
public sealed class AdminBootstrapHostedService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<AdminBootstrapHostedService> logger) : IHostedService
{
    public const string EmailKey = "CaptainIntegration:AdminBootstrap:Email";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration[EmailKey];
        if (string.IsNullOrWhiteSpace(email)) return;
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAdministrationStore>()
            .BootstrapAdminAsync(email.Trim().ToUpperInvariant(), cancellationToken);
        // Outcome code only; the account identifier and email are not logged.
        logger.LogWarning("Admin bootstrap outcome: {Outcome}", result.Status == AdminStatus.Success ? "GRANTED" : result.Code);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using BuildMind.Application.Features.Administration;

namespace BuildMind.API.Composition;

/// <summary>Dispatches due outbox messages every few seconds (bounded batch; counts are logged, never content).</summary>
public sealed class OutboxHostedService(IServiceScopeFactory scopes, ILogger<OutboxHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var (sent, failed, dead) = await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchDueAsync(20, stoppingToken);
                if (sent + failed + dead > 0)
                    logger.LogInformation("Outbox dispatch: sent={Sent} retry={Failed} dead={Dead}", sent, failed, dead);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Outbox dispatch failed ({Type})", exception.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}

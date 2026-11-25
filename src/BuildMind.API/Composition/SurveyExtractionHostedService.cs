using BuildMind.Application.Features.Planning;
using BuildMind.Infrastructure.CaptainIntegration;

namespace BuildMind.API.Composition;

/// <summary>
/// Hosted polling loop for durable survey extraction (Captain profile only). Bounded concurrency; each survey is
/// processed in its own DI scope; claims are atomic and leased in the database, so a crash or shutdown mid-processing
/// is recovered by lease expiry (bounded attempts). No database transaction is held while Python/OCR runs.
/// </summary>
public sealed class SurveyExtractionHostedService(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<SurveyExtractionHostedService> logger, WorkerStatusRegistry status) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = SurveyWorkerOptions.From(configuration);
        using var gate = new SemaphoreSlim(options.Concurrency);
        var running = new List<Task>();
        var nextReconciliation = DateTimeOffset.MinValue;
        status.Registered("survey-extraction");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (DateTimeOffset.UtcNow >= nextReconciliation)
                {
                    nextReconciliation = DateTimeOffset.UtcNow.AddMinutes(10);
                    await using var reconcileScope = scopes.CreateAsyncScope();
                    var report = await reconcileScope.ServiceProvider.GetRequiredService<ISurveyExtractionWork>().ReconcileStorageAsync(stoppingToken);
                    logger.LogInformation("Survey storage reconciliation: {Report}", report);
                }
                IReadOnlyList<Guid> candidates;
                await using (var scope = scopes.CreateAsyncScope())
                    candidates = await scope.ServiceProvider.GetRequiredService<ISurveyExtractionWork>()
                        .FindClaimableAsync(options.Concurrency * 4, stoppingToken);
                foreach (var id in candidates)
                {
                    await gate.WaitAsync(stoppingToken);
                    running.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await using var scope = scopes.CreateAsyncScope();
                            var outcome = await scope.ServiceProvider.GetRequiredService<ISurveyExtractionWork>().ProcessAsync(id, stoppingToken);
                            if (outcome != "NOT_CLAIMED")
                                logger.LogInformation("Survey extraction {Outcome} (survey {SurveyId})", outcome, id);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                        catch (Exception exception)
                        {
                            logger.LogError("Survey extraction attempt failed unexpectedly ({Type})", exception.GetType().Name);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, stoppingToken));
                }
                running.RemoveAll(task => task.IsCompleted);
                status.Succeeded("survey-extraction");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Survey extraction poll failed ({Type})", exception.GetType().Name);
                status.Failed("survey-extraction", exception);
            }

            try { await Task.Delay(options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
        await Task.WhenAll(running.Where(task => !task.IsCompleted));
    }
}

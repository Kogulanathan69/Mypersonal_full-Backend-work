using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BuildMind.API.Health;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// STEP 07-B audit: MySQL readiness timeouts, cancellation, single-flight, exception hygiene and the
/// "healthy database + degraded optional services → 200" path. No database is used.
/// </summary>
public sealed class ReadinessCheckBehaviourTests
{
    private static readonly IOptions<DatabaseOptions> Enabled = Options.Create(new DatabaseOptions { Enabled = true });

    private static MySqlReadinessCheck Check(
        Func<IServiceProvider, CancellationToken, Task<bool>> probe,
        ReadinessProbeGate? gate = null,
        TimeSpan? timeout = null) =>
        new(Enabled, new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            gate ?? new ReadinessProbeGate(), probe, timeout ?? TimeSpan.FromSeconds(2));

    private static Task<HealthCheckResult> Run(MySqlReadinessCheck check, CancellationToken token = default) =>
        check.CheckHealthAsync(new HealthCheckContext(), token);

    private static string Reason(HealthCheckResult result) => (string)result.Data["reason"];

    [Fact]
    public async Task Reachable_database_is_healthy()
    {
        var result = await Run(Check((_, _) => Task.FromResult(true)));

        Assert.Equal((HealthStatus.Healthy, "reachable"), (result.Status, Reason(result)));
    }

    [Fact]
    public async Task Probe_exceptions_are_unhealthy_without_any_exception_detail()
    {
        var result = await Run(Check((_, _) => Task.FromException<bool>(
            new InvalidOperationException("Server=db.internal;User=bm_runtime;Password=SYNTHETIC-SECRET-7f3a9c41"))));

        Assert.Equal((HealthStatus.Unhealthy, "unreachable"), (result.Status, Reason(result)));
        Assert.Null(result.Exception);
        Assert.Equal("unreachable", result.Description);
    }

    [Fact]
    public async Task A_hanging_probe_that_ignores_cancellation_still_times_out_on_schedule()
    {
        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = Stopwatch.StartNew();

        var result = await Run(Check((_, _) => never.Task, timeout: TimeSpan.FromMilliseconds(200)));

        Assert.Equal((HealthStatus.Unhealthy, "timeout"), (result.Status, Reason(result)));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Took {watch.Elapsed}");
        never.SetResult(false);
    }

    [Fact]
    public async Task Abandoned_probe_blocks_new_probes_until_it_finishes_then_releases_the_gate()
    {
        var gate = new ReadinessProbeGate();
        var slow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task<bool> Probe(IServiceProvider _, CancellationToken __) => Interlocked.Increment(ref calls) == 1 ? slow.Task : Task.FromResult(true);

        var first = await Run(Check(Probe, gate, TimeSpan.FromMilliseconds(100)));
        var whileStillRunning = await Run(Check(Probe, gate, TimeSpan.FromMilliseconds(100)));
        var probesStartedWhileBlocked = Volatile.Read(ref calls);
        Assert.Equal(0, gate.Semaphore.CurrentCount); // the abandoned probe still holds the gate
        slow.SetResult(false);
        await WaitUntilAsync(() => gate.Semaphore.CurrentCount == 1);
        var afterwards = await Run(Check(Probe, gate));

        Assert.Equal("timeout", Reason(first));
        Assert.Equal((HealthStatus.Unhealthy, "check_in_progress"), (whileStillRunning.Status, Reason(whileStillRunning)));
        Assert.Equal(1, probesStartedWhileBlocked); // the second run never started a probe
        Assert.Equal((HealthStatus.Healthy, "reachable"), (afterwards.Status, Reason(afterwards)));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        using var cancelled = new CancellationTokenSource();
        var never = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Run(Check((_, _) => never.Task, timeout: TimeSpan.FromSeconds(30)), cancelled.Token);

        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        never.SetResult(false);
    }

    [Fact]
    public async Task Probe_scope_is_disposed_after_the_probe_finishes()
    {
        var services = new ServiceCollection();
        services.AddScoped<DisposalTracker>();
        using var provider = services.BuildServiceProvider();
        DisposalTracker? tracker = null;
        var check = new MySqlReadinessCheck(Enabled, provider.GetRequiredService<IServiceScopeFactory>(), new ReadinessProbeGate(),
            (sp, _) => { tracker = sp.GetRequiredService<DisposalTracker>(); return Task.FromResult(true); }, TimeSpan.FromSeconds(2));

        await Run(check);
        await WaitUntilAsync(() => tracker?.Disposed == true);

        Assert.True(tracker!.Disposed);
    }

    [Fact]
    public async Task Healthy_database_with_degraded_optional_services_is_ready_with_200()
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                var mysql = options.Registrations.Single(r => r.Name == "mysql");
                options.Registrations.Remove(mysql);
                options.Registrations.Add(new HealthCheckRegistration(
                    "mysql", new StubHealthy(), HealthStatus.Unhealthy, mysql.Tags, mysql.Timeout));
            }))).CreateClient();

        using var response = await client.GetAsync(Readiness.Path);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", body.RootElement.GetProperty("status").GetString());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }
    }

    private sealed class StubHealthy : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("reachable", new Dictionary<string, object> { ["reason"] = "reachable" }));
    }

    private sealed class DisposalTracker : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}

using System.Text.Json;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BuildMind.API.Health;

/// <summary>
/// Readiness (BE-M4-D04, design C11b approved 2026-11-18). Liveness (<c>/health</c>) stays dependency-free.
/// MySQL is mandatory: not configured, unreachable or too slow → Unhealthy (HTTP 503). Python and RAG are
/// instance-optional: never reported Healthy until a real client and check exist (BE-M4-D05), only Degraded
/// (HTTP 200). Results carry names, statuses and fixed reason codes only, never exception text, hosts or
/// connection details. Readiness never writes to or migrates the database.
/// </summary>
public static class Readiness
{
    public const string Path = "/health/ready";
    public const string Tag = "ready";

    /// <summary>Framework-level timeout per check; the MySQL check enforces its own shorter deadline.</summary>
    public static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(5);

    public static class Reasons
    {
        public const string Reachable = "reachable";
        public const string NotConfigured = "not_configured";
        public const string Unreachable = "unreachable";
        public const string Timeout = "timeout";
        public const string Busy = "check_in_progress";
        public const string Unverified = "unverified";
        public const string Unavailable = "unavailable";
    }

    /// <summary>Explicit mapping (C11b): Healthy/Degraded → 200, Unhealthy → 503.</summary>
    public static readonly IReadOnlyDictionary<HealthStatus, int> StatusCodes = new Dictionary<HealthStatus, int>
    {
        [HealthStatus.Healthy] = Microsoft.AspNetCore.Http.StatusCodes.Status200OK,
        [HealthStatus.Degraded] = Microsoft.AspNetCore.Http.StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
    };

    internal static HealthCheckResult Result(HealthStatus status, string reason) =>
        new(status, reason, exception: null, data: new Dictionary<string, object> { ["reason"] = reason });

    /// <summary>Writes <c>{"status":"…","checks":[{"name":"…","status":"…","reason":"…"}]}</c>.</summary>
    public static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    // A check that timed out at framework level or threw carries no reason of ours; its exception is never surfaced.
                    reason = entry.Value.Data.TryGetValue("reason", out var reason) && reason is string text
                        ? text
                        : Reasons.Unavailable,
                }),
        };

        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            context.RequestAborted);
    }
}

/// <summary>
/// Allows at most one MySQL readiness probe at a time per host, so slow or hanging connection attempts can never
/// pile up when readiness is polled faster than the database answers. Singleton.
/// </summary>
public sealed class ReadinessProbeGate
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

    /// <summary>The MySQL probe's gate.</summary>
    internal SemaphoreSlim Semaphore => For("mysql");

    /// <summary>One single-flight gate per check name (STEP 08-B: also used by the Python check).</summary>
    internal SemaphoreSlim For(string check) => gates.GetOrAdd(check, _ => new SemaphoreSlim(1, 1));
}

/// <summary>
/// Mandatory MySQL readiness: opens and closes one connection through the configured DbContext (read-only; no
/// queries against feature tables, no writes, no migrations).
/// </summary>
/// <remarks>
/// The check enforces its own deadline with <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/>, so it
/// returns on time even if the driver ignores cancellation. An abandoned probe keeps the single-flight gate and its
/// DI scope until it really finishes; only then is the scope (DbContext, connection) disposed and the gate released.
/// </remarks>
public sealed class MySqlReadinessCheck : IHealthCheck
{
    internal static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly IOptions<DatabaseOptions> options;
    private readonly IServiceScopeFactory scopes;
    private readonly ReadinessProbeGate gate;
    private readonly Func<IServiceProvider, CancellationToken, Task<bool>> probe;
    private readonly TimeSpan probeTimeout;

    public MySqlReadinessCheck(IOptions<DatabaseOptions> options, IServiceScopeFactory scopes, ReadinessProbeGate gate)
        : this(options, scopes, gate, CanConnectAsync, DefaultProbeTimeout)
    {
    }

    internal MySqlReadinessCheck(
        IOptions<DatabaseOptions> options,
        IServiceScopeFactory scopes,
        ReadinessProbeGate gate,
        Func<IServiceProvider, CancellationToken, Task<bool>> probe,
        TimeSpan probeTimeout)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
        this.probeTimeout = probeTimeout;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return Readiness.Result(HealthStatus.Unhealthy, Readiness.Reasons.NotConfigured);
        }

        if (!await gate.Semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return Readiness.Result(HealthStatus.Unhealthy, Readiness.Reasons.Busy);
        }

        var scope = scopes.CreateAsyncScope();
        // Not `using`: if the probe is abandoned, the source must outlive this method (see ProbeDeadline.Settle).
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(probeTimeout);

        Task<bool> probing;
        try
        {
            probing = probe(scope.ServiceProvider, deadline.Token);
        }
        catch (Exception)
        {
            deadline.Dispose();
            await ReleaseAsync(scope, gate.Semaphore).ConfigureAwait(false);
            return Readiness.Result(HealthStatus.Unhealthy, Readiness.Reasons.Unreachable);
        }

        // Dispose the scope and release the gate only when the probe has really finished (even if abandoned).
        var cleanup = probing.ContinueWith(
            async finished =>
            {
                _ = finished.Exception; // observe; the message is never logged or returned
                await ReleaseAsync(scope, gate.Semaphore).ConfigureAwait(false);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();

        // A completed readiness result must not race its own scope cleanup.
        // Keep a single bounded wait for both the probe and cleanup.
        var completion = Task.WhenAll(probing, cleanup);
        _ = completion.ContinueWith(
            static finished => { _ = finished.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            await completion.WaitAsync(probeTimeout, cancellationToken).ConfigureAwait(false);
            var reachable = await probing.ConfigureAwait(false);
            return Readiness.Result(
                reachable ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                reachable ? Readiness.Reasons.Reachable : Readiness.Reasons.Unreachable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // caller (request abort or framework timeout) cancelled the whole readiness run
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return Readiness.Result(HealthStatus.Unhealthy, Readiness.Reasons.Timeout);
        }
        catch (Exception)
        {
            // The exception (host, user, provider text) is deliberately dropped.
            return Readiness.Result(HealthStatus.Unhealthy, Readiness.Reasons.Unreachable);
        }
        finally
        {
            ProbeDeadline.Settle(probing, deadline);
        }
    }

    /// <summary>
    /// STEP 08-C: the gate is ALWAYS released, even if disposing the probe scope throws (a failing Dispose of any
    /// scoped service previously left the single-flight gate held forever, so every later probe reported
    /// "check_in_progress"). A disposal failure is swallowed: readiness has nothing safe to report about it.
    /// </summary>
    private static async Task ReleaseAsync(AsyncServiceScope scope, SemaphoreSlim semaphore)
    {
        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Intentionally ignored; the gate release below must still happen.
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static Task<bool> CanConnectAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<BuildMindDbContext>().Database.CanConnectAsync(cancellationToken);
}

/// <summary>
/// Python readiness (BE-M4-D05) against the CONFIRMED contract <c>GET /health</c> →
/// <c>{"service":"buildmind-python","status":"ok"}</c>. Python is instance-optional (C11b): success → Healthy
/// (<c>reachable</c>); any failure → Degraded (<c>not_configured</c>, <c>timeout</c> or <c>unreachable</c>), never 503.
/// Bounded by its own deadline. Feature calls that need Python still fail with their own 503/504 Problem Details.
/// </summary>
public sealed class PythonReadinessCheck : IHealthCheck
{
    internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(3);

    private readonly BoundedServiceProbe probe;

    public PythonReadinessCheck(BuildMind.Infrastructure.Integration.PythonServiceClient client, ReadinessProbeGate gate)
        : this((client ?? throw new ArgumentNullException(nameof(client))).CheckHealthAsync, client.IsEnabled, gate, DefaultDeadline)
    {
    }

    internal PythonReadinessCheck(Func<CancellationToken, Task> probe, bool enabled, ReadinessProbeGate gate, TimeSpan deadline) =>
        this.probe = new BoundedServiceProbe("python", probe, enabled, gate, deadline);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        probe.RunAsync(cancellationToken);
}

/// <summary>
/// RAG readiness (M7, evidence contract v1) against the rag-service <c>GET /health</c>. RAG is instance-optional
/// (C11b): it is never Unhealthy (never makes <c>/health/ready</c> 503).
/// <list type="bullet">
/// <item>Healthy (<c>reachable</c>) only when the service identity is <c>buildmind-rag</c>, the contract version is
/// <c>1</c> AND the regulation corpus is loaded.</item>
/// <item>Degraded <c>unavailable</c>: the service is up but has no verified corpus (search would return 503).</item>
/// <item>Degraded <c>unverified</c>: wrong identity or contract version.</item>
/// <item>Degraded <c>not_configured</c>, <c>timeout</c>, <c>unreachable</c> or <c>check_in_progress</c> as for
/// Python.</item>
/// </list>
/// Bounded deadline (3 s, enforced even if the transport ignores cancellation) and its own single-flight gate.
/// No URL, exception text or response body is ever reported.
/// </summary>
public sealed class RagReadinessCheck : IHealthCheck
{
    private readonly BoundedServiceProbe probe;

    public RagReadinessCheck(BuildMind.Infrastructure.Integration.RagServiceClient client, ReadinessProbeGate gate)
        : this(client, gate, PythonReadinessCheck.DefaultDeadline)
    {
    }

    internal RagReadinessCheck(BuildMind.Infrastructure.Integration.RagServiceClient client, ReadinessProbeGate gate, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(client);
        probe = new BoundedServiceProbe("rag", token => ProbeAsync(client, token), client.IsEnabled, gate, deadline);
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        probe.RunAsync(cancellationToken);

    private static async Task ProbeAsync(BuildMind.Infrastructure.Integration.RagServiceClient client, CancellationToken cancellationToken)
    {
        BuildMind.Infrastructure.Integration.RagServiceClient.RagHealthResponse health;
        try
        {
            health = await client.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BuildMind.Application.Abstractions.Errors.DependencyFailureException failure)
            when (failure.Kind == BuildMind.Application.Abstractions.Errors.DependencyFailureKind.MalformedResponse)
        {
            throw new ReadinessDegradedException(Readiness.Reasons.Unverified);
        }

        if (!health.CorpusLoaded)
        {
            throw new ReadinessDegradedException(Readiness.Reasons.Unavailable);
        }
    }
}

/// <summary>A probe outcome that is a specific Degraded reason rather than a transport failure.</summary>
internal sealed class ReadinessDegradedException(string reason) : Exception
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Bounded, single-flight readiness probe for one private internal service (shared by Python and RAG). Same
/// protections as the MySQL check (STEP 08-B / 10-A / 10-B): a hard deadline via
/// <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> that holds even if a transport ignores cancellation, a
/// per-service single-flight gate so polling can never accumulate outstanding probes, and abandoned probes cancelled
/// through <see cref="ProbeDeadline"/>. Never returns Unhealthy.
/// </summary>
internal sealed class BoundedServiceProbe(
    string gateKey, Func<CancellationToken, Task> probe, bool enabled, ReadinessProbeGate gate, TimeSpan deadline)
{
    private readonly Func<CancellationToken, Task> probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly ReadinessProbeGate gate = gate ?? throw new ArgumentNullException(nameof(gate));

    internal async Task<HealthCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return Readiness.Result(HealthStatus.Degraded, Readiness.Reasons.NotConfigured);
        }

        var semaphore = gate.For(gateKey);
        if (!await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return Readiness.Result(HealthStatus.Degraded, Readiness.Reasons.Busy);
        }

        // Not `using`: if the probe is abandoned, the source must outlive this method (see ProbeDeadline.Settle).
        var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(deadline);

        Task probing;
        try
        {
            probing = probe(bounded.Token);
        }
        catch (Exception)
        {
            bounded.Dispose();
            semaphore.Release();
            return Readiness.Result(HealthStatus.Degraded, Readiness.Reasons.Unreachable);
        }

        _ = probing.ContinueWith(
            finished =>
            {
                _ = finished.Exception; // observe; never logged or returned
                semaphore.Release();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            await probing.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
            return Readiness.Result(HealthStatus.Healthy, Readiness.Reasons.Reachable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            return Readiness.Result(HealthStatus.Degraded, Readiness.Reasons.Timeout);
        }
        catch (ReadinessDegradedException degraded)
        {
            return Readiness.Result(HealthStatus.Degraded, degraded.Reason);
        }
        catch (BuildMind.Application.Abstractions.Errors.DependencyFailureException failure)
        {
            return Readiness.Result(HealthStatus.Degraded, failure.Kind switch
            {
                BuildMind.Application.Abstractions.Errors.DependencyFailureKind.Disabled => Readiness.Reasons.NotConfigured,
                BuildMind.Application.Abstractions.Errors.DependencyFailureKind.Timeout => Readiness.Reasons.Timeout,
                _ => Readiness.Reasons.Unreachable,
            });
        }
        catch (Exception)
        {
            return Readiness.Result(HealthStatus.Degraded, Readiness.Reasons.Unreachable);
        }
        finally
        {
            ProbeDeadline.Settle(probing, bounded);
        }
    }
}

/// <summary>
/// Owns the probe's deadline source once a readiness check has returned (STEP 10-A/10-B).
/// </summary>
/// <remarks>
/// <para>A probe still running when the check returns (timeout or caller cancellation) is ABANDONED. Its token must be
/// cancelled, otherwise a probe that relies on the token runs until the provider's own timeout and keeps the
/// single-flight gate (<c>check_in_progress</c>; 503 for MySQL). <c>WaitAsync(timeout)</c> and
/// <c>CancelAfter(timeout)</c> race, so the timer alone is not enough.</para>
/// <para>Cancellation is requested with <see cref="CancellationTokenSource.CancelAsync"/>, so registered callbacks do
/// NOT run on the readiness thread: a slow or blocking callback (a provider may open a second connection to kill a
/// query) cannot delay the readiness result past its deadline. Callback failures are observed and dropped (never
/// logged or returned). The source is disposed only after BOTH the probe and its callbacks have finished, never
/// while they can still use it. The gate itself is released by the probe's own continuation when the probe really
/// finishes, so a non-cooperative probe can hold at most one gate slot and probes never accumulate.</para>
/// </remarks>
internal static class ProbeDeadline
{
    internal static void Settle(Task probing, CancellationTokenSource source)
    {
        ArgumentNullException.ThrowIfNull(probing);
        ArgumentNullException.ThrowIfNull(source);

        if (probing.IsCompleted)
        {
            source.Dispose();
            return;
        }

        Task cancelling;
        try
        {
            cancelling = source.CancelAsync();
        }
        catch (Exception)
        {
            // Not expected (this method is the only owner that disposes the source); never fatal for readiness.
            cancelling = Task.CompletedTask;
        }

        _ = Task.WhenAll(probing, cancelling).ContinueWith(
            static (finished, state) =>
            {
                _ = finished.Exception; // observe probe and callback failures; never logged or returned
                ((CancellationTokenSource)state!).Dispose();
            },
            source,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

using System.Net;
using System.Text;
using BuildMind.API;
using BuildMind.API.Health;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Integration;

/// <summary>STEP 08-B security/reliability audit regressions for the internal service integration. No network.</summary>
public sealed class InternalServiceSecurityAuditTests
{
    private const string Base = "http://python-services.internal:8000/";

    private static InternalServiceClient Client(StubHttpHandler handler, string baseUrl = Base, int maxBytes = 1024) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new InternalServiceOptions { Enabled = true, BaseUrl = baseUrl, TimeoutSeconds = 5 }, "python", maxBytes);

    private sealed record Health(string? Service, string? Status);

    // ---- Transport configuration ----

    [Fact]
    public void Transport_never_follows_redirects_uses_proxies_or_cookies_and_bounds_connections()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(ApiServiceCollectionExtensions.CreateInternalServiceHandler());

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.False(handler.UseCookies);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(ApiServiceCollectionExtensions.MaxInternalConnectionsPerServer, handler.MaxConnectionsPerServer);
        Assert.True(handler.MaxConnectionsPerServer is > 0 and <= 64);
        Assert.Equal(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
    }

    // ---- Base URL defence in depth ----

    [Theory]
    [InlineData("http://user:secret@python-services.internal:8000/")]
    [InlineData("ftp://python-services.internal/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://python-services.internal/?mode=debug")]
    [InlineData("http://python-services.internal/#f")]
    [InlineData("not a url")]
    public async Task Unsafe_base_urls_leave_the_client_disabled(string baseUrl)
    {
        var handler = StubHttpHandler.Json("{}");
        var client = Client(handler, baseUrl);

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() => client.GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.False(client.IsEnabled);
        Assert.Equal(DependencyFailureKind.Disabled, failure.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Resolved_destination_keeps_scheme_host_port_and_base_path()
    {
        var target = Client(StubHttpHandler.Json("{}"), "https://python-services.internal:8443/svc").Resolve("v1/health");

        Assert.Equal(new Uri("https://python-services.internal:8443/svc/v1/health"), target);
    }

    // ---- JSON contract reliability (confirmed /health contract only) ----

    [Theory]
    [InlineData("")]                                                    // empty body
    [InlineData("{\"service\":\"buildmind-python\",\"status\":")]       // truncated
    [InlineData("null")]                                                // JSON null
    [InlineData("{\"service\":null,\"status\":\"ok\"}")]                // null identity
    [InlineData("{\"service\":\"buildmind-python\",\"status\":\"degraded\"}")] // unexpected status
    [InlineData("{\"service\":\"buildmind-rag\",\"status\":\"ok\"}")]   // wrong service identity
    [InlineData("[]")]                                                  // wrong JSON shape
    public async Task Python_health_contract_violations_are_malformed(string body)
    {
        var python = new PythonServiceClient(
            new HttpClient(StubHttpHandler.Json(body)) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new ServicesOptions { Python = new InternalServiceOptions { Enabled = true, BaseUrl = Base, TimeoutSeconds = 5 } }),
            new InternalServiceBulkhead());

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() => python.CheckHealthAsync(CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task Excessive_json_nesting_is_rejected()
    {
        var deep = new string('[', 200) + new string(']', 200);

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(deep), maxBytes: 4096).GetJsonAsync<object>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Theory]
    [InlineData("application/problem+json")]
    [InlineData("application/json; charset=utf-8")]
    public async Task Json_media_types_are_accepted(string mediaType)
    {
        var handler = new StubHttpHandler((_, _) =>
        {
            var content = new StringContent("{\"service\":\"s\",\"status\":\"ok\"}", Encoding.UTF8);
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        Assert.Equal("ok", (await Client(handler).GetJsonAsync<Health>("health", null, CancellationToken.None)).Status);
    }

    // ---- Resources and concurrency ----

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"service\":\"s\",\"status\":\"ok\"}")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task Responses_are_disposed_on_success_and_failure(HttpStatusCode status, string body)
    {
        var content = new TrackingContent(body);
        var handler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = content }));

        try
        {
            await Client(handler).GetJsonAsync<Health>("health", null, CancellationToken.None);
        }
        catch (DependencyFailureException)
        {
        }

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Concurrent_calls_are_isolated()
    {
        var handler = new StubHttpHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"service\":\"{request.RequestUri!.Segments[^1]}\",\"status\":\"ok\"}}", Encoding.UTF8, "application/json"),
        }));
        var client = Client(handler);

        var results = await Task.WhenAll(Enumerable.Range(0, 25).Select(i => client.GetJsonAsync<Health>($"item-{i}", null, CancellationToken.None)));

        Assert.Equal(Enumerable.Range(0, 25).Select(i => $"item-{i}"), results.Select(r => r.Service));
        Assert.Equal(25, handler.Calls);
    }

    // ---- Python readiness: same protections as MySQL ----

    [Fact]
    public async Task Python_readiness_returns_on_time_even_if_the_probe_ignores_cancellation()
    {
        var never = new TaskCompletionSource();
        var check = new PythonReadinessCheck(_ => never.Task, enabled: true, new ReadinessProbeGate(), TimeSpan.FromMilliseconds(150));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal((HealthStatus.Degraded, "timeout"), (result.Status, (string)result.Data["reason"]));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"Took {watch.Elapsed}");
        never.SetResult();
    }

    [Fact]
    public async Task Python_readiness_is_single_flight_until_an_abandoned_probe_finishes()
    {
        var gate = new ReadinessProbeGate();
        var slow = new TaskCompletionSource();
        var calls = 0;
        Task Probe(CancellationToken _) => Interlocked.Increment(ref calls) == 1 ? slow.Task : Task.CompletedTask;

        var first = await new PythonReadinessCheck(Probe, true, gate, TimeSpan.FromMilliseconds(100)).CheckHealthAsync(new HealthCheckContext());
        var blocked = await new PythonReadinessCheck(Probe, true, gate, TimeSpan.FromMilliseconds(100)).CheckHealthAsync(new HealthCheckContext());
        var startedWhileBlocked = Volatile.Read(ref calls);
        slow.SetResult();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (gate.For("python").CurrentCount == 0 && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }

        var after = await new PythonReadinessCheck(Probe, true, gate, TimeSpan.FromSeconds(1)).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal("timeout", (string)first.Data["reason"]);
        Assert.Equal((HealthStatus.Degraded, "check_in_progress"), (blocked.Status, (string)blocked.Data["reason"]));
        Assert.Equal(1, startedWhileBlocked);
        Assert.Equal((HealthStatus.Healthy, "reachable"), (after.Status, (string)after.Data["reason"]));
        Assert.Equal(1, gate.Semaphore.CurrentCount); // the MySQL gate is independent
    }

    // ---- STEP 08-C: bounded queue and gate release ----

    [Fact]
    public async Task Saturated_dependency_fails_fast_without_network_and_recovers_when_slots_free()
    {
        var bulkhead = new InternalServiceBulkhead(maxConcurrentCallsPerDependency: 2);
        var release = new TaskCompletionSource();
        var handler = new StubHttpHandler(async (_, token) =>
        {
            await release.Task.WaitAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"service\":\"s\",\"status\":\"ok\"}", Encoding.UTF8, "application/json"),
            };
        });
        var client = new InternalServiceClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new InternalServiceOptions { Enabled = true, BaseUrl = Base, TimeoutSeconds = 5 }, "python", 1024, bulkhead);

        var first = client.GetJsonAsync<Health>("health", null, CancellationToken.None);
        var second = client.GetJsonAsync<Health>("health", null, CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<DependencyFailureException>(() => client.GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Unavailable, rejected.Kind);
        Assert.Null(rejected.StatusCode);
        Assert.Equal(0, bulkhead.Available("python"));

        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, bulkhead.Available("python"));
        Assert.Equal("ok", (await client.GetJsonAsync<Health>("health", null, CancellationToken.None)).Status);
        Assert.Equal(3, handler.Calls); // the rejected call never reached the transport
    }

    [Fact]
    public async Task Bulkhead_slots_are_released_after_timeout_and_cancellation()
    {
        var bulkhead = new InternalServiceBulkhead(maxConcurrentCallsPerDependency: 1);
        var client = new InternalServiceClient(new HttpClient(StubHttpHandler.Hangs()) { Timeout = Timeout.InfiniteTimeSpan },
            new InternalServiceOptions { Enabled = true, BaseUrl = Base, TimeoutSeconds = 1 }, "python", 1024, bulkhead);

        await Assert.ThrowsAsync<DependencyFailureException>(() => client.GetJsonAsync<Health>("health", null, CancellationToken.None));
        Assert.Equal(1, bulkhead.Available("python"));

        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetJsonAsync<Health>("health", null, cancelled.Token));
        Assert.Equal(1, bulkhead.Available("python"));
    }

    [Fact]
    public async Task Abandoned_mysql_probe_is_cancelled_so_the_gate_is_released_promptly()
    {
        // STEP 10-A: a probe that ends ONLY when its token is cancelled (no provider timeout of its own).
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var gate = new ReadinessProbeGate();
        var probeCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new MySqlReadinessCheck(
            Options.Create(new DatabaseOptions { Enabled = true }),
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider),
            gate,
            async (_, token) =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return true;
                }
                finally
                {
                    probeCancelled.TrySetResult();
                }
            },
            TimeSpan.FromMilliseconds(100));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal("timeout", (string)result.Data["reason"]); // never "check_in_progress": the previous probe was cancelled
            await probeCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForGateAsync(gate.Semaphore);
            probeCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    [Fact]
    public async Task Abandoned_python_probe_is_cancelled_so_the_gate_is_released_promptly()
    {
        var gate = new ReadinessProbeGate();
        var check = new PythonReadinessCheck(token => Task.Delay(Timeout.Infinite, token), true, gate, TimeSpan.FromMilliseconds(100));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await check.CheckHealthAsync(new HealthCheckContext());
            Assert.Equal("timeout", (string)result.Data["reason"]);
            await WaitForGateAsync(gate.For("python"));
        }
    }

    [Fact]
    public async Task Settle_cancels_an_abandoned_probe_without_running_a_blocking_callback_inline()
    {
        // STEP 10-B, deterministic: no timer race. A callback that blocks must not delay the readiness result.
        var source = new CancellationTokenSource();
        using var unblock = new ManualResetEventSlim(false);
        source.Token.Register(() => unblock.Wait(TimeSpan.FromSeconds(10)));
        var probe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        ProbeDeadline.Settle(probe.Task, source);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Settle blocked for {watch.ElapsedMilliseconds} ms");
        Assert.True(source.IsCancellationRequested);
        Assert.False(IsDisposed(source)); // probe and callback still running: the source must stay usable

        unblock.Set();
        await Task.Delay(50);
        Assert.False(IsDisposed(source)); // callback done, probe still running
        probe.SetResult();
        await WaitUntilAsync(() => IsDisposed(source));
    }

    [Fact]
    public async Task Settle_drops_callback_failures_and_still_disposes_after_the_probe_finishes()
    {
        var source = new CancellationTokenSource();
        source.Token.Register(() => throw new InvalidOperationException("SYNTHETIC-SECRET-callback"));
        var probe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var settle = Record.Exception(() => ProbeDeadline.Settle(probe.Task, source));
        probe.SetException(new InvalidOperationException("SYNTHETIC-SECRET-probe"));

        Assert.Null(settle);
        Assert.True(source.IsCancellationRequested);
        await WaitUntilAsync(() => IsDisposed(source));
    }

    [Fact]
    public void Settle_disposes_immediately_when_the_probe_already_finished()
    {
        var source = new CancellationTokenSource();

        ProbeDeadline.Settle(Task.CompletedTask, source);

        Assert.False(source.IsCancellationRequested); // a finished probe is never cancelled
        Assert.True(IsDisposed(source));
    }

    private static bool IsDisposed(CancellationTokenSource source)
    {
        try
        {
            _ = source.Token;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static async Task WaitForGateAsync(SemaphoreSlim semaphore)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (semaphore.CurrentCount == 0 && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, semaphore.CurrentCount);
    }

    [Fact]
    public async Task Mysql_readiness_gate_is_released_even_when_scope_disposal_throws()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<ThrowingOnDispose>(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var gate = new ReadinessProbeGate();
        var check = new MySqlReadinessCheck(
            Options.Create(new DatabaseOptions { Enabled = true }),
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(provider),
            gate,
            (sp, token) =>
            {
                Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ThrowingOnDispose>(sp);
                return Task.FromResult(!token.IsCancellationRequested);
            },
            TimeSpan.FromSeconds(2));

        var first = await check.CheckHealthAsync(new HealthCheckContext());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (gate.Semaphore.CurrentCount == 0 && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }

        var second = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, first.Status);
        Assert.Equal(1, gate.Semaphore.CurrentCount);
        Assert.Equal("reachable", (string)second.Data["reason"]); // not "check_in_progress": the gate was released
    }

    private sealed class ThrowingOnDispose : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("synthetic dispose failure");
    }

    private sealed class TrackingContent(string body) : StringContent(body, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BuildMind.API;
using BuildMind.API.Health;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// BE-M4-D05 LIVE C#-to-Python smoke tests (lead-approved 2026-11-18, STEP 08-C). The CI job starts the real
/// FastAPI app (<c>python-services/app/main.py</c>) with uvicorn on 127.0.0.1 and sets
/// <see cref="PythonUrlVariable"/>. Every call goes through the production <see cref="PythonServiceClient"/> and the
/// production transport handler over real loopback sockets. Failure modes (closed port, silent server) also use real
/// sockets, never mocked transports. The tests FAIL (never skip) when the service URL is missing.
/// </summary>
public sealed class PythonLiveSmokeTests(ITestOutputHelper output)
{
    public const string PythonUrlVariable = "BUILDMIND_TEST_PYTHON_URL";

    private static string PythonUrl()
    {
        var url = Environment.GetEnvironmentVariable(PythonUrlVariable);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException($"{PythonUrlVariable} is not set: the live Python service must be started by the CI job.");
        }

        var uri = new Uri(url);
        if (!IPAddress.IsLoopback(IPAddress.Parse(uri.Host)))
        {
            throw new InvalidOperationException("The live Python service must be bound to loopback only.");
        }

        return url;
    }

    private static PythonServiceClient Client(string baseUrl, int timeoutSeconds = 5, InternalServiceBulkhead? bulkhead = null) =>
        new(new HttpClient(ApiServiceCollectionExtensions.CreateInternalServiceHandler()) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new ServicesOptions
            {
                Python = new InternalServiceOptions { Enabled = true, BaseUrl = baseUrl, TimeoutSeconds = timeoutSeconds },
            }),
            bulkhead ?? new InternalServiceBulkhead());

    private sealed record Health(string? Service, string? Status);

    [Fact]
    public async Task PY01_real_python_health_answers_through_the_production_client()
    {
        var client = Client(PythonUrl());

        await client.CheckHealthAsync(CancellationToken.None); // validates service identity and status
        var health = await client.GetJsonAsync<Health>("health", null, CancellationToken.None);

        output.WriteLine($"Live Python /health: service={health.Service} status={health.Status}");
        Assert.Equal(new Health("buildmind-python", "ok"), health);
    }

    [Fact]
    public async Task PY02_live_response_validation_rejects_a_wrong_identity()
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(PythonUrl()).GetJsonAsync<Health>("health", h => h.Service == "not-python", CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task PY03_live_readiness_reports_python_healthy()
    {
        var check = new PythonReadinessCheck(Client(PythonUrl()), new ReadinessProbeGate());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("reachable", result.Data["reason"]);
    }

    [Fact]
    public async Task PY04_closed_port_is_unavailable()
    {
        var port = FreeLoopbackPort();

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client($"http://127.0.0.1:{port}").CheckHealthAsync(CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Unavailable, failure.Kind);
        Assert.Equal(SharedErrorCodes.DependencyUnavailable, failure.Code);
    }

    [Fact]
    public async Task PY05_silent_server_times_out_within_the_configured_bound()
    {
        using var silent = new SilentServer();
        var watch = Stopwatch.StartNew();

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(silent.BaseUrl, timeoutSeconds: 1).CheckHealthAsync(CancellationToken.None));

        output.WriteLine($"Timeout after {watch.ElapsedMilliseconds} ms");
        Assert.Equal(DependencyFailureKind.Timeout, failure.Kind);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PY06_caller_cancellation_is_not_reported_as_a_dependency_failure()
    {
        using var silent = new SilentServer();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(silent.BaseUrl, timeoutSeconds: 30).CheckHealthAsync(cancel.Token));
    }

    [Fact]
    public async Task PY07_wire_request_carries_traceparent_and_no_credentials()
    {
        using var recorder = new RecordingServer("{\"service\":\"buildmind-python\",\"status\":\"ok\"}");
        using var activity = new Activity("live-smoke").SetIdFormat(ActivityIdFormat.W3C).Start();

        await Client(recorder.BaseUrl).CheckHealthAsync(CancellationToken.None);
        var raw = await recorder.Request;

        output.WriteLine(raw.Split("\r\n")[0]);
        Assert.StartsWith("GET /health HTTP/1.1", raw, StringComparison.Ordinal);
        Assert.Contains($"traceparent: {activity.Id}", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization:", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie:", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proxy-", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Accepts connections on loopback and never answers.</summary>
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> accepted = [];

        public SilentServer()
        {
            listener.Start();
            _ = AcceptAsync();
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    lock (accepted)
                    {
                        accepted.Add(client);
                    }
                }
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                // listener stopped
            }
        }

        public void Dispose()
        {
            listener.Stop();
            lock (accepted)
            {
                accepted.ForEach(c => c.Dispose());
            }
        }
    }

    /// <summary>Loopback server that records the raw request head and answers with fixed JSON.</summary>
    private sealed class RecordingServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);

        public RecordingServer(string json)
        {
            listener.Start();
            Request = ServeOnceAsync(json);
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

        public Task<string> Request { get; }

        private async Task<string> ServeOnceAsync(string json)
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var head = new StringBuilder();
            while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                head.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var body = Encoding.UTF8.GetBytes(json);
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.WriteAsync(body);
            return head.ToString();
        }

        public void Dispose() => listener.Stop();
    }
}

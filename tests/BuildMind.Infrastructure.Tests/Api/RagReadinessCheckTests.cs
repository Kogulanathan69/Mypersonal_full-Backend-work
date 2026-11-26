using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BuildMind.API.Health;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// M7 RAG readiness: service identity, contract version, corpus availability, bounded timeout, single-flight and a
/// safe Degraded status (never Unhealthy, never any URL or exception text). Controlled transport, no network.
/// </summary>
public sealed class RagReadinessCheckTests
{
    private const string Healthy = "{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true}";
    private const string Secret = SyntheticTestEndpoints.SyntheticSecret;

    private static RagServiceClient Client(StubHttpHandler handler, bool enabled = true) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new ServicesOptions { Rag = new InternalServiceOptions { Enabled = enabled, BaseUrl = "http://rag.internal:8100/", TimeoutSeconds = 5 } }),
            new InternalServiceBulkhead());

    private static async Task<HealthCheckResult> Check(StubHttpHandler handler, bool enabled = true, ReadinessProbeGate? gate = null, TimeSpan? deadline = null) =>
        await new RagReadinessCheck(Client(handler, enabled), gate ?? new ReadinessProbeGate(), deadline ?? TimeSpan.FromSeconds(3))
            .CheckHealthAsync(new HealthCheckContext());

    private static (HealthStatus, string) Outcome(HealthCheckResult result) => (result.Status, (string)result.Data["reason"]);

    [Fact]
    public async Task Identity_contract_and_loaded_corpus_are_healthy()
    {
        var handler = StubHttpHandler.Json(Healthy);

        Assert.Equal((HealthStatus.Healthy, "reachable"), Outcome(await Check(handler)));
        Assert.Equal(new Uri("http://rag.internal:8100/health"), handler.Requests.Single().RequestUri);
    }

    [Fact]
    public async Task Service_without_a_verified_corpus_is_degraded_unavailable()
    {
        var result = await Check(StubHttpHandler.Json(Healthy.Replace("\"corpus_loaded\":true", "\"corpus_loaded\":false", StringComparison.Ordinal)));

        Assert.Equal((HealthStatus.Degraded, "unavailable"), Outcome(result));
    }

    [Theory]
    [InlineData("{\"service\":\"buildmind-python\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true}")] // wrong identity
    [InlineData("{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"2\",\"corpus_loaded\":true}")]     // unknown contract
    [InlineData("{\"service\":\"buildmind-rag\",\"status\":\"starting\",\"contract_version\":\"1\",\"corpus_loaded\":true}")] // not ok
    [InlineData("{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"corpus_loaded\":true}")]                                // version missing
    public async Task Wrong_identity_or_contract_is_degraded_unverified(string json)
    {
        Assert.Equal((HealthStatus.Degraded, "unverified"), Outcome(await Check(StubHttpHandler.Json(json))));
    }

    [Fact]
    public async Task Unreachable_service_is_degraded_without_leaking_details()
    {
        var result = await Check(StubHttpHandler.Throws(new HttpRequestException($"refused {Secret} http://rag.internal:8100")));

        Assert.Equal((HealthStatus.Degraded, "unreachable"), Outcome(result));
        Assert.Null(result.Exception);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result.Data) + result.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("rag.internal", JsonSerializer.Serialize(result.Data) + result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_reporting_503_is_degraded_unreachable()
    {
        Assert.Equal((HealthStatus.Degraded, "unreachable"), Outcome(await Check(StubHttpHandler.Status(HttpStatusCode.ServiceUnavailable))));
    }

    [Fact]
    public async Task Hanging_service_is_bounded_and_reported_as_timeout()
    {
        var watch = Stopwatch.StartNew();

        var result = await Check(StubHttpHandler.Hangs(), deadline: TimeSpan.FromMilliseconds(200));

        Assert.Equal((HealthStatus.Degraded, "timeout"), Outcome(result));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Disabled_rag_is_not_configured_and_sends_nothing()
    {
        var handler = StubHttpHandler.Json(Healthy);

        Assert.Equal((HealthStatus.Degraded, "not_configured"), Outcome(await Check(handler, enabled: false)));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Rag_has_its_own_single_flight_gate()
    {
        var gate = new ReadinessProbeGate();
        Assert.True(await gate.For("python").WaitAsync(0)); // a stuck Python probe must not block RAG readiness

        var rag = await Check(StubHttpHandler.Json(Healthy), gate: gate);
        Assert.True(await gate.For("rag").WaitAsync(0));     // an in-flight RAG probe makes the next check report busy
        var busy = await Check(StubHttpHandler.Json(Healthy), gate: gate);

        Assert.Equal((HealthStatus.Healthy, "reachable"), Outcome(rag));
        Assert.Equal((HealthStatus.Degraded, "check_in_progress"), Outcome(busy));
    }

    [Fact]
    public async Task Readiness_endpoint_reports_a_verified_rag_as_healthy()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?> { ["Services:Rag:Enabled"] = "true", ["Services:Rag:BaseUrl"] = "https://rag.internal:8100", ["Services:Rag:AuthToken"] = "synthetic-internal-token-0123456789abcdef" });
        await using var host = factory.WithRagTransport(StubHttpHandler.Json(Healthy));
        using var client = host.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rag = body.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "rag");

        Assert.Equal(("Healthy", "reachable"), (rag.GetProperty("status").GetString(), rag.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task A_corpus_less_rag_never_makes_readiness_unhealthy()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?> { ["Services:Rag:Enabled"] = "true", ["Services:Rag:BaseUrl"] = "https://rag.internal:8100", ["Services:Rag:AuthToken"] = "synthetic-internal-token-0123456789abcdef" });
        await using var host = factory.WithRagTransport(
            StubHttpHandler.Json(Healthy.Replace("\"corpus_loaded\":true", "\"corpus_loaded\":false", StringComparison.Ordinal)));
        using var client = host.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rag = body.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == "rag");

        Assert.Equal(("Degraded", "unavailable"), (rag.GetProperty("status").GetString(), rag.GetProperty("reason").GetString()));
    }
}

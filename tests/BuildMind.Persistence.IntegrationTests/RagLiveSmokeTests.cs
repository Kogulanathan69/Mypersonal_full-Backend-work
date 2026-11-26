using System.Net;
using BuildMind.API;
using BuildMind.API.Health;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// M7 LIVE C#-to-RAG smoke tests (lead-approved 2026-11-19). The CI job starts the REAL rag-service on 127.0.0.1
/// with a random per-run internal token (G11 MVP) and no regulation corpus (the corpus never enters CI). Every call
/// uses the production <see cref="RagServiceClient"/> and transport over real loopback sockets. The tests FAIL (never
/// skip) when the URL or token is missing.
/// </summary>
public sealed class RagLiveSmokeTests(ITestOutputHelper output)
{
    public const string RagUrlVariable = "BUILDMIND_TEST_RAG_URL";
    public const string RagTokenVariable = "BUILDMIND_TEST_RAG_TOKEN";

    private static string Require(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{variable} is not set: the live RAG service must be started by the CI job.");

    private static RagServiceClient Client(string? token) =>
        new(new HttpClient(ApiServiceCollectionExtensions.CreateInternalServiceHandler()) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new ServicesOptions
            {
                Rag = new InternalServiceOptions { Enabled = true, BaseUrl = Require(RagUrlVariable), TimeoutSeconds = 10, AuthToken = token },
            }),
            new InternalServiceBulkhead());

    private static RagServiceClient Authenticated() => Client(Require(RagTokenVariable));

    [Fact]
    public async Task RG01_live_health_reports_identity_and_contract_version()
    {
        var health = await Authenticated().CheckHealthAsync(CancellationToken.None);

        output.WriteLine($"Live RAG /health: service={health.Service} contract={health.ContractVersion} corpus_loaded={health.CorpusLoaded}");
        Assert.Equal(("buildmind-rag", "ok", "1"), (health.Service, health.Status, health.ContractVersion));
    }

    [Fact]
    public async Task RG02_authenticated_search_is_answered_truthfully()
    {
        var client = Authenticated();
        var health = await client.CheckHealthAsync(CancellationToken.None);

        if (health.CorpusLoaded)
        {
            // A machine with the approved corpus: real evidence with contract invariants (validated by the client).
            var evidence = await client.SearchAsync(new RegulationSearchQuery("plot coverage", MaxPassages: 3), CancellationToken.None);
            output.WriteLine($"Corpus loaded: status={evidence.Status} passages={evidence.Passages.Count}");
            return;
        }

        // CI: no corpus. Authentication PASSED (otherwise 401) and the service truthfully reports 503, mapped to
        // DEPENDENCY_UNAVAILABLE; it never fabricates evidence.
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            client.SearchAsync(new RegulationSearchQuery("plot coverage"), CancellationToken.None));
        Assert.Equal(DependencyFailureKind.Unavailable, failure.Kind);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, failure.StatusCode);
    }

    [Theory]
    [InlineData(null)]                                           // no token configured on the client
    [InlineData("wrong-internal-token-0123456789abcdef0123")]    // well-formed but wrong
    public async Task RG03_missing_or_wrong_token_is_rejected_by_the_live_service(string? token)
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(token).SearchAsync(new RegulationSearchQuery("plot coverage"), CancellationToken.None));

        Assert.Equal(DependencyFailureKind.UnexpectedResponse, failure.Kind);
        Assert.Equal((int)HttpStatusCode.Unauthorized, failure.StatusCode);
    }

    [Fact]
    public async Task RG04_live_readiness_reflects_corpus_availability_and_is_never_unhealthy()
    {
        var client = Authenticated();
        var corpusLoaded = (await client.CheckHealthAsync(CancellationToken.None)).CorpusLoaded;

        var result = await new RagReadinessCheck(client, new ReadinessProbeGate()).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(
            corpusLoaded ? (HealthStatus.Healthy, "reachable") : (HealthStatus.Degraded, "unavailable"),
            (result.Status, (string)result.Data["reason"]));
    }
}

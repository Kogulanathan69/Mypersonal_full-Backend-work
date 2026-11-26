using System.Net;
using System.Text.Json;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Integration;

/// <summary>
/// M7 evidence contract v1 on the C# side, over a controlled transport (no network). The response example is the SAME
/// file the Python service tests validate (rag-service/contract/search_response.v1.example.json).
/// </summary>
public sealed class RagServiceClientTests
{
    private const string Base = "http://rag.internal:8100/";
    private static readonly string Example = File.ReadAllText(
        Path.Combine(RepositoryPaths.Root, "rag-service", "contract", "search_response.v1.example.json"));

    private static RagServiceClient Client(StubHttpHandler handler, bool enabled = true) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new ServicesOptions { Rag = new InternalServiceOptions { Enabled = enabled, BaseUrl = Base, TimeoutSeconds = 5 } }),
            new InternalServiceBulkhead());

    private static string Response(string status, bool withPassage = false, string legal = "false", string answer = "false") =>
        "{\"status\":\"" + status + "\",\"passages\":" + (withPassage ? PassageArray() : "[]")
        + ",\"warnings\":[\"w\"],\"legal_status_verified\":" + legal + ",\"answer_generated\":" + answer + "}";

    private static string PassageArray(string page = "1", string title = "north_plan.pdf", string evidence = "EVIDENCE_CANDIDATE") =>
        "[{\"passage_id\":\"p1\",\"document_id\":\"" + new string('a', 64) + "\",\"source_title\":\"" + title
        + "\",\"jurisdiction\":\"North\",\"page\":" + page + ",\"start_char\":0,\"end_char\":10,\"excerpt\":\"rear space\","
        + "\"score\":1.5,\"evidence_status\":\"" + evidence + "\",\"warnings\":[]}]";

    [Fact]
    public async Task Shared_contract_example_maps_to_cited_evidence()
    {
        var evidence = await Client(StubHttpHandler.Json(Example)).SearchAsync(new RegulationSearchQuery("rear space"), CancellationToken.None);

        Assert.Equal(RegulationEvidenceStatus.EvidenceFound, evidence.Status);
        var passage = Assert.Single(evidence.Passages);
        Assert.Equal(("north_plan.pdf", 1, 49, 213), (passage.SourceTitle, passage.Page, passage.StartChar, passage.EndChar));
        Assert.Equal(PassageEvidenceStatus.EvidenceCandidate, passage.EvidenceStatus);
        Assert.Contains(evidence.Warnings, w => w.Contains("not verified", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Request_is_a_single_post_with_snake_case_contract_fields()
    {
        var handler = StubHttpHandler.Json(Response("NO_MATCHING_EVIDENCE"));

        await Client(handler).SearchAsync(
            new RegulationSearchQuery("  plot coverage ", "Colombo Municipal Council planning area", [new string('e', 64)], 3),
            CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri(Base + "internal/v1/regulations/search"), request.RequestUri);
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies)!); // captured in flight; content is disposed after the call
        Assert.Equal("plot coverage", body.RootElement.GetProperty("query").GetString());
        Assert.Equal("Colombo Municipal Council planning area", body.RootElement.GetProperty("jurisdiction").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("k").GetInt32());
        Assert.Equal(new string('e', 64), body.RootElement.GetProperty("document_ids")[0].GetString());
    }

    [Fact]
    public async Task Optional_fields_are_omitted_rather_than_sent_as_null()
    {
        var handler = StubHttpHandler.Json(Response("NO_MATCHING_EVIDENCE"));

        await Client(handler).SearchAsync(new RegulationSearchQuery("rear space"), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies)!);
        Assert.False(body.RootElement.TryGetProperty("jurisdiction", out _));
        Assert.False(body.RootElement.TryGetProperty("document_ids", out _));
    }

    [Theory]
    [InlineData("NO_MATCHING_EVIDENCE", RegulationEvidenceStatus.NoMatchingEvidence)]
    [InlineData("INSUFFICIENT_JURISDICTION_EVIDENCE", RegulationEvidenceStatus.InsufficientJurisdictionEvidence)]
    [InlineData("INSUFFICIENT_SOURCE_EVIDENCE", RegulationEvidenceStatus.InsufficientSourceEvidence)]
    public async Task Missing_evidence_statuses_are_preserved_with_no_passages(string wire, RegulationEvidenceStatus expected)
    {
        var evidence = await Client(StubHttpHandler.Json(Response(wire))).SearchAsync(new RegulationSearchQuery("q"), CancellationToken.None);

        Assert.Equal(expected, evidence.Status);
        Assert.Empty(evidence.Passages);
    }

    [Fact]
    public async Task Ambiguous_evidence_keeps_its_passages_flagged()
    {
        var json = Response("AMBIGUOUS_EVIDENCE").Replace("\"passages\":[]", "\"passages\":" + PassageArray(evidence: "AMBIGUOUS_EVIDENCE"), StringComparison.Ordinal);

        var evidence = await Client(StubHttpHandler.Json(json)).SearchAsync(new RegulationSearchQuery("q"), CancellationToken.None);

        Assert.Equal(RegulationEvidenceStatus.AmbiguousEvidence, evidence.Status);
        Assert.Equal(PassageEvidenceStatus.AmbiguousEvidence, Assert.Single(evidence.Passages).EvidenceStatus);
    }

    public static TheoryData<string> MalformedResponses => new()
    {
        Response("EVIDENCE_FOUND", withPassage: true, legal: "true"),      // claims verified legal status
        Response("EVIDENCE_FOUND", withPassage: true, answer: "true"),     // claims a generated answer
        Response("NO_MATCHING_EVIDENCE", withPassage: true),              // passages without evidence status
        Response("SOMETHING_ELSE"),                                       // unknown status
        Response("EVIDENCE_FOUND").Replace("\"passages\":[]", "\"passages\":" + PassageArray(page: "0"), StringComparison.Ordinal),
        Response("EVIDENCE_FOUND").Replace("\"passages\":[]", "\"passages\":" + PassageArray(title: "C:\\\\private\\\\x.pdf"), StringComparison.Ordinal),
        "{\"status\":\"NO_MATCHING_EVIDENCE\",\"passages\":[],\"warnings\":[]}", // flags missing
    };

    [Theory]
    [MemberData(nameof(MalformedResponses))]
    public async Task Responses_violating_the_contract_are_rejected_as_malformed(string json)
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json(json)).SearchAsync(new RegulationSearchQuery("q"), CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task Unavailable_corpus_is_a_truthful_dependency_failure()
    {
        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json("{\"code\":\"CORPUS_UNAVAILABLE\"}", HttpStatusCode.ServiceUnavailable))
                .SearchAsync(new RegulationSearchQuery("q"), CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Unavailable, failure.Kind);
    }

    [Fact]
    public async Task Disabled_service_fails_without_any_request()
    {
        var handler = StubHttpHandler.Json(Example);

        var failure = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(handler, enabled: false).SearchAsync(new RegulationSearchQuery("q"), CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Disabled, failure.Kind);
        Assert.Equal(0, handler.Calls);
    }

    public static TheoryData<RegulationSearchQuery> InvalidQueries => new()
    {
        new RegulationSearchQuery(" "),
        new RegulationSearchQuery(new string('x', 501)),
        new RegulationSearchQuery("q", MaxPassages: 0),
        new RegulationSearchQuery("q", MaxPassages: 11),
        new RegulationSearchQuery("q", Jurisdiction: " "),
        new RegulationSearchQuery("q", DocumentIds: ["not-a-hash"]),
        new RegulationSearchQuery("q", DocumentIds: []),
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public async Task Invalid_queries_are_rejected_before_any_request(RegulationSearchQuery query)
    {
        var handler = StubHttpHandler.Json(Example);

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).SearchAsync(query, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Health_requires_the_rag_service_identity_and_contract_version()
    {
        var ok = await Client(StubHttpHandler.Json("{\"service\":\"buildmind-rag\",\"status\":\"ok\",\"contract_version\":\"1\",\"corpus_loaded\":true}"))
            .CheckHealthAsync(CancellationToken.None);
        var wrong = await Assert.ThrowsAsync<DependencyFailureException>(() =>
            Client(StubHttpHandler.Json("{\"service\":\"buildmind-python\",\"status\":\"ok\",\"contract_version\":\"1\"}")).CheckHealthAsync(CancellationToken.None));

        Assert.True(ok.CorpusLoaded);
        Assert.Equal(DependencyFailureKind.MalformedResponse, wrong.Kind);
    }

    [Fact]
    public void Application_port_resolves_to_the_hardened_rag_client()
    {
        using var factory = new BuildMindApiFactory("Production");
        using var scope = factory.Services.CreateScope();

        Assert.IsType<RagServiceClient>(scope.ServiceProvider.GetRequiredService<IRegulationEvidenceSource>());
    }
}

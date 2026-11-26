using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Integration;

/// <summary>
/// G11 MVP (lead-approved 2026-11-19): per-service shared-secret header, sent only when configured, validated at
/// start-up (header-safe, bounded, never echoed), required for RAG outside Development (fail closed).
/// </summary>
public sealed class InternalServiceAuthTests
{
    private const string Token = "synthetic-internal-token-0123456789abcdef";
    private const string NoMatches = "{\"status\":\"NO_MATCHING_EVIDENCE\",\"passages\":[],\"warnings\":[],\"legal_status_verified\":false,\"answer_generated\":false}";

    private static RagServiceClient Rag(StubHttpHandler handler, string? token) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new ServicesOptions { Rag = new InternalServiceOptions { Enabled = true, BaseUrl = "http://rag.internal:8100/", TimeoutSeconds = 5, AuthToken = token } }),
            new InternalServiceBulkhead());

    [Fact]
    public async Task Configured_token_is_sent_in_the_internal_header_only()
    {
        var handler = StubHttpHandler.Json(NoMatches);

        await Rag(handler, Token).SearchAsync(new RegulationSearchQuery("rear space"), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(Token, Assert.Single(request.Headers.GetValues(InternalServiceOptions.AuthHeaderName)));
        Assert.Null(request.Headers.Authorization); // user credentials are never forwarded
    }

    [Fact]
    public async Task No_header_is_sent_when_no_token_is_configured()
    {
        var handler = StubHttpHandler.Json(NoMatches);

        await Rag(handler, null).SearchAsync(new RegulationSearchQuery("rear space"), CancellationToken.None);

        Assert.False(handler.Requests.Single().Headers.Contains(InternalServiceOptions.AuthHeaderName));
    }

    [Theory]
    [InlineData("too-short")]
    [InlineData("has a space in it so it is not header safe 0123456789")]
    [InlineData("line\r\nInjected: header-0123456789abcdef0123456789")]
    [InlineData("non-ascii-token-é-0123456789abcdef0123456789")]
    public void Unsafe_tokens_are_rejected_without_echoing_them(string token)
    {
        var options = new ServicesOptions { Rag = new InternalServiceOptions { Enabled = true, BaseUrl = "http://rag.internal:8100", AuthToken = token } };

        var failures = options.Validate();

        Assert.Contains(failures, f => f.StartsWith("Services:Rag:AuthToken must be", StringComparison.Ordinal));
        Assert.All(failures, f => Assert.DoesNotContain(token, f, StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_rag_requires_a_token_only_when_required()
    {
        var options = new ServicesOptions { Rag = new InternalServiceOptions { Enabled = true, BaseUrl = "http://rag.internal:8100" } };

        Assert.Contains(options.Validate(requireRagAuthToken: true), f => f.Contains("Services:Rag:AuthToken is required", StringComparison.Ordinal));
        Assert.Empty(options.Validate(requireRagAuthToken: false));
        Assert.Empty(new ServicesOptions().Validate(requireRagAuthToken: true)); // disabled RAG never blocks start-up
    }

    [Fact]
    public async Task Production_start_up_fails_closed_without_a_rag_token()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?> { ["Services:Rag:Enabled"] = "true", ["Services:Rag:BaseUrl"] = "https://rag.internal:8100" });

        var exception = Record.Exception(() => factory.CreateClient());

        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(exception, factory.Logs);
        Assert.Contains(validation.Failures, f => f.Contains("Services:Rag:AuthToken is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_start_up_allows_an_unauthenticated_local_rag()
    {
        await using var factory = new BuildMindApiFactory(
            "Development",
            new Dictionary<string, string?> { ["Services:Rag:Enabled"] = "true", ["Services:Rag:BaseUrl"] = "http://127.0.0.1:8100" });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Token_never_appears_in_logs()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?>
            {
                ["Services:Rag:Enabled"] = "true",
                ["Services:Rag:BaseUrl"] = "https://rag.internal:8100",
                ["Services:Rag:AuthToken"] = Token,
            });
        await using var host = factory.WithRagTransport(StubHttpHandler.Throws(new HttpRequestException("refused")));
        using var client = host.CreateClient();

        using var _ = await client.GetAsync("/health/ready");

        Assert.All(factory.Logs.Entries, e => Assert.DoesNotContain(Token, e.AllText, StringComparison.Ordinal));
    }
}

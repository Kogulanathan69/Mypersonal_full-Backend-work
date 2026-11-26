using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using BuildMind.API.Endpoints.Regulations;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Abstractions.Regulations;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>M7 public regulation retrieval module (isolated; never registered by the production composition root).</summary>
public sealed class RegulationRetrievalEndpointTests
{
    private const string Route = "/api/v1/regulations/retrieve";
    private const string AuthHeader = "X-Regulations-Test-Auth";
    private static readonly string DocA = new('a', 64);
    private static readonly string DocB = new('b', 64);

    [Fact]
    public async Task Unauthenticated_request_is_401_and_never_reaches_the_evidence_source()
    {
        var source = new StubSource();
        await using var app = await Host(source);
        var response = await app.GetTestClient().PostAsync(Route, Json("""{"query":"rear setback"}"""));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Valid_request_returns_cited_evidence_with_explicit_non_legal_flags()
    {
        var source = new StubSource
        {
            Result = new RegulationEvidence(
                RegulationEvidenceStatus.EvidenceFound,
                [new RegulationPassage("p-1", DocA, "Planning Regulations", "Colombo", 12, 100, 420, "Rear space shall be...", 3.21,
                    PassageEvidenceStatus.EvidenceCandidate, ["ocr-low-confidence"])],
                ["single-source"]),
        };
        await using var app = await Host(source);

        var response = await Client(app).PostAsync(Route, Json("""{"query":"  rear setback  "}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("1", root.GetProperty("contractVersion").GetString());
        Assert.Equal("EVIDENCE_FOUND", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("legalStatusVerified").GetBoolean());
        Assert.False(root.GetProperty("answerGenerated").GetBoolean());
        Assert.Equal(RegulationRetrievalEndpointModule.Disclaimer, root.GetProperty("disclaimer").GetString());
        Assert.Equal("single-source", root.GetProperty("warnings")[0].GetString());
        var passage = root.GetProperty("passages")[0];
        Assert.Equal("p-1", passage.GetProperty("passageId").GetString());
        Assert.Equal(DocA, passage.GetProperty("documentId").GetString());
        Assert.Equal("Planning Regulations", passage.GetProperty("sourceTitle").GetString());
        Assert.Equal("Colombo", passage.GetProperty("jurisdiction").GetString());
        Assert.Equal(12, passage.GetProperty("page").GetInt32());
        Assert.Equal(100, passage.GetProperty("startChar").GetInt32());
        Assert.Equal(420, passage.GetProperty("endChar").GetInt32());
        Assert.Equal("Rear space shall be...", passage.GetProperty("excerpt").GetString());
        Assert.Equal(3.21, passage.GetProperty("lexicalScore").GetDouble());
        Assert.Equal("EVIDENCE_CANDIDATE", passage.GetProperty("evidenceStatus").GetString());
        Assert.Equal("ocr-low-confidence", passage.GetProperty("warnings")[0].GetString());
        Assert.False(passage.TryGetProperty("score", out _));

        var query = Assert.Single(source.Queries);
        Assert.Equal("rear setback", query.Query);
        Assert.Null(query.Jurisdiction);
        Assert.Null(query.DocumentIds);
        Assert.Equal(RegulationRetrievalEndpointModule.DefaultMaxResults, query.MaxPassages);
    }

    [Fact]
    public async Task All_optional_members_are_passed_through_exactly()
    {
        var source = new StubSource();
        await using var app = await Host(source);
        var response = await Client(app).PostAsync(Route,
            Json($$"""{"query":"floor area ratio","jurisdiction":"Colombo","documentIds":["{{DocA}}","{{DocB}}"],"maxResults":10}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var query = Assert.Single(source.Queries);
        Assert.Equal("Colombo", query.Jurisdiction);
        Assert.Equal([DocA, DocB], query.DocumentIds!);
        Assert.Equal(10, query.MaxPassages);
    }

    public static TheoryData<string, string> InvalidBodies() => new()
    {
        { """{}""", "query" },
        { """{"query":null}""", "query" },
        { """{"query":"   "}""", "query" },
        { "{\"query\":\"" + new string('q', 501) + "\"}", "query" },
        { """{"query":"ok","jurisdiction":""}""", "jurisdiction" },
        { "{\"query\":\"ok\",\"jurisdiction\":\"" + new string('j', 201) + "\"}", "jurisdiction" },
        { """{"query":"ok","documentIds":[]}""", "documentIds" },
        { $$"""{"query":"ok","documentIds":["{{DocA}}","{{DocB}}","{{new string('c', 64)}}","{{new string('d', 64)}}"]}""", "documentIds" },
        { $$"""{"query":"ok","documentIds":["{{DocA.ToUpperInvariant()}}"]}""", "documentIds" },
        { $$"""{"query":"ok","documentIds":["{{DocA}}","{{DocA}}"]}""", "documentIds" },
        { """{"query":"ok","documentIds":[null]}""", "documentIds" },
        { """{"query":"ok","maxResults":0}""", "maxResults" },
        { """{"query":"ok","maxResults":11}""", "maxResults" },
        { """{"query":"ok","projectId":"00000000-0000-0000-0000-000000000001"}""", "request" },
        { """{"query":"ok","maxResults":"5"}""", "request" },
        { """{"query":42}""", "request" },
        { """{"query":"ok" """, "request" },
        { """null""", "request" },
        { """["query"]""", "request" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_requests_are_400_validation_failures_without_calling_the_source(string body, string field)
    {
        var source = new StubSource();
        await using var app = await Host(source);
        var response = await Client(app).PostAsync(Route, Json(body));

        var problem = await ExpectProblem(response, HttpStatusCode.BadRequest, SharedErrorCodes.ValidationFailed);
        Assert.True(problem.Root.GetProperty("errors").TryGetProperty(field, out _), $"expected an error for '{field}'");
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Validation_errors_never_echo_the_submitted_query()
    {
        const string secret = "lot 42 owned by a private person";
        await using var app = await Host(new StubSource());
        var response = await Client(app).PostAsync(Route, Json($$"""{"query":"{{secret}}","maxResults":99}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(secret, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_json_content_type_is_415()
    {
        var source = new StubSource();
        await using var app = await Host(source);
        var response = await Client(app).PostAsync(Route, new StringContent("""{"query":"ok"}""", Encoding.UTF8, "text/plain"));

        await ExpectProblem(response, HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData(RegulationEvidenceStatus.EvidenceFound, "EVIDENCE_FOUND")]
    [InlineData(RegulationEvidenceStatus.NoMatchingEvidence, "NO_MATCHING_EVIDENCE")]
    [InlineData(RegulationEvidenceStatus.InsufficientJurisdictionEvidence, "INSUFFICIENT_JURISDICTION_EVIDENCE")]
    [InlineData(RegulationEvidenceStatus.InsufficientSourceEvidence, "INSUFFICIENT_SOURCE_EVIDENCE")]
    [InlineData(RegulationEvidenceStatus.AmbiguousEvidence, "AMBIGUOUS_EVIDENCE")]
    public async Task Every_evidence_status_maps_to_its_public_code(RegulationEvidenceStatus status, string expected)
    {
        await using var app = await Host(new StubSource { Result = new RegulationEvidence(status, [], []) });
        var response = await Client(app).PostAsync(Route, Json("""{"query":"ok"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("status").GetString());
        Assert.Empty(body.RootElement.GetProperty("passages").EnumerateArray());
    }

    [Fact]
    public void Every_enum_value_is_mapped()
    {
        foreach (var status in Enum.GetValues<RegulationEvidenceStatus>())
        {
            Assert.Matches("^[A-Z_]+$", RegulationRetrievalEndpointModule.StatusCode(status));
        }

        foreach (var status in Enum.GetValues<PassageEvidenceStatus>())
        {
            var passage = new RegulationPassage("p", DocA, "t", null, 1, 0, 1, "e", 0, status, []);
            var mapped = RegulationRetrievalEndpointModule.ToResponse(new RegulationEvidence(RegulationEvidenceStatus.EvidenceFound, [passage], []));
            Assert.Matches("^[A-Z_]+$", mapped.Passages[0].EvidenceStatus);
        }
    }

    [Theory]
    [InlineData(DependencyFailureKind.Unavailable, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData(DependencyFailureKind.Disabled, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData(DependencyFailureKind.MalformedResponse, HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData(DependencyFailureKind.Timeout, HttpStatusCode.GatewayTimeout, "DEPENDENCY_TIMEOUT")]
    public async Task Dependency_failures_map_to_safe_problem_details(DependencyFailureKind kind, HttpStatusCode status, string code)
    {
        await using var app = await Host(new StubSource { Failure = new DependencyFailureException("rag", kind) });
        var response = await Client(app).PostAsync(Route, Json("""{"query":"ok"}"""));

        await ExpectProblem(response, status, code);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("rag", text.Replace("DEPENDENCY", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Caller_abort_cancels_the_internal_call()
    {
        var source = new StubSource { WaitForCancellation = true };
        await using var app = await Host(source);
        using var abort = new CancellationTokenSource();
        var request = Client(app).PostAsync(Route, Json("""{"query":"ok"}"""), abort.Token);

        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await abort.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(await source.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Endpoint_requires_authorization_and_is_an_expensive_operation_on_post_only()
    {
        await using var app = await Host(new StubSource());
        var endpoint = Assert.Single(app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>(),
            e => e.RoutePattern.RawText == Route);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAuthorizeData>());
        Assert.NotNull(endpoint.Metadata.GetMetadata<ExpensiveOperationMetadata>());
        Assert.Equal(["POST"], endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods);
        var get = await Client(app).GetAsync(Route);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
    }

    [Fact]
    public async Task Expensive_limit_applies_per_user_through_the_real_rate_limiter()
    {
        var source = new StubSource { WaitForCancellation = true };
        await using var app = await Host(source, rateLimited: true);
        using var stop = new CancellationTokenSource();
        var client = Client(app);
        var first = client.PostAsync(Route, Json("""{"query":"one"}"""), stop.Token);
        var second = client.PostAsync(Route, Json("""{"query":"two"}"""), stop.Token);
        await source.WaitForEntries(2);

        // Bounded: if the limit regressed, the third call would block in the stub; fail fast instead of hanging.
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var third = await client.PostAsync(Route, Json("""{"query":"three"}"""), bound.Token);

        await ExpectProblem(third, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
        Assert.Equal(2, source.Calls);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Registration_is_refused_outside_testing_hosts(string environment)
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddIsolatedRegulationRetrievalFeature(new TestEnvironment(environment)));
        Assert.Empty(services);
    }

    [Fact]
    public async Task Production_composition_root_does_not_expose_the_endpoint()
    {
        await using var factory = new BuildMindApiFactory();
        Assert.DoesNotContain(factory.Services.GetServices<IApiEndpointModule>(), module => module is RegulationRetrievalEndpointModule);
        var response = await factory.CreateClient().PostAsync(Route, Json("""{"query":"ok"}"""));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<ProblemResponse> ExpectProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await ProblemResponse.ReadAsync(response);
        Assert.Equal("application/problem+json", problem.MediaType);
        Assert.Equal(code, problem.GetString("code"));
        return problem;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static HttpClient Client(WebApplication app)
    {
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(AuthHeader, "yes");
        return client;
    }

    private static async Task<WebApplication> Host(StubSource source, bool rateLimited = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("RegulationsTest").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("RegulationsTest", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Apply);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddSingleton<IRegulationEvidenceSource>(source);
        builder.Services.AddIsolatedRegulationRetrievalFeature(builder.Environment);
        if (rateLimited)
        {
            builder.Services.AddBuildMindRateLimiting(new ConfigurationBuilder().Build());
        }

        var app = builder.Build();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRouting();
        // Documented production order (17 §25): authentication → rate limiter → authorization.
        app.UseAuthentication();
        if (rateLimited)
        {
            app.UseRateLimiter();
        }

        app.UseAuthorization();
        app.MapBuildMindApiV1();
        await app.StartAsync();
        return app;
    }

    private sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(
            Request.Headers[AuthHeader] == "yes"
                ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "70000000-0000-0000-0000-000000000007")], Scheme.Name)), Scheme.Name))
                : AuthenticateResult.NoResult());
    }

    private sealed class StubSource : IRegulationEvidenceSource
    {
        private int _calls;
        private readonly object _gate = new();
        private readonly List<RegulationSearchQuery> _queries = [];

        public RegulationEvidence Result { get; init; } = new(RegulationEvidenceStatus.NoMatchingEvidence, [], []);
        public Exception? Failure { get; init; }
        public bool WaitForCancellation { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref _calls);

        public IReadOnlyList<RegulationSearchQuery> Queries
        {
            get { lock (_gate) { return _queries.ToArray(); } }
        }

        public async Task WaitForEntries(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Calls < count)
            {
                Assert.True(DateTime.UtcNow < deadline, "timed out waiting for in-flight requests");
                await Task.Delay(10);
            }
        }

        public async Task<RegulationEvidence> SearchAsync(RegulationSearchQuery query, CancellationToken cancellationToken)
        {
            lock (_gate) { _queries.Add(query); }
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            if (Failure is not null)
            {
                throw Failure;
            }

            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Cancelled.TrySetResult(true);
                    throw;
                }
            }

            return Result;
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "BuildMind.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

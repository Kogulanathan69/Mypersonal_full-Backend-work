using System.Diagnostics;
using System.Net;
using System.Text;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Integration;
using BuildMind.Infrastructure.Tests.Support;

namespace BuildMind.Infrastructure.Tests.Integration;

/// <summary>Hardened internal service client (BE-M4-D05) driven by a controlled transport. No network.</summary>
public sealed class InternalServiceClientTests
{
    private const string Base = "http://python-services.internal:8000/";
    private const string Secret = SyntheticTestEndpoints.SyntheticSecret;

    private static InternalServiceClient Client(StubHttpHandler handler, bool enabled = true, string baseUrl = Base, int timeoutSeconds = 5, int maxBytes = 1024) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new InternalServiceOptions { Enabled = enabled, BaseUrl = baseUrl, TimeoutSeconds = timeoutSeconds },
            "python", maxBytes);

    private static async Task<DependencyFailureException> Fails(Func<Task> call) => await Assert.ThrowsAsync<DependencyFailureException>(call);

    private sealed record Health(string? Service, string? Status);

    [Fact]
    public async Task Successful_json_response_is_returned()
    {
        var handler = StubHttpHandler.Json("{\"service\":\"buildmind-python\",\"status\":\"ok\"}");

        var health = await Client(handler).GetJsonAsync<Health>("health", null, CancellationToken.None);

        Assert.Equal(new Health("buildmind-python", "ok"), health);
        Assert.Equal(new Uri(Base + "health"), handler.Requests.Single().RequestUri);
    }

    [Fact]
    public async Task Disabled_dependency_fails_without_any_request()
    {
        var handler = StubHttpHandler.Json("{}");

        var failure = await Fails(() => Client(handler, enabled: false).GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Disabled, failure.Kind);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_a_dependency_failure()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(StubHttpHandler.Hangs()).GetJsonAsync<Health>("health", null, cancelled.Token));
    }

    [Fact]
    public async Task Configured_timeout_becomes_a_timeout_failure()
    {
        var watch = Stopwatch.StartNew();

        var failure = await Fails(() => Client(StubHttpHandler.Hangs(), timeoutSeconds: 1).GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Timeout, failure.Kind);
        Assert.Equal(SharedErrorCodes.DependencyTimeout, failure.Code);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Took {watch.Elapsed}");
    }

    [Fact]
    public async Task Connection_failure_is_unavailable()
    {
        var failure = await Fails(() => Client(StubHttpHandler.Throws(new HttpRequestException("refused " + Base))).GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.Unavailable, failure.Kind);
        Assert.Equal(SharedErrorCodes.DependencyUnavailable, failure.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, DependencyFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, DependencyFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, DependencyFailureKind.Timeout)]
    [InlineData(HttpStatusCode.InternalServerError, DependencyFailureKind.UnexpectedResponse)]
    [InlineData(HttpStatusCode.NotFound, DependencyFailureKind.UnexpectedResponse)]
    [InlineData(HttpStatusCode.Found, DependencyFailureKind.UnexpectedResponse)]          // redirects are never followed
    [InlineData(HttpStatusCode.TemporaryRedirect, DependencyFailureKind.UnexpectedResponse)]
    public async Task Unexpected_statuses_are_classified(HttpStatusCode status, DependencyFailureKind expected)
    {
        var failure = await Fails(() => Client(StubHttpHandler.Status(status)).GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(expected, failure.Kind);
        Assert.Equal((int)status, failure.StatusCode);
    }

    [Theory]
    [InlineData("not json", "application/json")]
    [InlineData("{\"service\":", "application/json")]
    [InlineData("null", "application/json")]
    [InlineData("{\"service\":\"buildmind-python\",\"status\":\"ok\"}", "text/html")]
    public async Task Malformed_responses_are_rejected(string body, string mediaType)
    {
        var failure = await Fails(() => Client(StubHttpHandler.Json(body, mediaType: mediaType)).GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task Responses_failing_validation_are_malformed()
    {
        var failure = await Fails(() => Client(StubHttpHandler.Json("{\"service\":\"other\",\"status\":\"ok\"}"))
            .GetJsonAsync<Health>("health", h => h.Service == "buildmind-python", CancellationToken.None));

        Assert.Equal(DependencyFailureKind.MalformedResponse, failure.Kind);
    }

    [Fact]
    public async Task Oversized_response_with_content_length_is_rejected()
    {
        var failure = await Fails(() => Client(StubHttpHandler.Json("{\"a\":\"" + new string('x', 2048) + "\"}"), maxBytes: 1024)
            .GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.ResponseTooLarge, failure.Kind);
    }

    [Fact]
    public async Task Oversized_streamed_response_without_length_is_rejected()
    {
        var handler = new StubHttpHandler((_, _) =>
        {
            var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":\"" + new string('x', 4096) + "\"}")));
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var failure = await Fails(() => Client(handler, maxBytes: 1024).GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.Equal(DependencyFailureKind.ResponseTooLarge, failure.Kind);
    }

    [Fact]
    public async Task Trace_context_is_propagated_from_the_current_activity()
    {
        var handler = StubHttpHandler.Json("{\"service\":\"buildmind-python\",\"status\":\"ok\"}");
        using var activity = new Activity("synthetic-request").SetIdFormat(ActivityIdFormat.W3C).Start();

        await Client(handler).GetJsonAsync<Health>("health", null, CancellationToken.None);

        var traceparent = Assert.Single(handler.Requests.Single().Headers.GetValues("traceparent"));
        Assert.Equal(activity.Id, traceparent);
        Assert.Contains(activity.TraceId.ToHexString(), traceparent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_credentials_or_cookies_are_ever_sent()
    {
        var handler = StubHttpHandler.Json("{\"service\":\"buildmind-python\",\"status\":\"ok\"}");

        await Client(handler).PostJsonAsync<Health, Health>("health", new Health("x", "y"), null, CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.False(request.Headers.Contains("Authorization"));
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData("http://evil.example/x")]
    [InlineData("//evil.example/x")]
    [InlineData("/health")]
    [InlineData("../admin")]
    [InlineData("a/../../admin")]
    [InlineData("a\\b")]
    [InlineData("")]
    // STEP 08-B regression: encoded traversal / separators, query, fragment, dot and empty segments, non-ASCII.
    [InlineData("%2e%2e/admin")]
    [InlineData("%2E%2E/admin")]
    [InlineData("a/%2e%2e/%2e%2e/admin")]
    [InlineData("%252e%252e/admin")]
    [InlineData("a%2Fb")]
    [InlineData("a%5Cb")]
    [InlineData("health?debug=1")]
    [InlineData("health#fragment")]
    [InlineData("./health")]
    [InlineData("health/")]
    [InlineData("a//b")]
    [InlineData(" health")]
    [InlineData("héalth")]
    [InlineData("health∕admin")]
    public async Task Destinations_outside_the_configured_base_are_rejected_before_any_request(string path)
    {
        var handler = StubHttpHandler.Json("{}");

        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).GetJsonAsync<Health>(path, null, CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Base_path_is_preserved()
    {
        var handler = StubHttpHandler.Json("{\"service\":\"s\",\"status\":\"ok\"}");

        await Client(handler, baseUrl: "https://rag.internal/base").GetJsonAsync<Health>("v1/health", null, CancellationToken.None);

        Assert.Equal(new Uri("https://rag.internal/base/v1/health"), handler.Requests.Single().RequestUri);
    }

    [Fact]
    public async Task Failures_are_never_retried()
    {
        var handler = StubHttpHandler.Status(HttpStatusCode.ServiceUnavailable);

        await Fails(() => Client(handler).PostJsonAsync<Health, Health>("generate", new Health("x", "y"), null, CancellationToken.None));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Failure_messages_contain_no_url_host_or_payload()
    {
        var failure = await Fails(() => Client(StubHttpHandler.Throws(new HttpRequestException("refused python-services.internal " + Secret)))
            .GetJsonAsync<Health>("health", null, CancellationToken.None));

        Assert.DoesNotContain("python-services.internal", failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, failure.ToString(), StringComparison.Ordinal);
        Assert.Null(failure.InnerException);
    }
}

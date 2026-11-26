using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuildMind.Infrastructure.Tests.Support;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>RFC 9457 Problem Details behaviour (decisions C3, C4, C5) in Development and Production.</summary>
public sealed class ProblemDetailsTests
{
    private const string ProblemJson = "application/problem+json";

    private static readonly string[] ForbiddenFragments =
    [
        SyntheticTestEndpoints.SyntheticSecret, "Password=", "Server=", "Uid=", "C:\\private",
        "InvalidOperationException", "ArgumentException", "OperationCanceledException",
        "BadHttpRequestException", "   at ", "StackTrace", "stackTrace", "exception",
    ];

    public static TheoryData<string> Environments => new() { "Development", "Production" };

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Unknown_api_route_returns_problem_404(string environment)
    {
        var problem = await GetProblemAsync(environment, "/api/v1/does-not-exist");

        AssertProblem(problem, HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal("/api/v1/does-not-exist", problem.GetString("instance"));
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Unexpected_exception_returns_safe_500(string environment)
    {
        var problem = await GetProblemAsync(environment, SyntheticTestEndpoints.Prefix + "/throw");

        AssertProblem(problem, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Equal("urn:buildmind:problem:internal-error", problem.GetString("type"));
        Assert.Equal("An unexpected error occurred.", problem.GetString("detail"));
        AssertNoSensitiveContent(problem.RawBody);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task ArgumentException_is_not_globally_mapped_to_400(string environment)
    {
        var problem = await GetProblemAsync(environment, SyntheticTestEndpoints.Prefix + "/argument-error");

        AssertProblem(problem, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        AssertNoSensitiveContent(problem.RawBody);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Server_side_cancellation_is_not_mapped_to_504(string environment)
    {
        var problem = await GetProblemAsync(environment, SyntheticTestEndpoints.Prefix + "/server-cancelled");

        AssertProblem(problem, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        AssertNoSensitiveContent(problem.RawBody);
    }

    [Theory]
    [InlineData(400, "MALFORMED_REQUEST")]
    [InlineData(413, "PAYLOAD_TOO_LARGE")]
    [InlineData(415, "UNSUPPORTED_MEDIA_TYPE")]
    public async Task Framework_bad_request_exceptions_keep_their_client_status(int status, string code)
    {
        var problem = await GetProblemAsync("Production", SyntheticTestEndpoints.Prefix + $"/bad-request/{status}");

        AssertProblem(problem, (HttpStatusCode)status, code);
        AssertNoSensitiveContent(problem.RawBody);
    }

    [Theory]
    [InlineData(400, "MALFORMED_REQUEST")]
    [InlineData(401, "UNAUTHENTICATED")]
    [InlineData(403, "FORBIDDEN")]
    [InlineData(404, "NOT_FOUND")]
    [InlineData(409, "CONFLICT")]
    [InlineData(413, "PAYLOAD_TOO_LARGE")]
    [InlineData(415, "UNSUPPORTED_MEDIA_TYPE")]
    [InlineData(422, "VALIDATION_FAILED")]
    [InlineData(429, "RATE_LIMITED")]
    [InlineData(500, "INTERNAL_ERROR")]
    [InlineData(503, "DEPENDENCY_UNAVAILABLE")]
    [InlineData(504, "DEPENDENCY_TIMEOUT")]
    public async Task Empty_error_statuses_become_problems_with_shared_codes(int status, string code)
    {
        var problem = await GetProblemAsync("Production", SyntheticTestEndpoints.Prefix + $"/status/{status}");

        AssertProblem(problem, (HttpStatusCode)status, code);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Malformed_json_returns_400_malformed_request(string environment)
    {
        await using var factory = new BuildMindApiFactory(environment);
        using var client = factory.CreateClient();
        using var content = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(SyntheticTestEndpoints.Prefix + "/json", content);
        var problem = await ProblemResponse.ReadAsync(response);

        AssertProblem(problem, HttpStatusCode.BadRequest, "MALFORMED_REQUEST");
        AssertNoSensitiveContent(problem.RawBody);
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Unsupported_content_type_returns_415(string environment)
    {
        await using var factory = new BuildMindApiFactory(environment);
        using var client = factory.CreateClient();
        using var content = new StringContent("name=x", Encoding.UTF8, "text/plain");

        using var response = await client.PostAsync(SyntheticTestEndpoints.Prefix + "/json", content);
        var problem = await ProblemResponse.ReadAsync(response);

        AssertProblem(problem, HttpStatusCode.UnsupportedMediaType, "UNSUPPORTED_MEDIA_TYPE");
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Endpoint_supplied_500_problem_is_reduced_to_generic_fields(string environment)
    {
        var problem = await GetProblemAsync(environment, SyntheticTestEndpoints.Prefix + "/leaky-500/invalid");

        AssertProblem(problem, HttpStatusCode.InternalServerError, "INTERNAL_ERROR");
        Assert.Equal("An unexpected error occurred.", problem.GetString("detail"));
        Assert.False(problem.Has("debug"));
        Assert.False(problem.Has("connectionString"));
        AssertNoSensitiveContent(problem.RawBody);
    }

    [Fact]
    public async Task Well_formed_feature_code_is_preserved_on_reduced_500_problem()
    {
        var problem = await GetProblemAsync("Production", SyntheticTestEndpoints.Prefix + "/leaky-500/feature");

        AssertProblem(problem, HttpStatusCode.InternalServerError, "SYNTHETIC_FEATURE_FAILURE");
        Assert.Equal("urn:buildmind:problem:synthetic-feature-failure", problem.GetString("type"));
        Assert.Equal("An unexpected error occurred.", problem.GetString("detail"));
        AssertNoSensitiveContent(problem.RawBody);
    }

    [Fact]
    public async Task Validation_helper_returns_400_with_field_errors()
    {
        var problem = await GetProblemAsync("Production", SyntheticTestEndpoints.Prefix + "/validation");

        AssertProblem(problem, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        Assert.Equal("urn:buildmind:problem:validation-failed", problem.GetString("type"));
        var errors = problem.Root.GetProperty("errors");
        Assert.Equal("Name is required.", errors.GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Domain_rule_helper_returns_422_with_feature_code_and_diagnostics()
    {
        var problem = await GetProblemAsync("Production", SyntheticTestEndpoints.Prefix + "/domain-rule");

        AssertProblem(problem, HttpStatusCode.UnprocessableEntity, "SYNTHETIC_RULE_VIOLATED");
        Assert.Equal("urn:buildmind:problem:synthetic-rule-violated", problem.GetString("type"));
        Assert.Equal("synthetic-diagnostic", problem.Root.GetProperty("diagnostics")[0].GetString());
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public async Task Missing_authenticated_user_returns_401(string environment)
    {
        var problem = await GetProblemAsync(environment, SyntheticTestEndpoints.Prefix + "/require-user");

        AssertProblem(problem, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [Fact]
    public async Task Client_supplied_correlation_values_are_not_used_as_trace_id()
    {
        await using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        request.Headers.Add("X-Correlation-Id", "attacker-controlled-value");
        request.Headers.Add("X-Trace-Id", "attacker-controlled-value");

        using var response = await client.SendAsync(request);
        var problem = await ProblemResponse.ReadAsync(response);

        Assert.NotEqual("attacker-controlled-value", problem.GetString("traceId"));
    }

    [Fact]
    public async Task Liveness_contract_is_unchanged_by_error_handling()
    {
        await using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("{\"service\":\"buildmind-api\",\"status\":\"ok\"}", body);
    }

    private static async Task<ProblemResponse> GetProblemAsync(string environment, string path)
    {
        await using var factory = new BuildMindApiFactory(environment);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProblemJson));
        using var response = await client.GetAsync(path);
        return await ProblemResponse.ReadAsync(response);
    }

    private static void AssertProblem(ProblemResponse problem, HttpStatusCode status, string code)
    {
        Assert.Equal(status, problem.Status);
        Assert.Equal(ProblemJson, problem.MediaType);
        Assert.Equal((int)status, problem.GetInt("status"));
        Assert.Equal(code, problem.GetString("code"));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetString("title")));
        Assert.StartsWith("urn:buildmind:problem:", problem.GetString("type"));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetString("instance")));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetString("traceId")));
    }

    private static void AssertNoSensitiveContent(string body)
    {
        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, body, StringComparison.Ordinal);
        }

        // The body must be a single JSON object without any nested exception data.
        using var document = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }
}

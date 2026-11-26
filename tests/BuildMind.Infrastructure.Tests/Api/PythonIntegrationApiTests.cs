using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BuildMind.API.Health;
using BuildMind.Infrastructure.Tests.Support;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// BE-M4-D05 through the real API host: Python readiness against the confirmed /health contract, dependency
/// failures through the public Problem Details contract, and no internal URLs or credentials in logs.
/// Python transport is a controlled stub; nothing leaves the process.
/// </summary>
public sealed class PythonIntegrationApiTests
{
    private const string PythonHost = "python-services.internal";

    private static readonly Dictionary<string, string?> PythonEnabled = new()
    {
        ["Services:Python:Enabled"] = "true",
        ["Services:Python:BaseUrl"] = $"https://{PythonHost}:8000",
        ["Services:Python:TimeoutSeconds"] = "1",
    };

    private static StubHttpHandler HealthyPython() => StubHttpHandler.Json("{\"service\":\"buildmind-python\",\"status\":\"ok\"}");

    [Theory]
    [InlineData("healthy", "Healthy", "reachable")]
    [InlineData("down", "Degraded", "unreachable")]
    [InlineData("error-500", "Degraded", "unreachable")]
    [InlineData("wrong-body", "Degraded", "unreachable")]
    [InlineData("hangs", "Degraded", "timeout")]
    public async Task Python_readiness_reflects_the_confirmed_health_contract(string scenario, string status, string reason)
    {
        var handler = scenario switch
        {
            "healthy" => HealthyPython(),
            "down" => StubHttpHandler.Throws(new HttpRequestException("synthetic")),
            "error-500" => StubHttpHandler.Status(HttpStatusCode.InternalServerError),
            "wrong-body" => StubHttpHandler.Json("{\"service\":\"something-else\",\"status\":\"ok\"}"),
            _ => StubHttpHandler.Hangs(),
        };
        await using var factory = new BuildMindApiFactory("Production", PythonEnabled);
        await using var host = factory.WithPythonTransport(handler);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);
        var python = Check(await response.Content.ReadAsStringAsync(), "python");

        Assert.Equal((status, reason), python);
        // MySQL is still not configured, so the instance is not ready whatever Python says.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_python_is_degraded_not_configured_and_never_contacted()
    {
        var handler = HealthyPython();
        await using var factory = new BuildMindApiFactory("Production");
        await using var host = factory.WithPythonTransport(handler);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(Readiness.Path);

        Assert.Equal(("Degraded", "not_configured"), Check(await response.Content.ReadAsStringAsync(), "python"));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("down", HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData("hangs", HttpStatusCode.GatewayTimeout, "DEPENDENCY_TIMEOUT")]
    [InlineData("invalid-json", HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData("healthy", HttpStatusCode.NoContent, null)]
    public async Task Feature_calls_translate_dependency_failures_to_problem_details(string scenario, HttpStatusCode expected, string? code)
    {
        var handler = scenario switch
        {
            "down" => StubHttpHandler.Throws(new HttpRequestException("synthetic " + PythonHost)),
            "hangs" => StubHttpHandler.Hangs(),
            "invalid-json" => StubHttpHandler.Json("{ not json"),
            _ => HealthyPython(),
        };
        await using var factory = new BuildMindApiFactory("Production", PythonEnabled);
        await using var host = factory.WithPythonTransport(handler);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        using var response = await client.GetAsync(SyntheticTestEndpoints.Prefix + "/python-health");

        Assert.Equal(expected, response.StatusCode);
        if (code is not null)
        {
            var problem = await ProblemResponse.ReadAsync(response);
            Assert.Equal(code, problem.GetString("code"));
            AssertNoInternalDetails(problem.RawBody);
        }
    }

    /// <summary>
    /// Structured leakage check. The random traceId (32 lowercase hex, or the ASP.NET Core connection id) can contain
    /// any digit sequence, so it is validated by FORMAT and excluded from substring checks; every other property name
    /// and string value must not reveal the internal host, the port, a URL or an exception type. The raw body is still
    /// checked for the host, URL schemes and exception names, which can never occur inside a valid trace id.
    /// </summary>
    internal static void AssertNoInternalDetails(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        if (root.TryGetProperty("traceId", out var traceId))
        {
            Assert.Equal(JsonValueKind.String, traceId.ValueKind);
            Assert.Matches(@"^(?:[0-9a-f]{32}|[0-9A-Z]{13}:[0-9A-F]{8})$", traceId.GetString());
        }

        foreach (var text in Texts(root, isRoot: true))
        {
            Assert.DoesNotContain(PythonHost, text, StringComparison.OrdinalIgnoreCase);
            foreach (var forbidden in new[] { "8000", "://", "Exception" })
            {
                Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
            }
        }

        Assert.DoesNotContain(PythonHost, rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("://", rawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", rawBody, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Texts(JsonElement element, bool isRoot = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    if (isRoot && property.NameEquals("traceId"))
                    {
                        continue; // validated by format above
                    }

                    foreach (var text in Texts(property.Value))
                    {
                        yield return text;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var text in Texts(item))
                    {
                        yield return text;
                    }
                }

                break;
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Number:
                yield return element.GetRawText(); // a bare 8000 anywhere else is still a leak
                break;
        }
    }

    [Theory]
    [InlineData("{\"code\":\"DEPENDENCY_UNAVAILABLE\",\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e8000\"}")] // the CI false positive
    [InlineData("{\"code\":\"DEPENDENCY_UNAVAILABLE\",\"traceId\":\"80008000800080008000800080008000\"}")]
    [InlineData("{\"code\":\"DEPENDENCY_UNAVAILABLE\",\"traceId\":\"0HN8000ABCDEF:80000001\"}")]
    public void Leakage_check_accepts_trace_ids_that_happen_to_contain_the_port_digits(string body) =>
        AssertNoInternalDetails(body);

    [Theory]
    [InlineData("{\"code\":\"X\",\"detail\":\"python-services.internal unreachable\",\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}")]
    [InlineData("{\"code\":\"X\",\"detail\":\"connect to 10.0.0.5:8000 failed\",\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}")]
    [InlineData("{\"code\":\"X\",\"instance\":\"https://internal/x\",\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}")]
    [InlineData("{\"code\":\"X\",\"detail\":\"HttpRequestException\",\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}")]
    [InlineData("{\"code\":\"X\",\"errors\":{\"port\":[8000]},\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}")]
    [InlineData("{\"code\":\"X\",\"8000\":\"x\",\"traceId\":\"4bf92f3577b34da6a3ce929d0e0e4736\"}")]
    [InlineData("{\"code\":\"X\",\"traceId\":\"python-services.internal:8000\"}")]                             // a leak disguised as a traceId
    [InlineData("{\"code\":\"X\",\"traceId\":\"not-a-trace-id-8000\"}")]
    public void Leakage_check_still_detects_host_port_url_and_exception_leaks(string body) =>
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoInternalDetails(body));

    [Theory]
    [InlineData("Timeout", HttpStatusCode.GatewayTimeout, "DEPENDENCY_TIMEOUT")]
    [InlineData("Unavailable", HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData("Disabled", HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData("MalformedResponse", HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData("ResponseTooLarge", HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    [InlineData("UnexpectedResponse", HttpStatusCode.ServiceUnavailable, "DEPENDENCY_UNAVAILABLE")]
    public async Task Every_failure_kind_maps_to_the_shared_contract(string kind, HttpStatusCode expected, string code)
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        using var response = await client.GetAsync(SyntheticTestEndpoints.Prefix + "/dependency-failure/" + kind);
        var problem = await ProblemResponse.ReadAsync(response);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, problem.GetString("code"));
        Assert.Contains(factory.Logs.Entries, e => e.Category == "BuildMind.API.Errors.GlobalExceptionHandler"
            && e.Properties.TryGetValue("FailureKind", out var logged) && logged == kind);
    }

    [Fact]
    public async Task Internal_urls_and_incoming_credentials_never_reach_logs_or_python()
    {
        var handler = StubHttpHandler.Throws(new HttpRequestException("synthetic " + PythonHost));
        await using var factory = new BuildMindApiFactory("Production", PythonEnabled);
        await using var host = factory.WithPythonTransport(handler);
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SyntheticTestEndpoints.Prefix + "/python-health");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + SyntheticTestEndpoints.SyntheticSecret);
        request.Headers.TryAddWithoutValidation("Cookie", "session=" + SyntheticTestEndpoints.SyntheticSecret);

        using var response = await client.SendAsync(request);
        using var ready = await client.GetAsync(Readiness.Path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotEmpty(factory.Logs.Entries);
        Assert.All(factory.Logs.Entries, e =>
        {
            Assert.DoesNotContain(PythonHost, e.AllText, StringComparison.Ordinal);
            Assert.DoesNotContain(SyntheticTestEndpoints.SyntheticSecret, e.AllText, StringComparison.Ordinal);
        });
        Assert.All(handler.Requests, r =>
        {
            Assert.False(r.Headers.Contains("Authorization"));
            Assert.False(r.Headers.Contains("Cookie"));
        });
    }

    private static (string Status, string Reason) Check(string body, string name)
    {
        using var document = JsonDocument.Parse(body);
        var check = document.RootElement.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        return (check.GetProperty("status").GetString()!, check.GetProperty("reason").GetString()!);
    }
}

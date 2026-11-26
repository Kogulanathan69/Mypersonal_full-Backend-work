using System.Net.Http.Headers;
using System.Text;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Infrastructure.Tests.Logging;

/// <summary>Operational logging safety (decision C10), verified in the Production configuration.</summary>
public sealed class LoggingSafetyTests
{
    private const string HandlerCategory = "BuildMind.API.Errors.GlobalExceptionHandler";

    [Fact]
    public async Task Unexpected_exception_is_logged_with_trace_id_and_without_secret_content()
    {
        await using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        using var response = await client.GetAsync(SyntheticTestEndpoints.Prefix + "/throw");
        var problem = await ProblemResponse.ReadAsync(response);
        var traceId = problem.GetString("traceId");

        var handlerEntries = factory.Logs.Entries.Where(e => e.Category == HandlerCategory).ToArray();
        Assert.Contains(handlerEntries, e =>
            e.Properties.TryGetValue("TraceId", out var logged) && logged == traceId);
        Assert.Contains(handlerEntries, e =>
            e.Properties.TryGetValue("ExceptionType", out var type) && type == typeof(InvalidOperationException).FullName);

        AssertNoSecretInAnyLog(factory.Logs);
    }

    [Fact]
    public async Task Encoded_control_characters_in_paths_cannot_inject_log_lines()
    {
        await using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            SyntheticTestEndpoints.Prefix + "/throw-with-segment/line1%0D%0AFORGED-LOG-ENTRY");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        var handlerEntries = factory.Logs.Entries.Where(e => e.Category == HandlerCategory).ToArray();
        Assert.NotEmpty(handlerEntries);
        Assert.All(handlerEntries, entry =>
        {
            Assert.DoesNotContain('\n', entry.Message);
            Assert.DoesNotContain('\r', entry.Message);
        });
    }

    [Theory]
    [InlineData(Microsoft.AspNetCore.Diagnostics.ExceptionHandledType.ExceptionHandlerService, true)]
    [InlineData(Microsoft.AspNetCore.Diagnostics.ExceptionHandledType.Unhandled, false)]
    [InlineData(Microsoft.AspNetCore.Diagnostics.ExceptionHandledType.ProblemDetailsService, false)]
    public async Task Framework_exception_diagnostics_are_suppressed_only_for_handled_exceptions(
        Microsoft.AspNetCore.Diagnostics.ExceptionHandledType handledBy,
        bool expectedSuppressed)
    {
        await using var factory = new ProductionApiFactory();
        var options = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ExceptionHandlerOptions>>()
            .Value;

        Assert.NotNull(options.SuppressDiagnosticsCallback);
        var context = new Microsoft.AspNetCore.Diagnostics.ExceptionHandlerSuppressDiagnosticsContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            Exception = new InvalidOperationException("synthetic"),
            ExceptionHandledBy = handledBy,
        };

        Assert.Equal(expectedSuppressed, options.SuppressDiagnosticsCallback!(context));
    }

    [Fact]
    public async Task Request_bodies_are_not_logged()
    {
        await using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();
        var body = "{ \"name\": \"" + SyntheticTestEndpoints.SyntheticSecret + "\", ";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(SyntheticTestEndpoints.Prefix + "/json", content);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoSecretInAnyLog(factory.Logs);
    }

    [Fact]
    public async Task Rejected_configuration_credentials_are_not_logged()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?>
            {
                ["Services:Python:BaseUrl"] = "http://svc:" + SyntheticTestEndpoints.SyntheticSecret + "@python.internal",
            });

        var exception = Record.Exception(() => factory.CreateClient());

        // Guard against a vacuous pass: start-up must fail because of options validation specifically,
        // and the host's own start-up failure entry must have been captured by the logs under test.
        ExceptionChains.RequireStartupOptionsValidationFailure(exception, factory.Logs);
        var recorded = ExceptionChains.RequireHostStartupFailure(factory.Logs);
        Assert.Contains(recorded.Failures, f => f.StartsWith("Services:Python:BaseUrl", StringComparison.Ordinal));
        AssertNoSecretInAnyLog(factory.Logs);
    }

    private static void AssertNoSecretInAnyLog(CapturingLoggerProvider logs)
    {
        Assert.All(logs.Entries, entry =>
            Assert.DoesNotContain(SyntheticTestEndpoints.SyntheticSecret, entry.AllText, StringComparison.Ordinal));
    }
}

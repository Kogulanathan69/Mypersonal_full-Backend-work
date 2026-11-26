using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Tests.Configuration;

/// <summary>Typed internal-service configuration and start-up validation (decision C8).</summary>
public sealed class ServicesOptionsTests
{
    [Fact]
    public void Defaults_are_valid_and_disabled()
    {
        var options = new ServicesOptions();

        Assert.Empty(options.Validate());
        Assert.False(options.Python.Enabled);
        Assert.False(options.Rag.Enabled);
    }

    [Theory]
    [InlineData("http://python-services.internal:8000")]
    [InlineData("https://rag.internal/base/")]
    public void Valid_enabled_configuration_is_accepted(string url)
    {
        var options = new ServicesOptions
        {
            Python = new InternalServiceOptions { Enabled = true, BaseUrl = url, TimeoutSeconds = 10 },
        };

        Assert.Empty(options.Validate());
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://python.internal")]
    [InlineData("/relative/path")]
    [InlineData("http://")]
    [InlineData("http://python.internal/?mode=debug")]
    [InlineData("http://python.internal/#fragment")]
    public void Malformed_urls_are_rejected(string url)
    {
        var options = new ServicesOptions { Python = new InternalServiceOptions { BaseUrl = url } };

        Assert.Contains(options.Validate(), failure => failure.StartsWith("Services:Python:BaseUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://user:" + SyntheticTestEndpoints.SyntheticSecret + "@python.internal")]
    [InlineData("https://" + SyntheticTestEndpoints.SyntheticSecret + "@rag.internal")]
    public void Embedded_credentials_are_rejected_without_echoing_them(string url)
    {
        var options = new ServicesOptions { Rag = new InternalServiceOptions { BaseUrl = url } };

        var failures = options.Validate();

        Assert.Contains(failures, failure => failure.StartsWith("Services:Rag:BaseUrl", StringComparison.Ordinal));
        Assert.DoesNotContain(failures, failure => failure.Contains(SyntheticTestEndpoints.SyntheticSecret, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(301)]
    public void Out_of_range_timeouts_are_rejected(int timeoutSeconds)
    {
        var options = new ServicesOptions { Python = new InternalServiceOptions { TimeoutSeconds = timeoutSeconds } };

        Assert.Contains(options.Validate(), failure => failure.StartsWith("Services:Python:TimeoutSeconds", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public void Boundary_timeouts_are_accepted(int timeoutSeconds)
    {
        var options = new ServicesOptions { Python = new InternalServiceOptions { TimeoutSeconds = timeoutSeconds } };

        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Enabled_service_without_url_is_rejected()
    {
        var options = new ServicesOptions { Rag = new InternalServiceOptions { Enabled = true } };

        Assert.Contains(options.Validate(), failure => failure.Contains("Services:Rag:BaseUrl is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shipped_configuration_starts_with_integrations_disabled()
    {
        await using var factory = new BuildMindApiFactory("Production");
        using var client = factory.CreateClient();

        var options = factory.Services.GetRequiredService<IOptions<ServicesOptions>>().Value;

        Assert.False(options.Python.Enabled);
        Assert.False(options.Rag.Enabled);
        Assert.Equal(10, options.Python.TimeoutSeconds);
        Assert.Equal(5, options.Rag.TimeoutSeconds);
    }

    [Fact]
    public async Task Enabled_service_without_url_fails_at_startup()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?> { ["Services:Python:Enabled"] = "true", ["Services:Python:BaseUrl"] = "" });

        var exception = Record.Exception(() => factory.CreateClient());

        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(exception, factory.Logs);
        Assert.Contains(validation.Failures, f => f.Contains("Services:Python:BaseUrl is required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Startup_failure_does_not_echo_configured_credentials()
    {
        await using var factory = new BuildMindApiFactory(
            "Production",
            new Dictionary<string, string?>
            {
                ["Services:Rag:Enabled"] = "true",
                ["Services:Rag:BaseUrl"] = "http://svc:" + SyntheticTestEndpoints.SyntheticSecret + "@rag.internal",
            });

        var exception = Record.Exception(() => factory.CreateClient());

        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(exception, factory.Logs);
        Assert.DoesNotContain(SyntheticTestEndpoints.SyntheticSecret, validation.Message, StringComparison.Ordinal);
    }
}

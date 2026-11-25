using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuildMind.API.Configuration;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// STEP 05 S4-A (decisions S4-1..S4-3): 1 MiB global request-body limit applied to Kestrel, start-up validation
/// (outside Development: set and at most 8 MiB), and the real Kestrel 413 behaviour. Kestrel cases run the real
/// composition root on a real Kestrel listener (<c>UseKestrel</c>), because TestServer does not apply Kestrel limits.
/// </summary>
public sealed class RequestBodyLimitTests
{
    private const string BodyRoute = SyntheticTestEndpoints.Prefix + "/json";
    private const long OneMiB = 1024 * 1024;

    // ---------- configuration ----------

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Default_global_limit_is_one_mebibyte(string environment)
    {
        await using var factory = new BuildMindApiFactory(environment);

        Assert.Equal(OneMiB, Limit(factory));
        Assert.Equal(RequestBodyLimitValidation.DefaultMaxRequestBodySize, Limit(factory));
    }

    [Fact]
    public async Task Missing_or_empty_key_falls_back_to_one_mebibyte_not_kestrels_30MB_default()
    {
        await using var factory = new StartupSettingsApiFactory("Production",
            new Dictionary<string, string?> { [RequestBodyLimitValidation.ConfigurationKey] = "" });
        factory.CreateClient().Dispose();

        Assert.Equal(OneMiB, factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize);
    }

    [Theory]
    [InlineData("Production", 1)]
    [InlineData("Production", 2 * OneMiB)]
    [InlineData("Production", 8 * OneMiB)]          // S4-3 boundary: accepted
    [InlineData("Staging", 8 * OneMiB)]
    [InlineData("Development", 64 * OneMiB)]        // Development is not capped
    public async Task Accepted_configured_limits(string environment, long bytes)
    {
        await using var factory = new StartupSettingsApiFactory(environment,
            new Dictionary<string, string?> { [RequestBodyLimitValidation.ConfigurationKey] = bytes.ToString() });
        factory.CreateClient().Dispose();

        Assert.Equal(bytes, factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize);
    }

    [Theory]
    [InlineData("Production", 8 * OneMiB + 1, "8 MiB")]
    [InlineData("Production", 30_000_000, "8 MiB")]
    [InlineData("Staging", 30_000_000, "8 MiB")]
    [InlineData("Testing", 8 * OneMiB + 1, "8 MiB")]
    [InlineData("Production", 0, "rejects every request body")]
    [InlineData("Development", 0, "rejects every request body")]
    public async Task Refused_configured_limits(string environment, long bytes, string expected)
    {
        await using var factory = new StartupSettingsApiFactory(environment,
            new Dictionary<string, string?> { [RequestBodyLimitValidation.ConfigurationKey] = bytes.ToString() });

        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(
            Record.Exception(() => factory.CreateClient()), factory.Logs);

        Assert.Equal(typeof(KestrelServerOptions), validation.OptionsType);
        Assert.Contains(validation.Failures, f => f.Contains(RequestBodyLimitValidation.ConfigurationKey, StringComparison.Ordinal)
            && f.Contains(expected, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1MB")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public async Task Non_numeric_limit_stops_start_up_naming_the_key(string value)
    {
        await using var factory = new StartupSettingsApiFactory("Production",
            new Dictionary<string, string?> { [RequestBodyLimitValidation.ConfigurationKey] = value });

        var thrown = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(thrown);
        var evidence = thrown!.ToString() + string.Join("\n", factory.Logs.Entries.Select(e => e.AllText));
        Assert.Contains(RequestBodyLimitValidation.ConfigurationKey, evidence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1L, false, true)]
    [InlineData(8L * 1024 * 1024, false, true)]
    [InlineData(8L * 1024 * 1024 + 1, false, false)]
    [InlineData(null, false, false)]
    [InlineData(0L, false, false)]
    [InlineData(0L, true, false)]
    [InlineData(null, true, true)]
    [InlineData(1L << 40, true, true)]
    public void Validation_rules(long? value, bool development, bool valid) =>
        Assert.Equal(valid, RequestBodyLimitValidation.Check(value, development) is null);

    // ---------- real Kestrel behaviour ----------

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Kestrel_accepts_a_body_exactly_at_the_limit_and_rejects_one_byte_more(string environment)
    {
        await using var factory = KestrelFactory(environment);
        using var client = factory.CreateClient();

        using var atLimit = await PostAsync(client, (int)OneMiB, chunked: false);
        using var overLimit = await PostAsync(client, (int)OneMiB + 1, chunked: false);

        Assert.Equal(HttpStatusCode.NoContent, atLimit.StatusCode);
        await AssertPayloadTooLargeAsync(overLimit);
    }

    [Fact]
    public async Task Kestrel_rejects_an_oversized_chunked_body_without_content_length()
    {
        await using var factory = KestrelFactory("Development");
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, 2 * (int)OneMiB, chunked: true);

        await AssertPayloadTooLargeAsync(response);
    }

    [Fact]
    public async Task Kestrel_applies_a_configured_override()
    {
        await using var factory = KestrelFactory("Production",
            new Dictionary<string, string?> { [RequestBodyLimitValidation.ConfigurationKey] = (2 * OneMiB).ToString() });
        using var client = factory.CreateClient();

        using var aboveDefault = await PostAsync(client, (int)OneMiB + 1, chunked: false);
        using var aboveOverride = await PostAsync(client, 2 * (int)OneMiB + 1, chunked: false);

        Assert.Equal(HttpStatusCode.NoContent, aboveDefault.StatusCode);
        await AssertPayloadTooLargeAsync(aboveOverride);
    }

    [Fact]
    public async Task The_limit_applies_only_when_a_body_is_read()
    {
        // GET-only route: the oversized body is never read, so routing answers (405), not the body limit.
        await using var factory = KestrelFactory("Development");
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, 2 * (int)OneMiB, chunked: false, path: "/health");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    private static BuildMindApiFactory KestrelFactory(string environment, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var factory = new BuildMindApiFactory(environment, settings);
        factory.UseKestrel(0);   // real Kestrel on a free loopback port
        factory.StartServer();
        return factory;
    }

    private static long? Limit(BuildMindApiFactory factory)
    {
        factory.CreateClient().Dispose();
        return factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxRequestBodySize;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, int size, bool chunked, string path = BodyRoute)
    {
        const string json = "{\"name\":\"x\",\"count\":1}";
        var bytes = Encoding.ASCII.GetBytes(json + new string(' ', size - json.Length));   // valid JSON of exact size
        HttpContent content = chunked ? new StreamContent(new MemoryStream(bytes)) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        if (chunked)
        {
            request.Headers.TransferEncodingChunked = true;
        }
        else
        {
            // Deterministic early rejection: Kestrel checks Content-Length against the limit before it asks for the body
            // (100 Continue), so the 413 comes from the real limit check while no body is in flight. Without this the
            // server closes the connection after its 413 while the client is still uploading, and the client may observe
            // a broken pipe/reset before reading the response (the intermittent failure, P-13).
            request.Headers.ExpectContinue = true;
        }

        return await client.SendAsync(request);
    }

    private static async Task AssertPayloadTooLargeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("PAYLOAD_TOO_LARGE", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(413, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.False(problem.RootElement.TryGetProperty("detail", out var detail)
            && detail.GetString()!.Contains("count", StringComparison.Ordinal));                  // no body echo
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-referrer", string.Join(",", response.Headers.GetValues("Referrer-Policy")));
    }
}

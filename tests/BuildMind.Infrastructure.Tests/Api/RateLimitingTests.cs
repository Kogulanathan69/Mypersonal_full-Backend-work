using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Api;

/// <summary>
/// STEP 05 S4-B rate limiting (lead decisions R1–R9). TestServer cases simulate the TCP peer and an authenticated
/// user with a TEST-ONLY startup filter (headers <c>X-Test-Peer</c>, <c>X-Test-User</c>), which runs before the
/// limiter exactly where authentication will run. Real-Kestrel cases use the real peer address.
/// </summary>
public sealed class RateLimitingTests
{
    private const string Plain = RateLimitTestEndpoints.Prefix + "/plain";
    private const string Auth = RateLimitTestEndpoints.Prefix + "/auth";
    private const string Expensive = RateLimitTestEndpoints.Prefix + "/expensive";
    private const string ExpensiveThrows = RateLimitTestEndpoints.Prefix + "/expensive-throws";

    // ---------- thresholds, 429 shape, exactly-once counting ----------

    [Fact]
    public async Task Default_anonymous_limit_admits_exactly_120_then_returns_429_problem_details()
    {
        await using var factory = Factory("Production");
        using var client = factory.CreateClient();

        for (var i = 0; i < 120; i++)
        {
            using var ok = await SendAsync(client, Plain, peer: "203.0.113.5");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);   // exactly 120: no double counting
        }

        using var rejected = await SendAsync(client, Plain, peer: "203.0.113.5");
        await AssertRateLimitedAsync(rejected, "203.0.113.5");
    }

    [Fact]
    public async Task Unmatched_routes_count_against_the_global_limit()
    {
        await using var factory = Factory("Development", Small());
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var missing = await SendAsync(client, "/api/v1/does-not-exist", peer: "198.51.100.7");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        using var rejected = await SendAsync(client, "/api/v1/does-not-exist", peer: "198.51.100.7");
        await AssertRateLimitedAsync(rejected, "198.51.100.7");
    }

    // ---------- partitioning ----------

    [Fact]
    public async Task Different_ipv4_peers_are_isolated_and_mapped_ipv6_shares_the_ipv4_bucket()
    {
        await using var factory = Factory("Development", Small());
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "192.0.2.10");
        using var mapped = await SendAsync(client, Plain, peer: "::ffff:192.0.2.10");
        using var other = await SendAsync(client, Plain, peer: "192.0.2.11");

        Assert.Equal(HttpStatusCode.TooManyRequests, mapped.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Ipv6_addresses_in_one_64_prefix_share_a_bucket_and_other_prefixes_are_isolated()
    {
        await using var factory = Factory("Development", Small());
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "2001:db8:1:2::1");
        using var samePrefix = await SendAsync(client, Plain, peer: "2001:db8:1:2:ffff:ffff:ffff:fffe");
        using var otherPrefix = await SendAsync(client, Plain, peer: "2001:db8:1:3::1");

        Assert.Equal(HttpStatusCode.TooManyRequests, samePrefix.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherPrefix.StatusCode);
    }

    [Fact]
    public async Task Prefix_length_128_isolates_individual_ipv6_addresses()
    {
        await using var factory = Factory("Development", Small(("RateLimiting:Ipv6PrefixLength", "128")));
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "2001:db8::1");
        using var neighbour = await SendAsync(client, Plain, peer: "2001:db8::2");

        Assert.Equal(HttpStatusCode.OK, neighbour.StatusCode);
    }

    [Fact]
    public async Task Spoofed_forwarded_headers_never_change_the_partition()
    {
        await using var factory = Factory("Development", Small());
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var ok = await SendAsync(client, Plain, peer: "192.0.2.50", forwardedFor: $"10.0.0.{i}");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var spoofed = await SendAsync(client, Plain, peer: "192.0.2.50", forwardedFor: "10.9.9.9");
        Assert.Equal(HttpStatusCode.TooManyRequests, spoofed.StatusCode);
    }

    [Fact]
    public async Task Authenticated_users_are_partitioned_by_user_not_by_address()
    {
        await using var factory = Factory("Development", Small());
        using var client = factory.CreateClient();
        var alice = Guid.NewGuid().ToString();
        var bob = Guid.NewGuid().ToString();

        for (var i = 0; i < 3; i++)
        {
            using var ok = await SendAsync(client, Plain, peer: $"192.0.2.{60 + i}", user: alice);   // three addresses
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var aliceAgain = await SendAsync(client, Plain, peer: "192.0.2.99", user: alice);
        using var bobOnAliceAddress = await SendAsync(client, Plain, peer: "192.0.2.60", user: bob);
        using var anonymousOnAliceAddress = await SendAsync(client, Plain, peer: "192.0.2.60");

        Assert.Equal(HttpStatusCode.TooManyRequests, aliceAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobOnAliceAddress.StatusCode);
        Assert.Equal(HttpStatusCode.OK, anonymousOnAliceAddress.StatusCode);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Invalid_subjects_are_treated_as_anonymous(string subject)
    {
        await using var factory = Factory("Development", Small());
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "192.0.2.70");
        using var withBadSubject = await SendAsync(client, Plain, peer: "192.0.2.70", user: subject);

        Assert.Equal(HttpStatusCode.TooManyRequests, withBadSubject.StatusCode);
    }

    [Fact]
    public async Task Anonymous_ceiling_limits_all_anonymous_traffic_but_not_authenticated_users()
    {
        await using var factory = Factory("Development", Small(
            ("RateLimiting:Anonymous:PermitsPerMinute", "60"),
            ("RateLimiting:Anonymous:GlobalCeilingPerMinute", "60")));
        using var client = factory.CreateClient();

        var admitted = 0;
        for (var i = 0; i < 70; i++)
        {
            using var response = await SendAsync(client, Plain, peer: $"10.1.{i / 250}.{i % 250 + 1}");
            if (response.StatusCode == HttpStatusCode.OK) admitted++;
        }

        using var user = await SendAsync(client, Plain, peer: "10.9.9.9", user: Guid.NewGuid().ToString());

        Assert.InRange(admitted, 60, 62);   // ceiling burst 60 (+ at most a refill during the loop)
        Assert.Equal(HttpStatusCode.OK, user.StatusCode);
    }

    // ---------- exemptions ----------

    [Fact]
    public async Task Health_readiness_swagger_and_cors_preflight_are_never_limited()
    {
        await using var factory = Factory("Development", Small(("Cors:AllowedOrigins:0", "http://localhost:4200")));
        using var client = factory.CreateClient();

        await ExhaustAsync(client, "192.0.2.80");
        for (var i = 0; i < 10; i++)
        {
            using var health = await SendAsync(client, "/health", peer: "192.0.2.80");
            using var ready = await SendAsync(client, "/health/ready", peer: "192.0.2.80");
            using var swagger = await SendAsync(client, "/swagger/v1/swagger.json", peer: "192.0.2.80");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);   // MySQL not configured, not 429
            Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        }

        using var fresh = Factory("Development", Small(("Cors:AllowedOrigins:0", "http://localhost:4200")));
        using var freshClient = fresh.CreateClient();
        for (var i = 0; i < 10; i++)
        {
            using var preflight = new HttpRequestMessage(HttpMethod.Options, Plain);
            preflight.Headers.Add("X-Test-Peer", "192.0.2.81");
            preflight.Headers.Add("Origin", "http://localhost:4200");
            preflight.Headers.Add("Access-Control-Request-Method", "GET");
            using var response = await freshClient.SendAsync(preflight);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        using var afterPreflights = await SendAsync(freshClient, Plain, peer: "192.0.2.81");
        Assert.Equal(HttpStatusCode.OK, afterPreflights.StatusCode);   // preflights consumed nothing
    }

    // ---------- auth-strict ----------

    [Fact]
    public async Task Auth_strict_policy_admits_10_per_minute_per_address()
    {
        await using var factory = Factory("Production");
        using var client = factory.CreateClient();

        for (var i = 0; i < 10; i++)
        {
            using var ok = await SendAsync(client, Auth, peer: "192.0.2.90");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rejected = await SendAsync(client, Auth, peer: "192.0.2.90");
        using var otherAddress = await SendAsync(client, Auth, peer: "192.0.2.91");
        using var plainSameAddress = await SendAsync(client, Plain, peer: "192.0.2.90");

        await AssertRateLimitedAsync(rejected, "192.0.2.90");
        Assert.Equal(HttpStatusCode.OK, otherAddress.StatusCode);
        Assert.Equal(HttpStatusCode.OK, plainSameAddress.StatusCode);   // global bucket (120) not exhausted
    }

    // ---------- expensive operations ----------

    [Fact]
    public async Task Expensive_operations_allow_two_concurrent_per_user_with_retry_after_two_seconds()
    {
        await using var factory = Factory("Development");
        using var client = factory.CreateClient();
        var gate = factory.Services.GetRequiredService<ExpensiveGate>();
        var user = Guid.NewGuid().ToString();

        var first = SendAsync(client, Expensive, peer: "192.0.2.100", user: user);
        var second = SendAsync(client, Expensive, peer: "192.0.2.100", user: user);
        await gate.WaitForEnteredAsync(2);

        using var third = await SendAsync(client, Expensive, peer: "192.0.2.100", user: user);
        var otherUser = SendAsync(client, Expensive, peer: "192.0.2.100", user: Guid.NewGuid().ToString());
        await gate.WaitForEnteredAsync(3);   // a different user is admitted while this user is at its limit

        await AssertRateLimitedAsync(third, "192.0.2.100");
        Assert.Equal("2", string.Join(",", third.Headers.GetValues("Retry-After")));

        gate.ReleaseAll();
        Assert.Equal(HttpStatusCode.OK, (await first).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otherUser).StatusCode);
    }

    [Fact]
    public async Task Expensive_operations_allow_eight_concurrent_globally()
    {
        await using var factory = Factory("Development");
        using var client = factory.CreateClient();
        var gate = factory.Services.GetRequiredService<ExpensiveGate>();

        var held = Enumerable.Range(0, 8)
            .Select(i => SendAsync(client, Expensive, peer: $"192.0.2.{110 + i}", user: Guid.NewGuid().ToString()))
            .ToArray();
        await gate.WaitForEnteredAsync(8);

        using var ninth = await SendAsync(client, Expensive, peer: "192.0.2.120", user: Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.TooManyRequests, ninth.StatusCode);

        gate.ReleaseAll();
        foreach (var response in await Task.WhenAll(held))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }

        using var afterRelease = await SendAsync(client, Expensive, peer: "192.0.2.120", user: Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, afterRelease.StatusCode);
    }

    [Fact]
    public async Task Expensive_permits_are_released_after_failures()
    {
        await using var factory = Factory("Development");
        using var client = factory.CreateClient();
        var user = Guid.NewGuid().ToString();

        for (var i = 0; i < 6; i++)   // > 2 per user: each failed request must release its permit
        {
            using var failed = await SendAsync(client, ExpensiveThrows, peer: "192.0.2.130", user: user);
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }
    }

    [Fact]
    public async Task Expensive_permits_are_released_after_client_cancellation()
    {
        await using var factory = Factory("Development");
        using var client = factory.CreateClient();
        var gate = factory.Services.GetRequiredService<ExpensiveGate>();
        var user = Guid.NewGuid().ToString();

        using var cancel = new CancellationTokenSource();
        var a = SendAsync(client, Expensive, peer: "192.0.2.140", user: user, cancel.Token);
        var b = SendAsync(client, Expensive, peer: "192.0.2.140", user: user, cancel.Token);
        await gate.WaitForEnteredAsync(2);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);

        // The aborted requests finish asynchronously on the server; their permits must come back.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpStatusCode status;
        do
        {
            var probe = SendAsync(client, Expensive, peer: "192.0.2.140", user: user);
            var winner = await Task.WhenAny(probe, gate.WaitForEnteredAsync(3));
            if (winner == probe)
            {
                using var rejected = await probe;
                status = rejected.StatusCode;
                await Task.Delay(100);
            }
            else
            {
                gate.ReleaseAll();
                using var admitted = await probe;
                status = admitted.StatusCode;
            }
        }
        while (status == HttpStatusCode.TooManyRequests && DateTime.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    // ---------- memory safety evidence ----------

    [Fact]
    public void Rejected_anonymous_requests_do_not_create_per_client_partitions()
    {
        var created = new ConcurrentBag<string>();
        var settings = SmallSettings(anonymousPerMinute: 60, burst: 1, ceilingPerMinute: 60);
        using var limiter = RateLimitingConfiguration.BuildGlobalLimiter(settings, created.Add);

        var acquired = 0;
        for (var i = 0; i < 2_000; i++)   // 2,000 distinct addresses within one ceiling window
        {
            using var lease = limiter.AttemptAcquire(Context($"10.{i / 250}.{i % 250}.1"));
            if (lease.IsAcquired) acquired++;
        }

        var perClient = created.Count(k => k.StartsWith("a4:", StringComparison.Ordinal));
        Assert.InRange(acquired, 60, 62);
        Assert.InRange(perClient, 60, 62);   // bounded by the ceiling, not by the 2,000 addresses
    }

    [Fact]
    public async Task Idle_partitions_are_evicted_by_the_framework()
    {
        var created = new ConcurrentBag<string>();
        var settings = SmallSettings(anonymousPerMinute: 60, burst: 1, ceilingPerMinute: 6000);
        using var limiter = RateLimitingConfiguration.BuildGlobalLimiter(settings, created.Add);

        limiter.AttemptAcquire(Context("10.20.30.40")).Dispose();
        Assert.Single(created, k => k == "a4:10.20.30.40");

        // Measured on .NET 10.0.15: idle limiters are disposed about 10 s after they become idle (bucket refilled).
        // A later request must therefore build a new partition.
        await Task.Delay(TimeSpan.FromSeconds(13));
        limiter.AttemptAcquire(Context("10.20.30.40")).Dispose();

        Assert.Equal(2, created.Count(k => k == "a4:10.20.30.40"));
    }

    [Theory]
    [InlineData("192.0.2.1", 64, "a4:192.0.2.1")]
    [InlineData("::ffff:192.0.2.1", 64, "a4:192.0.2.1")]
    [InlineData("2001:db8:1:2:3:4:5:6", 64, "a6:2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:3:4:5:6", 48, "a6:2001:db8:1::/48")]
    [InlineData("2001:db8::1", 128, "a6:2001:db8::1/128")]
    [InlineData("fe80::1%3", 64, "a6:fe80::/64")]
    public void Peer_keys(string address, int prefix, string expected) =>
        Assert.Equal(expected, RateLimitingConfiguration.PeerKey(IPAddress.Parse(address), prefix));

    [Fact]
    public void Missing_peer_address_uses_one_shared_bucket() =>
        Assert.Equal("a:none", RateLimitingConfiguration.PeerKey(null, 64));

    // ---------- configuration ----------

    [Theory]
    [InlineData("Production", "RateLimiting:Enabled", "false", "cannot be disabled")]
    [InlineData("Staging", "RateLimiting:Enabled", "false", "cannot be disabled")]
    [InlineData("Production", "ForwardedHeaders_Enabled", "true", "ASPNETCORE_FORWARDEDHEADERS_ENABLED")]
    [InlineData("Production", "RateLimiting:Anonymous:PermitsPerMinute", "0", "Anonymous:PermitsPerMinute")]
    [InlineData("Production", "RateLimiting:Anonymous:Burst", "0", "Anonymous:Burst")]
    [InlineData("Production", "RateLimiting:Anonymous:GlobalCeilingPerMinute", "100", "GlobalCeilingPerMinute must be at least")]
    [InlineData("Production", "RateLimiting:Authenticated:PermitsPerMinute", "100001", "Authenticated:PermitsPerMinute")]
    [InlineData("Production", "RateLimiting:AuthStrict:PermitsPerMinute", "0", "AuthStrict:PermitsPerMinute")]
    [InlineData("Production", "RateLimiting:Expensive:ConcurrentPerUser", "9", "must not exceed")]
    [InlineData("Production", "RateLimiting:Expensive:RetryAfterSeconds", "0", "RetryAfterSeconds")]
    [InlineData("Production", "RateLimiting:Ipv6PrefixLength", "47", "Ipv6PrefixLength")]
    [InlineData("Production", "RateLimiting:Ipv6PrefixLength", "129", "Ipv6PrefixLength")]
    public async Task Unsafe_or_invalid_configuration_stops_start_up(string environment, string key, string value, string expected)
    {
        await using var factory = new StartupSettingsApiFactory(environment, new Dictionary<string, string?> { [key] = value });

        var validation = ExceptionChains.RequireStartupOptionsValidationFailure(
            Record.Exception(() => factory.CreateClient()), factory.Logs);

        Assert.Equal(typeof(BuildMindRateLimitingOptions), validation.OptionsType);
        Assert.Contains(validation.Failures, f => f.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_may_disable_rate_limiting_and_enable_forwarded_headers()
    {
        await using var factory = Factory("Development", new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "false",
            ["ForwardedHeaders_Enabled"] = "true",
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < 130; i++)
        {
            using var ok = await SendAsync(client, Auth, peer: "192.0.2.150");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }
    }

    // ---------- real Kestrel ----------

    [Fact]
    public async Task Kestrel_returns_429_problem_details_and_ignores_spoofed_forwarded_headers()
    {
        await using var factory = KestrelFactory("Production", Small());
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var ok = await SendAsync(client, Plain, forwardedFor: $"10.0.0.{i}");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var spoofed = await SendAsync(client, Plain, forwardedFor: "10.9.9.9");
        await AssertRateLimitedAsync(spoofed, "127.0.0.1");

        using var health = await SendAsync(client, "/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Kestrel_shows_why_the_forwarded_headers_switch_is_refused_outside_development()
    {
        // Development only: with the framework switch on, the client-supplied X-Forwarded-For becomes the peer
        // address, so every spoofed value gets a fresh bucket. Outside Development start-up is refused (above).
        await using var factory = KestrelFactory("Development",
            Small(("ForwardedHeaders_Enabled", "true")));
        using var client = factory.CreateClient();

        for (var i = 0; i < 6; i++)
        {
            using var response = await SendAsync(client, Plain, forwardedFor: $"10.0.0.{i}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // more than the burst of 3: partitions spoofed
        }
    }

    // ---------- helpers ----------

    private static Dictionary<string, string?> Small(params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:Anonymous:PermitsPerMinute"] = "60",
            ["RateLimiting:Anonymous:Burst"] = "3",
            ["RateLimiting:Anonymous:GlobalCeilingPerMinute"] = "6000",
            ["RateLimiting:Authenticated:PermitsPerMinute"] = "60",
            ["RateLimiting:Authenticated:Burst"] = "3",
        };
        foreach (var (key, value) in extra) settings[key] = value;
        return settings;
    }

    private static BuildMindRateLimitingOptions SmallSettings(int anonymousPerMinute, int burst, int ceilingPerMinute) => new()
    {
        Anonymous = new() { PermitsPerMinute = anonymousPerMinute, Burst = burst, GlobalCeilingPerMinute = ceilingPerMinute },
    };

    private static DefaultHttpContext Context(string peer)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        context.Request.Path = "/api/v1/anything";
        return context;
    }

    private static StartupSettingsApiFactory Factory(string environment, Dictionary<string, string?>? settings = null) =>
        new(environment, settings ?? new Dictionary<string, string?>(), services =>
        {
            services.AddTransient<IStartupFilter, TestIdentityStartupFilter>();
            services.AddSingleton<IApiEndpointModule, RateLimitTestEndpoints>();
            services.AddSingleton<ExpensiveGate>();
        });

    private static StartupSettingsApiFactory KestrelFactory(string environment, Dictionary<string, string?> settings)
    {
        var factory = new StartupSettingsApiFactory(environment, settings, services =>
        {
            services.AddSingleton<IApiEndpointModule, RateLimitTestEndpoints>();
            services.AddSingleton<ExpensiveGate>();
        });
        factory.UseKestrel(0);   // real Kestrel, real loopback peer address; no test identity filter
        factory.StartServer();
        return factory;
    }

    private static async Task ExhaustAsync(HttpClient client, string peer)
    {
        for (var i = 0; i < 3; i++)
        {
            using var ok = await SendAsync(client, Plain, peer: peer);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rejected = await SendAsync(client, Plain, peer: peer);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string? peer = null,
        string? user = null, CancellationToken cancellationToken = default, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (peer is not null) request.Headers.Add("X-Test-Peer", peer);
        if (user is not null) request.Headers.Add("X-Test-User", user);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
            request.Headers.Add("Forwarded", $"for={forwardedFor}");
        }

        return client.SendAsync(request, cancellationToken);
    }

    private static async Task AssertRateLimitedAsync(HttpResponseMessage response, string peerThatMustNotLeak)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        Assert.Equal("RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.DoesNotContain(peerThatMustNotLeak, body, StringComparison.Ordinal);
        Assert.True(int.TryParse(string.Join(",", response.Headers.GetValues("Retry-After")), out var seconds) && seconds >= 1);
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", string.Join(",", response.Headers.GetValues("X-Frame-Options")));
    }

    /// <summary>TEST-ONLY: sets the peer address and an authenticated subject before the limiter runs.</summary>
    private sealed class TestIdentityStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Peer", out var peer))
                    context.Connection.RemoteIpAddress = IPAddress.Parse(peer.ToString());
                if (context.Request.Headers.TryGetValue("X-Test-User", out var user))
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test"));
                await nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>TEST-ONLY endpoints carrying the rate-limit markers (never part of the API).</summary>
internal sealed class RateLimitTestEndpoints : IApiEndpointModule
{
    public const string Prefix = "/api/v1/__ratelimit-tests";

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        var tests = apiV1.MapGroup("/__ratelimit-tests");
        tests.MapGet("/plain", () => Results.Ok());
        tests.MapGet("/auth", () => Results.Ok()).RequireRateLimiting(RateLimitingConfiguration.AuthStrictPolicy);
        tests.MapGet("/expensive", async (ExpensiveGate gate, HttpContext context) =>
        {
            gate.Entered();
            await gate.WaitAsync(context.RequestAborted);
            return Results.Ok();
        }).RequireExpensiveOperationLimit();
        tests.MapGet("/expensive-throws", IResult () => throw new InvalidOperationException("synthetic failure"))
            .RequireExpensiveOperationLimit();
    }
}

/// <summary>TEST-ONLY gate holding expensive requests in flight.</summary>
internal sealed class ExpensiveGate
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _entered;

    public void Entered() => Interlocked.Increment(ref _entered);

    public Task WaitAsync(CancellationToken cancellationToken) => _release.Task.WaitAsync(cancellationToken);

    public void ReleaseAll() => _release.TrySetResult();

    public async Task WaitForEnteredAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref _entered) < count)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Only {_entered} of {count} requests entered.");
            await Task.Delay(10);
        }
    }
}

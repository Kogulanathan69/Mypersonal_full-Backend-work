using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using BuildMind.API.Health;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BuildMind.API.Security;

/// <summary>Settings bound from <c>RateLimiting</c> (STEP 05 S4-B, lead decisions R1–R9, 2026-11-21).</summary>
public sealed class BuildMindRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>May be false only in Development.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>IPv6 addresses are grouped by this prefix (48..128); IPv4 is per address.</summary>
    public int Ipv6PrefixLength { get; set; } = 64;

    public AnonymousSettings Anonymous { get; set; } = new();

    public AuthenticatedSettings Authenticated { get; set; } = new();

    public AuthStrictSettings AuthStrict { get; set; } = new();

    public ExpensiveSettings Expensive { get; set; } = new();

    public sealed class AnonymousSettings
    {
        public int PermitsPerMinute { get; set; } = 120;

        public int Burst { get; set; } = 120;

        /// <summary>One shared bucket for all anonymous traffic; also bounds anonymous partition creation.</summary>
        public int GlobalCeilingPerMinute { get; set; } = 3000;
    }

    public sealed class AuthenticatedSettings
    {
        public int PermitsPerMinute { get; set; } = 300;

        public int Burst { get; set; } = 300;
    }

    public sealed class AuthStrictSettings
    {
        public int PermitsPerMinute { get; set; } = 10;
    }

    public sealed class ExpensiveSettings
    {
        public int ConcurrentPerUser { get; set; } = 2;

        public int GlobalConcurrent { get; set; } = 8;

        /// <summary>Concurrency limiters carry no retry time, so this fixed hint is sent instead.</summary>
        public int RetryAfterSeconds { get; set; } = 2;
    }
}

/// <summary>
/// Start-up validation. Outside Development rate limiting cannot be disabled, and the framework's
/// <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> switch is refused: it trusts forwarded headers from any sender, which
/// would let clients choose their rate-limit partition. Trusted proxies are a G11 decision.
/// </summary>
public sealed class RateLimitingOptionsValidation(IConfiguration configuration, IHostEnvironment environment)
    : IValidateOptions<BuildMindRateLimitingOptions>
{
    /// <summary>Configuration key the host reads for <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c>.</summary>
    public const string ForwardedHeadersSwitchKey = "ForwardedHeaders_Enabled";

    public const int MaxPermits = 100_000;

    public ValidateOptionsResult Validate(string? name, BuildMindRateLimitingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();
        var development = environment.IsDevelopment();
        const string p = BuildMindRateLimitingOptions.SectionName;

        if (!options.Enabled && !development)
            failures.Add($"{p}:Enabled is false; rate limiting cannot be disabled outside Development.");

        if (!development && string.Equals(configuration[ForwardedHeadersSwitchKey], "true", StringComparison.OrdinalIgnoreCase))
            failures.Add("ASPNETCORE_FORWARDEDHEADERS_ENABLED=true trusts forwarded headers from any sender and is not "
                + "permitted outside Development until trusted proxies are configured (G11).");

        Range(failures, $"{p}:Ipv6PrefixLength", options.Ipv6PrefixLength, 48, 128);
        Range(failures, $"{p}:Anonymous:PermitsPerMinute", options.Anonymous.PermitsPerMinute, 1, MaxPermits);
        Range(failures, $"{p}:Anonymous:Burst", options.Anonymous.Burst, 1, MaxPermits);
        Range(failures, $"{p}:Anonymous:GlobalCeilingPerMinute", options.Anonymous.GlobalCeilingPerMinute, 1, MaxPermits);
        Range(failures, $"{p}:Authenticated:PermitsPerMinute", options.Authenticated.PermitsPerMinute, 1, MaxPermits);
        Range(failures, $"{p}:Authenticated:Burst", options.Authenticated.Burst, 1, MaxPermits);
        Range(failures, $"{p}:AuthStrict:PermitsPerMinute", options.AuthStrict.PermitsPerMinute, 1, MaxPermits);
        Range(failures, $"{p}:Expensive:ConcurrentPerUser", options.Expensive.ConcurrentPerUser, 1, 1_000);
        Range(failures, $"{p}:Expensive:GlobalConcurrent", options.Expensive.GlobalConcurrent, 1, 10_000);
        Range(failures, $"{p}:Expensive:RetryAfterSeconds", options.Expensive.RetryAfterSeconds, 1, 300);

        if (options.Anonymous.GlobalCeilingPerMinute < options.Anonymous.PermitsPerMinute)
            failures.Add($"{p}:Anonymous:GlobalCeilingPerMinute must be at least {p}:Anonymous:PermitsPerMinute.");
        if (options.Expensive.ConcurrentPerUser > options.Expensive.GlobalConcurrent)
            failures.Add($"{p}:Expensive:ConcurrentPerUser must not exceed {p}:Expensive:GlobalConcurrent.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Range(List<string> failures, string key, int value, int min, int max)
    {
        if (value < min || value > max)
            failures.Add($"{key} is {value}; it must be between {min} and {max}.");
    }
}

/// <summary>Endpoint metadata marking a costly operation (generation, extraction, retrieval). See <see cref="RateLimitingConfiguration"/>.</summary>
public sealed class ExpensiveOperationMetadata;

/// <summary>
/// M4 rate limiting (STEP 05 S4-B). One global chained limiter, evaluated once per request by the single
/// <c>UseRateLimiter</c> middleware placed by the composition root:
/// <list type="number">
/// <item>anonymous ceiling: one shared token bucket for all anonymous requests (first, so a rejected request never
/// creates a per-client partition: anonymous partition creation is bounded by the ceiling);</item>
/// <item>per client: token bucket per validated user (<c>sub</c>) or, for anonymous requests, per TCP peer
/// address (IPv4; IPv4-mapped IPv6 normalized to IPv4; IPv6 grouped by prefix). Forwarded headers are never read;</item>
/// <item>expensive operations (endpoints marked with <see cref="RequireExpensiveOperationLimit{TBuilder}"/>): a
/// concurrency limit per client and a global concurrency limit.</item>
/// </list>
/// The named policy <see cref="AuthStrictPolicy"/> (fixed window per peer address) is attached by endpoint owners.
/// Health endpoints are exempt; Swagger is answered before routing and never reaches the limiter. Rejections are 429
/// with <c>Retry-After</c>; the body is produced by the status-code-pages Problem Details (<c>RATE_LIMITED</c>).
/// Idle partitions are disposed by the framework about 10 seconds after their limiter becomes idle (measured).
/// </summary>
public static class RateLimitingConfiguration
{
    public const string AuthStrictPolicy = "auth-strict";

    private const string ClientKeyItem = "BuildMind.RateLimit.ClientKey";

    public static IServiceCollection AddBuildMindRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<BuildMindRateLimitingOptions>()
            .Bind(configuration.GetSection(BuildMindRateLimitingOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<BuildMindRateLimitingOptions>, RateLimitingOptionsValidation>();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<BuildMindRateLimitingOptions>, ILoggerFactory>((options, settings, loggers) =>
                Configure(options, settings.Value, loggers.CreateLogger("BuildMind.RateLimiting")));

        return services;
    }

    /// <summary>Marks an endpoint as an expensive operation (per-client and global concurrency limits apply).</summary>
    public static TBuilder RequireExpensiveOperationLimit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add(endpoint => endpoint.Metadata.Add(new ExpensiveOperationMetadata()));
        return builder;
    }

    private static void Configure(RateLimiterOptions options, BuildMindRateLimitingOptions settings, ILogger logger)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        if (!settings.Enabled)
        {
            options.GlobalLimiter = null;
            options.AddPolicy(AuthStrictPolicy, _ => RateLimitPartition.GetNoLimiter("disabled"));
            return;
        }

        options.GlobalLimiter = BuildGlobalLimiter(settings);
        options.AddPolicy(AuthStrictPolicy, context => IsExempt(context)
            ? RateLimitPartition.GetNoLimiter("exempt")
            : RateLimitPartition.GetFixedWindowLimiter(
                PeerKey(context.Connection.RemoteIpAddress, settings.Ipv6PrefixLength),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.AuthStrict.PermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

        options.OnRejected = (context, _) =>
        {
            var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                : settings.Expensive.RetryAfterSeconds;   // concurrency limiters provide no retry time
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

            // No IP address, user id, query or body: only the endpoint name and the trace id.
            logger.LogInformation(
                "Request rejected by rate limiting. Endpoint: {Endpoint}. TraceId: {TraceId}. RetryAfterSeconds: {RetryAfterSeconds}.",
                context.HttpContext.GetEndpoint()?.DisplayName ?? "(none)",
                context.HttpContext.TraceIdentifier,
                seconds);
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>The chained global limiter. <paramref name="onPartitionCreated"/> is a test hook (null in production).</summary>
    internal static PartitionedRateLimiter<HttpContext> BuildGlobalLimiter(
        BuildMindRateLimitingOptions settings, Action<string>? onPartitionCreated = null)
    {
        var ceiling = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            IsExempt(context) || IsAuthenticatedKey(ClientKey(context, settings))
                ? RateLimitPartition.GetNoLimiter("none")
                : RateLimitPartition.GetTokenBucketLimiter("anonymous-ceiling", key =>
                {
                    onPartitionCreated?.Invoke(key);
                    return Bucket(settings.Anonymous.GlobalCeilingPerMinute, settings.Anonymous.GlobalCeilingPerMinute);
                }));

        var perClient = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            if (IsExempt(context))
                return RateLimitPartition.GetNoLimiter("none");

            var key = ClientKey(context, settings);
            return RateLimitPartition.GetTokenBucketLimiter(key, k =>
            {
                onPartitionCreated?.Invoke(k);
                return IsAuthenticatedKey(k)
                    ? Bucket(settings.Authenticated.PermitsPerMinute, settings.Authenticated.Burst)
                    : Bucket(settings.Anonymous.PermitsPerMinute, settings.Anonymous.Burst);
            });
        });

        var expensivePerClient = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            !IsExpensive(context)
                ? RateLimitPartition.GetNoLimiter("none")
                : RateLimitPartition.GetConcurrencyLimiter("x:" + ClientKey(context, settings), k =>
                {
                    onPartitionCreated?.Invoke(k);
                    return new ConcurrencyLimiterOptions { PermitLimit = settings.Expensive.ConcurrentPerUser, QueueLimit = 0 };
                }));

        var expensiveGlobal = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            !IsExpensive(context)
                ? RateLimitPartition.GetNoLimiter("none")
                : RateLimitPartition.GetConcurrencyLimiter("x:global", k =>
                {
                    onPartitionCreated?.Invoke(k);
                    return new ConcurrencyLimiterOptions { PermitLimit = settings.Expensive.GlobalConcurrent, QueueLimit = 0 };
                }));

        // Order matters: the first rejecting limiter stops the chain, so later partitions are not created, and
        // leases already acquired by earlier limiters in the chain are released.
        return PartitionedRateLimiter.CreateChained(ceiling, perClient, expensivePerClient, expensiveGlobal);
    }

    /// <summary>Validated user (<c>u:</c>) when exactly one authenticated subject exists; otherwise the peer address.</summary>
    internal static string ClientKey(HttpContext context, BuildMindRateLimitingOptions settings)
    {
        if (context.Items.TryGetValue(ClientKeyItem, out var cached) && cached is string key)
            return key;

        var identity = HttpContextCurrentUser.ResolveFrom(context.User);
        key = identity.UserId is Guid userId
            ? "u:" + userId.ToString("N")
            : PeerKey(context.Connection.RemoteIpAddress, settings.Ipv6PrefixLength);
        context.Items[ClientKeyItem] = key;
        return key;
    }

    /// <summary>Partition key from the TCP peer address only (never from forwarded headers).</summary>
    internal static string PeerKey(IPAddress? address, int ipv6PrefixLength)
    {
        if (address is null)
            return "a:none";

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return "a4:" + address;

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return "a:other";

        var bytes = address.GetAddressBytes();   // scope id is dropped
        for (var bit = ipv6PrefixLength; bit < 128; bit++)
            bytes[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        return "a6:" + new IPAddress(bytes) + "/" + ipv6PrefixLength.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsAuthenticatedKey(string key) => key.StartsWith("u:", StringComparison.Ordinal);

    private static bool IsExpensive(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ExpensiveOperationMetadata>() is not null;

    private static bool IsExempt(HttpContext context) =>
        context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || context.Request.Path.Equals(Readiness.Path, StringComparison.OrdinalIgnoreCase);

    /// <summary>Token bucket refilled every second when the rate is a multiple of 60, otherwise once per minute.</summary>
    private static TokenBucketRateLimiterOptions Bucket(int permitsPerMinute, int burst) => new()
    {
        TokenLimit = burst,
        TokensPerPeriod = permitsPerMinute % 60 == 0 ? permitsPerMinute / 60 : permitsPerMinute,
        ReplenishmentPeriod = permitsPerMinute % 60 == 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}

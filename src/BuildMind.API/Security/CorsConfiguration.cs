namespace BuildMind.API.Security;

/// <summary>
/// Browser cross-origin policy for the BuildMind frontend (decision C6, design approved 2026-11-21).
/// </summary>
/// <remarks>
/// Origins come only from <c>Cors:AllowedOrigins</c> (environment-specific configuration). Each entry must be an
/// exact <c>scheme://host[:port]</c> origin: no wildcards, paths, queries, fragments or user information. Outside
/// Development only <c>https</c> origins are accepted. Credentials (cookies) are never allowed: the API uses bearer
/// tokens in the <c>Authorization</c> header. When no origin is configured, no CORS policy is registered and the
/// API answers same-origin only.
/// </remarks>
public static class CorsConfiguration
{
    public const string SectionName = "Cors";
    public const string AllowedOriginsKey = SectionName + ":AllowedOrigins";
    public const string PolicyName = "BuildMindFrontend";

    /// <summary>Methods used by the current public product API.</summary>
    public static readonly IReadOnlyList<string> AllowedMethods = ["GET", "POST", "PUT", "PATCH"];

    /// <summary>Request headers the frontend needs to send.</summary>
    public static readonly IReadOnlyList<string> AllowedHeaders = ["Authorization", "Content-Type"];

    /// <summary>Response headers the frontend may read (created-resource location).</summary>
    public static readonly IReadOnlyList<string> ExposedHeaders = ["Location"];

    public static readonly TimeSpan PreflightMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>Registers the policy; returns false (and registers nothing) when no origin is configured.</summary>
    public static bool AddBuildMindCors(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configured = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];
        if (configured.Length == 0)
        {
            return false;
        }

        var origins = NormalizeOrigins(configured, allowHttp: environment.IsDevelopment());

        services.AddCors(options => options.AddPolicy(PolicyName, policy => policy
            .WithOrigins([.. origins])
            .WithMethods([.. AllowedMethods])
            .WithHeaders([.. AllowedHeaders])
            .WithExposedHeaders([.. ExposedHeaders])
            .DisallowCredentials()
            .SetPreflightMaxAge(PreflightMaxAge)));

        return true;
    }

    /// <summary>Validates and normalizes origins; throws with the offending entries (origins are not secrets).</summary>
    public static IReadOnlyList<string> NormalizeOrigins(IEnumerable<string?> configured, bool allowHttp)
    {
        ArgumentNullException.ThrowIfNull(configured);

        var origins = new List<string>();
        var failures = new List<string>();
        foreach (var raw in configured)
        {
            var value = raw?.Trim() ?? string.Empty;
            if (TryNormalize(value, allowHttp, out var origin, out var reason))
            {
                if (!origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    origins.Add(origin);
                }
            }
            else
            {
                failures.Add($"'{value}' {reason}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                AllowedOriginsKey + " is invalid: " + string.Join("; ", failures) + ".");
        }

        return origins;
    }

    private static bool TryNormalize(string value, bool allowHttp, out string origin, out string reason)
    {
        origin = string.Empty;

        if (value.Length == 0)
        {
            reason = "is empty";
            return false;
        }

        if (value.Contains('*', StringComparison.Ordinal))
        {
            reason = "contains a wildcard";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            reason = "is not an absolute origin";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp))
        {
            reason = allowHttp ? "must use http or https" : "must use https outside Development";
            return false;
        }

        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0)
            || value.EndsWith('/'))
        {
            reason = "must be scheme://host[:port] without path, query, fragment or user information";
            return false;
        }

        origin = uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.IdnHost}"
            : $"{uri.Scheme}://{uri.IdnHost}:{uri.Port}";
        reason = string.Empty;
        return true;
    }
}

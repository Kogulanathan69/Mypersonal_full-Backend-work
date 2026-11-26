using System.Net;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Options;

namespace BuildMind.API.Configuration;

/// <summary>
/// Start-up validation of host-header filtering (STEP 05 hosting decisions H1–H4, 2026-11-21).
/// </summary>
/// <remarks>
/// <para>The framework's host-filtering middleware reads <c>AllowedHosts</c> (semicolon-separated) and, when the key
/// is missing or empty, silently falls back to <c>*</c> (every Host accepted). This validator therefore checks both
/// the raw configuration value and the effective <see cref="HostFilteringOptions.AllowedHosts"/> list.</para>
/// <para>Outside Development (Testing, Staging, Production, any custom environment) every entry must be an exact
/// host name, IPv4 address or bracketed IPv6 address: no <c>*</c>, no subdomain wildcard, no scheme, port, path,
/// query, fragment or user information. Development is unrestricted, so a developer may set <c>*</c> locally.
/// Failures name the key and the offending entries (host names are not secrets).</para>
/// <para>Deployment host names are supplied by deployment configuration (G11); none are hard-coded here.</para>
/// </remarks>
public sealed class AllowedHostsValidation(IConfiguration configuration, IHostEnvironment environment)
    : IValidateOptions<HostFilteringOptions>
{
    public const string ConfigurationKey = "AllowedHosts";

    public ValidateOptionsResult Validate(string? name, HostFilteringOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        var raw = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            failures.Add($"{ConfigurationKey} is missing or empty; outside Development it must list the exact host names "
                + "this API serves (unset means every host is accepted).");
        }

        var entries = options.AllowedHosts ?? [];
        if (entries.Count == 0 && failures.Count == 0)
        {
            failures.Add($"{ConfigurationKey} resolved to an empty host list.");
        }

        foreach (var entry in entries)
        {
            if (!IsExactHost(entry, out var reason))
            {
                failures.Add($"{ConfigurationKey} entry '{entry}' {reason}.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Exact host name, IPv4 address or bracketed IPv6 address; nothing else.</summary>
    internal static bool IsExactHost(string? entry, out string reason)
    {
        var value = entry?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            reason = "is empty";
            return false;
        }

        if (value.Contains('*', StringComparison.Ordinal))
        {
            reason = value == "*" ? "allows every host (unrestricted)" : "is a wildcard (not permitted outside Development)";
            return false;
        }

        if (value.Contains("://", StringComparison.Ordinal) || value.IndexOfAny(['/', '?', '#', '@', '\\']) >= 0)
        {
            reason = "must be a host name only (no scheme, path, query, fragment or user information)";
            return false;
        }

        if (value.StartsWith('['))
        {
            if (value.EndsWith(']') && IPAddress.TryParse(value[1..^1], out var v6)
                && v6.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                reason = string.Empty;
                return true;
            }

            reason = "must be a bracketed IPv6 address without a port";
            return false;
        }

        if (value.Contains(':', StringComparison.Ordinal))
        {
            reason = "must not include a port (IPv6 addresses need brackets)";
            return false;
        }

        if (Uri.CheckHostName(value) is UriHostNameType.Dns or UriHostNameType.IPv4)
        {
            reason = string.Empty;
            return true;
        }

        reason = "is not a valid host name or IP address";
        return false;
    }
}

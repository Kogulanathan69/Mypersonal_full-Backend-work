using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace BuildMind.API.Configuration;

/// <summary>
/// Start-up validation of the global request-body limit (STEP 05 S4-A, decisions S4-1..S4-3, 2026-11-21).
/// </summary>
/// <remarks>
/// <para>The limit is Kestrel's <c>Limits.MaxRequestBodySize</c>. The host does not bind <c>Kestrel:Limits</c> from
/// configuration, so the composition root applies <c>Kestrel:Limits:MaxRequestBodySize</c> explicitly through
/// <see cref="ReadConfiguredLimit"/>: 1 MiB when the key is absent (never Kestrel's 30,000,000-byte default); a value that
/// is not a whole number of bytes stops start-up.</para>
/// <para>Outside Development (Testing, Staging, Production, any other environment) the limit must be set and lie in
/// 1 byte .. 8 MiB. A larger body is only allowed per endpoint (request-size metadata on that endpoint), approved by
/// the owning member and M4 (S4-2), never by raising the global limit. In Development the global value is not
/// restricted except that it must not be zero.</para>
/// </remarks>
public sealed class RequestBodyLimitValidation(IHostEnvironment environment) : IValidateOptions<KestrelServerOptions>
{
    public const string ConfigurationKey = "Kestrel:Limits:MaxRequestBodySize";

    /// <summary>S4-1: default global limit (1 MiB).</summary>
    public const long DefaultMaxRequestBodySize = 1L * 1024 * 1024;

    /// <summary>S4-3: largest global limit accepted outside Development (8 MiB).</summary>
    public const long MaxConfigurableOutsideDevelopment = 8L * 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, KestrelServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failure = Check(options.Limits.MaxRequestBodySize, environment.IsDevelopment());
        return failure is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failure);
    }

    /// <summary>The configured global limit in bytes; 1 MiB when the key is absent or empty.</summary>
    /// <exception cref="InvalidOperationException">The value is not a non-negative whole number of bytes.</exception>
    public static long ReadConfiguredLimit(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var raw = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultMaxRequestBodySize;
        }

        if (long.TryParse(raw.Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new InvalidOperationException(
            $"{ConfigurationKey} must be a whole number of bytes (for example {DefaultMaxRequestBodySize}).");
    }

    /// <summary>Returns the failure message, or null when the value is acceptable.</summary>
    internal static string? Check(long? maxRequestBodySize, bool isDevelopment)
    {
        if (maxRequestBodySize == 0)
        {
            return $"{ConfigurationKey} is 0, which rejects every request body.";
        }

        if (isDevelopment)
        {
            return null;
        }

        if (maxRequestBodySize is null)
        {
            return $"{ConfigurationKey} is unlimited; outside Development it must be set (1..{MaxConfigurableOutsideDevelopment} bytes).";
        }

        if (maxRequestBodySize > MaxConfigurableOutsideDevelopment)
        {
            return $"{ConfigurationKey} is {maxRequestBodySize} bytes, above the {MaxConfigurableOutsideDevelopment}-byte (8 MiB) maximum "
                + "outside Development. Raise limits per endpoint instead (owner and M4 approval).";
        }

        return null;
    }
}

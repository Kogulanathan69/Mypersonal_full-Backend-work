using BuildMind.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuildMind.API.Configuration;

/// <summary>
/// Start-up validation adapter for <see cref="ServicesOptions"/> (decision C8). Outside Development an enabled RAG
/// service requires an internal auth token (G11 MVP, fail closed); in Production every enabled internal service must use
/// https or loopback with no plaintext exception; synthetic constraint evidence is allowed only in Development/Testing.
/// </summary>
public sealed class ServicesOptionsValidation(IHostEnvironment environment) : IValidateOptions<ServicesOptions>
{
    /// <summary>Isolated automated-test environment name (synthetic evidence allowed, never real users or data).</summary>
    public const string TestingEnvironmentName = "Testing";

    public ValidateOptionsResult Validate(string? name, ServicesOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var development = environment.IsDevelopment();
        var failures = options.Validate(
            requireRagAuthToken: !development,
            enforceSecureTransport: !development,
            production: environment.IsProduction(),
            isolatedTestEnvironment: development || environment.IsEnvironment(TestingEnvironmentName));
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

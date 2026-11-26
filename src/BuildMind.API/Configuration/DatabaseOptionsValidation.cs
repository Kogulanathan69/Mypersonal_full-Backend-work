using BuildMind.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuildMind.API.Configuration;

/// <summary>
/// Start-up validation adapter for <see cref="DatabaseOptions"/> (BE-M4-D02a). The connection string is read
/// from <c>ConnectionStrings:BuildMind</c> only to validate it; failures name keys and never echo values.
/// Outside Development the server identity must be verified (<c>SslMode=VerifyFull</c>).
/// </summary>
public sealed class DatabaseOptionsValidation(IConfiguration configuration, IHostEnvironment environment)
    : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = options.Validate(
            configuration.GetConnectionString(DatabaseOptions.ConnectionStringName),
            requireServerIdentityVerification: !environment.IsDevelopment());
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

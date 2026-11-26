using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.Persistence.Errors;

/// <summary>
/// Secret-safe structured logging of classified persistence failures. Replaces the EF Core failure events
/// suppressed in <see cref="MySqlContextOptions"/>: it records the operation, kind, provider error number and
/// constraint name, never provider messages, SQL, parameter values or connection details.
/// </summary>
public static partial class PersistenceLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Warning,
        Message = "Persistence operation {Operation} failed: {FailureKind} (provider error {ProviderErrorNumber}, constraint {ConstraintName}, code {DefaultCode}, transient {IsTransient})")]
    private static partial void LogFailure(
        ILogger logger,
        string operation,
        PersistenceFailureKind failureKind,
        int? providerErrorNumber,
        string? constraintName,
        string defaultCode,
        bool isTransient);

    /// <summary>Logs a classified failure. <paramref name="operation"/> must be a fixed code-defined name.</summary>
    public static void Failure(ILogger logger, string operation, PersistenceFailure failure)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(failure);

        LogFailure(
            logger,
            operation,
            failure.Kind,
            failure.ProviderErrorNumber,
            failure.ConstraintName,
            failure.DefaultCode,
            failure.IsTransient);
    }
}

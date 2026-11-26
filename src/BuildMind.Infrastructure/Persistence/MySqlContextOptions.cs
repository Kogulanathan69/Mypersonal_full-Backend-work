using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BuildMind.Infrastructure.Persistence;

/// <summary>Single place where the MySQL provider is configured for runtime and design-time contexts.</summary>
internal static class MySqlContextOptions
{
    /// <summary>Migrations live in this assembly under <c>Persistence/Migrations</c> (none exist yet).</summary>
    public static readonly string MigrationsAssembly = typeof(BuildMindDbContext).Assembly.GetName().Name!;

    /// <summary>
    /// EF Core failure events whose log text includes raw provider exception messages or connection details.
    /// <see cref="RelationalEventId.ConnectionError"/> (added in BE-M4-D04) logs the database name and server host
    /// on every failed connection, for example on each readiness probe while MySQL is down.
    /// </summary>
    public static readonly EventId[] SuppressedFailureEvents =
    [
        RelationalEventId.ConnectionError,
        RelationalEventId.CommandError,
        CoreEventId.SaveChangesFailed,
        CoreEventId.QueryIterationFailed,
        CoreEventId.OptimisticConcurrencyException,
    ];

    public static DbContextOptionsBuilder Apply(
        DbContextOptionsBuilder builder,
        string connectionString,
        int commandTimeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Target: MySQL 8.4 LTS (compatibility UNVERIFIED until the provider spike passes).
        // No retrying execution strategy: a transaction that may have committed must never be replayed.
        builder.UseMySQL(connectionString, mysql =>
        {
            mysql.CommandTimeout(commandTimeoutSeconds);
            mysql.MigrationsAssembly(MigrationsAssembly);
        });

        // Parameter values and provider error details must never reach logs (C10).
        builder.EnableSensitiveDataLogging(false);
        builder.EnableDetailedErrors(false);

        // These events log the provider exception message, which for MySQL can quote stored values
        // (for example "Duplicate entry '<email>' for key ..."). Adapters log PersistenceErrorClassifier
        // results instead, which carry only kind, error number and constraint name.
        builder.ConfigureWarnings(warnings => warnings.Ignore(SuppressedFailureEvents));

        // STEP 10-E: serialize connection opening (MySql.Data static TLS-state race; see MySqlConnectionOpenGate).
        builder.AddInterceptors(Connections.MySqlConnectionOpenGate.Interceptor);

        return builder;
    }
}

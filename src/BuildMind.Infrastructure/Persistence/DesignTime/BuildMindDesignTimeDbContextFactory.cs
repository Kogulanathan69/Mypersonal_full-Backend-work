using BuildMind.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BuildMind.Infrastructure.Persistence.DesignTime;

/// <summary>
/// Design-time factory used only by <c>dotnet ef</c> (repository-local tool). The migration connection string
/// is taken exclusively from the <see cref="ConnectionVariable"/> environment variable: there is no fallback to
/// application configuration, User Secrets or any application database, and nothing is migrated automatically.
/// </summary>
public sealed class BuildMindDesignTimeDbContextFactory : IDesignTimeDbContextFactory<BuildMindDbContext>
{
    /// <summary>Environment variable holding the migration-identity connection string (never the runtime one).</summary>
    public const string ConnectionVariable = "BUILDMIND_MIGRATIONS_CONNECTION";

    private const int DesignTimeCommandTimeoutSeconds = 60;

    public BuildMindDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable(ConnectionVariable));

    internal static BuildMindDbContext Create(string? connectionString)
    {
        var failures = DatabaseOptions.ValidateConnectionString(connectionString, ConnectionVariable);
        if (failures.Count > 0)
        {
            // Failures name the variable only; the connection string is never echoed.
            throw new InvalidOperationException(
                "Design-time database configuration is invalid: " + string.Join(" ", failures));
        }

        var builder = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(builder, connectionString!, DesignTimeCommandTimeoutSeconds);
        return new BuildMindDbContext(builder.Options);
    }
}

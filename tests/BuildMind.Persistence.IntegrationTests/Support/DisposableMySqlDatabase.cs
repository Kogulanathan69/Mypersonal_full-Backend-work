using System.Text.RegularExpressions;
using BuildMind.Infrastructure.Configuration;
using MySql.Data.MySqlClient;
using BuildMind.Infrastructure.Persistence.Connections;

namespace BuildMind.Persistence.IntegrationTests.Support;

/// <summary>Environment contract for the spike. Missing configuration FAILS the test (never skips it).</summary>
internal static class IntegrationEnvironment
{
    /// <summary>Connection string of the disposable-test identity (<c>bm_test</c>); any database name in it is ignored.</summary>
    public const string TestConnectionVariable = "BUILDMIND_TEST_MYSQL";

    /// <summary>Connection string of the least-privilege runtime identity (<c>bm_runtime</c>), used only for privilege probes.</summary>
    public const string RuntimeConnectionVariable = "BUILDMIND_TEST_MYSQL_RUNTIME";

    public static string Require(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{variable} is not set. Database compatibility tests need a disposable MySQL 8.4 server (see README.md).");
        }

        return value;
    }
}

/// <summary>
/// Creates a uniquely named <c>buildmind_test_&lt;guid&gt;</c> database for one test and drops it afterwards.
/// It never connects to, modifies or drops any database that it did not create.
/// </summary>
internal sealed partial class DisposableMySqlDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;

    private DisposableMySqlDatabase(string serverConnectionString, string name, string connectionString)
    {
        _serverConnectionString = serverConnectionString;
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<DisposableMySqlDatabase> CreateAsync()
    {
        var builder = new MySqlConnectionStringBuilder(IntegrationEnvironment.Require(IntegrationEnvironment.TestConnectionVariable));
        if (string.Equals(builder.UserID?.Trim(), "root", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The test identity must not be root.");
        }

        var name = "buildmind_test_" + Guid.NewGuid().ToString("N");
        builder.Database = string.Empty;
        var server = builder.ConnectionString;

        await ExecuteAsync(server, $"CREATE DATABASE `{RequireDisposableName(name)}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");

        builder.Database = name;
        var connection = builder.ConnectionString;
        var failures = DatabaseOptions.ValidateConnectionString(connection, IntegrationEnvironment.TestConnectionVariable);
        if (failures.Count > 0)
        {
            await ExecuteAsync(server, $"DROP DATABASE IF EXISTS `{name}`;");
            throw new InvalidOperationException(string.Join(" ", failures));
        }

        return new DisposableMySqlDatabase(server, name, connection);
    }

    public async ValueTask DisposeAsync()
    {
        // Each disposable database has its own connection string, hence its own provider pool. Without clearing it,
        // idle pooled server connections to the dropped database stay open until the provider's idle timeout and
        // accumulate across the suite (observed: max_connections 151 exhausted -> connection-gate timeouts).
        await using (var pooled = new MySqlConnection(ConnectionString))
        {
            MySqlConnection.ClearPool(pooled);
        }

        await ExecuteAsync(_serverConnectionString, $"DROP DATABASE IF EXISTS `{RequireDisposableName(Name)}`;");
    }

    private static string RequireDisposableName(string name) =>
        DisposableName().IsMatch(name)
            ? name
            : throw new InvalidOperationException("Refusing to touch a database that is not a disposable test database.");

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await MySqlConnectionOpenGate.OpenAsync(connection);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [GeneratedRegex("^buildmind_test_[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex DisposableName();
}

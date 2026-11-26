using BuildMind.API.Health;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;
using Xunit.Abstractions;
using BuildMind.Infrastructure.Persistence.Connections;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// BE-M4-D04: the real <see cref="MySqlReadinessCheck"/> against a disposable MySQL 8.4 database. Readiness must
/// connect, report Healthy, and never write, migrate or leave connections behind.
/// </summary>
public sealed class ReadinessLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RD01_reachable_database_is_healthy_and_readiness_writes_nothing()
    {
        await using var database = await DisposableMySqlDatabase.CreateAsync();
        using var provider = Services(database.ConnectionString);
        var check = ActivatorUtilities.CreateInstance<MySqlReadinessCheck>(provider);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("reachable", result.Data["reason"]);
        Assert.Equal(0L, await TableCountAsync(database)); // no schema, no migration history, no writes
    }

    [Fact]
    public async Task RD02_repeated_checks_do_not_leak_connections()
    {
        await using var database = await DisposableMySqlDatabase.CreateAsync();
        using var provider = Services(database.ConnectionString);
        var before = await OwnConnectionsAsync(database);

        for (var i = 0; i < 10; i++)
        {
            var check = ActivatorUtilities.CreateInstance<MySqlReadinessCheck>(provider);
            Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
        }

        MySqlConnection.ClearAllPools();
        var after = await OwnConnectionsAsync(database);
        output.WriteLine($"Connections to the disposable database before/after: {before}/{after}");
        Assert.True(after <= before, $"Connections grew from {before} to {after}.");
    }

    private static ServiceProvider Services(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<DatabaseOptions>>(Options.Create(new DatabaseOptions { Enabled = true }));
        services.AddSingleton<ReadinessProbeGate>();
        services.AddDbContext<BuildMindDbContext>(builder => MySqlContextOptions.Apply(builder, connectionString, 30));
        return services.BuildServiceProvider();
    }

    private static async Task<long> TableCountAsync(DisposableMySqlDatabase database) =>
        Convert.ToInt64(await ScalarAsync(database, "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE();"));

    private static async Task<long> OwnConnectionsAsync(DisposableMySqlDatabase database) =>
        Convert.ToInt64(await ScalarAsync(database,
            "SELECT COUNT(*) - 1 FROM information_schema.processlist WHERE db = DATABASE();")); // minus this query's own connection

    private static async Task<object?> ScalarAsync(DisposableMySqlDatabase database, string sql)
    {
        await using var connection = new MySqlConnection(database.ConnectionString);
        await MySqlConnectionOpenGate.OpenAsync(connection);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}

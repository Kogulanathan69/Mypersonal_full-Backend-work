using System.Net;
using System.Net.Sockets;
using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.Connections;
using BuildMind.Persistence.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Abstractions;

namespace BuildMind.Persistence.IntegrationTests;

/// <summary>
/// STEP 10-E regression for the CI run #55 failure: MySql.Data 26.10.0 mutates static TLS-state dictionaries
/// outside its lock after each TLS handshake. Parallel connection INITIALISATION is driven hard here: pooling is off,
/// so every open is a new physical connection with its own TLS handshake. It mixes the production EF Core path (gate
/// interceptor) with direct opens (gate helper). Every connection must open AND prove it negotiated TLS
/// (<c>Ssl_cipher</c> non-empty): the fix serializes opening and never weakens TLS.
/// </summary>
public sealed class MySqlTlsConcurrencyLiveTests(ITestOutputHelper output)
{
    private const int Rounds = 6;
    private const int ParallelOpens = 24;

    [Fact(Timeout = 180_000)]
    public async Task SSL01_parallel_tls_connection_initialisation_never_corrupts_provider_state()
    {
        var connectionString = new MySqlConnectionStringBuilder(IntegrationEnvironment.Require(IntegrationEnvironment.TestConnectionVariable))
        {
            Pooling = false,
        }.ConnectionString;

        for (var round = 0; round < Rounds; round++)
        {
            var ciphers = await Task.WhenAll(Enumerable.Range(0, ParallelOpens).Select(i =>
                i % 2 == 0 ? OpenThroughEfAsync(connectionString) : OpenDirectAsync(connectionString)));

            Assert.All(ciphers, cipher => Assert.False(string.IsNullOrWhiteSpace(cipher), "connection did not negotiate TLS"));
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task SSL02_every_ef_core_open_is_intercepted_including_back_to_back_opens()
    {
        // STEP 10-F: EF Core only calls interceptors when ShouldLogConnectionOpen is true. Prove that every open of
        // the SAME context (the per-context logger's suppression window) still reaches the gate.
        var (interceptor, gate) = Dedicated();
        await using var context = ContextWith(interceptor, TestConnectionString(pooling: true));

        for (var i = 0; i < 3; i++)
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.CloseConnectionAsync();
        }

        Assert.Equal(3, interceptor.Acquisitions);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact(Timeout = 60_000)]
    public async Task SSL03_a_stalled_then_failed_open_releases_its_gate_without_touching_the_process_gate()
    {
        // STEP 10-H: a server that accepts TCP but never sends the MySQL greeting. In PR #30's CI the provider ignored
        // both the 500 ms token and ConnectionTimeout=5 here, and because the 10-F version used the PRODUCTION gate it
        // held the process-wide gate for the whole run (47 other tests failed). This version is isolated: a dedicated
        // gate only, and it never depends on the provider honouring cancellation. After a bounded wait the server side
        // is closed, which forces the stalled open to fail.
        var (interceptor, gate) = Dedicated();
        var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;
        var target = new MySqlConnectionStringBuilder(TestConnectionString(pooling: false))
        {
            Server = "127.0.0.1",
            Port = (uint)port,
            ConnectionTimeout = 5,
        }.ConnectionString;
        var context = IsolatedContext(interceptor, target);
        var connection = context.Database.GetDbConnection();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        TcpClient? serverSide = null;
        var opening = context.Database.OpenConnectionAsync(cancel.Token);
        try
        {
            serverSide = await silent.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var honouredCancellation = await Task.WhenAny(opening, Task.Delay(TimeSpan.FromSeconds(3))) == opening;
            output.WriteLine($"Provider honoured the 500 ms cancellation within 3 s: {honouredCancellation}");

            serverSide.Client.Close(); // force the stalled handshake to fail
            await Task.WhenAny(opening, Task.Delay(TimeSpan.FromSeconds(20)));

            Assert.True(opening.IsCompleted, "the stalled open did not end after the server side was closed");
            Assert.True(opening.IsFaulted || opening.IsCanceled, "an open against a silent server cannot succeed");
            Assert.Equal(1, interceptor.Acquisitions);
            Assert.False(gate.IsHeldBy(connection));
            Assert.Equal(1, gate.AvailableSlots);
        }
        finally
        {
            // Guaranteed, bounded cleanup on every path (including a failed accept or assertion): close both socket
            // ends, then wait a bounded time for the provider open to end. The context is disposed only once the open
            // has ended, so disposal can never block on an in-flight handshake.
            serverSide?.Dispose();
            silent.Stop();
            silent.Dispose();
            await Task.WhenAny(opening, Task.Delay(TimeSpan.FromSeconds(20)));
            _ = opening.Exception; // observe the expected failure
            if (opening.IsCompleted)
            {
                await context.DisposeAsync();
            }
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task SSL04_a_failed_production_open_is_followed_by_a_successful_one()
    {
        // STEP 10-H: a refused open through the PRODUCTION configuration (process-wide gate), then real opens through
        // both the EF Core and direct paths must succeed: the gate recovers after failure.
        var refused = new MySqlConnectionStringBuilder(TestConnectionString(pooling: false))
        {
            Server = "127.0.0.1",
            Port = (uint)FreeLoopbackPort(),
            ConnectionTimeout = 3,
        }.ConnectionString;
        await using (var failing = BuildMindMigrationTests.CreateContext(refused))
        {
            Assert.NotNull(await Record.ExceptionAsync(() => failing.Database.OpenConnectionAsync()));
        }

        var ef = await OpenThroughEfAsync(TestConnectionString(pooling: false));
        var direct = await OpenDirectAsync(TestConnectionString(pooling: false));

        Assert.False(string.IsNullOrWhiteSpace(ef));
        Assert.False(string.IsNullOrWhiteSpace(direct));
    }

    private static (SerializedOpenInterceptor Interceptor, OpenGate Gate) Dedicated()
    {
        var gate = new OpenGate();
        return (new SerializedOpenInterceptor(gate), gate);
    }

    private static BuildMindDbContext ContextWith(SerializedOpenInterceptor interceptor, string connectionString)
    {
        // Production configuration (includes the process-wide gate) plus a dedicated, observable interceptor.
        var builder = new DbContextOptionsBuilder<BuildMindDbContext>();
        MySqlContextOptions.Apply(builder, connectionString, 30);
        builder.AddInterceptors(interceptor);
        return new BuildMindDbContext(builder.Options);
    }

    private static BuildMindDbContext IsolatedContext(SerializedOpenInterceptor interceptor, string connectionString) =>
        // Deliberately NOT MySqlContextOptions.Apply: this context must never touch the process-wide gate.
        new(new DbContextOptionsBuilder<BuildMindDbContext>()
            .UseMySQL(connectionString)
            .AddInterceptors(interceptor)
            .Options);

    private static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string TestConnectionString(bool pooling) =>
        new MySqlConnectionStringBuilder(IntegrationEnvironment.Require(IntegrationEnvironment.TestConnectionVariable))
        {
            Pooling = pooling,
        }.ConnectionString;

    private static async Task<string?> OpenThroughEfAsync(string connectionString)
    {
        await using var context = BuildMindMigrationTests.CreateContext(connectionString);
        await context.Database.OpenConnectionAsync();
        try
        {
            return await CipherAsync((MySqlConnection)context.Database.GetDbConnection());
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<string?> OpenDirectAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await MySqlConnectionOpenGate.OpenAsync(connection);
        return await CipherAsync(connection);
    }

    private static async Task<string?> CipherAsync(MySqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW SESSION STATUS LIKE 'Ssl_cipher'";
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? reader.GetString(1) : null;
    }
}

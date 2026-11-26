// RH09 isolated provider comparison: MySql.Data 26.10.0 vs MySqlConnector 2.4.0, ADO.NET only, same VerifyFull TLS
// connection string (BUILDMIND_TEST_MYSQL, restricted test account). Every scenario runs under a 20 s watchdog so a hang
// is RECORDED, never waited on; the process exits explicitly at the end (a hung provider thread cannot block it).
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;

var cs = Environment.GetEnvironmentVariable("BUILDMIND_TEST_MYSQL") ?? throw new InvalidOperationException("BUILDMIND_TEST_MYSQL missing");
var results = new List<object>();
var providers = new (string Name, Func<string, DbConnection> Create)[]
{
    ("MySql.Data 26.10.0", s => new MySql.Data.MySqlClient.MySqlConnection(s)),
    ("MySqlConnector 2.4.0", s => new MySqlConnector.MySqlConnection(s)),
};

async Task<(string Outcome, double Seconds, string Detail)> Watch(Func<Task<string>> scenario)
{
    var sw = Stopwatch.StartNew();
    var task = Task.Run(scenario);
    var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
    if (done != task) return ("HANG", sw.Elapsed.TotalSeconds, "no completion within 20 s");
    try { return ("COMPLETED", sw.Elapsed.TotalSeconds, await task); }
    catch (Exception e) { return ("EXCEPTION", sw.Elapsed.TotalSeconds, $"{e.GetType().Name}: {Short(e.Message)}"); }
}

static string Short(string s) => s.Length > 140 ? s[..140] : s;

async Task<object> Scalar(DbConnection c, string sql, int timeout = 30, CancellationToken token = default)
{
    await using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    cmd.CommandTimeout = timeout;
    return await cmd.ExecuteScalarAsync(token) ?? "null";
}

foreach (var (name, create) in providers)
{
    var p = new Dictionary<string, object> { ["provider"] = name };
    DbConnection? shared = null;
    try
    {
        shared = create(cs);
        await shared.OpenAsync();
        p["tls"] = (await Scalar(shared, "SHOW SESSION STATUS LIKE 'Ssl_version'") ?? "").ToString()!;
        await using (var v = shared.CreateCommand())
        {
            v.CommandText = "SHOW SESSION STATUS LIKE 'Ssl_version'";
            await using var r = await v.ExecuteReaderAsync();
            p["tls"] = await r.ReadAsync() ? r.GetString(1) : "none";
        }
    }
    catch (Exception e) { p["open"] = $"{e.GetType().Name}: {Short(e.Message)}"; results.Add(p); continue; }

    p["S1_command_timeout"] = await Watch(async () => $"returned {await Scalar(shared, "SELECT SLEEP(5)", timeout: 1)}");
    p["S2_reuse_same_connection"] = await Watch(async () => $"state={shared.State} select1={await Scalar(shared, "SELECT 1")}");
    p["S3_new_pooled_connection"] = await Watch(async () =>
    {
        await using var c = create(cs);
        await c.OpenAsync();
        return $"select1={await Scalar(c, "SELECT 1")}";
    });
    p["S4_server_orphans"] = await Watch(async () =>
    {
        await using var c = create(cs);
        await c.OpenAsync();
        return $"sleeping_queries={await Scalar(c, "SELECT COUNT(*) FROM information_schema.processlist WHERE info LIKE 'SELECT SLEEP(5)%'")}";
    });
    p["S5_token_cancellation"] = await Watch(async () =>
    {
        await using var c = create(cs);
        await c.OpenAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        return $"returned {await Scalar(c, "SELECT SLEEP(5)", 30, cts.Token)}";
    });
    // Containment candidates: the SERVER ends the statement (normal error) before the client timeout (30 s) fires.
    p["S7_server_max_execution_time"] = await Watch(async () =>
    {
        await using var c = create(cs);
        await c.OpenAsync();
        await Scalar(c, "SET SESSION max_execution_time = 1000");
        string first;
        try { first = $"returned {await Scalar(c, "SELECT BENCHMARK(2000000000, SHA2('x', 256))", timeout: 30)}"; }
        catch (Exception e) { first = $"{e.GetType().Name}: {Short(e.Message)}"; }
        return $"{first} | reuse select1={await Scalar(c, "SELECT 1")}";
    });
    p["S8_server_lock_wait_timeout"] = await Watch(async () =>
    {
        var lockDb = $"buildmind_test_rh09l_{Guid.NewGuid():N}"[..40];
        await using var a = create(cs);
        await a.OpenAsync();
        await Scalar(a, $"CREATE DATABASE `{lockDb}`");
        await Scalar(a, $"CREATE TABLE `{lockDb}`.t (id INT PRIMARY KEY) ENGINE=InnoDB");
        await Scalar(a, $"INSERT INTO `{lockDb}`.t VALUES (1)");
        await using var holder = await a.BeginTransactionAsync();
        await using (var l = a.CreateCommand()) { l.Transaction = holder; l.CommandText = $"SELECT id FROM `{lockDb}`.t WHERE id = 1 FOR UPDATE"; await l.ExecuteScalarAsync(); }
        await using var b = create(cs);
        await b.OpenAsync();
        await Scalar(b, "SET SESSION innodb_lock_wait_timeout = 1");
        string outcome;
        try { outcome = $"returned {await Scalar(b, $"SELECT id FROM `{lockDb}`.t WHERE id = 1 FOR UPDATE", timeout: 30)}"; }
        catch (Exception e) { outcome = $"{e.GetType().Name}: {Short(e.Message)}"; }
        var reuse = await Scalar(b, "SELECT 1");
        await holder.RollbackAsync();
        await Scalar(a, $"DROP DATABASE `{lockDb}`");
        return $"{outcome} | reuse select1={reuse}";
    });
    p["S9_client_timeout_real_query"] = await Watch(async () =>
    {
        await using var c = create(cs);
        await c.OpenAsync();
        try { return $"returned {await Scalar(c, "SELECT BENCHMARK(2000000000, SHA2('x', 256))", timeout: 1)}"; }
        catch (Exception e) { return $"{e.GetType().Name}: {Short(e.Message)}"; }
    });
    var db = $"buildmind_test_rh09_{Guid.NewGuid():N}"[..40];
    p["S6_transaction_after_timeout"] = await Watch(async () =>
    {
        await using var c = create(cs);
        await c.OpenAsync();
        await Scalar(c, $"CREATE DATABASE `{db}`");
        await Scalar(c, $"CREATE TABLE `{db}`.t (id INT PRIMARY KEY)");
        await using (var tx = await c.BeginTransactionAsync())
        {
            await using (var ins = c.CreateCommand()) { ins.Transaction = tx; ins.CommandText = $"INSERT INTO `{db}`.t VALUES (1)"; await ins.ExecuteNonQueryAsync(); }
            try
            {
                await using var slow = c.CreateCommand();
                slow.Transaction = tx; slow.CommandText = "SELECT SLEEP(5)"; slow.CommandTimeout = 1;
                await slow.ExecuteScalarAsync();
            }
            catch (Exception e) { _ = e; }
            try { await tx.RollbackAsync(); } catch (Exception e) { return $"rollback failed: {e.GetType().Name}"; }
        }
        await using var check = create(cs);
        await check.OpenAsync();
        return $"rows_after_rollback={await Scalar(check, $"SELECT COUNT(*) FROM `{db}`.t")}";
    });
    p["cleanup"] = await Watch(async () =>
    {
        await using var c = create(cs);
        await c.OpenAsync();
        await Scalar(c, $"DROP DATABASE IF EXISTS `{db}`");
        return "dropped";
    });
    results.Add(p);
}

Console.WriteLine(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
Environment.Exit(0);

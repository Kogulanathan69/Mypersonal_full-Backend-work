using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BuildMind.Infrastructure.Persistence.Connections;

/// <summary>
/// STEP 10-E: process-wide serialization of MySQL connection OPENING. This works around a thread-safety defect in
/// the MySql.Data provider (verified in the shipped MySql.Data 26.10.0 IL, net10.0 build SHA-256
/// <c>adda88e99fcef0b5…</c>, <c>&lt;StartSSLAsync&gt;d__11.MoveNext</c>). The provider's static
/// <c>Dictionary</c> fields <c>tlsConnectionRef</c> and <c>tlsRetry</c> are mutated after the TLS handshake
/// (<c>set_Item</c>, <c>Remove</c> at IL 891–1055) <b>outside</b> its own <c>SemaphoreSlim</c> (released at IL 613).
/// Two TLS handshakes completing concurrently anywhere in the process can corrupt them. That is the CI run #55
/// failure. Upstream: MySQL Bug #111759 and #110717. No fixed provider version exists (26.10.0 is the latest).
/// </summary>
/// <remarks>
/// <para>TLS is NOT weakened: only the opening step is serialized. Commands, transactions and already-open pooled
/// connections run fully concurrently. No retry is added.</para>
/// <para>Waiting for the gate is bounded (STEP 10-F). <b>Holding it is NOT</b> (open decision, STEP 10-H): MySql.Data
/// does not bound or cancel its handshake once TCP is connected, so one stalled handshake holds the gate until it
/// ends and every other open fails after its wait limit (503). See doc 25 §16 (risk R-3).</para>
/// <para>Remove this gate only after a provider release whose IL no longer mutates these fields outside a lock,
/// verified the same way.</para>
/// </remarks>
public static class MySqlConnectionOpenGate
{
    /// <summary>The single process-wide gate (the provider state it protects is process-wide).</summary>
    internal static readonly OpenGate Default = new();

    /// <summary>EF Core interceptor applied by <see cref="MySqlContextOptions"/> to every BuildMind context.</summary>
    public static IInterceptor Interceptor { get; } = new SerializedOpenInterceptor(Default);

    /// <summary>For code (tests, tools) that opens a <see cref="DbConnection"/> directly, without EF Core.</summary>
    public static Task OpenAsync(DbConnection connection, CancellationToken cancellationToken = default) =>
        Default.OpenAsync(connection, cancellationToken);
}

/// <summary>
/// The gate could not be acquired within the connection's own connect timeout (STEP 10-F, bounded wait). Classified
/// as a database connection failure (503 <c>DEPENDENCY_UNAVAILABLE</c>), never retried.
/// </summary>
public sealed class MySqlConnectionGateTimeoutException(TimeSpan waited)
    : TimeoutException("A database connection could not be started in time.")
{
    public TimeSpan Waited { get; } = waited;
}

/// <summary>
/// Identity of ONE connection-opening operation. EF Core passes the same <c>ConnectionId</c> and <c>StartTime</c> to
/// every callback of one open (<c>RelationalConnection.OpenInternal</c> reads <c>UtcNow</c> once); a direct open uses
/// a fresh unique identity.
/// </summary>
internal readonly record struct OpenOperation(Guid ConnectionId, DateTimeOffset StartTime)
{
    internal static OpenOperation From(ConnectionEventData eventData) => new(eventData.ConnectionId, eventData.StartTime);

    internal static OpenOperation NewDirect() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);
}

/// <summary>
/// One-at-a-time opening. Ownership is tracked per connection AND per open operation (STEP 10-H/10-I):
/// <list type="bullet">
/// <item>Re-entry without waiting is allowed only for the SAME operation (for example the shared interceptor
/// registered twice, or two interceptors sharing one gate, during one EF Core open).</item>
/// <item>An independent open of the same connection is NOT re-entry: it waits like any other opener, so two opens of
/// one connection can never run concurrently.</item>
/// <item>Release is accepted only from the operation that holds the gate. Duplicate, late or foreign completion
/// callbacks are no-ops, so they can never release another operation's hold.</item>
/// <item>The semaphore is released exactly once, when the holding operation's last registration releases.</item>
/// </list>
/// </summary>
internal sealed class OpenGate
{
    /// <summary>Used when a connection reports no connect timeout (0 means "infinite" in ADO.NET).</summary>
    internal static readonly TimeSpan FallbackWait = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim semaphore = new(1, 1);
    private readonly ConditionalWeakTable<DbConnection, Hold> holds = new();
    private readonly Lock holdsLock = new();

    internal int AvailableSlots => semaphore.CurrentCount;

    /// <summary>
    /// STEP 10-F: waiting for the gate is bounded by the connection's own connect timeout. The caller's cancellation
    /// still applies. HOLDING the gate is not bounded (open risk R-3).
    /// </summary>
    internal static TimeSpan WaitLimit(DbConnection connection) =>
        connection.ConnectionTimeout > 0 ? TimeSpan.FromSeconds(connection.ConnectionTimeout) : FallbackWait;

    internal async Task OpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var operation = OpenOperation.NewDirect();
        await EnterAsync(connection, operation, cancellationToken).ConfigureAwait(false);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Release(connection, operation);
        }
    }

    /// <summary>Returns true when the semaphore was really acquired, false for a re-entry by the same operation.</summary>
    internal bool Enter(DbConnection connection, OpenOperation operation)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (TryReenter(connection, operation))
        {
            return false;
        }

        var limit = WaitLimit(connection);
        if (!semaphore.Wait(limit))
        {
            throw new MySqlConnectionGateTimeoutException(limit);
        }

        Register(connection, operation);
        return true;
    }

    /// <summary>Returns true when the semaphore was really acquired, false for a re-entry by the same operation.</summary>
    internal async Task<bool> EnterAsync(DbConnection connection, OpenOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (TryReenter(connection, operation))
        {
            return false;
        }

        var limit = WaitLimit(connection);
        if (!await semaphore.WaitAsync(limit, cancellationToken).ConfigureAwait(false))
        {
            throw new MySqlConnectionGateTimeoutException(limit);
        }

        Register(connection, operation);
        return true;
    }

    /// <summary>
    /// Releases one registration of <paramref name="operation"/>. A callback from any other operation (late,
    /// duplicate or foreign) is ignored. The semaphore is released when the holder's last registration ends.
    /// </summary>
    internal void Release(DbConnection connection, OpenOperation operation)
    {
        lock (holdsLock)
        {
            if (!holds.TryGetValue(connection, out var hold) || hold.Operation != operation)
            {
                return;
            }

            if (--hold.Count > 0)
            {
                return;
            }

            holds.Remove(connection);
        }

        semaphore.Release();
    }

    internal bool IsHeldBy(DbConnection connection)
    {
        lock (holdsLock)
        {
            return holds.TryGetValue(connection, out _);
        }
    }

    private bool TryReenter(DbConnection connection, OpenOperation operation)
    {
        lock (holdsLock)
        {
            if (!holds.TryGetValue(connection, out var hold) || hold.Operation != operation)
            {
                return false; // not held, or held by an INDEPENDENT operation: wait for the gate
            }

            hold.Count++;
            return true;
        }
    }

    private void Register(DbConnection connection, OpenOperation operation)
    {
        lock (holdsLock)
        {
            // The semaphore is held, so no other operation can hold the gate (for this or any connection).
            holds.AddOrUpdate(connection, new Hold(operation));
        }
    }

    private sealed class Hold(OpenOperation operation)
    {
        public OpenOperation Operation { get; } = operation;

        public int Count = 1;
    }
}

/// <summary>
/// Holds the gate from <c>ConnectionOpening</c> until <c>ConnectionOpened</c>, <c>ConnectionFailed</c> or
/// <c>ConnectionCanceled</c> of the SAME open operation. Ownership and exactly-once release are enforced by
/// <see cref="OpenGate"/>.
/// </summary>
/// <remarks>
/// Verified against the shipped EF Core Relational 10.0.12 IL (<c>RelationalConnection.OpenInternal</c> and
/// <c>&lt;OpenInternalAsync&gt;d__72</c>):
/// <list type="bullet">
/// <item>Opening goes through <c>ConnectionOpening[Async]</c>, then <c>OpenDbConnection[Async]</c> (skipped when
/// suppressed), then <c>ConnectionOpened[Async]</c>; <c>startTime</c> is read once for the whole operation.</item>
/// <item>On an exception, the catch calls <c>IExceptionDetector.IsCancellation</c>: a cancellation goes to
/// <c>ConnectionCanceled[Async]</c>, any other failure to <c>ConnectionFailed[Async]</c>. All three exits release.</item>
/// <item>Interceptors run only when <c>ShouldLogConnectionOpen</c> is true; with an interceptor present the
/// per-context suppression is reset on every open (verified live by <c>SSL02</c>).</item>
/// </list>
/// Not covered (provider-internal, residual risk R-1): MySql.Data's <c>MySqlConnection.CancelQuery</c> opens its own
/// unpooled connection for <c>KILL QUERY</c> directly, bypassing EF Core.
/// </remarks>
internal sealed class SerializedOpenInterceptor(OpenGate gate) : DbConnectionInterceptor
{
    private long acquisitions;
    private long reentries;

    /// <summary>Number of times this interceptor really acquired the gate (diagnostics and tests).</summary>
    internal long Acquisitions => Interlocked.Read(ref acquisitions);

    /// <summary>Number of re-entrant (duplicate, same-operation) openings that did not wait.</summary>
    internal long Reentries => Interlocked.Read(ref reentries);

    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Count(gate.Enter(connection, OpenOperation.From(eventData)));
        return result;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Count(await gate.EnterAsync(connection, OpenOperation.From(eventData), cancellationToken).ConfigureAwait(false));
        return result;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        gate.Release(connection, OpenOperation.From(eventData));

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        gate.Release(connection, OpenOperation.From(eventData));
        return Task.CompletedTask;
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) =>
        gate.Release(connection, OpenOperation.From(eventData));

    public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        gate.Release(connection, OpenOperation.From(eventData));
        return Task.CompletedTask;
    }

    // STEP 10-F (G1): EF Core reports a cancelled open here, never through ConnectionFailed.
    public override void ConnectionCanceled(DbConnection connection, ConnectionEndEventData eventData) =>
        gate.Release(connection, OpenOperation.From(eventData));

    public override Task ConnectionCanceledAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        gate.Release(connection, OpenOperation.From(eventData));
        return Task.CompletedTask;
    }

    private void Count(bool acquired)
    {
        if (acquired)
        {
            Interlocked.Increment(ref acquisitions);
        }
        else
        {
            Interlocked.Increment(ref reentries);
        }
    }
}

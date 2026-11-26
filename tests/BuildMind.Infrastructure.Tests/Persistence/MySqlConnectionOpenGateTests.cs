using System.Data;
using System.Data.Common;
using BuildMind.API.Errors;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Persistence.Connections;
using BuildMind.Infrastructure.Persistence.Errors;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>
/// STEP 10-E…10-I: deterministic semantics of the open gate (no database). Every test uses its OWN gate instance, so
/// parallel tests that open connections through the process-wide default gate cannot interfere. EF Core callbacks
/// receive real event data: one open operation = one (ConnectionId, StartTime), as EF Core passes it.
/// </summary>
public sealed class MySqlConnectionOpenGateTests
{
    private readonly OpenGate gate = new();
    private readonly SerializedOpenInterceptor interceptor;

    public MySqlConnectionOpenGateTests() => interceptor = new SerializedOpenInterceptor(gate);

    private SerializedOpenInterceptor Interceptor => interceptor;

    [Fact]
    public void Production_contexts_use_the_process_wide_interceptor()
    {
        Assert.IsType<SerializedOpenInterceptor>(MySqlConnectionOpenGate.Interceptor);
        Assert.Same(MySqlConnectionOpenGate.Interceptor, MySqlConnectionOpenGate.Interceptor);
    }

    [Fact]
    public async Task Parallel_opens_never_overlap()
    {
        var tracker = new ConcurrencyTracker();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            gate.OpenAsync(new FakeConnection(tracker, TimeSpan.FromMilliseconds(15)), CancellationToken.None)));

        Assert.Equal(1, tracker.Max);
        Assert.Equal(16, tracker.Completed);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task A_failed_open_releases_the_gate_and_keeps_the_original_exception()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.OpenAsync(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, fail: true), CancellationToken.None));

        Assert.Equal("synthetic open failure", failure.Message);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Cancellation_while_waiting_does_not_consume_or_leak_a_slot()
    {
        var blocker = new FakeConnection(new ConcurrencyTracker(), TimeSpan.FromMilliseconds(300));
        var holding = gate.OpenAsync(blocker, CancellationToken.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gate.OpenAsync(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero), cancel.Token));
        await holding;

        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Interceptor_holds_from_opening_until_opened_or_failed_exactly_once()
    {
        var opened = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        var failed = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));

        await Interceptor.ConnectionOpeningAsync(opened.Connection, opened.Opening, default);
        Assert.Equal(0, gate.AvailableSlots);
        await Interceptor.ConnectionOpenedAsync(opened.Connection, opened.End);
        await Interceptor.ConnectionOpenedAsync(opened.Connection, opened.End); // a second notification must not over-release
        Assert.Equal(1, gate.AvailableSlots);

        Interceptor.ConnectionOpening(failed.Connection, failed.Opening, default);
        Assert.Equal(0, gate.AvailableSlots);
        await Interceptor.ConnectionFailedAsync(failed.Connection, failed.Error);
        Assert.Equal(1, gate.AvailableSlots);

        // A failure notification for an operation that never acquired (e.g. cancelled while waiting) is a no-op.
        var never = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        Interceptor.ConnectionFailed(never.Connection, never.Error);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Interceptor_waiter_cancelled_before_acquiring_holds_nothing()
    {
        var holder = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        var waiter = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        await Interceptor.ConnectionOpeningAsync(holder.Connection, holder.Opening, default);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Interceptor.ConnectionOpeningAsync(waiter.Connection, waiter.Opening, default, cancel.Token));
        await Interceptor.ConnectionCanceledAsync(waiter.Connection, waiter.End); // EF reports the cancellation: no-op
        Assert.Equal(0, gate.AvailableSlots); // the holder is unaffected
        await Interceptor.ConnectionOpenedAsync(holder.Connection, holder.End);

        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Interceptor_releases_on_cancelled_open_sync_and_async()
    {
        // STEP 10-F (G1): EF Core 10.0.12 reports a cancelled open via ConnectionCanceled, never ConnectionFailed.
        var asyncOpen = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        await Interceptor.ConnectionOpeningAsync(asyncOpen.Connection, asyncOpen.Opening, default);
        Assert.Equal(0, gate.AvailableSlots);
        await Interceptor.ConnectionCanceledAsync(asyncOpen.Connection, asyncOpen.End);
        Assert.Equal(1, gate.AvailableSlots);

        var syncOpen = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        Interceptor.ConnectionOpening(syncOpen.Connection, syncOpen.Opening, default);
        Interceptor.ConnectionCanceled(syncOpen.Connection, syncOpen.End);
        Interceptor.ConnectionCanceled(syncOpen.Connection, syncOpen.End); // repeated notification: exactly one release
        Assert.Equal(1, gate.AvailableSlots);
        Assert.Equal(2, Interceptor.Acquisitions);
    }

    [Fact]
    public async Task Waiting_for_the_gate_is_bounded_by_the_connection_timeout_async_and_sync()
    {
        // STEP 10-F (G2): without a bound, a sync open (no token) could wait forever behind a stuck open.
        var holder = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        await Interceptor.ConnectionOpeningAsync(holder.Connection, holder.Opening, default);
        var asyncWaiter = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1));
        var syncWaiter = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var asyncWait = await Assert.ThrowsAsync<MySqlConnectionGateTimeoutException>(async () =>
            await Interceptor.ConnectionOpeningAsync(asyncWaiter.Connection, asyncWaiter.Opening, default));
        var syncWait = Assert.Throws<MySqlConnectionGateTimeoutException>(() =>
            Interceptor.ConnectionOpening(syncWaiter.Connection, syncWaiter.Opening, default));
        var directWait = await Assert.ThrowsAsync<MySqlConnectionGateTimeoutException>(() =>
            gate.OpenAsync(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1), CancellationToken.None));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"waited {watch.ElapsedMilliseconds} ms");
        Assert.All([asyncWait, syncWait, directWait], e => Assert.Equal(TimeSpan.FromSeconds(1), e.Waited));
        Assert.Equal(0, gate.AvailableSlots); // still held by the real holder; timed-out waiters took nothing
        await Interceptor.ConnectionOpenedAsync(holder.Connection, holder.End);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public void A_connection_without_a_timeout_still_gets_a_bounded_wait()
    {
        Assert.Equal(OpenGate.FallbackWait, OpenGate.WaitLimit(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 0)));
        Assert.Equal(TimeSpan.FromSeconds(7), OpenGate.WaitLimit(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 7)));
    }

    [Fact]
    public void A_gate_timeout_is_a_classified_503_and_never_an_unexpected_500()
    {
        Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(
            new MySqlConnectionGateTimeoutException(TimeSpan.FromSeconds(15)), out var failure));

        Assert.Equal(PersistenceFailureKind.ConnectionFailure, failure.Kind);
        Assert.False(failure.IsTransient); // no retry semantics are implied
        var mapping = PersistenceProblemMapping.Map(failure);
        Assert.Equal((503, SharedErrorCodes.DependencyUnavailable), (mapping.Status, mapping.Code));
    }

    [Fact]
    public async Task Duplicate_registration_of_the_same_interceptor_neither_deadlocks_nor_double_releases()
    {
        // STEP 10-H: EF calls every registered interceptor with the SAME event data, so the same instance registered
        // twice sees each callback twice for one operation. Before 10-H the second Opening waited for itself.
        var open = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Interceptor.ConnectionOpeningAsync(open.Connection, open.Opening, default);
        await Interceptor.ConnectionOpeningAsync(open.Connection, open.Opening, default);

        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(900), "the duplicate opening waited for itself");
        Assert.Equal((1, 1), (Interceptor.Acquisitions, Interceptor.Reentries));
        await Interceptor.ConnectionOpenedAsync(open.Connection, open.End);
        Assert.Equal(0, gate.AvailableSlots); // one registration still holds
        await Interceptor.ConnectionOpenedAsync(open.Connection, open.End);
        Assert.Equal(1, gate.AvailableSlots);
        await Interceptor.ConnectionOpenedAsync(open.Connection, open.End); // extra notification: no over-release
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public void Two_interceptors_sharing_one_gate_reenter_instead_of_deadlocking()
    {
        var second = new SerializedOpenInterceptor(gate);
        var open = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1));

        Interceptor.ConnectionOpening(open.Connection, open.Opening, default);
        second.ConnectionOpening(open.Connection, open.Opening, default); // sync path, would block 1 s and throw before 10-H
        second.ConnectionFailed(open.Connection, open.Error);
        Assert.Equal(0, gate.AvailableSlots);
        Interceptor.ConnectionFailed(open.Connection, open.Error);

        Assert.Equal(1, gate.AvailableSlots);
        Assert.Equal(1, second.Reentries);
    }

    [Fact]
    public async Task Independent_opens_of_the_same_connection_are_serialized_not_reentrant()
    {
        // STEP 10-I: re-entry is per OPERATION. A second, independent open of the same connection must wait (here
        // until its bounded limit), never proceed concurrently.
        var connection = new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1);
        var first = Op.For(connection);
        var second = Op.For(connection);
        await Interceptor.ConnectionOpeningAsync(first.Connection, first.Opening, default);

        await Assert.ThrowsAsync<MySqlConnectionGateTimeoutException>(async () =>
            await Interceptor.ConnectionOpeningAsync(second.Connection, second.Opening, default));
        Assert.Equal((1, 0), (Interceptor.Acquisitions, Interceptor.Reentries));

        await Interceptor.ConnectionOpenedAsync(first.Connection, first.End);
        var third = Op.For(connection);
        await Interceptor.ConnectionOpeningAsync(third.Connection, third.Opening, default); // acquires normally now
        await Interceptor.ConnectionOpenedAsync(third.Connection, third.End);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Concurrent_direct_opens_of_one_connection_never_overlap()
    {
        var tracker = new ConcurrencyTracker();
        var connection = new FakeConnection(tracker, TimeSpan.FromMilliseconds(50));

        await Task.WhenAll(gate.OpenAsync(connection, CancellationToken.None), gate.OpenAsync(connection, CancellationToken.None));

        Assert.Equal(1, tracker.Max);
        Assert.Equal(2, tracker.Completed);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Late_duplicate_or_foreign_callbacks_never_release_another_operations_hold()
    {
        // STEP 10-I: callbacks are matched to the holding operation.
        var connection = new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero);
        var earlier = Op.For(connection);
        await Interceptor.ConnectionOpeningAsync(earlier.Connection, earlier.Opening, default);
        await Interceptor.ConnectionOpenedAsync(earlier.Connection, earlier.End);

        var current = Op.For(connection);
        await Interceptor.ConnectionOpeningAsync(current.Connection, current.Opening, default);
        await Interceptor.ConnectionOpenedAsync(earlier.Connection, earlier.End);      // late duplicate
        await Interceptor.ConnectionFailedAsync(earlier.Connection, earlier.Error);    // late failure
        Interceptor.ConnectionCanceled(earlier.Connection, earlier.End);               // late cancel
        var foreign = Op.For(connection);
        Interceptor.ConnectionOpened(foreign.Connection, foreign.End);                  // never acquired
        Assert.Equal(0, gate.AvailableSlots);
        Assert.True(gate.IsHeldBy(connection));

        await Interceptor.ConnectionOpenedAsync(current.Connection, current.End);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task Reentrant_acquisition_by_the_same_operation_is_counted_and_released_once()
    {
        var connection = new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero);
        var operation = new OpenOperation(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.True(await gate.EnterAsync(connection, operation, CancellationToken.None));
        Assert.False(gate.Enter(connection, operation));
        Assert.False(await gate.EnterAsync(connection, operation, CancellationToken.None));
        gate.Release(connection, operation);
        gate.Release(connection, operation);
        Assert.True(gate.IsHeldBy(connection));
        gate.Release(connection, operation);

        Assert.False(gate.IsHeldBy(connection));
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task A_failed_open_is_followed_by_a_successful_one()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.OpenAsync(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, fail: true), CancellationToken.None));
        var failed = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        await Interceptor.ConnectionOpeningAsync(failed.Connection, failed.Opening, default);
        await Interceptor.ConnectionFailedAsync(failed.Connection, failed.Error);

        var ok = new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero);
        await gate.OpenAsync(ok, CancellationToken.None);

        Assert.Equal(ConnectionState.Open, ok.State);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public async Task The_gate_recovers_immediately_after_a_waiter_timed_out()
    {
        var holder = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero));
        var waiter = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1));
        await Interceptor.ConnectionOpeningAsync(holder.Connection, holder.Opening, default);
        await Assert.ThrowsAsync<MySqlConnectionGateTimeoutException>(async () =>
            await Interceptor.ConnectionOpeningAsync(waiter.Connection, waiter.Opening, default));
        await Interceptor.ConnectionOpenedAsync(holder.Connection, holder.End);

        var next = Op.For(new FakeConnection(new ConcurrencyTracker(), TimeSpan.Zero, timeoutSeconds: 1));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Interceptor.ConnectionOpeningAsync(next.Connection, next.Opening, default);

        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500));
        await Interceptor.ConnectionOpenedAsync(next.Connection, next.End);
        Assert.Equal(1, gate.AvailableSlots);
    }

    [Fact]
    public void A_gate_timeout_wrapped_by_the_provider_execution_strategy_is_still_a_503()
    {
        // PR #30 CI: MySQLExecutionStrategy wrapped the gate timeout in an InvalidOperationException suggesting
        // EnableRetryOnFailure. It must still classify as 503 (and no retry is configured).
        var wrapped = new InvalidOperationException("An exception has been raised that is likely due to a transient failure.",
            new MySqlConnectionGateTimeoutException(TimeSpan.FromSeconds(15)));

        Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(wrapped, out var failure));
        Assert.Equal(503, PersistenceProblemMapping.Map(failure).Status);
    }

    /// <summary>One EF Core open operation: the same ConnectionId and StartTime for all of its callbacks.</summary>
    private sealed record Op(DbConnection Connection, Guid ConnectionId, DateTimeOffset StartTime)
    {
        private static long ticks = DateTimeOffset.UtcNow.UtcTicks;

        public static Op For(DbConnection connection) =>
            new(connection, Guid.NewGuid(), new DateTimeOffset(Interlocked.Increment(ref ticks), TimeSpan.Zero));

        public ConnectionEventData Opening => new(null!, null!, Connection, null, ConnectionId, true, StartTime);

        public ConnectionEndEventData End => new(null!, null!, Connection, null, ConnectionId, true, StartTime, TimeSpan.Zero);

        public ConnectionErrorEventData Error =>
            new(null!, null!, Connection, null, ConnectionId, new InvalidOperationException("synthetic"), true, StartTime, TimeSpan.Zero);
    }

    private sealed class ConcurrencyTracker
    {
        private int current;
        private int max;
        private int completed;

        public int Max => Volatile.Read(ref max);

        public int Completed => Volatile.Read(ref completed);

        public void Enter()
        {
            var now = Interlocked.Increment(ref current);
            int seen;
            while (now > (seen = Volatile.Read(ref max)) && Interlocked.CompareExchange(ref max, now, seen) != seen)
            {
            }
        }

        public void Exit()
        {
            Interlocked.Decrement(ref current);
            Interlocked.Increment(ref completed);
        }
    }

    private sealed class FakeConnection(ConcurrencyTracker tracker, TimeSpan openTime, bool fail = false, int timeoutSeconds = 15) : DbConnection
    {
        private ConnectionState state = ConnectionState.Closed;

        public override int ConnectionTimeout => timeoutSeconds;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => "synthetic";

        public override string DataSource => "synthetic";

        public override string ServerVersion => "0";

        public override ConnectionState State => state;

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            tracker.Enter();
            try
            {
                await Task.Delay(openTime, cancellationToken);
                if (fail)
                {
                    throw new InvalidOperationException("synthetic open failure");
                }

                state = ConnectionState.Open;
            }
            finally
            {
                tracker.Exit();
            }
        }

        public override void Open() => OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override void Close() => state = ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}

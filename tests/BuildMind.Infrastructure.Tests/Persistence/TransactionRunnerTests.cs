using System.Data;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore.Storage;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>Transaction helper semantics verified with an in-memory fake transaction (no database).</summary>
public sealed class TransactionRunnerTests
{
    [Fact]
    public async Task Successful_operation_commits_once_and_disposes()
    {
        var source = new FakeTransactionSource();

        var result = await new TransactionRunner(source).ExecuteAsync(_ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(0, source.Discards);
        Assert.Equal(1, source.Transaction!.Commits);
        Assert.Equal(0, source.Transaction.Rollbacks);
        Assert.True(source.Transaction.Disposed);
        Assert.False(source.Transaction.CommitTokenCanBeCancelled);
    }

    [Fact]
    public async Task Failing_operation_rolls_back_and_rethrows_the_original_exception()
    {
        var source = new FakeTransactionSource();
        var original = new InvalidOperationException("synthetic");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TransactionRunner(source).ExecuteAsync<int>(_ => throw original, CancellationToken.None));

        Assert.Same(original, thrown);
        Assert.Equal(0, source.Transaction!.Commits);
        Assert.Equal(1, source.Transaction.Rollbacks);
        Assert.Equal(1, source.Discards); // rolled-back entities can never be saved later in the scope
        Assert.True(source.Transaction.Disposed);
    }

    [Fact]
    public async Task Rollback_failure_is_reported_as_unconfirmed_and_keeps_the_original_exception()
    {
        var source = new FakeTransactionSource { FailRollback = true };
        var original = new InvalidOperationException("synthetic");

        var thrown = await Assert.ThrowsAsync<TransactionRollbackUncertainException>(() =>
            new TransactionRunner(source).ExecuteAsync<int>(_ => throw original, CancellationToken.None));

        Assert.Same(original, thrown.InnerException);
        Assert.IsType<InvalidOperationException>(thrown.RollbackFailure);
        Assert.Equal(0, source.Transaction!.Commits);
        Assert.Equal(1, source.Discards);
        Assert.True(source.Transaction.Disposed);
    }

    [Fact]
    public async Task Disposal_failure_never_replaces_the_operation_failure()
    {
        var source = new FakeTransactionSource { FailDispose = true };
        var original = new InvalidOperationException("synthetic");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TransactionRunner(source).ExecuteAsync<int>(_ => throw original, CancellationToken.None));

        Assert.Same(original, thrown);
        Assert.Equal(1, source.Transaction!.Rollbacks);
    }

    [Fact]
    public async Task Disposal_failure_after_commit_does_not_turn_success_into_failure()
    {
        var source = new FakeTransactionSource { FailDispose = true };

        var result = await new TransactionRunner(source).ExecuteAsync(_ => Task.FromResult(7), CancellationToken.None);

        Assert.Equal(7, result);
        Assert.Equal(1, source.Transaction!.Commits);
    }

    [Fact]
    public async Task Cancellation_before_start_begins_no_transaction()
    {
        var source = new FakeTransactionSource();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TransactionRunner(source).ExecuteAsync(_ => Task.FromResult(1), cancellation.Token));

        Assert.Null(source.Transaction);
    }

    [Fact]
    public async Task Cancellation_during_operation_rolls_back_without_commit()
    {
        var source = new FakeTransactionSource();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TransactionRunner(source).ExecuteAsync(
                async _ =>
                {
                    await cancellation.CancelAsync();
                    return 1;
                },
                cancellation.Token));

        Assert.Equal(0, source.Transaction!.Commits);
        Assert.Equal(1, source.Transaction.Rollbacks);
        Assert.Equal(1, source.Discards);
        Assert.True(source.Transaction.Disposed);
    }

    [Fact]
    public async Task Commit_failure_reports_unknown_outcome_and_is_not_retried()
    {
        var source = new FakeTransactionSource { FailCommit = true };
        var calls = 0;

        var thrown = await Assert.ThrowsAsync<TransactionOutcomeUnknownException>(() =>
            new TransactionRunner(source).ExecuteAsync(
                _ =>
                {
                    calls++;
                    return Task.FromResult(1);
                },
                CancellationToken.None));

        Assert.IsType<TimeoutException>(thrown.InnerException);
        Assert.Equal(1, calls);
        Assert.Equal(1, source.BeginCount);
        Assert.Equal(1, source.Discards);
        Assert.Equal(0, source.Transaction!.Rollbacks);
        Assert.True(source.Transaction.Disposed);
    }

    [Fact]
    public async Task Nested_transactions_are_rejected()
    {
        var source = new FakeTransactionSource { HasActiveTransaction = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new TransactionRunner(source).ExecuteAsync(_ => Task.FromResult(1), CancellationToken.None));

        Assert.Equal(0, source.BeginCount);
    }

    [Fact]
    public async Task Requested_isolation_level_is_used_and_defaults_to_read_committed()
    {
        var source = new FakeTransactionSource();
        var runner = new TransactionRunner(source);

        await runner.ExecuteAsync(_ => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(IsolationLevel.ReadCommitted, source.LastIsolationLevel);

        await runner.ExecuteAsync(_ => Task.CompletedTask, CancellationToken.None, IsolationLevel.Serializable);
        Assert.Equal(IsolationLevel.Serializable, source.LastIsolationLevel);
    }

    [Fact]
    public async Task Null_operation_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new TransactionRunner(new FakeTransactionSource()).ExecuteAsync<int>(null!, CancellationToken.None));
    }

    private sealed class FakeTransactionSource : IDbTransactionSource
    {
        public bool HasActiveTransaction { get; init; }

        public bool FailCommit { get; init; }

        public bool FailRollback { get; init; }

        public bool FailDispose { get; init; }

        public int BeginCount { get; private set; }

        public int Discards { get; private set; }

        public void DiscardTrackedChanges() => Discards++;

        public IsolationLevel? LastIsolationLevel { get; private set; }

        public FakeTransaction? Transaction { get; private set; }

        public Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeginCount++;
            LastIsolationLevel = isolationLevel;
            Transaction = new FakeTransaction(FailCommit, FailRollback, FailDispose);
            return Task.FromResult<IDbContextTransaction>(Transaction);
        }
    }

    private sealed class FakeTransaction(bool failCommit, bool failRollback, bool failDispose) : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();

        public int Commits { get; private set; }

        public int Rollbacks { get; private set; }

        public bool Disposed { get; private set; }

        public bool CommitTokenCanBeCancelled { get; private set; }

        public void Commit() => throw new NotSupportedException("Synchronous commit must not be used.");

        public void Rollback() => throw new NotSupportedException("Synchronous rollback must not be used.");

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitTokenCanBeCancelled = cancellationToken.CanBeCanceled;
            if (failCommit)
            {
                throw new TimeoutException("synthetic commit failure");
            }

            Commits++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            Rollbacks++;
            return failRollback
                ? throw new InvalidOperationException("synthetic rollback failure")
                : Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return failDispose
                ? throw new InvalidOperationException("synthetic dispose failure")
                : ValueTask.CompletedTask;
        }
    }
}

using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>Database error classification (BE-M4-D02a); provider messages here are synthetic.</summary>
public sealed class PersistenceErrorClassifierTests
{
    private const string Secret = DatabaseOptionsTests.Secret;

    [Theory]
    [InlineData(1062, PersistenceFailureKind.DuplicateKey)]
    [InlineData(1586, PersistenceFailureKind.DuplicateKey)]
    [InlineData(1451, PersistenceFailureKind.ForeignKeyViolation)]
    [InlineData(1452, PersistenceFailureKind.ForeignKeyViolation)]
    [InlineData(1213, PersistenceFailureKind.Deadlock)]
    [InlineData(1205, PersistenceFailureKind.LockTimeout)]
    [InlineData(1042, PersistenceFailureKind.ConnectionFailure)]
    [InlineData(1045, PersistenceFailureKind.ConnectionFailure)]
    [InlineData(2013, PersistenceFailureKind.ConnectionFailure)]
    [InlineData(1064, PersistenceFailureKind.Unexpected)]
    [InlineData(0, PersistenceFailureKind.Unexpected)]
    public void Provider_error_numbers_are_classified(int number, PersistenceFailureKind expected)
    {
        Assert.Equal(expected, PersistenceErrorClassifier.KindOf(number));
    }

    [Fact]
    public void Duplicate_key_keeps_the_constraint_name_but_never_the_duplicated_value()
    {
        var failure = PersistenceErrorClassifier.FromProviderError(
            1062, $"Duplicate entry '{Secret}@example.invalid' for key 'users.ux_users_normalized_email'");

        Assert.Equal(PersistenceFailureKind.DuplicateKey, failure.Kind);
        Assert.Equal(SharedErrorCodes.Conflict, failure.DefaultCode);
        Assert.Equal("ux_users_normalized_email", failure.ConstraintName);
        Assert.Equal(1062, failure.ProviderErrorNumber);
        Assert.False(failure.IsTransient);
        Assert.DoesNotContain(Secret, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Foreign_key_violation_keeps_the_constraint_name()
    {
        var failure = PersistenceErrorClassifier.FromProviderError(
            1452,
            "Cannot add or update a child row: a foreign key constraint fails (`buildmind_dev`.`projects`, CONSTRAINT `fk_projects_users_owner_id` FOREIGN KEY (`owner_id`) REFERENCES `users` (`id`))");

        Assert.Equal(PersistenceFailureKind.ForeignKeyViolation, failure.Kind);
        Assert.Equal("fk_projects_users_owner_id", failure.ConstraintName);
    }

    [Theory]
    [InlineData("Duplicate entry 'x' for key 'bad name; DROP TABLE users'")]
    [InlineData("Duplicate entry 'x'")]
    [InlineData("")]
    [InlineData(null)]
    public void Unrecognised_messages_yield_no_constraint_name(string? message)
    {
        Assert.Null(PersistenceErrorClassifier.FromProviderError(1062, message).ConstraintName);
    }

    [Fact]
    public void Optimistic_concurrency_failure_maps_to_revision_conflict()
    {
        var failure = PersistenceErrorClassifier.Classify(new DbUpdateConcurrencyException("synthetic"));

        Assert.Equal(PersistenceFailureKind.ConcurrencyConflict, failure.Kind);
        Assert.Equal(SharedErrorCodes.RevisionConflict, failure.DefaultCode);
    }

    [Fact]
    public void Wrapped_timeout_maps_to_dependency_timeout()
    {
        var failure = PersistenceErrorClassifier.Classify(new DbUpdateException("synthetic", new TimeoutException()));

        Assert.Equal(PersistenceFailureKind.CommandTimeout, failure.Kind);
        Assert.Equal(SharedErrorCodes.DependencyTimeout, failure.DefaultCode);
        Assert.False(failure.IsTransient);
    }

    [Fact]
    public void Unknown_commit_outcome_is_not_transient()
    {
        var failure = PersistenceErrorClassifier.Classify(new TransactionOutcomeUnknownException(new IOException()));

        Assert.Equal(PersistenceFailureKind.TransactionOutcomeUnknown, failure.Kind);
        Assert.False(failure.IsTransient);
        Assert.Equal(SharedErrorCodes.InternalError, failure.DefaultCode);
    }

    [Fact]
    public void Only_a_deadlock_means_the_server_rolled_back_the_whole_transaction()
    {
        var deadlock = PersistenceErrorClassifier.FromProviderError(1213, null);
        var lockTimeout = PersistenceErrorClassifier.FromProviderError(1205, null);

        Assert.True(deadlock.ServerRolledBackTransaction);
        Assert.False(lockTimeout.ServerRolledBackTransaction); // 1205 rolls back only the statement by default
        Assert.True(lockTimeout.IsTransient);
    }

    [Fact]
    public void Unconfirmed_rollback_is_classified_separately_and_not_transient()
    {
        var failure = PersistenceErrorClassifier.Classify(
            new TransactionRollbackUncertainException(new DbUpdateException("synthetic"), new IOException()));

        Assert.Equal(PersistenceFailureKind.RollbackUnconfirmed, failure.Kind);
        Assert.False(failure.IsTransient);
        Assert.False(failure.ServerRolledBackTransaction);
        Assert.Equal(SharedErrorCodes.InternalError, failure.DefaultCode);
    }

    [Fact]
    public void Unrelated_exceptions_are_unexpected_without_carrying_their_message()
    {
        var failure = PersistenceErrorClassifier.Classify(new InvalidOperationException("Password=" + Secret));

        Assert.Equal(PersistenceFailureKind.Unexpected, failure.Kind);
        Assert.Equal(SharedErrorCodes.InternalError, failure.DefaultCode);
        Assert.DoesNotContain(Secret, failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_deadlocks_and_lock_timeouts_are_transient()
    {
        var transient = Enum.GetValues<PersistenceFailureKind>()
            .Where(kind => SampleNumber(kind) is int number && PersistenceErrorClassifier.FromProviderError(number, null).IsTransient)
            .ToArray();

        Assert.Equal(new[] { PersistenceFailureKind.Deadlock, PersistenceFailureKind.LockTimeout }, transient);
    }

    [Fact]
    public void Default_codes_are_shared_codes_and_not_all_the_same()
    {
        var codes = new[] { 1062, 1452, 1213, 1205, 1042, 1064 }
            .Select(n => PersistenceErrorClassifier.FromProviderError(n, null).DefaultCode)
            .Append(PersistenceErrorClassifier.Classify(new DbUpdateConcurrencyException()).DefaultCode)
            .Append(PersistenceErrorClassifier.Classify(new TimeoutException()).DefaultCode)
            .ToArray();

        Assert.All(codes, code => Assert.Contains(code, SharedErrorCodes.All));
        Assert.True(codes.Distinct().Count() >= 4);
    }

    [Fact]
    public void Failure_log_records_safe_diagnostics_only()
    {
        using var logs = new BuildMind.Infrastructure.Tests.Support.CapturingLoggerProvider();
        var failure = PersistenceErrorClassifier.FromProviderError(
            1062, $"Duplicate entry '{Secret}' for key 'users.ux_users_normalized_email'");

        PersistenceLog.Failure(logs.CreateLogger("BuildMind.Persistence"), "Users.Register", failure);

        var entry = Assert.Single(logs.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
        Assert.Contains("DuplicateKey", entry.Message, StringComparison.Ordinal);
        Assert.Contains("ux_users_normalized_email", entry.Message, StringComparison.Ordinal);
        Assert.Contains("1062", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, entry.AllText, StringComparison.Ordinal);
    }

    private static int? SampleNumber(PersistenceFailureKind kind) => kind switch
    {
        PersistenceFailureKind.DuplicateKey => 1062,
        PersistenceFailureKind.ForeignKeyViolation => 1452,
        PersistenceFailureKind.Deadlock => 1213,
        PersistenceFailureKind.LockTimeout => 1205,
        PersistenceFailureKind.ConnectionFailure => 2013,
        PersistenceFailureKind.Unexpected => 1064,
        _ => null,
    };
}

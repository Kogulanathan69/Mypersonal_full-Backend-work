using System.Text.RegularExpressions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace BuildMind.Infrastructure.Persistence.Errors;

public enum PersistenceFailureKind
{
    DuplicateKey,
    ForeignKeyViolation,
    Deadlock,
    LockTimeout,
    CommandTimeout,
    ConnectionFailure,
    ConcurrencyConflict,
    TransactionOutcomeUnknown,
    RollbackUnconfirmed,
    Unexpected,
}

/// <summary>
/// Safe classification of a persistence failure. It carries no provider message, SQL, parameter value or
/// connection detail. <see cref="DefaultCode"/> is only a default: feature adapters translate failures into
/// their own port results (for example a duplicate idempotency key → existing outcome, a duplicate natural
/// key → <c>CONFLICT</c>) and keep feature context, so not every database error maps to one HTTP status.
/// </summary>
/// <remarks>
/// <see cref="IsTransient"/> means only that the condition (lock contention) may clear later. It does NOT make
/// the business operation safe to repeat: a retry is allowed only after the whole transaction was rolled back,
/// for an idempotent operation (idempotency key or compare-and-set) with no external side effects inside the
/// transaction, using freshly read state, with a bounded number of attempts.
/// <see cref="ServerRolledBackTransaction"/> is true only for a deadlock (1213), where InnoDB rolls back the
/// whole transaction. A lock wait timeout (1205) rolls back only the failed statement by default
/// (<c>innodb_rollback_on_timeout=OFF</c>): the transaction stays open and must still be rolled back, which
/// <c>TransactionRunner</c> does.
/// </remarks>
public sealed record PersistenceFailure(
    PersistenceFailureKind Kind,
    string DefaultCode,
    bool IsTransient,
    int? ProviderErrorNumber,
    string? ConstraintName)
{
    public bool ServerRolledBackTransaction => Kind == PersistenceFailureKind.Deadlock;

    /// <summary>
    /// Table of a duplicate-key constraint when MySQL reports it (<c>'table.key'</c>). Needed because MySQL names
    /// every primary key <c>PRIMARY</c>, so the key name alone cannot tell which table's key was violated.
    /// A schema name only; never a value.
    /// </summary>
    public string? ConstraintTable { get; init; }
}

/// <summary>Classifies EF Core / MySQL exceptions into <see cref="PersistenceFailure"/> (BE-M4-D02a).</summary>
public static partial class PersistenceErrorClassifier
{
    private const int MaxDepth = 16;

    /// <summary>
    /// BE-M4-D06: classifies ONLY when the exception chain really contains a database failure (MySQL provider
    /// exception, EF <see cref="DbUpdateException"/>, unknown commit outcome or unconfirmed rollback). Unrelated
    /// exceptions (for example a <see cref="TimeoutException"/> from elsewhere) return false and are never mistaken
    /// for database failures.
    /// </summary>
    /// <remarks>
    /// Order: (1) an unknown commit outcome or unconfirmed rollback ANYWHERE in the chain always wins (it can never
    /// be reported as a conflict, timeout or outage); (2) otherwise a cancellation anywhere in the chain is NOT a
    /// database failure (false: the caller keeps its cancellation handling); (3) otherwise a MySQL/EF exception.
    /// </remarks>
    public static bool TryClassifyDatabaseFailure(Exception exception, out PersistenceFailure failure)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (FindUncertainty(exception) is { } uncertain)
        {
            failure = uncertain;
            return true;
        }

        var isDatabaseFailure = false;
        var current = exception;
        for (var depth = 0; current is not null && depth < MaxDepth; depth++, current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                failure = null!;
                return false;
            }

            isDatabaseFailure |= current is MySqlException or DbUpdateException or Connections.MySqlConnectionGateTimeoutException;
        }

        failure = isDatabaseFailure ? Classify(exception) : null!;
        return isDatabaseFailure;
    }

    public static PersistenceFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // D06: commit/rollback uncertainty takes precedence over any provider error nested inside or around it.
        if (FindUncertainty(exception) is { } uncertain)
        {
            return uncertain;
        }

        var current = exception;
        for (var depth = 0; current is not null && depth < MaxDepth; depth++, current = current.InnerException)
        {
            switch (current)
            {
                case DbUpdateConcurrencyException:
                    return Failure(PersistenceFailureKind.ConcurrencyConflict, null, null);
                case Connections.MySqlConnectionGateTimeoutException:
                    // STEP 10-F: the connection could not even start (open gate wait expired): an availability failure.
                    return Failure(PersistenceFailureKind.ConnectionFailure, null, null);
                case MySqlException mySql:
                    return mySql.InnerException is TimeoutException
                        ? Failure(PersistenceFailureKind.CommandTimeout, mySql.Number, null)
                        : FromProviderError(mySql.Number, mySql.Message);
                case TimeoutException:
                    return Failure(PersistenceFailureKind.CommandTimeout, null, null);
            }
        }

        return Failure(PersistenceFailureKind.Unexpected, null, null);
    }

    private static PersistenceFailure? FindUncertainty(Exception exception)
    {
        var current = exception;
        for (var depth = 0; current is not null && depth < MaxDepth; depth++, current = current.InnerException)
        {
            switch (current)
            {
                case TransactionOutcomeUnknownException:
                    return Failure(PersistenceFailureKind.TransactionOutcomeUnknown, null, null);
                case TransactionRollbackUncertainException:
                    return Failure(PersistenceFailureKind.RollbackUnconfirmed, null, null);
            }
        }

        return null;
    }

    /// <summary>
    /// Classifies a MySQL server/client error number. The provider message is used only to extract a
    /// schema constraint name (never data values); the message itself is discarded.
    /// </summary>
    public static PersistenceFailure FromProviderError(int errorNumber, string? providerMessage)
    {
        var kind = KindOf(errorNumber);
        var constraint = kind switch
        {
            PersistenceFailureKind.DuplicateKey => Extract(DuplicateKeyName(), providerMessage),
            PersistenceFailureKind.ForeignKeyViolation => Extract(ForeignKeyName(), providerMessage),
            _ => null,
        };
        var table = kind == PersistenceFailureKind.DuplicateKey
            ? Extract(DuplicateKeyName(), providerMessage, "table")
            : null;

        return Failure(kind, errorNumber, constraint) with { ConstraintTable = table };
    }

    public static PersistenceFailureKind KindOf(int errorNumber) => errorNumber switch
    {
        1022 or 1062 or 1169 or 1586 => PersistenceFailureKind.DuplicateKey,
        1216 or 1217 or 1451 or 1452 => PersistenceFailureKind.ForeignKeyViolation,
        1213 => PersistenceFailureKind.Deadlock,
        1205 => PersistenceFailureKind.LockTimeout,
        1040 or 1042 or 1043 or 1045 or 1129 or 1130 or 1158 or 1159 or 1160 or 1161 or 2002 or 2003 or 2006
            or 2013 or 2055 => PersistenceFailureKind.ConnectionFailure,
        _ => PersistenceFailureKind.Unexpected,
    };

    private static PersistenceFailure Failure(PersistenceFailureKind kind, int? number, string? constraint) =>
        new(kind, DefaultCodeOf(kind), IsTransient(kind), number, constraint);

    private static string DefaultCodeOf(PersistenceFailureKind kind) => kind switch
    {
        PersistenceFailureKind.DuplicateKey => SharedErrorCodes.Conflict,
        PersistenceFailureKind.ForeignKeyViolation => SharedErrorCodes.Conflict,
        PersistenceFailureKind.ConcurrencyConflict => SharedErrorCodes.RevisionConflict,
        PersistenceFailureKind.Deadlock => SharedErrorCodes.DependencyUnavailable,
        PersistenceFailureKind.LockTimeout => SharedErrorCodes.DependencyUnavailable,
        PersistenceFailureKind.ConnectionFailure => SharedErrorCodes.DependencyUnavailable,
        PersistenceFailureKind.CommandTimeout => SharedErrorCodes.DependencyTimeout,
        _ => SharedErrorCodes.InternalError,
    };

    /// <summary>
    /// True for lock contention that may clear (deadlock, lock wait timeout). It says nothing about retry
    /// safety; see <see cref="PersistenceFailure"/> remarks. Commit-unknown, rollback-unconfirmed and command
    /// timeouts are deliberately not transient.
    /// </summary>
    private static bool IsTransient(PersistenceFailureKind kind) =>
        kind is PersistenceFailureKind.Deadlock or PersistenceFailureKind.LockTimeout;

    private static string? Extract(Regex pattern, string? message, string group = "name")
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }

        var match = pattern.Match(message);
        return match.Success && match.Groups[group].Success ? match.Groups[group].Value : null;
    }

    // MySQL: "Duplicate entry '<value>' for key '<table>.<key>'" — only the table and key names are captured,
    // anchored at the end of the message so a value can never be mistaken for them.
    [GeneratedRegex(@"for key '(?:(?<table>[A-Za-z0-9_]{1,64})\.)?(?<name>[A-Za-z0-9_]{1,64})'\s*$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex DuplicateKeyName();

    // MySQL: "... a foreign key constraint fails (`db`.`table`, CONSTRAINT `<name>` FOREIGN KEY ...".
    [GeneratedRegex(@"CONSTRAINT `(?<name>[A-Za-z0-9_]{1,64})` FOREIGN KEY", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ForeignKeyName();
}

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using BuildMind.API.Errors;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Transactions;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>
/// BE-M4-D06: consistent public translation of database failures that escape a feature (lead-approved mapping,
/// STEP 09-B; C11b, C5) and a structural guard against direct HTTP dependencies in persistence. No database is used.
/// </summary>
public sealed class PersistenceProblemMappingTests
{
    [Theory]
    [InlineData(1062, 409, "CONFLICT")]                 // duplicate key
    [InlineData(1213, 503, "DEPENDENCY_UNAVAILABLE")]   // deadlock (fully rolled back)
    [InlineData(1205, 503, "DEPENDENCY_UNAVAILABLE")]   // lock wait timeout
    [InlineData(1042, 503, "DEPENDENCY_UNAVAILABLE")]   // cannot connect (C11b: MySQL unreachable → 503)
    [InlineData(2013, 503, "DEPENDENCY_UNAVAILABLE")]   // lost connection
    [InlineData(1452, 500, "INTERNAL_ERROR")]           // FK: the feature must translate it; never guessed
    [InlineData(1064, 500, "INTERNAL_ERROR")]           // unexpected
    public void Provider_failures_map_by_kind(int number, int status, string code)
    {
        var mapping = PersistenceProblemMapping.Map(PersistenceErrorClassifier.FromProviderError(number, null));

        Assert.Equal((status, code), (mapping.Status, mapping.Code));
    }

    [Fact]
    public void Stale_concurrency_token_maps_to_revision_conflict()
    {
        var mapping = PersistenceProblemMapping.Map(PersistenceErrorClassifier.Classify(new DbUpdateConcurrencyException()));

        Assert.Equal((409, SharedErrorCodes.RevisionConflict), (mapping.Status, mapping.Code));
    }

    [Fact]
    public void Not_every_database_failure_gets_the_same_status()
    {
        var statuses = new[] { 1062, 1213, 1042, 1452 }
            .Select(n => PersistenceProblemMapping.Map(PersistenceErrorClassifier.FromProviderError(n, null)).Status)
            .Append(PersistenceProblemMapping.Map(PersistenceErrorClassifier.Classify(new TimeoutException())).Status)
            .Distinct();

        Assert.True(statuses.Count() >= 4);
    }

    [Fact]
    public void Deadlock_and_lock_wait_share_a_status_but_stay_distinct_internally()
    {
        var deadlock = PersistenceErrorClassifier.FromProviderError(1213, null);
        var lockWait = PersistenceErrorClassifier.FromProviderError(1205, null);

        Assert.Equal(PersistenceProblemMapping.Map(deadlock), PersistenceProblemMapping.Map(lockWait));
        Assert.NotEqual(deadlock.Kind, lockWait.Kind);
        Assert.True(deadlock.ServerRolledBackTransaction);   // InnoDB rolled back the whole transaction
        Assert.False(lockWait.ServerRolledBackTransaction);  // only the statement; the transaction must still be rolled back
    }

    [Fact]
    public void Commit_and_rollback_uncertainty_take_precedence_over_any_nested_provider_error()
    {
        Exception[] chains =
        [
            new TransactionRollbackUncertainException(new DbUpdateConcurrencyException("stale"), new IOException()),
            new TransactionOutcomeUnknownException(new DbUpdateException("x", new TimeoutException())),
            new DbUpdateConcurrencyException("wrapper", new TransactionRollbackUncertainException(new DbUpdateException("x"), new IOException())),
            new DbUpdateException("wrapper", new TransactionOutcomeUnknownException(new IOException())),
        ];

        foreach (var chain in chains)
        {
            Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(chain, out var failure));
            Assert.Contains(failure.Kind, new[] { PersistenceFailureKind.RollbackUnconfirmed, PersistenceFailureKind.TransactionOutcomeUnknown });
            Assert.Equal((500, SharedErrorCodes.InternalError), (PersistenceProblemMapping.Map(failure).Status, PersistenceProblemMapping.Map(failure).Code));
        }
    }

    [Fact]
    public void Cancellation_is_never_classified_as_a_database_failure()
    {
        Assert.False(PersistenceErrorClassifier.TryClassifyDatabaseFailure(new OperationCanceledException(), out _));
        Assert.False(PersistenceErrorClassifier.TryClassifyDatabaseFailure(
            new OperationCanceledException("x", new DbUpdateException("x", new TimeoutException())), out _));
        Assert.False(PersistenceErrorClassifier.TryClassifyDatabaseFailure(
            new DbUpdateException("x", new TaskCanceledException()), out _));
    }

    [Fact]
    public void Only_real_database_failures_are_classified()
    {
        Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(new DbUpdateConcurrencyException(), out var stale));
        Assert.Equal(PersistenceFailureKind.ConcurrencyConflict, stale.Kind);
        Assert.True(PersistenceErrorClassifier.TryClassifyDatabaseFailure(new DbUpdateException("x", new TimeoutException()), out var timeout));
        Assert.Equal(PersistenceFailureKind.CommandTimeout, timeout.Kind);

        Assert.False(PersistenceErrorClassifier.TryClassifyDatabaseFailure(new TimeoutException(), out _));
        Assert.False(PersistenceErrorClassifier.TryClassifyDatabaseFailure(new InvalidOperationException("x"), out _));
        Assert.False(PersistenceErrorClassifier.TryClassifyDatabaseFailure(new DependencyFailureException("python", DependencyFailureKind.Timeout), out _));
    }

    [Theory]
    [InlineData("stale-token", HttpStatusCode.Conflict, "REVISION_CONFLICT")]
    [InlineData("db-timeout", HttpStatusCode.GatewayTimeout, "DEPENDENCY_TIMEOUT")]
    [InlineData("db-unexpected", HttpStatusCode.InternalServerError, "INTERNAL_ERROR")]
    [InlineData("plain-timeout", HttpStatusCode.InternalServerError, "INTERNAL_ERROR")] // a non-database timeout is NOT a 504
    [InlineData("rollback-unconfirmed-conflict", HttpStatusCode.InternalServerError, "INTERNAL_ERROR")] // never a 409
    [InlineData("commit-unknown-duplicate", HttpStatusCode.InternalServerError, "INTERNAL_ERROR")] // never a 504
    [InlineData("cancelled-db", HttpStatusCode.InternalServerError, "INTERNAL_ERROR")] // server-side cancellation stays 500
    public async Task Escaped_database_failures_return_consistent_problem_details(string kind, HttpStatusCode expected, string code)
    {
        await AssertEscapedFailureAsync("Production", kind, expected, code);
    }

    [Theory]
    [InlineData("db-unexpected")]
    [InlineData("rollback-unconfirmed-conflict")]
    [InlineData("stale-token")]
    public async Task Development_logging_never_contains_database_exception_text(string kind)
    {
        await using var factory = new BuildMindApiFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(SyntheticTestEndpoints.Prefix + "/database-failure/" + kind);

        Assert.All(factory.Logs.Entries, e => Assert.DoesNotContain(SyntheticTestEndpoints.SyntheticSecret, e.AllText, StringComparison.Ordinal));
        // AllText includes the full exception text of any logged exception, so the check above covers it; in addition
        // the handler must not attach the exception object at all for database failures.
        Assert.DoesNotContain(factory.Logs.Entries, e => e.ExceptionObject is not null && e.Category.EndsWith("GlobalExceptionHandler", StringComparison.Ordinal));
        Assert.Contains(factory.Logs.Entries, e => e.Properties.ContainsKey("FailureKind"));
    }

    private static async Task AssertEscapedFailureAsync(string environment, string kind, HttpStatusCode expected, string code)
    {
        await using var factory = new BuildMindApiFactory(environment);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        using var response = await client.GetAsync(SyntheticTestEndpoints.Prefix + "/database-failure/" + kind);
        var problem = await ProblemResponse.ReadAsync(response);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, problem.GetString("code"));
        Assert.Equal("urn:buildmind:problem:" + code.ToLowerInvariant().Replace('_', '-'), problem.GetString("type"));
        Assert.Equal((int)expected, problem.GetInt("status"));
        Assert.DoesNotContain(SyntheticTestEndpoints.SyntheticSecret, problem.RawBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", problem.RawBody, StringComparison.Ordinal);
        Assert.All(factory.Logs.Entries, e => Assert.DoesNotContain(SyntheticTestEndpoints.SyntheticSecret, e.AllText, StringComparison.Ordinal));
        if (kind is not ("plain-timeout" or "cancelled-db"))
        {
            Assert.Contains(factory.Logs.Entries, e => e.Properties.ContainsKey("FailureKind") && e.Properties.ContainsKey("ProviderErrorNumber"));
        }
    }

    [Fact]
    public void Persistence_types_take_no_direct_http_or_internal_service_dependency()
    {
        // D06 structural guard ONLY: no persistence type takes (constructor) or holds (field) an HttpClient or a
        // BuildMind.Infrastructure.Integration type. It CANNOT detect indirect calls (for example a delegate passed
        // to TransactionRunner that calls a service, or a call through an interface from another namespace).
        // "No external call inside a transaction" remains a code-review rule documented on TransactionRunner.
        static bool IsHttpDependency(Type t) =>
            t == typeof(HttpClient) || t.Namespace == "BuildMind.Infrastructure.Integration";

        var offenders = typeof(BuildMind.Infrastructure.Persistence.BuildMindDbContext).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("BuildMind.Infrastructure.Persistence", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(c => c.GetParameters().Select(p => (t, p.ParameterType)))
                .Concat(t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Select(f => (t, f.FieldType))))
            .Where(pair => IsHttpDependency(pair.Item2))
            .Select(pair => $"{pair.t.FullName} → {pair.Item2.FullName}")
            .ToArray();

        Assert.True(offenders.Length == 0, "Persistence must not call internal services: " + string.Join("; ", offenders));
    }
}

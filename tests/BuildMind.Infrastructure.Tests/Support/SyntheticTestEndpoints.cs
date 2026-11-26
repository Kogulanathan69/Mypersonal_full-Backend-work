using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>
/// TEST-ONLY endpoints, registered exclusively through <see cref="BuildMindApiFactory"/>. They exist to
/// exercise shared framework behaviour where no product route exists yet; they are never part of the API.
/// </summary>
internal sealed class SyntheticTestEndpoints : IApiEndpointModule
{
    public const string Prefix = ApiRouteExtensions.ApiV1Prefix + "/__synthetic-tests";

    /// <summary>Synthetic marker standing in for a secret. It is not a real credential.</summary>
    public const string SyntheticSecret = "SYNTHETIC-SECRET-7f3a9c41";

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        var tests = apiV1.MapGroup("/__synthetic-tests");

        tests.MapGet("/throw", ThrowWithSecretMessage);
        tests.MapGet("/argument-error", ThrowArgumentException);
        tests.MapGet("/server-cancelled", ThrowServerSideCancellation);
        tests.MapGet("/bad-request/{status:int}", ThrowBadHttpRequest);

        tests.MapGet("/throw-with-segment/{segment}", ThrowWithSegment);

        tests.MapGet("/status/{status:int}", (int status) => Results.StatusCode(status));

        // An endpoint that (wrongly) puts sensitive text into a 500 problem; the shared customizer must reduce it.
        tests.MapGet("/leaky-500/{codeKind}", (string codeKind) => TypedResults.Problem(
            detail: $"Database password is {SyntheticSecret}",
            statusCode: StatusCodes.Status500InternalServerError,
            title: $"Leaky title {SyntheticSecret}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = codeKind == "feature" ? "SYNTHETIC_FEATURE_FAILURE" : $"bad-code-{SyntheticSecret}",
                ["debug"] = SyntheticSecret,
                ["connectionString"] = $"Server=db;Password={SyntheticSecret}",
            }));

        tests.MapPost("/json", (SyntheticPayload payload) => Results.NoContent());

        tests.MapGet("/validation", () => ApiProblems.ValidationFailed(
            new Dictionary<string, string[]> { ["name"] = ["Name is required."] }));

        tests.MapGet("/domain-rule", () => ApiProblems.DomainRuleViolation(
            "SYNTHETIC_RULE_VIOLATED",
            "The synthetic rule was violated.",
            new Dictionary<string, object?> { ["diagnostics"] = new[] { "synthetic-diagnostic" } }));

        tests.MapGet("/require-user", (ICurrentUser currentUser) =>
            Results.Ok(new { userId = currentUser.GetRequiredUserId() }));

        // BE-M4-D05: drives the real Python typed client (stub transport in tests) through the public error contract.
        tests.MapGet("/python-health", async (BuildMind.Infrastructure.Integration.PythonServiceClient python, CancellationToken token) =>
        {
            await python.CheckHealthAsync(token);
            return Results.NoContent();
        });

        tests.MapGet("/dependency-failure/{kind}", ThrowDependencyFailure);

        // BE-M4-D06: database failures escaping a feature (EF exception types; no database involved).
        tests.MapGet("/database-failure/{kind}", ThrowDatabaseFailure);

        tests.MapGet("/current-user", (ICurrentUser currentUser) => Results.Ok(new
        {
            isAuthenticated = currentUser.IsAuthenticated,
            userId = currentUser.UserId,
            roles = currentUser.Roles.OrderBy(role => role, StringComparer.Ordinal).ToArray(),
        }));
    }

    private static IResult ThrowWithSecretMessage() =>
        throw new InvalidOperationException(
            $"Server=db.internal;Uid=root;Password={SyntheticSecret};Path=C:\\private\\keys");

    private static IResult ThrowWithSegment(string segment) =>
        throw new InvalidOperationException($"Failure for segment {segment}");

    private static IResult ThrowArgumentException() =>
        throw new ArgumentException($"Invalid internal argument {SyntheticSecret}");

    private static IResult ThrowServerSideCancellation() =>
        throw new OperationCanceledException($"Server-side cancellation {SyntheticSecret}");

    private static IResult ThrowDatabaseFailure(string kind) => throw (kind switch
    {
        "stale-token" => new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException($"Stale {SyntheticSecret}"),
        "db-timeout" => new Microsoft.EntityFrameworkCore.DbUpdateException($"Save {SyntheticSecret}", new TimeoutException()),
        "db-unexpected" => new Microsoft.EntityFrameworkCore.DbUpdateException($"Save {SyntheticSecret}"),
        "plain-timeout" => new TimeoutException($"Not a database timeout {SyntheticSecret}"),
        "rollback-unconfirmed-conflict" => new BuildMind.Infrastructure.Persistence.Transactions.TransactionRollbackUncertainException(
            new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException($"Stale {SyntheticSecret}"),
            new InvalidOperationException($"Rollback {SyntheticSecret}")),
        "commit-unknown-duplicate" => new BuildMind.Infrastructure.Persistence.Transactions.TransactionOutcomeUnknownException(
            new Microsoft.EntityFrameworkCore.DbUpdateException($"Commit {SyntheticSecret}", new TimeoutException())),
        "cancelled-db" => new OperationCanceledException($"Cancelled {SyntheticSecret}",
            new Microsoft.EntityFrameworkCore.DbUpdateException($"Save {SyntheticSecret}", new TimeoutException())),
        _ => new InvalidOperationException("unknown synthetic kind"),
    });

    private static IResult ThrowDependencyFailure(DependencyFailureKind kind) =>
        throw new DependencyFailureException("python", kind, 503);

    private static IResult ThrowBadHttpRequest(int status) =>
        throw new BadHttpRequestException($"Framework detail {SyntheticSecret}", status);

    internal sealed record SyntheticPayload(string Name, int Count);
}

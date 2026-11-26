using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Persistence.Errors;

namespace BuildMind.API.Errors;

/// <summary>
/// BE-M4-D06: public translation of database failures that escape a feature without being turned into a port
/// result. Consistent with C11b ("MySQL unreachable → persistence-backed endpoints 503 DEPENDENCY_UNAVAILABLE") and
/// C5 shared codes; D06 acceptance ("stale token returns the agreed conflict"). Not every database error maps to the
/// same status, and the outcome is never guessed: unknown commit outcomes, unconfirmed rollbacks, foreign-key
/// violations and unexpected errors stay 500 (features must translate FK violations themselves).
/// </summary>
public static class PersistenceProblemMapping
{
    public sealed record Mapping(int Status, string Code, string Detail);

    public static Mapping Map(PersistenceFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.Kind switch
        {
            PersistenceFailureKind.ConcurrencyConflict => new(
                StatusCodes.Status409Conflict, SharedErrorCodes.RevisionConflict,
                "The resource was changed by another request. Reload it and try again."),
            PersistenceFailureKind.DuplicateKey => new(
                StatusCodes.Status409Conflict, SharedErrorCodes.Conflict,
                "The request conflicts with existing data."),
            PersistenceFailureKind.ConnectionFailure or PersistenceFailureKind.Deadlock or PersistenceFailureKind.LockTimeout => new(
                StatusCodes.Status503ServiceUnavailable, SharedErrorCodes.DependencyUnavailable,
                "The database is temporarily unavailable."),
            PersistenceFailureKind.CommandTimeout => new(
                StatusCodes.Status504GatewayTimeout, SharedErrorCodes.DependencyTimeout,
                "The database did not respond in time."),
            _ => new(
                StatusCodes.Status500InternalServerError, SharedErrorCodes.InternalError,
                ProblemDetailsDefaults.InternalErrorDetail),
        };
    }
}

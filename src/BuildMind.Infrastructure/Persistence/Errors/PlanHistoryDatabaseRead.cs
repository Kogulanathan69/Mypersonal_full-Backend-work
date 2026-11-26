using BuildMind.Application.Abstractions.Errors;
using MySql.Data.MySqlClient;

namespace BuildMind.Infrastructure.Persistence.Errors;

internal static class PlanHistoryDatabaseRead
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (MySqlException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (PersistenceErrorClassifier.TryClassifyDatabaseFailure(exception, out var failure))
        {
            switch (failure.DefaultCode)
            {
                case SharedErrorCodes.DependencyTimeout:
                    throw new DependencyFailureException("mysql", DependencyFailureKind.Timeout);
                case SharedErrorCodes.DependencyUnavailable:
                    throw new DependencyFailureException("mysql", DependencyFailureKind.Unavailable);
                default:
                    throw;
            }
        }
    }
}

using BuildMind.Application.Features.Dashboard.Abstractions;
using BuildMind.Application.Features.Dashboard.Contracts;

namespace BuildMind.Application.Features.Dashboard.Services;

public interface IDashboardApplicationService
{
    Task<DashboardSummaryResult> GetSummaryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Aggregates only data supplied through the server-side current-user port. Owner-binding checks are
/// defense against a faulty adapter, not proof that the adapter authenticated the user or filtered SQL.
/// No exception messages, owner identities or source rows are exposed in failure results.
/// </summary>
public sealed class DashboardApplicationService : IDashboardApplicationService
{
    private readonly IDashboardProjectSource _source;

    public DashboardApplicationService(IDashboardProjectSource source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<DashboardSummaryResult> GetSummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DashboardProjectReadResult? sourceResult;
        try
        {
            sourceResult = await _source.GetForCurrentUserAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DashboardSummaryResult.Failure(DashboardSummaryStatus.DependencyTimeout);
        }
        catch (ArgumentException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DashboardSummaryResult.Failure(DashboardSummaryStatus.InvalidDependencyResponse);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Includes dependency-owned cancellation when this request was not cancelled.
            // No fake success or forwarding of raw exception details.
            return DashboardSummaryResult.Failure(DashboardSummaryStatus.DependencyUnavailable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (sourceResult is null)
            return DashboardSummaryResult.Failure(DashboardSummaryStatus.InvalidDependencyResponse);
        if (sourceResult.Status == DashboardProjectReadStatus.Unauthenticated)
            return DashboardSummaryResult.Failure(DashboardSummaryStatus.Unauthenticated);
        if (sourceResult.Status == DashboardProjectReadStatus.Unavailable)
            return DashboardSummaryResult.Failure(DashboardSummaryStatus.DependencyUnavailable);

        var rows = sourceResult.Projects!;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.OwnerId != sourceResult.AuthenticatedOwnerId)
                return DashboardSummaryResult.Failure(DashboardSummaryStatus.InvalidDependencyResponse);
        }

        try
        {
            // The existing DTO owns aggregation and duplicate-ID validation. Never silently deduplicate.
            var summary = new DashboardSummaryDto(rows.Select(row => row.Project));
            cancellationToken.ThrowIfCancellationRequested();
            return DashboardSummaryResult.Available(summary);
        }
        catch (ArgumentException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DashboardSummaryResult.Failure(DashboardSummaryStatus.InvalidDependencyResponse);
        }
    }
}

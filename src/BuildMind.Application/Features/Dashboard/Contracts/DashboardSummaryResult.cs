namespace BuildMind.Application.Features.Dashboard.Contracts;

// Application outcomes only; these do not define shared HTTP errors or authoritative project stages.
public enum DashboardSummaryStatus
{
    Available, Unauthenticated, DependencyUnavailable, DependencyTimeout, InvalidDependencyResponse
}

public sealed class DashboardSummaryResult
{
    public DashboardSummaryStatus Status { get; }
    public DashboardSummaryDto? Summary { get; }

    private DashboardSummaryResult(DashboardSummaryStatus status, DashboardSummaryDto? summary)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if ((status == DashboardSummaryStatus.Available) != (summary is not null))
            throw new ArgumentException("Summary payload must match the operation outcome.", nameof(summary));
        Status = status;
        Summary = summary;
    }

    public static DashboardSummaryResult Available(DashboardSummaryDto summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new(DashboardSummaryStatus.Available, summary);
    }

    public static DashboardSummaryResult Failure(DashboardSummaryStatus status)
    {
        if (status == DashboardSummaryStatus.Available)
            throw new ArgumentException("Available requires a summary.", nameof(status));
        return new(status, null);
    }
}

using BuildMind.Application.Features.Dashboard.Contracts;
using BuildMind.Application.Features.Dashboard.Services;

namespace BuildMind.Application.Features.Dashboard.Queries;

public sealed class DashboardSummaryQueryHandler
{
    private readonly IDashboardApplicationService _service;

    public DashboardSummaryQueryHandler(IDashboardApplicationService service) =>
        _service = service ?? throw new ArgumentNullException(nameof(service));

    public Task<DashboardSummaryResult> HandleAsync(DashboardSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        return _service.GetSummaryAsync(cancellationToken);
    }
}

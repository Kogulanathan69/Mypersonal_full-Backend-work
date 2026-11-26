using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Application.Features.Requirements.Abstractions;

/// <summary>Isolated D01-D08 application contract; not a verified atomic authorization protocol.</summary>
public interface IRequirementsApplicationService
{
    Task<RequirementsOperationResult> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<RequirementsOperationResult> SaveAsync(Guid projectId, SaveRequirementsRequest? request, CancellationToken cancellationToken);
}

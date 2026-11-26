using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Application.Features.Requirements.Abstractions;

/// <summary>Provisional isolated Day 04 contract; GET returns the complete current snapshot.</summary>
public interface IRequirementsSectionApplicationService
{
    Task<RequirementsOperationResult> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<RequirementsOperationResult> SaveDesignMethodAsync(Guid projectId, SaveDesignMethodRequest request, CancellationToken cancellationToken);
    Task<RequirementsOperationResult> SaveSpacesAsync(Guid projectId, SaveAdditionalSpacesRequest request, CancellationToken cancellationToken);
    Task<RequirementsOperationResult> SaveRelationshipsAsync(Guid projectId, SaveRoomRelationshipsRequest request, CancellationToken cancellationToken);
}

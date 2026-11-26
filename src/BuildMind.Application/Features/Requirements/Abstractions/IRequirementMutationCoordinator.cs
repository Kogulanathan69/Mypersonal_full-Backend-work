using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Application.Features.Requirements.Abstractions;

public interface IRequirementMutationCoordinator
{
    Task<RequirementMutationResult> ExecuteAsync(Guid projectId, Guid operationId, RequirementMutation? mutation, CancellationToken token);
}

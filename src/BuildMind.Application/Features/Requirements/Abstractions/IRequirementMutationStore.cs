using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Requirements.Abstractions;

public interface IRequirementMutationStore
{
    Task<RequirementMutationResult> ObserveAsync(RequirementMutationScope scope, RequirementMutation? mutation, CancellationToken token);
    Task<RequirementMutationResult> CommitAsync(RequirementMutationScope scope, RequirementMutation mutation,
        RequirementRevision proposed, CancellationToken token);
}

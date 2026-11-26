using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Application.Features.Requirements.Services;

public sealed class RequirementMutationCoordinator(ICurrentUser user, IProjectAuthorizationPort authorization,
    RequirementRevisionReader revisions, IRequirementCatalogueProvider catalogues, IRequirementMutationStore store,
    TimeProvider clock) : IRequirementMutationCoordinator
{
    public async Task<RequirementMutationResult> ExecuteAsync(Guid projectId, Guid operationId, RequirementMutation? mutation,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId is not Guid actor || actor == Guid.Empty || user.GetRequiredUserId() != actor)
            return new(RequirementMutationStatus.Denied);
        var snapshot = await RequirementsApplicationService.ReadSnapshotAsync(authorization, projectId, actor, token);
        if (snapshot is null) return new(RequirementMutationStatus.Denied);
        var scope = new RequirementMutationScope(projectId, actor, operationId, snapshot.Version.Value);
        var observed = await store.ObserveAsync(scope, mutation, token);
        if (mutation is null || observed.Status != RequirementMutationStatus.Absent) return observed;

        var fields = mutation.Fields;
        var current = await revisions.GetCurrentAsync(projectId, token);
        if (current?.Id != fields.ExpectedRevisionId)
        {
            observed = await store.ObserveAsync(scope, mutation, token);
            return observed.Status == RequirementMutationStatus.Absent
                ? new(RequirementMutationStatus.Failed, Failure: new(RequirementsOperationStatus.RevisionConflict))
                : observed;
        }
        var request = fields;
        if (mutation.Category != RequirementMutationCategory.Full)
        {
            var basis = new RequirementRevisionDto(current ?? throw new InvalidOperationException("Section base required."));
            request = new(fields.ExpectedRevisionId, fields.CatalogueVersion, basis.FloorCount, basis.GroundFloorRooms,
                mutation.Category == RequirementMutationCategory.Spaces ? fields.AdditionalSpaces : basis.AdditionalSpaces,
                mutation.Category == RequirementMutationCategory.Relationships ? fields.Relationships : basis.Relationships,
                mutation.Category == RequirementMutationCategory.DesignMethod ? fields.DesignMethod : basis.DesignMethod);
        }
        var catalogue = await catalogues.GetForProjectAsync(projectId, token);
        token.ThrowIfCancellationRequested();
        var validation = RequirementContractValidator.Validate(request, catalogue);
        if (RequirementsApplicationService.ValidationFailure(validation) is { } failure)
            return new(RequirementMutationStatus.Failed, Failure: failure);
        var proposed = validation.CreateRevision(Guid.NewGuid(), projectId, checked((current?.RevisionNumber ?? 0) + 1), clock.GetUtcNow());
        return await store.CommitAsync(scope, mutation, proposed, token);
    }
}

using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

// Explicit test-only consumers. These are configurable contract doubles, not live member integrations.
internal sealed class TestProjectOwnershipGate(Func<Guid, Guid, CancellationToken, Task<bool>> handler)
    : IProjectOwnershipGate
{
    public Task<bool> OwnsProjectAsync(Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken) =>
        handler(projectId, authenticatedUserId, cancellationToken);
}

internal sealed class TestProjectLifecycleEligibilityPort(
    Func<Guid, Guid, CancellationToken, Task<ProjectLifecycleEligibility>> handler)
    : IProjectLifecycleEligibilityPort
{
    public Task<ProjectLifecycleEligibility> GetConfirmationEligibilityAsync(
        Guid projectId, Guid authenticatedUserId, CancellationToken cancellationToken) =>
        handler(projectId, authenticatedUserId, cancellationToken);
}

internal sealed class TestConfirmedLandRevisionPort(
    Func<Guid, Guid, CancellationToken, Task<ConfirmedLandRevision?>> handler)
    : IConfirmedLandRevisionPort
{
    public Task<ConfirmedLandRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken) =>
        handler(projectId, revisionId, cancellationToken);
}

internal sealed class TestRequirementRevisionPort(
    Func<Guid, Guid, CancellationToken, Task<RequirementRevisionReference?>> handler)
    : IRequirementRevisionPort
{
    public Task<RequirementRevisionReference?> GetAsync(
        Guid projectId, Guid revisionId, CancellationToken cancellationToken) =>
        handler(projectId, revisionId, cancellationToken);
}

/// <summary>Test-only authorization-aware requirement dependency.</summary>
internal sealed class TestAuthorizedRequirementRevisionPort(
    Func<Guid, Guid, Guid, CancellationToken, Task<RequirementRevisionAccessResult>> handler)
    : IAuthorizedRequirementRevisionPort
{
    public Task<RequirementRevisionAccessResult> GetAsync(
        Guid projectId,
        Guid revisionId,
        Guid authenticatedActorId,
        CancellationToken cancellationToken) =>
        handler(projectId, revisionId, authenticatedActorId, cancellationToken);
}

internal sealed class TestPlanEditorComputeClient(
    Func<PlanEditorComputeRequest, CancellationToken, Task<PlanEditResultDto>> handler)
    : IPlanEditorComputeClient
{
    public Task<PlanEditResultDto> PreviewAsync(PlanEditorComputeRequest request, CancellationToken cancellationToken) =>
        handler(request, cancellationToken);
}

internal sealed class TestAiEditProposalPort(
    Func<Guid, Guid, Guid, string, string, CancellationToken, Task<AiEditProposal>> handler)
    : IAiEditProposalPort
{
    public Task<AiEditProposal> ProposeAsync(
        Guid projectId, Guid planId, Guid baseRevisionId, string customerInstruction,
        string correlationId, CancellationToken cancellationToken) =>
        handler(projectId, planId, baseRevisionId, customerInstruction, correlationId, cancellationToken);
}

internal sealed class TestPlanValidationEvidenceVerifier(
    Func<Guid, Guid, PlanValidationReferenceDto, string, CancellationToken, Task<ValidationEvidenceStatus>> handler)
    : IPlanValidationEvidenceVerifier
{
    public Task<ValidationEvidenceStatus> VerifyAsync(
        Guid projectId, Guid planId, PlanValidationReferenceDto evidence, string exactGeometryHash,
        CancellationToken cancellationToken) =>
        handler(projectId, planId, evidence, exactGeometryHash, cancellationToken);
}

internal sealed class TestApplicableInputRevisionSource(
    Func<Guid, CancellationToken, Task<ApplicableInputRevisions>> handler)
    : IApplicableInputRevisionSource
{
    public Task<ApplicableInputRevisions> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken) =>
        handler(projectId, cancellationToken);
}

internal sealed class TestValidationFreshnessPolicy(bool isFresh) : IValidationFreshnessPolicy
{
    public bool IsFresh(PlanRevisionSnapshot revision, DateTimeOffset now) => isFresh;
}

internal sealed class TestFinalConfirmationTransaction(
    Func<PlanConfirmationSnapshot, PlanRevisionSnapshot, CancellationToken, Task<ConfirmationResult>> handler)
    : IFinalConfirmationTransaction
{
    public Task<ConfirmationResult> ConfirmAtomicallyAsync(
        PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision,
        CancellationToken cancellationToken) => handler(confirmation, exactRevision, cancellationToken);
}

internal sealed class TestConfirmedPlanSnapshotSink(
    Func<PlanConfirmationSnapshot, PlanRevisionSnapshot, CancellationToken, Task>? handler = null)
    : IConfirmedPlanSnapshotSink
{
    public Guid? PublishedRevisionId { get; private set; }

    public async Task PublishAsync(
        PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision,
        CancellationToken cancellationToken)
    {
        if (handler is not null) await handler(confirmation, exactRevision, cancellationToken);
        PublishedRevisionId = exactRevision.RevisionId;
    }
}

internal sealed class TestPlanRevisionStore(PlanRevisionSnapshot initial) : IPlanRevisionStore
{
    private readonly List<PlanRevisionSnapshot> revisions = [initial];
    private readonly object sync = new();

    public bool ThrowOnAppend { get; init; }

    public Task<PlanRevisionSnapshot?> GetAsync(
        Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(() => revisions.SingleOrDefault(item =>
            item.ProjectId == projectId && item.PlanId == planId && item.RevisionId == revisionId)));
    }

    public Task<PlanRevisionSnapshot?> GetCurrentAsync(
        Guid projectId, Guid planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(() => revisions
            .Where(item => item.ProjectId == projectId && item.PlanId == planId)
            .MaxBy(item => item.VersionNumber)));
    }

    public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
        Guid projectId, Guid planId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PlanRevisionSnapshot>>(Read(() => revisions
            .Where(item => item.ProjectId == projectId && item.PlanId == planId).ToArray()));
    }

    public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
        Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ThrowOnAppend) throw new InvalidOperationException("Explicit persistence test-double failure.");
        lock (sync)
        {
            if (proposedRevision.EditCommandId is not null && revisions.Any(item =>
                    item.ProjectId == proposedRevision.ProjectId && item.PlanId == proposedRevision.PlanId
                    && item.EditCommandId == proposedRevision.EditCommandId))
                return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.DuplicateCommand));
            if (revisions.MaxBy(item => item.VersionNumber)!.RevisionId != expectedBaseRevisionId)
                return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.RevisionConflict));
            revisions.Add(proposedRevision);
            return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.Created, proposedRevision));
        }
    }

    private T Read<T>(Func<T> action) { lock (sync) return action(); }
}

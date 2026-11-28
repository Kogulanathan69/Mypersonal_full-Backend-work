using System.Security.Cryptography;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.Entities;
using BuildMind.Infrastructure.Persistence.Mapping;
using BuildMind.Infrastructure.Persistence.Records.Requirements;
using BuildMind.Infrastructure.Persistence.Repositories.Projects;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence.Repositories.Requirements;

internal sealed class RequirementMutationStore(BuildMindDbContext context, TransactionRunner transactions,
    RequirementRevisionRepository revisions, ICurrentUser? currentUser = null) : IRequirementMutationStore
{
    public Task<RequirementMutationResult> ObserveAsync(RequirementMutationScope scope, RequirementMutation? mutation,
        CancellationToken token) => ExecuteAsync(scope, mutation, null, token);

    public Task<RequirementMutationResult> CommitAsync(RequirementMutationScope scope, RequirementMutation mutation,
        RequirementRevision proposed, CancellationToken token) => ExecuteAsync(scope, mutation, proposed, token);

    private async Task<RequirementMutationResult> ExecuteAsync(RequirementMutationScope scope, RequirementMutation? mutation,
        RequirementRevision? proposed, CancellationToken token)
    {
        if (proposed is not null && proposed.ProjectId != scope.ProjectId)
            throw new ArgumentException("Revision project mismatch.", nameof(proposed));
        var digest = mutation is null ? null : RequirementMutationFingerprint.Compute(scope.ProjectId, scope.ActorId,
            scope.OperationId, scope.Version, mutation.Fields.ExpectedRevisionId, RequirementMutationCanonicalPayload.Encode(mutation));
        try
        {
            return await transactions.ExecuteAsync<RequirementMutationResult>(async lockedToken =>
            {
                if (!await new ProjectMutationAuthorization(context).LockAndCheckAsync(scope.ProjectId, scope.ActorId,
                        scope.Version, lockedToken, currentUser?.TokenVersion))
                    return new(RequirementMutationStatus.Denied);
                var receipt = await context.Set<RequirementMutationReceiptRecord>().AsNoTracking().SingleOrDefaultAsync(
                    r => r.ProjectId == scope.ProjectId && r.ActorId == scope.ActorId && r.OperationId == scope.OperationId,
                    lockedToken);
                if (receipt is not null)
                {
                    if (receipt.AuthorizationVersion != scope.Version) return new(RequirementMutationStatus.Denied);
                    if (receipt.FormatVersion != 1 || receipt.Digest.Length != 32)
                        throw new InvalidDataException("Invalid requirement mutation receipt.");
                    if (digest is not null && !CryptographicOperations.FixedTimeEquals(receipt.Digest, digest))
                        return new(RequirementMutationStatus.Reused);
                    var saved = await revisions.GetAsync(scope.ProjectId, receipt.SavedRevisionId, lockedToken)
                        ?? throw new InvalidDataException("Receipt revision unavailable.");
                    return new(RequirementMutationStatus.Committed, new RequirementRevisionDto(saved));
                }
                if (proposed is null) return new(RequirementMutationStatus.Absent);
                await revisions.WriteRevisionAsync(mutation!.Fields.ExpectedRevisionId, proposed,
                    RequirementRevisionMapper.ToRecord(proposed), lockedToken);
                context.Add(new RequirementMutationReceiptRecord
                {
                    ProjectId = scope.ProjectId, ActorId = scope.ActorId, OperationId = scope.OperationId,
                    AuthorizationVersion = scope.Version, FormatVersion = 1, Category = (byte)mutation.Category,
                    Digest = digest!, SavedRevisionId = proposed.Id
                });
                await context.SaveChangesAsync(lockedToken);
                return new(RequirementMutationStatus.Committed, new RequirementRevisionDto(proposed));
            }, token);
        }
        catch (RequirementRevisionRepository.SaveRejected rejected) when (rejected.Status == RequirementSaveStatus.RevisionConflict)
        {
            return new(RequirementMutationStatus.Failed, Failure: new(RequirementsOperationStatus.RevisionConflict));
        }
        catch (Exception ex) when (proposed is not null && ex is (TransactionOutcomeUnknownException or TransactionRollbackUncertainException))
        {
            return new(RequirementMutationStatus.Unknown);
        }
    }
}

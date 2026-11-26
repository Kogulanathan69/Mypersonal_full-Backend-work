using BuildMind.Application.Features.ArchitectRequests.Contracts;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.ArchitectRequests.Services;

// Isolated candidate only. All actor identifiers must come from trusted authentication.
public enum ArchitectBoundaryStatus { Accepted, Hidden, Conflict, NotEligible, Unavailable, Unknown }
public sealed record ArchitectReceipt(ArchitectBoundaryStatus Status, Guid Actor, ArchitectRequest? Request = null,
    ArchitectRequest? Previous = null);
public sealed record ArchitectListReceipt(ArchitectBoundaryStatus Status, Guid Actor, Guid ProjectId,
    IReadOnlyList<ArchitectRequest>? Requests = null);
public enum ArchitectOutcome { Available, Hidden, Invalid, Conflict, NotEligible, Unavailable, OutcomeUnknown }
public sealed record ArchitectResult(ArchitectOutcome Outcome, ArchitectRequestDto? Request = null, Guid? AttemptId = null);
public sealed record ArchitectListResult(ArchitectOutcome Outcome, IReadOnlyList<ArchitectRequestDto>? Requests = null);

// Not a sequential wrapper around IArchitectRequestRepository. Production binding requires M1/M3/M4/G12.
public interface IArchitectWorkflowBoundaryCandidate
{
    // Atomic actor/owner, current policy, recipient, exact selected document revisions and report eligibility checks.
    // Report must be Ready, private, intact and tied to the eligible persisted confirmed revision. No dispatch.
    // Persist Pending and consent together, deduplicate by approved operation identity; unknown needs reconciliation.
    Task<ArchitectReceipt> CreateAsync(Guid actor, ArchitectRequest proposed, CancellationToken token);
    // Only current authorized owner-scoped records; missing and unauthorized both Hidden.
    Task<ArchitectReceipt> ReadAsync(Guid actor, Guid project, Guid request, CancellationToken token);
    // Enforce bound before materialization; if too many return Unavailable, never silently truncate.
    Task<ArchitectListReceipt> ListAsync(Guid actor, Guid project, int maximum, CancellationToken token);
    // Atomically reauthorize, enforce withdrawal policy/version, record actual event, revoke future grants.
    // Return exact previous and saved states on Accepted; no statement about deleting delivered copies.
    Task<ArchitectReceipt> WithdrawAsync(Guid actor, Guid project, Guid request, long version, CancellationToken token);
}

public sealed class ArchitectWorkflowCandidate(IArchitectWorkflowBoundaryCandidate boundary, TimeProvider clock)
{
    private readonly IArchitectWorkflowBoundaryCandidate boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
    private readonly TimeProvider clock = clock ?? throw new ArgumentNullException(nameof(clock));
    public const int MaximumDocuments = 100;
    public const int MaximumList = 100;
    private static bool Identity(Guid actor, Guid project) => actor != Guid.Empty && project != Guid.Empty;
    private static bool IsOwnerRecord(ArchitectRequest? r, Guid actor, Guid project) => r is not null &&
        r.ProjectId == project && r.RequestedBy == actor;
    private static bool SameConsent(ShareConsent a, ShareConsent b) => a.Id == b.Id && a.RequestId == b.RequestId &&
        a.ProjectId == b.ProjectId && a.GrantedBy == b.GrantedBy && a.RecipientId == b.RecipientId &&
        a.ReportId == b.ReportId && a.DocumentIds.SequenceEqual(b.DocumentIds) &&
        a.PolicyVersion == b.PolicyVersion && a.GrantedAtUtc.EqualsExact(b.GrantedAtUtc);
    private static bool SameImmutable(ArchitectRequest a, ArchitectRequest b) => SameConsent(a.Consent,b.Consent) &&
        a.RequestedAtUtc.EqualsExact(b.RequestedAtUtc);
    private static ArchitectOutcome Failure(ArchitectBoundaryStatus status, bool write) => status switch {
        ArchitectBoundaryStatus.Hidden => ArchitectOutcome.Hidden,
        ArchitectBoundaryStatus.Conflict => ArchitectOutcome.Conflict,
        ArchitectBoundaryStatus.NotEligible => ArchitectOutcome.NotEligible,
        ArchitectBoundaryStatus.Unavailable => ArchitectOutcome.Unavailable,
        _ => write ? ArchitectOutcome.OutcomeUnknown : ArchitectOutcome.Unavailable };
    private static bool Empty(ArchitectReceipt receipt) => receipt.Request is null && receipt.Previous is null;

    public async Task<ArchitectResult> CreateAsync(Guid actor, Guid project, CreateArchitectRequestDto input,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!Identity(actor,project)) return new(ArchitectOutcome.Hidden);
        if (input is null || !input.ExplicitConsent || input.RecipientId == Guid.Empty || input.ReportId == Guid.Empty ||
            input.DocumentIds is not { } docs || docs.Count > MaximumDocuments || docs.Any(x=>x==Guid.Empty) ||
            docs.Distinct().Count()!=docs.Count || (input.ReportId is null && docs.Count==0) ||
            string.IsNullOrWhiteSpace(input.AcknowledgedPolicyVersion) || input.AcknowledgedPolicyVersion.Length>128)
            return new(ArchitectOutcome.Invalid);
        var attempt = Guid.NewGuid();
        var now = clock.GetUtcNow();
        var proposed = ArchitectRequest.CreatePending(new ShareConsent(Guid.NewGuid(),attempt,project,actor,
            input.RecipientId,input.ReportId,docs,input.AcknowledgedPolicyVersion,now,true),now);
        token.ThrowIfCancellationRequested();
        ArchitectResult Unknown() => new(ArchitectOutcome.OutcomeUnknown, AttemptId: attempt);
        try {
            var receipt = await boundary.CreateAsync(actor,proposed,token);
            if (token.IsCancellationRequested || receipt is null || receipt.Actor!=actor) return Unknown();
            if (receipt.Status!=ArchitectBoundaryStatus.Accepted)
                return Empty(receipt) ? new(Failure(receipt.Status,true),AttemptId:attempt) : Unknown();
            var saved=receipt.Request;
            if (saved is null || receipt.Previous is not null || saved.Status!=ArchitectRequestStatus.Pending ||
                saved.StateVersion!=1 || !SameImmutable(saved,proposed)) return Unknown();
            return new(ArchitectOutcome.Available,new(saved),attempt);
        } catch (Exception e) when (e is not OutOfMemoryException) { return Unknown(); }
    }

    public async Task<ArchitectResult> GetAsync(Guid actor, Guid project, Guid request, CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if (!Identity(actor,project) || request==Guid.Empty) return new(ArchitectOutcome.Hidden);
        try {
            var receipt=await boundary.ReadAsync(actor,project,request,token);
            token.ThrowIfCancellationRequested();
            if(receipt is null || receipt.Actor!=actor || receipt.Previous is not null) return new(ArchitectOutcome.Unavailable);
            if(receipt.Status!=ArchitectBoundaryStatus.Accepted)
                return new(receipt.Status==ArchitectBoundaryStatus.Hidden && Empty(receipt) ? ArchitectOutcome.Hidden : ArchitectOutcome.Unavailable);
            var r=receipt.Request;
            return IsOwnerRecord(r,actor,project) && r!.Id==request ? new(ArchitectOutcome.Available,new(r)) : new(ArchitectOutcome.Unavailable);
        } catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
        catch(Exception e) when(e is not OutOfMemoryException) { return new(ArchitectOutcome.Unavailable); }
    }

    public async Task<ArchitectListResult> ListAsync(Guid actor, Guid project, CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(!Identity(actor,project)) return new(ArchitectOutcome.Hidden);
        try {
            var receipt=await boundary.ListAsync(actor,project,MaximumList,token);
            token.ThrowIfCancellationRequested();
            if(receipt is null || receipt.Actor!=actor || receipt.ProjectId!=project) return new(ArchitectOutcome.Unavailable);
            if(receipt.Status!=ArchitectBoundaryStatus.Accepted)
                return new(receipt.Status==ArchitectBoundaryStatus.Hidden && receipt.Requests is null ? ArchitectOutcome.Hidden : ArchitectOutcome.Unavailable);
            if(receipt.Requests is not { } list || list.Count>MaximumList) return new(ArchitectOutcome.Unavailable);
            var copy=list.ToArray();
            if(copy.Length>MaximumList || copy.Any(r=>!IsOwnerRecord(r,actor,project)) || copy.Select(r=>r.Id).Distinct().Count()!=copy.Length)
                return new(ArchitectOutcome.Unavailable);
            token.ThrowIfCancellationRequested();
            return new(ArchitectOutcome.Available,Array.AsReadOnly(copy.Select(r=>new ArchitectRequestDto(r)).ToArray()));
        } catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
        catch(Exception e) when(e is not OutOfMemoryException) { return new(ArchitectOutcome.Unavailable); }
    }

    public async Task<ArchitectResult> WithdrawAsync(Guid actor, Guid project, Guid request, long version, CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        if(!Identity(actor,project) || request==Guid.Empty) return new(ArchitectOutcome.Hidden);
        if(version!=1) return new(ArchitectOutcome.Conflict);
        try {
            var receipt=await boundary.WithdrawAsync(actor,project,request,version,token);
            if(token.IsCancellationRequested || receipt is null || receipt.Actor!=actor) return new(ArchitectOutcome.OutcomeUnknown);
            if(receipt.Status!=ArchitectBoundaryStatus.Accepted)
                return new(Empty(receipt) ? Failure(receipt.Status,true) : ArchitectOutcome.OutcomeUnknown);
            var prior=receipt.Previous; var saved=receipt.Request;
            if(!IsOwnerRecord(prior,actor,project) || !IsOwnerRecord(saved,actor,project) || prior!.Id!=request || saved!.Id!=request ||
                prior.Status!=ArchitectRequestStatus.Pending || prior.StateVersion!=version || saved.Status!=ArchitectRequestStatus.Withdrawn ||
                saved.StateVersion!=version+1 || !SameImmutable(prior,saved)) return new(ArchitectOutcome.OutcomeUnknown);
            return new(ArchitectOutcome.Available,new(saved));
        } catch(Exception e) when(e is not OutOfMemoryException) { return new(ArchitectOutcome.OutcomeUnknown); }
    }
}

// Structural selection check only. NEVER sufficient to authorize reading or delivering an artifact.
public static class ArchitectConsentSelection
{
    public static bool Includes(ArchitectRequest? request, Guid project, Guid recipient, Guid item, bool report) =>
        request is not null && project!=Guid.Empty && recipient!=Guid.Empty && item!=Guid.Empty &&
        request.ProjectId==project && request.RecipientId==recipient && request.Status==ArchitectRequestStatus.Pending &&
        (report ? request.Consent.ReportId==item : request.Consent.DocumentIds.Contains(item));
}

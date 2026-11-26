using BuildMind.Application.Features.ArchitectRequests.Contracts;
using BuildMind.Application.Features.ArchitectRequests.Services;
using BuildMind.Domain.Entities;

namespace BuildMind.Workflow.Tests;

public sealed class ArchitectWorkflowCandidateTests
{
    private static readonly Guid Actor=Guid.NewGuid(), Project=Guid.NewGuid(), Recipient=Guid.NewGuid(), Doc=Guid.NewGuid(), Pdf=Guid.NewGuid();
    private static readonly DateTimeOffset Now=new(2026,11,20,0,0,0,TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow()=>Now; }
    private static CreateArchitectRequestDto Input(bool consent=true, Guid? recipient=null, Guid? report=null, Guid[]? docs=null, string? policy="test-v1")=>new(recipient??Recipient,report,docs??[Doc],consent,policy);
    private static ArchitectRequest Pending(Guid? actor=null, Guid? project=null)=>ArchitectRequest.CreatePending(new ShareConsent(Guid.NewGuid(),Guid.NewGuid(),project??Project,actor??Actor,Recipient,Pdf,[Doc],"test-v1",Now,true),Now);
    private sealed class Boundary : IArchitectWorkflowBoundaryCandidate
    {
        public ArchitectRequest? Stored {get;set;}
        public ArchitectBoundaryStatus Status {get;set;}=ArchitectBoundaryStatus.Accepted;
        public bool Throw {get;set;}
        public bool WrongActor {get;set;}
        public bool WrongProject {get;set;}
        public bool WrongReceipt {get;set;}
        public Func<ArchitectRequest, ArchitectRequest>? TransformCreateReceipt { get; set; }
        public IReadOnlyList<ArchitectRequest>? OverrideList {get;set;}
        public CancellationTokenSource? CancelAfter {get;set;}
        public int Calls {get;private set;}
        private void Called(){Calls++;if(Throw)throw new TimeoutException("private fixture failure");CancelAfter?.Cancel();}
        private Guid ReceiptActor(Guid actor)=>WrongActor?Guid.NewGuid():actor;
        public Task<ArchitectReceipt> CreateAsync(Guid actor,ArchitectRequest proposed,CancellationToken token)
        {
            Called();
            if (Status != ArchitectBoundaryStatus.Accepted)
                return Task.FromResult(new ArchitectReceipt(Status, ReceiptActor(actor)));
            Stored = proposed;
            var receipt = WrongReceipt ? Pending() : TransformCreateReceipt?.Invoke(proposed) ?? proposed;
            return Task.FromResult(new ArchitectReceipt(Status, ReceiptActor(actor), receipt));
        }
        public Task<ArchitectReceipt> ReadAsync(Guid actor,Guid project,Guid request,CancellationToken token)
        {Called();return Task.FromResult(new ArchitectReceipt(Status,ReceiptActor(actor),Status==ArchitectBoundaryStatus.Accepted?Stored:null));}
        public Task<ArchitectListReceipt> ListAsync(Guid actor,Guid project,int maximum,CancellationToken token)
        {Called();Assert.Equal(100,maximum);return Task.FromResult(new ArchitectListReceipt(Status,ReceiptActor(actor),WrongProject?Guid.NewGuid():project,Status==ArchitectBoundaryStatus.Accepted?(OverrideList??(Stored is null?Array.Empty<ArchitectRequest>():new[]{Stored!})):null));}
        public Task<ArchitectReceipt> WithdrawAsync(Guid actor,Guid project,Guid request,long version,CancellationToken token)
        {Called();if(Status!=ArchitectBoundaryStatus.Accepted)return Task.FromResult(new ArchitectReceipt(Status,ReceiptActor(actor)));var prior=Stored!;Stored=prior.ProposeWithdrawal(version,Now.AddMinutes(1));return Task.FromResult(new ArchitectReceipt(Status,ReceiptActor(actor),WrongReceipt?Pending():Stored,prior));}
    }
    private static ArchitectWorkflowCandidate S(Boundary b)=>new(b,new Clock());
    [Fact] public async Task CreatePreservesExplicitSelection(){var b=new Boundary();var r=await S(b).CreateAsync(Actor,Project,Input(report:Pdf));Assert.Equal(ArchitectOutcome.Available,r.Outcome);Assert.Equal(r.AttemptId,r.Request!.Id);Assert.Equal(Recipient,r.Request.RecipientId);Assert.Equal((Guid?)Pdf,r.Request.ReportId);Assert.Equal(Doc,Assert.Single(r.Request.DocumentIds));Assert.Equal(ArchitectRequestStatus.Pending,r.Request.Status);Assert.Equal(1,b.Calls);}
    [Fact] public async Task ReportOnlyAllowed(){var b=new Boundary();var r=await S(b).CreateAsync(Actor,Project,Input(report:Pdf,docs:[]));Assert.Equal(ArchitectOutcome.Available,r.Outcome);Assert.Empty(r.Request!.DocumentIds);}
    [Fact] public async Task MissingConsentRejected(){var b=new Boundary();Assert.Equal(ArchitectOutcome.Invalid,(await S(b).CreateAsync(Actor,Project,Input(false))).Outcome);Assert.Equal(0,b.Calls);}
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)] public async Task InvalidSelectionsDoNotDispatch(int i){var b=new Boundary();var input=i switch{0=>Input(recipient:Guid.Empty),1=>Input(report:Guid.Empty),2=>Input(docs:[]),3=>Input(docs:[Doc,Doc]),4=>Input(docs:[Guid.Empty]),5=>Input(policy:" "),_=>Input(docs:Enumerable.Range(0,101).Select(_=>Guid.NewGuid()).ToArray())};Assert.Equal(ArchitectOutcome.Invalid,(await S(b).CreateAsync(Actor,Project,input)).Outcome);Assert.Equal(0,b.Calls);}
    [Fact] public async Task MissingDocumentSelectionRejected(){var b=new Boundary();Assert.Equal(ArchitectOutcome.Invalid,(await S(b).CreateAsync(Actor,Project,new(Recipient,Pdf,null,true,"test-v1"))).Outcome);Assert.Equal(0,b.Calls);}
    [Fact] public async Task EmptyActorDoesNotDispatch(){var b=new Boundary();Assert.Equal(ArchitectOutcome.Hidden,(await S(b).CreateAsync(Guid.Empty,Project,Input())).Outcome);Assert.Equal(0,b.Calls);}
    [Theory][InlineData(ArchitectBoundaryStatus.Hidden,ArchitectOutcome.Hidden)][InlineData(ArchitectBoundaryStatus.NotEligible,ArchitectOutcome.NotEligible)][InlineData(ArchitectBoundaryStatus.Conflict,ArchitectOutcome.Conflict)][InlineData(ArchitectBoundaryStatus.Unavailable,ArchitectOutcome.Unavailable)][InlineData(ArchitectBoundaryStatus.Unknown,ArchitectOutcome.OutcomeUnknown)] public async Task BoundaryRejectionNeverReturnsPayload(ArchitectBoundaryStatus status,ArchitectOutcome expected){var b=new Boundary{Status=status};var r=await S(b).CreateAsync(Actor,Project,Input());Assert.Equal(expected,r.Outcome);Assert.Null(r.Request);Assert.Null(b.Stored);}
    [Fact] public async Task WrongCreateReceiptUnknown(){var b=new Boundary{WrongReceipt=true};var r=await S(b).CreateAsync(Actor,Project,Input());Assert.Equal(ArchitectOutcome.OutcomeUnknown,r.Outcome);Assert.NotNull(r.AttemptId);Assert.Null(r.Request);}
    [Fact] public async Task TimeoutDoesNotRetry(){var b=new Boundary{Throw=true};var r=await S(b).CreateAsync(Actor,Project,Input());Assert.Equal(ArchitectOutcome.OutcomeUnknown,r.Outcome);Assert.Equal(1,b.Calls);}
    [Fact] public async Task CancellationAfterWriteIsUnknown(){using var c=new CancellationTokenSource();var b=new Boundary{CancelAfter=c};Assert.Equal(ArchitectOutcome.OutcomeUnknown,(await S(b).CreateAsync(Actor,Project,Input(),c.Token)).Outcome);Assert.Equal(1,b.Calls);}
    [Fact] public async Task ReadExactOwnerRecord(){var p=Pending();var b=new Boundary{Stored=p};var r=await S(b).GetAsync(Actor,Project,p.Id);Assert.Equal(ArchitectOutcome.Available,r.Outcome);Assert.Equal(p.Id,r.Request!.Id);}
    [Fact] public async Task CrossAccountReceiptFailsClosed(){var p=Pending(Guid.NewGuid());var b=new Boundary{Stored=p};var r=await S(b).GetAsync(Actor,Project,p.Id);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);Assert.Null(r.Request);}
    [Fact] public async Task WrongProjectReceiptFailsClosed(){var p=Pending(project:Guid.NewGuid());var r=await S(new Boundary{Stored=p}).GetAsync(Actor,Project,p.Id);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);Assert.Null(r.Request);}
    [Fact] public async Task HiddenReadHasNoPayload(){var r=await S(new Boundary{Status=ArchitectBoundaryStatus.Hidden}).GetAsync(Actor,Project,Guid.NewGuid());Assert.Equal(ArchitectOutcome.Hidden,r.Outcome);Assert.Null(r.Request);}
    [Fact] public async Task WrongActorReceiptFailsClosed(){var p=Pending();var r=await S(new Boundary{Stored=p,WrongActor=true}).GetAsync(Actor,Project,p.Id);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);}
    [Fact] public async Task ListRejectsMixedOwner(){var r=await S(new Boundary{OverrideList=[Pending(),Pending(Guid.NewGuid())]}).ListAsync(Actor,Project);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);Assert.Null(r.Requests);}
    [Fact] public async Task ListRejectsDuplicate(){var p=Pending();var r=await S(new Boundary{OverrideList=[p,p]}).ListAsync(Actor,Project);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);}
    [Fact] public async Task ListRejectsOversize(){var r=await S(new Boundary{OverrideList=Enumerable.Range(0,101).Select(_=>Pending()).ToArray()}).ListAsync(Actor,Project);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);}
    [Fact] public async Task ListReturnsOwnerRecords(){var p=Pending();var r=await S(new Boundary{Stored=p}).ListAsync(Actor,Project);Assert.Equal(ArchitectOutcome.Available,r.Outcome);Assert.Equal(p.Id,Assert.Single(r.Requests!).Id);}
    [Fact] public async Task WithdrawalPreservesConsentAndRecordsActualState(){var p=Pending();var b=new Boundary{Stored=p};var r=await S(b).WithdrawAsync(Actor,Project,p.Id,1);Assert.Equal(ArchitectOutcome.Available,r.Outcome);Assert.Equal(ArchitectRequestStatus.Withdrawn,r.Request!.Status);Assert.Equal(2L,r.Request.StateVersion);Assert.Equal(Now.AddMinutes(1),r.Request.WithdrawnAtUtc);Assert.False(ArchitectConsentSelection.Includes(b.Stored,Project,Recipient,Doc,false));}
    [Fact] public async Task StaleVersionNeverDispatches(){var p=Pending();var b=new Boundary{Stored=p};Assert.Equal(ArchitectOutcome.Conflict,(await S(b).WithdrawAsync(Actor,Project,p.Id,2)).Outcome);Assert.Equal(0,b.Calls);}
    [Fact] public async Task UnsupportedWithdrawalNoMutation(){var p=Pending();var b=new Boundary{Stored=p,Status=ArchitectBoundaryStatus.NotEligible};Assert.Equal(ArchitectOutcome.NotEligible,(await S(b).WithdrawAsync(Actor,Project,p.Id,1)).Outcome);Assert.Same(p,b.Stored);}
    [Fact] public async Task WrongWithdrawalReceiptIsUnknown(){var p=Pending();var b=new Boundary{Stored=p,WrongReceipt=true};Assert.Equal(ArchitectOutcome.OutcomeUnknown,(await S(b).WithdrawAsync(Actor,Project,p.Id,1)).Outcome);Assert.Equal(1,b.Calls);}
    [Fact] public async Task CreateWrongActorReceiptFailsClosed(){var b=new Boundary{WrongActor=true};var r=await S(b).CreateAsync(Actor,Project,Input());Assert.Equal(ArchitectOutcome.OutcomeUnknown,r.Outcome);Assert.Null(r.Request);Assert.Equal(1,b.Calls);}
    [Fact] public async Task ListWrongActorReceiptFailsClosed(){var b=new Boundary{WrongActor=true,Stored=Pending()};var r=await S(b).ListAsync(Actor,Project);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);Assert.Null(r.Requests);Assert.Equal(1,b.Calls);}
    [Fact] public async Task ListWrongProjectReceiptFailsClosed(){var b=new Boundary{WrongProject=true,Stored=Pending()};var r=await S(b).ListAsync(Actor,Project);Assert.Equal(ArchitectOutcome.Unavailable,r.Outcome);Assert.Null(r.Requests);Assert.Equal(1,b.Calls);}
    [Fact]
    public async Task EmptyActorReadDoesNotDispatch()
    {
        var b = new Boundary();
        var r = await S(b).GetAsync(Guid.Empty, Project, Guid.NewGuid());
        Assert.Equal(ArchitectOutcome.Hidden, r.Outcome);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task EmptyActorListDoesNotDispatch()
    {
        var b = new Boundary();
        var r = await S(b).ListAsync(Guid.Empty, Project);
        Assert.Equal(ArchitectOutcome.Hidden, r.Outcome);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task EmptyActorWithdrawDoesNotDispatch()
    {
        var b = new Boundary();
        var r = await S(b).WithdrawAsync(Guid.Empty, Project, Guid.NewGuid(), 1);
        Assert.Equal(ArchitectOutcome.Hidden, r.Outcome);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task PreCancelledCreateDoesNotDispatch()
    {
        using var c = new CancellationTokenSource();
        c.Cancel();
        var b = new Boundary();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => S(b).CreateAsync(Actor, Project, Input(), c.Token));
        Assert.Equal(0, b.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PreCancelledOtherOperationsDoNotDispatch(int operation)
    {
        using var c = new CancellationTokenSource();
        c.Cancel();

        var b = new Boundary();

        switch (operation)
        {
            case 0:
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => S(b).GetAsync(Actor, Project, Guid.NewGuid(), c.Token));
                break;

            case 1:
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => S(b).ListAsync(Actor, Project, c.Token));
                break;

            default:
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => S(b).WithdrawAsync(
                        Actor, Project, Guid.NewGuid(), 1, c.Token));
                break;
        }

        Assert.Equal(0, b.Calls);
    }


    [Theory]
    [InlineData("policy")]
    [InlineData("recipient")]
    [InlineData("document")]
    public async Task CreateRejectsAcceptedReceiptWithChangedConsent(string field)
    {
        var other = Guid.NewGuid();
        ArchitectRequest? altered = null;
        var b = new Boundary
        {
            TransformCreateReceipt = proposed =>
            {
                var c = proposed.Consent;
                return altered = ArchitectRequest.CreatePending(new ShareConsent(
                    c.Id, c.RequestId, c.ProjectId, c.GrantedBy,
                    field == "recipient" ? other : c.RecipientId, c.ReportId,
                    field == "document" ? new[] { other } : c.DocumentIds,
                    field == "policy" ? "test-v2" : c.PolicyVersion,
                    c.GrantedAtUtc, true), proposed.RequestedAtUtc);
            }
        };

        var result = await S(b).CreateAsync(Actor, Project, Input(report: Pdf));

        Assert.NotNull(altered);
        Assert.Equal(ArchitectOutcome.OutcomeUnknown, result.Outcome);
        Assert.Null(result.Request);
        Assert.Equal<Guid?>(b.Stored!.Id, result.AttemptId);
        Assert.Equal(1, b.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadCancellationAfterDispatchDiscardsValidReceipt(bool list)
    {
        using var cancellation = new CancellationTokenSource();
        var pending = Pending();
        var b = new Boundary { Stored = pending, CancelAfter = cancellation };
        var service = S(b);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => list
            ? (Task)service.ListAsync(Actor, Project, cancellation.Token)
            : service.GetAsync(Actor, Project, pending.Id, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, b.Calls);
    }

    [Fact]
    public async Task WithdrawalCancellationAfterDispatchReturnsUnknownWithoutRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = Pending();
        var b = new Boundary { Stored = pending, CancelAfter = cancellation };

        var result = await S(b).WithdrawAsync(Actor, Project, pending.Id, pending.StateVersion, cancellation.Token);

        Assert.Equal(ArchitectOutcome.OutcomeUnknown, result.Outcome);
        Assert.Null(result.Request);
        Assert.Equal(1, b.Calls);
        Assert.Equal(ArchitectRequestStatus.Withdrawn, b.Stored!.Status);
    }

    [Fact] public void SelectionNeverExtendsToOtherItemsOrRecipients(){var p=Pending();Assert.True(ArchitectConsentSelection.Includes(p,Project,Recipient,Doc,false));Assert.True(ArchitectConsentSelection.Includes(p,Project,Recipient,Pdf,true));Assert.False(ArchitectConsentSelection.Includes(p,Project,Guid.NewGuid(),Doc,false));Assert.False(ArchitectConsentSelection.Includes(p,Project,Recipient,Pdf,false));Assert.False(ArchitectConsentSelection.Includes(p,Guid.NewGuid(),Recipient,Doc,false));Assert.False(ArchitectConsentSelection.Includes(p,Project,Recipient,Guid.NewGuid(),false));}
}

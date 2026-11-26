using BuildMind.Domain.Entities;
using static BuildMind.Workflow.Tests.Fixtures;
namespace BuildMind.Workflow.Tests;
public sealed class ArchitectRequestTests
{
    [Fact] public void InitialState() { var r=Pending(); Assert.Equal(ArchitectRequestStatus.Pending,r.Status); Assert.Equal(1L,r.StateVersion); Assert.Null(r.WithdrawnAtUtc); Assert.Equal(r.Consent.GrantedBy,r.RequestedBy); }
    [Fact] public void WithdrawalPreservesOriginal() { var r=Pending(); var w=r.ProposeWithdrawal(1,Time.AddMinutes(1)); Assert.Equal(ArchitectRequestStatus.Withdrawn,w.Status); Assert.Equal(2L,w.StateVersion); Assert.Same(r.Consent,w.Consent); Assert.Equal(r.Id,w.Id); Assert.Equal(ArchitectRequestStatus.Pending,r.Status); Assert.Null(r.WithdrawnAtUtc); }
    [Theory][InlineData(0L)][InlineData(2L)][InlineData(-1L)] public void StaleVersionRejected(long v)=>Assert.Throws<InvalidOperationException>(()=>Pending().ProposeWithdrawal(v,Time));
    [Fact] public void TerminalTransitionRejected()=>Assert.Throws<InvalidOperationException>(()=>Pending().ProposeWithdrawal(1,Time).ProposeWithdrawal(2,Time));
    [Fact] public void PriorRequestRejected()=>Assert.Throws<ArgumentException>(()=>ArchitectRequest.CreatePending(Consent(),Time.AddSeconds(-1)));
    [Fact] public void PriorWithdrawalRejected()=>Assert.Throws<ArgumentException>(()=>Pending().ProposeWithdrawal(1,Time.AddSeconds(-1)));
    [Fact] public void NullConsentRejected()=>Assert.Throws<ArgumentNullException>(()=>ArchitectRequest.CreatePending(null!,Time));
    [Fact] public void UnknownStatusRejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>ArchitectRequest.Rehydrate(Consent(),Time,1,(ArchitectRequestStatus)99));
    [Theory][InlineData(0L)][InlineData(2L)] public void PendingVersionRejected(long v)=>Assert.Throws<ArgumentException>(()=>ArchitectRequest.Rehydrate(Consent(),Time,v,ArchitectRequestStatus.Pending));
    [Theory][InlineData(1L)][InlineData(3L)] public void TerminalVersionRejected(long v)=>Assert.Throws<ArgumentException>(()=>ArchitectRequest.Rehydrate(Consent(),Time,v,ArchitectRequestStatus.Withdrawn,Time));
    [Fact] public void MissingWithdrawalRejected()=>Assert.Throws<ArgumentException>(()=>ArchitectRequest.Rehydrate(Consent(),Time,2,ArchitectRequestStatus.Withdrawn));
    [Fact] public void UnexpectedWithdrawalRejected()=>Assert.Throws<ArgumentException>(()=>ArchitectRequest.Rehydrate(Consent(),Time,1,ArchitectRequestStatus.Pending,Time));
}

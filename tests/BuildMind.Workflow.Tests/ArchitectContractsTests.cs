using BuildMind.Application.Features.ArchitectRequests.Contracts;
using BuildMind.Application.Features.ArchitectRequests.Abstractions;
using BuildMind.Domain.Entities;
using static BuildMind.Workflow.Tests.Fixtures;
namespace BuildMind.Workflow.Tests;
public sealed class ArchitectContractsTests
{
    [Fact] public void CreateCopiesSelection() { var a=new[]{Id(7)}; var d=new CreateArchitectRequestDto(Id(5),Id(6),a,true,"test-policy"); a[0]=Id(9); Assert.Equal(Id(7),Assert.Single(d.DocumentIds!)); Assert.True(d.ExplicitConsent); Assert.Throws<NotSupportedException>(()=>((IList<Guid>)d.DocumentIds!).Clear()); }
    [Fact] public void MissingConsentNotSilentlyGranted() { var d=new CreateArchitectRequestDto(Guid.Empty,null,null,false,null); Assert.False(d.ExplicitConsent); Assert.Null(d.DocumentIds); Assert.Null(d.AcknowledgedPolicyVersion); }
    [Fact] public void ReadModelMapsExactState() { var r=Pending().ProposeWithdrawal(1,Time.AddMinutes(1)); var d=new ArchitectRequestDto(r); Assert.Equal(r.Id,d.Id); Assert.Equal(r.ProjectId,d.ProjectId); Assert.Equal(r.RecipientId,d.RecipientId); Assert.Equal(r.Consent.ReportId,d.ReportId); Assert.Equal(r.Consent.PolicyVersion,d.PolicyVersion); Assert.Equal(r.Consent.GrantedAtUtc,d.ConsentedAtUtc); Assert.Equal(r.RequestedAtUtc,d.RequestedAtUtc); Assert.Equal(r.StateVersion,d.StateVersion); Assert.Equal(r.Status,d.Status); Assert.Equal(r.WithdrawnAtUtc,d.WithdrawnAtUtc); Assert.Equal(Id(7),Assert.Single(d.DocumentIds)); Assert.Throws<NotSupportedException>(()=>((IList<Guid>)d.DocumentIds).Clear()); }
    [Fact] public void NullReadSourceRejected()=>Assert.Throws<ArgumentNullException>(()=>new ArchitectRequestDto(null!));
    [Fact] public void CreatedRequiresPending() { var r=Pending(); Assert.Same(r,new ArchitectRequestWriteResult(ArchitectRequestWriteStatus.Created,r).SavedRequest); Assert.Throws<ArgumentException>(()=>new ArchitectRequestWriteResult(ArchitectRequestWriteStatus.Created,r.ProposeWithdrawal(1,Time))); }
    [Fact] public void WithdrawnRequiresTerminal() { var r=Pending(); var w=r.ProposeWithdrawal(1,Time); Assert.Same(w,new ArchitectRequestWriteResult(ArchitectRequestWriteStatus.Withdrawn,w).SavedRequest); Assert.Throws<ArgumentException>(()=>new ArchitectRequestWriteResult(ArchitectRequestWriteStatus.Withdrawn,r)); }
    [Theory][InlineData(ArchitectRequestWriteStatus.Created)][InlineData(ArchitectRequestWriteStatus.Withdrawn)] public void SuccessRequiresPayload(ArchitectRequestWriteStatus s)=>Assert.Throws<ArgumentNullException>(()=>new ArchitectRequestWriteResult(s));
    [Theory][InlineData(ArchitectRequestWriteStatus.AlreadyExists)][InlineData(ArchitectRequestWriteStatus.NotFound)][InlineData(ArchitectRequestWriteStatus.StateConflict)][InlineData(ArchitectRequestWriteStatus.SourceConflict)][InlineData(ArchitectRequestWriteStatus.PolicyUnavailable)][InlineData(ArchitectRequestWriteStatus.NotEligible)] public void FailureCannotCarryPayload(ArchitectRequestWriteStatus s) { Assert.Null(new ArchitectRequestWriteResult(s).SavedRequest); Assert.Throws<ArgumentException>(()=>new ArchitectRequestWriteResult(s,Pending())); }
    [Fact] public void UnknownStatusRejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>new ArchitectRequestWriteResult((ArchitectRequestWriteStatus)99));
}

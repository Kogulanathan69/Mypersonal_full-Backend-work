using BuildMind.Domain.Entities;
using static BuildMind.Workflow.Tests.Fixtures;
namespace BuildMind.Workflow.Tests;
public sealed class ShareConsentTests
{
    [Fact] public void ValidConsentPreservesIdentityAndNormalizesTime() { var c=Consent(); Assert.Equal(Id(2),c.RequestId); Assert.Equal(Id(5),c.RecipientId); Assert.Equal((Guid?)Id(6),c.ReportId); Assert.Equal(Time.ToUniversalTime(),c.GrantedAtUtc); Assert.Equal(TimeSpan.Zero,c.GrantedAtUtc.Offset); }
    [Fact] public void ExplicitConsentRequired() => Assert.Throws<ArgumentException>(()=>new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),Id(6),[],"test",Time,false));
    [Fact] public void NoItemsRejected() => Assert.Throws<ArgumentException>(()=>new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),null,[],"test",Time,true));
    [Fact] public void DuplicateDocumentsRejected() => Assert.Throws<ArgumentException>(()=>new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),null,[Id(7),Id(7)],"test",Time,true));
    [Fact] public void EmptyDocumentRejected() => Assert.Throws<ArgumentException>(()=>new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),null,[Guid.Empty],"test",Time,true));
    [Fact] public void EmptyReportRejected() => Assert.Throws<ArgumentException>(()=>new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),Guid.Empty,[],"test",Time,true));
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)] public void EmptyIdentityRejected(int i) { var a=new[]{Id(1),Id(2),Id(3),Id(4),Id(5)}; a[i]=Guid.Empty; Assert.Throws<ArgumentException>(()=>new ShareConsent(a[0],a[1],a[2],a[3],a[4],Id(6),[],"test",Time,true)); }
    [Fact] public void NullDocumentsRejected() => Assert.Throws<ArgumentNullException>(()=>new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),Id(6),null!,"test",Time,true));
    [Fact] public void BlankPolicyRejected() => Assert.Throws<ArgumentException>(()=>new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),Id(6),[]," ",Time,true));
    [Fact] public void DocumentsCopiedAndReadOnly() { var a=new[]{Id(7)}; var c=new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),null,a,"test",Time,true); a[0]=Id(8); Assert.Equal(Id(7),Assert.Single(c.DocumentIds)); Assert.Throws<NotSupportedException>(()=>((IList<Guid>)c.DocumentIds).Clear()); }
    [Fact] public void ReportOnlyAccepted() { var c=new ShareConsent(Id(1),Id(2),Id(3),Id(4),Id(5),Id(6),[],"test",Time,true); Assert.Empty(c.DocumentIds); }
}

using BuildMind.Application.Features.Requirements.Integration;
using static BuildMind.Workflow.Tests.Fixtures;
namespace BuildMind.Workflow.Tests;
public sealed class ReadinessTests
{
    private static RequirementReadinessSnapshot Snapshot()=>new(Id(3),Id(9),Requirements(),Time);
    [Fact] public void SnapshotPreservesExactRevisions() { var r=Requirements(); var s=new RequirementReadinessSnapshot(Id(3),Id(9),r,Time); Assert.Same(r,s.Requirements); Assert.Equal(r.Id,s.RequirementsRevisionId); Assert.Equal(Id(9),s.LandRevisionId); Assert.Equal(TimeSpan.Zero,s.CapturedAtUtc.Offset); }
    [Fact] public void WrongProjectRejected()=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessSnapshot(Id(10),Id(9),Requirements(),Time));
    [Fact] public void EmptyLandRejected()=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessSnapshot(Id(3),Guid.Empty,Requirements(),Time));
    [Fact] public void NullRequirementsRejected()=>Assert.Throws<ArgumentNullException>(()=>new RequirementReadinessSnapshot(Id(3),Id(9),null!,Time));
    [Fact] public void ReadyRequiresSnapshot()=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessResult(RequirementReadinessStatus.Ready,null,[]));
    [Fact] public void ReadyCarriesSnapshot() { var s=Snapshot(); var r=new RequirementReadinessResult(RequirementReadinessStatus.Ready,s,[]); Assert.Same(s,r.Snapshot); Assert.Empty(r.DiagnosticCodes); }
    [Fact] public void ReadyWithDiagnosticsRejected()=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessResult(RequirementReadinessStatus.Ready,Snapshot(),["TEST"]));
    [Theory][InlineData(RequirementReadinessStatus.Incomplete)][InlineData(RequirementReadinessStatus.Unavailable)] public void FailureNeedsDiagnostics(RequirementReadinessStatus s)=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessResult(s,null,[]));
    [Theory][InlineData(RequirementReadinessStatus.Incomplete)][InlineData(RequirementReadinessStatus.Unavailable)] public void FailureCannotCarrySnapshot(RequirementReadinessStatus s)=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessResult(s,Snapshot(),["TEST"]));
    [Fact] public void DuplicateDiagnosticsRejected()=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessResult(RequirementReadinessStatus.Incomplete,null,["TEST","TEST"]));
    [Fact] public void BlankDiagnosticsRejected()=>Assert.Throws<ArgumentException>(()=>new RequirementReadinessResult(RequirementReadinessStatus.Incomplete,null,[" "]));
    [Fact] public void UnknownStatusRejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>new RequirementReadinessResult((RequirementReadinessStatus)99,null,[]));
    [Fact] public void DiagnosticsCopiedAndReadOnly() { var a=new[]{"TEST"}; var r=new RequirementReadinessResult(RequirementReadinessStatus.Unavailable,null,a); a[0]="OTHER"; Assert.Equal("TEST",Assert.Single(r.DiagnosticCodes)); Assert.Null(r.Snapshot); Assert.Throws<NotSupportedException>(()=>((IList<string>)r.DiagnosticCodes).Clear()); }
}

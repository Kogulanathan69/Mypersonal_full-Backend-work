using BuildMind.Application.Features.Dashboard.Contracts;
using static BuildMind.Workflow.Tests.Fixtures;
namespace BuildMind.Workflow.Tests;
public sealed class DashboardTests
{
    [Fact] public void EmptyProjection() { var s=new DashboardSummaryDto([]); Assert.Equal(0,s.TotalProjects); Assert.Empty(s.StageCounts); }
    [Fact] public void CountsDerivedFromRows() { var s=new DashboardSummaryDto([new(Id(1),"A","TEST_B"),new(Id(2),"B","TEST_A"),new(Id(3),"C","TEST_B")]); Assert.Equal(3,s.TotalProjects); Assert.Collection(s.StageCounts,x=>{Assert.Equal("TEST_A",x.StageCode);Assert.Equal(1,x.Count);},x=>{Assert.Equal("TEST_B",x.StageCode);Assert.Equal(2,x.Count);}); }
    [Fact] public void DuplicateRowsRejected()=>Assert.Throws<ArgumentException>(()=>new DashboardSummaryDto([new(Id(1),"A","TEST"),new(Id(1),"B","TEST")]));
    [Fact] public void NullRowRejected()=>Assert.Throws<ArgumentException>(()=>new DashboardSummaryDto([null!]));
    [Fact] public void NullProjectionRejected()=>Assert.Throws<ArgumentNullException>(()=>new DashboardSummaryDto(null!));
    [Fact] public void EmptyIdRejected()=>Assert.Throws<ArgumentException>(()=>new DashboardProjectDto(Guid.Empty,"A","TEST"));
    [Fact] public void BlankNameRejected()=>Assert.Throws<ArgumentException>(()=>new DashboardProjectDto(Id(1)," ","TEST"));
    [Fact] public void BlankStageRejected()=>Assert.Throws<ArgumentException>(()=>new DashboardProjectDto(Id(1),"A"," "));
    [Fact] public void RowsCopiedAndReadOnly() { var a=new[]{new DashboardProjectDto(Id(1),"A","TEST")}; var s=new DashboardSummaryDto(a); a[0]=new(Id(2),"B","OTHER"); Assert.Equal(Id(1),Assert.Single(s.Projects).ProjectId); Assert.Throws<NotSupportedException>(()=>((IList<DashboardProjectDto>)s.Projects).Clear()); Assert.Throws<NotSupportedException>(()=>((IList<DashboardStageCountDto>)s.StageCounts).Clear()); }
}

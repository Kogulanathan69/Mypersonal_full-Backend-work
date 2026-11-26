using BuildMind.Application.Features.Reports.Services;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportRoomLabelPlacementTests
{
    [Fact] public void InteriorLabelAvoidsExistingBox()
    {
        ReportPlotPoint[] ring=[new(0,0),new(100,0),new(100,100),new(0,100),new(0,0)];
        var first=ReportRoomLabelPlacement.Find(ring,20,10,[])!;
        var next=ReportRoomLabelPlacement.Find(ring,20,10,[first])!;
        Assert.NotNull(next);
        Assert.False(next.X<first.X+first.Width && next.X+next.Width>first.X && next.Y<first.Y+first.Height && next.Y+next.Height>first.Y);
    }
    [Fact] public void ConcaveNotchAndOversizeAreNotLabelled()
    {
        ReportPlotPoint[] ring=[new(0,0),new(100,0),new(100,30),new(30,30),new(30,100),new(0,100),new(0,0)];
        var box=ReportRoomLabelPlacement.Find(ring,15,10,[]);
        Assert.NotNull(box);
        Assert.True(box.X+box.Width<30 || box.Y+box.Height<30);
        Assert.Null(ReportRoomLabelPlacement.Find(ring,200,200,[]));
    }
}

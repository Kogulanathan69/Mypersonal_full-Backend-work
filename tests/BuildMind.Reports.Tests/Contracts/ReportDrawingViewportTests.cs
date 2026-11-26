using BuildMind.Application.Features.Reports.Services;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportDrawingViewportTests
{
    [Fact] public void UniformScaleFlipsYAndPreservesClosedRing()
    {
        ReportPlotPoint[] ring = [new(-2, -1), new(2, -1), new(2, 2), new(-2, 2), new(-2, -1)];
        var viewport = new ReportDrawingViewport(ring, 420, 320, 10);
        Assert.Equal(100, viewport.Scale);
        Assert.Equal(new(10, 310), viewport.Transform(ring[0]));
        Assert.Equal(new(410, 10), viewport.Transform(ring[2]));
        Assert.Equal(viewport.Transform(ring[0]), viewport.Transform(ring[^1]));
    }
    [Theory] [InlineData(0)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidViewportRejected(double width) => Assert.Throws<ArgumentException>(() => new ReportDrawingViewport([new(0,0),new(4,3)],width,100,5));
    [Fact] public void DegenerateAndNonfiniteGeometryRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReportDrawingViewport([new(0,0),new(0,3)],100,100,5));
        Assert.Throws<ArgumentException>(() => new ReportDrawingViewport([new(0,0),new(double.NaN,3)],100,100,5));
    }
    [Theory] [InlineData("door")] [InlineData("window")]
    public void OpeningUsesOffsetAlongDiagonalWall(string type)
    {
        var id=Guid.NewGuid(); var wall=new ReportPlotWall(id,new(1,2),new(4,6),0.2);
        var (start,end)=ReportDrawingViewport.Opening(wall,new(Guid.NewGuid(),type,id,1,2));
        Assert.Equal(1.6,start.X,10); Assert.Equal(2.8,start.Y,10);
        Assert.Equal(2.8,end.X,10); Assert.Equal(4.4,end.Y,10);
        Assert.Throws<ArgumentException>(()=>ReportDrawingViewport.Opening(wall,new(Guid.NewGuid(),type,id,4,2)));
    }
}

using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.Reports.Services;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportGeometryExtractorTests
{
    private static RoomDto Room(params (double X, double Y)[] points) => new()
    {
        Id = Guid.NewGuid(), Name = "Synthetic room", Type = "ROOM_A", WallIds = [],
        Polygon = points.Select(p => new CoordinatePointDto { X = p.X, Y = p.Y }).ToArray()
    };

    [Fact] public void RectangleUsesActualCoordinates()
    {
        var r = ReportGeometryExtractor.Measure(Room((0,0),(4,0),(4,3),(0,3)));
        Assert.Equal(12d, r.AreaSquareMetres); Assert.Equal(4d, r.BoundsWidthMetres); Assert.Equal(3d, r.BoundsHeightMetres);
    }
    [Fact] public void ConcavePolygonUsesAreaNotBoundingBox()
    {
        var r = ReportGeometryExtractor.Measure(Room((0,0),(4,0),(4,1),(1,1),(1,3),(0,3)));
        Assert.Equal(6d, r.AreaSquareMetres); Assert.Equal(4d, r.BoundsWidthMetres);
    }
    [Fact] public void ReversedClosedPolygonPreservesArea() =>
        Assert.Equal(12d, ReportGeometryExtractor.Measure(Room((0,0),(0,3),(4,3),(4,0),(0,0))).AreaSquareMetres);
    [Fact] public void TranslatedPolygonPreservesArea() =>
        Assert.Equal(12d, ReportGeometryExtractor.Measure(Room((1000,1000),(1004,1000),(1004,1003),(1000,1003))).AreaSquareMetres);
    [Fact] public void CollinearPolygonRejected() =>
        Assert.Throws<ArgumentException>(() => ReportGeometryExtractor.Measure(Room((0,0),(1,1),(2,2))));
    [Fact] public void NonfinitePolygonRejected() =>
        Assert.Throws<ArgumentException>(() => ReportGeometryExtractor.Measure(Room((0,0),(double.NaN,1),(2,0))));
    [Fact] public void TooFewPointsRejected() =>
        Assert.Throws<ArgumentException>(() => ReportGeometryExtractor.Measure(Room((0,0),(1,1))));
}

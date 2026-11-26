using BuildMind.Application.Features.Reports.Services;
using BuildMind.Application.Features.Reports.Contracts;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Domain.ValueObjects;
using System.Text.Json;

namespace BuildMind.Reports.Tests.Contracts;

public sealed class ReportDataBuilderTests
{
    private static ReportSnapshot Make(bool? requirementsMatch = true, string evidence = "VERIFIED", bool acknowledged = true, bool empty = false, string coordinateType = "localCartesian")
    {
        var old = JsonSerializer.Deserialize<PlanGeometryDto>(SyntheticReportFixtures.Geometry())!;
        var pts = new CoordinatePointDto[] { new() { X=0,Y=0 }, new() { X=4,Y=0 }, new() { X=4,Y=3 }, new() { X=0,Y=3 } };
        var walls = Enumerable.Range(0,4).Select(i => new WallDto { Id=SyntheticReportFixtures.Id(20+i), Start=pts[i], End=pts[(i+1)%4], Thickness=0.2 }).ToArray();
        var room = new RoomDto { Id=SyntheticReportFixtures.Id(30), Name="Synthetic room", Type="ROOM_A", Polygon=pts.Append(pts[0]).ToArray(), WallIds=walls.Select(w=>w.Id).ToArray() };
        var geometry = new PlanGeometryDto { SchemaVersion=old.SchemaVersion, ProjectId=old.ProjectId, PlanId=old.PlanId, LandRevisionId=old.LandRevisionId, RequirementsRevisionId=old.RequirementsRevisionId, CoordinateSystem=new CoordinateSystemDto { Type=coordinateType, Unit="m", Origin=new() { X=0, Y=0 }, XDirection="right", YDirection="up" },
            Floors=[new FloorDto { Id=SyntheticReportFixtures.Id(11), Level=0, Rooms=empty ? Array.Empty<RoomDto>() : new[] { room }, Walls=empty ? Array.Empty<WallDto>() : walls, Openings=[] }] };
        var plan= SyntheticReportFixtures.PlanSource(JsonSerializer.Serialize(geometry), requirementsMatch, evidence);
        var confirmation= SyntheticReportFixtures.Confirm(plan) with { WarningAcknowledgement=acknowledged };
        return ReportInputSnapshot.FromResolvedSources("test-report-v1", SyntheticReportFixtures.Time.AddMinutes(2), SyntheticReportFixtures.Requirements(), plan, confirmation, SyntheticReportFixtures.Disclaimer()).Snapshot;
    }
    [Fact] public void PreservesExactSnapshotAndMeasuredGeometry()
    {
        var snapshot=Make(); var data=ReportDataBuilder.Build(snapshot);
        Assert.Same(snapshot,data.Snapshot);
        var floor=Assert.Single(data.Floors); var room=Assert.Single(floor.Rooms);
        Assert.Equal(12d,room.Measurement.AreaSquareMetres);
        Assert.Equal(12d,data.SumRoomAreaSquareMetres);
        Assert.Equal(4,floor.Walls.Count); Assert.Equal(5,room.Polygon.Count); Assert.Equal(room.Polygon[0],room.Polygon[^1]);
        Assert.Contains("not a construction drawing",data.Notice);
        Assert.Equal(1,data.Snapshot.Requirements.Content.FloorCount);
    }
    [Fact] public void EmptyGeometryRejected() => Assert.ThrowsAny<ArgumentException>(()=>ReportDataBuilder.Build(Make(empty:true)));
    [Fact] public void UnacknowledgedWarningsRejected() => Assert.Throws<ArgumentException>(()=>ReportDataBuilder.Build(Make(null,"UNKNOWN",false)));
    [Fact] public void AcknowledgedUnknownEvidenceNotUpgraded()
    {
        var data=ReportDataBuilder.Build(Make(null,"UNKNOWN"));
        Assert.Equal("UNKNOWN",data.Snapshot.Plan.Validation.RegulatoryEvidenceStatus);
        Assert.Null(data.Snapshot.Plan.Validation.RequirementsMatch);
    }
    [Fact] public void UnsupportedCoordinateConventionRejected() => Assert.Throws<ArgumentException>(()=>ReportDataBuilder.Build(Make(coordinateType:"local")));
    [Fact] public void NullRejected() => Assert.Throws<ArgumentNullException>(()=>ReportDataBuilder.Build(null!));
}

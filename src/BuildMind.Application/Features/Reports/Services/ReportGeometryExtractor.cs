using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Reports.Services;

public sealed record ReportRoomMeasurement(Guid RoomId, string Name, string Type,
    double AreaSquareMetres, double BoundsWidthMetres, double BoundsHeightMetres);

public static class ReportGeometryExtractor
{
    // Draft-contract projection only; not an authoritative topology or confirmation validator.
    public static PlanGeometryDto ReadGeometry(ReportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var plan = snapshot.Plan;
        if (plan.GeometrySchemaVersion != GeometrySchemaInfo.DraftVersion ||
            !plan.Validation.GeometryValid || plan.Validation.HasBlockingIssues ||
            !string.Equals(PlanRevisionService.ComputeGeometryHash(plan.GeometryJson), plan.GeometryHash, StringComparison.Ordinal))
            throw new ArgumentException("Unsupported or invalid report geometry.", nameof(snapshot));
        var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(plan.GeometryJson, PlanEditorJson.StrictOptions)
            ?? throw new ArgumentException("Missing geometry.", nameof(snapshot));
        PlanEditorContractValidator.Validate(geometry);
        if (geometry.ProjectId != plan.ProjectId || geometry.PlanId != plan.PlanId ||
            geometry.LandRevisionId != plan.LandRevisionId || geometry.RequirementsRevisionId != plan.RequirementsRevisionId ||
            geometry.SchemaVersion != plan.GeometrySchemaVersion || geometry.CoordinateSystem.Unit != "m" ||
            geometry.CoordinateSystem.Type != "localCartesian" || geometry.CoordinateSystem.XDirection != "right" ||
            geometry.CoordinateSystem.YDirection != "up" || geometry.Floors.Count == 0 ||
            geometry.Floors.Any(f => f.Rooms.Count == 0))
            throw new ArgumentException("Geometry identity or supported rendering scope mismatch.", nameof(snapshot));
        foreach (var floor in geometry.Floors)
            foreach (var room in floor.Rooms) _ = Measure(room);
        return geometry;
    }

    // Width/height describe axis-aligned bounds, not individual wall lengths.
    public static ReportRoomMeasurement Measure(RoomDto room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var p = room.Polygon;
        if (p is null || p.Count < 3 || p.Count > 10000 || p.Any(v => v is null || !double.IsFinite(v.X) || !double.IsFinite(v.Y)))
            throw new ArgumentException("Invalid polygon coordinates.", nameof(room));
        double x0 = p[0].X, y0 = p[0].Y, sum = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            sum += (a.X - x0) * (b.Y - y0) - (b.X - x0) * (a.Y - y0);
        }
        double area = Math.Abs(sum) / 2;
        double w = p.Max(v => v.X) - p.Min(v => v.X);
        double h = p.Max(v => v.Y) - p.Min(v => v.Y);
        if (!double.IsFinite(area) || !double.IsFinite(w) || !double.IsFinite(h) || area <= 0 || w <= 0 || h <= 0)
            throw new ArgumentException("Degenerate or nonfinite polygon measurement.", nameof(room));
        return new(room.Id, room.Name, room.Type, area, w, h);
    }
}

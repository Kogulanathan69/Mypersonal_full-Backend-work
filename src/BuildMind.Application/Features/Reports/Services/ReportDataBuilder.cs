using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Reports.Services;

public sealed record ReportPlotPoint(double X, double Y);
public sealed record ReportPlotRoom(ReportRoomMeasurement Measurement, IReadOnlyList<ReportPlotPoint> Polygon);
public sealed record ReportPlotWall(Guid Id, ReportPlotPoint Start, ReportPlotPoint End, double ThicknessMetres);
public sealed record ReportPlotOpening(Guid Id, string Type, Guid WallId, double OffsetMetres, double WidthMetres);
public sealed record ReportPlotFloor(Guid Id, int Level, IReadOnlyList<ReportPlotRoom> Rooms,
    IReadOnlyList<ReportPlotWall> Walls, IReadOnlyList<ReportPlotOpening> Openings, double SumRoomAreaSquareMetres);

// Internal projection only. A structurally consistent snapshot does not establish ownership or persisted confirmation.
public sealed class ReportRenderData
{
    public ReportSnapshot Snapshot { get; }
    public IReadOnlyList<ReportPlotFloor> Floors { get; }
    public double SumRoomAreaSquareMetres { get; }
    public string Notice => "Preliminary planning report - not a construction drawing or regulatory approval.";
    internal ReportRenderData(ReportSnapshot snapshot, IReadOnlyList<ReportPlotFloor> floors, double sum)
        => (Snapshot, Floors, SumRoomAreaSquareMetres) = (snapshot, floors, sum);
}

public static class ReportDataBuilder
{
    public static ReportRenderData Build(ReportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var validation = snapshot.Plan.Validation;
        if ((!string.Equals(validation.RegulatoryEvidenceStatus, "VERIFIED", StringComparison.OrdinalIgnoreCase)
             || validation.RequirementsMatch is not true) && !snapshot.Confirmation.WarningAcknowledgement)
            throw new ArgumentException("Warnings have not been acknowledged.", nameof(snapshot));
        var geometry = ReportGeometryExtractor.ReadGeometry(snapshot);
        var floors = geometry.Floors.Select(f =>
        {
            var rooms = f.Rooms.Select(r => new ReportPlotRoom(ReportGeometryExtractor.Measure(r),
                Freeze(r.Polygon.Select(p => new ReportPlotPoint(p.X, p.Y))))).ToArray();
            var walls = Freeze(f.Walls.Select(w => new ReportPlotWall(w.Id, new(w.Start.X, w.Start.Y),
                new(w.End.X, w.End.Y), w.Thickness)));
            var openings = Freeze(f.Openings.Select(o => new ReportPlotOpening(o.Id, o.Type, o.WallId, o.Offset, o.Width)));
            return new ReportPlotFloor(f.Id, f.Level, Freeze(rooms), walls, openings,
                CheckedSum(rooms.Select(r => r.Measurement.AreaSquareMetres)));
        }).ToArray();
        return new ReportRenderData(snapshot, Freeze(floors), CheckedSum(floors.Select(f => f.SumRoomAreaSquareMetres)));
    }
    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
    private static double CheckedSum(IEnumerable<double> items)
    {
        var sum = items.Sum();
        if (!double.IsFinite(sum)) throw new ArgumentException("Nonfinite report area.");
        return sum;
    }
}

// Candidate rendering port; no library selection, public route, storage, or authorization implied.
public interface IPreliminaryReportRenderer
{
    Task<byte[]> RenderAsync(ReportRenderData data, CancellationToken cancellationToken);
}

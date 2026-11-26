namespace BuildMind.Application.Features.Reports.Services;

/// <summary>Library-independent metre-to-page transform. Page Y increases downwards.</summary>
/// <remarks>Presentation utility only; callers must first use ReportDataBuilder. Bounds are
/// axis-aligned extents, not wall dimensions. No confirmation or authorization is implied.</remarks>
public sealed class ReportDrawingViewport
{
    public double Scale { get; }
    private readonly double _minX, _maxY, _left, _top;

    public ReportDrawingViewport(IEnumerable<ReportPlotPoint> points, double width, double height, double padding)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(padding)
            || padding < 0 || width <= 2 * padding || height <= 2 * padding)
            throw new ArgumentException("Invalid drawing viewport.");
        var items = points.Take(100_001).ToArray();
        if (items.Length == 0 || items.Length > 100_000 || items.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new ArgumentException("Invalid drawing points.", nameof(points));
        _minX = items.Min(p => p.X); _maxY = items.Max(p => p.Y);
        var dx = items.Max(p => p.X) - _minX;
        var dy = _maxY - items.Min(p => p.Y);
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx <= 0 || dy <= 0)
            throw new ArgumentException("Invalid drawing extents.", nameof(points));
        Scale = Math.Min((width - 2 * padding) / dx, (height - 2 * padding) / dy);
        if (!double.IsFinite(Scale) || Scale <= 0) throw new ArgumentException("Invalid drawing scale.");
        _left = padding + (width - 2 * padding - dx * Scale) / 2;
        _top = padding + (height - 2 * padding - dy * Scale) / 2;
    }

    public ReportPlotPoint Transform(ReportPlotPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var x = _left + (point.X - _minX) * Scale;
        var y = _top + (_maxY - point.Y) * Scale;
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentException("Invalid drawing point.");
        return new(x, y);
    }

    /// <summary>Opening endpoints interpolated along the wall in metres, never pixel offsets.</summary>
    public static (ReportPlotPoint Start, ReportPlotPoint End) Opening(ReportPlotWall wall, ReportPlotOpening opening)
    {
        ArgumentNullException.ThrowIfNull(wall); ArgumentNullException.ThrowIfNull(opening);
        var dx = wall.End.X - wall.Start.X; var dy = wall.End.Y - wall.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (opening.WallId != wall.Id || !double.IsFinite(length) || length <= 0
            || !double.IsFinite(opening.OffsetMetres) || !double.IsFinite(opening.WidthMetres)
            || opening.OffsetMetres < 0 || opening.WidthMetres <= 0
            || opening.OffsetMetres + opening.WidthMetres > length)
            throw new ArgumentException("Invalid opening geometry.");
        ReportPlotPoint At(double distance) => new(wall.Start.X + dx * (distance / length), wall.Start.Y + dy * (distance / length));
        return (At(opening.OffsetMetres), At(opening.OffsetMetres + opening.WidthMetres));
    }
}

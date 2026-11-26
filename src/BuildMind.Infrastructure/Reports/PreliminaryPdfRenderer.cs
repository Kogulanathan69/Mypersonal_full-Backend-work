using System.Globalization;
using System.Text;
using BuildMind.Application.Features.Reports.Services;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace BuildMind.Infrastructure.Reports;

/// <summary>Isolated, in-memory renderer. No storage, authorization or production registration.</summary>
/// <remarks>Provisional resource caps: 1 MiB geometry JSON, 20 floors, 500 rooms,
/// 10000 drawing elements, 100 pages, 10 MiB output. Complex scripts fail closed
/// until a separately approved shaping implementation is verified.</remarks>
public sealed class PreliminaryPdfRenderer : IPreliminaryReportRenderer
{
    private readonly int _maximumPages;
    private readonly int _maximumOutputBytes;
    public PreliminaryPdfRenderer(int maximumPages = 100, int maximumOutputBytes = 10 * 1024 * 1024)
    {
        if(maximumPages is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumPages));
        if(maximumOutputBytes is < 1 or > 10 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumOutputBytes));
        _maximumPages=maximumPages; _maximumOutputBytes=maximumOutputBytes;
    }
    private static readonly object FontLock = new();
    private static readonly XColor Navy = XColor.FromArgb(20, 43, 67);
    private static readonly XColor Teal = XColor.FromArgb(0, 120, 128);

    public Task<byte[]> RenderAsync(ReportRenderData data, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(data); token.ThrowIfCancellationRequested();
        try { return Task.FromResult(Render(data, token)); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is not OutOfMemoryException)
        { throw new InvalidOperationException("Preliminary report rendering could not be completed."); }
    }

    private byte[] Render(ReportRenderData input, CancellationToken token)
    {
        if (Encoding.UTF8.GetByteCount(input.Snapshot.Plan.GeometryJson) > 1024 * 1024)
            throw new ArgumentException("Report input limit.");
        var data = ReportDataBuilder.Build(input.Snapshot); // Revalidate immutable source at boundary.
        if (data.Floors.Count > 20 || data.Floors.Sum(f => f.Rooms.Count) > 500
            || data.Floors.Sum(f => f.Walls.Count + f.Openings.Count + f.Rooms.Sum(r => r.Polygon.Count)) > 10000)
            throw new ArgumentException("Report complexity limit.");
        lock (FontLock)
        {
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new NotoResolver();
            else if (GlobalFontSettings.FontResolver is not NotoResolver)
                throw new InvalidOperationException("Incompatible font configuration.");
        }
        using var document = new PdfDocument();
        document.Info.Title = "BuildMind preliminary planning report";
        var font = new XFont("BuildMind Noto", 10);
        var small = new XFont("BuildMind Noto", 8);
        XGraphics? graphics = null; double y = 0;
        void Page()
        {
            token.ThrowIfCancellationRequested();
            if (document.PageCount >= _maximumPages) throw new ArgumentException("Report page limit.");
            graphics?.Dispose(); var page = document.AddPage(); page.Size = PdfSharp.PageSize.A4;
            graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(new XSolidBrush(Navy), 0, 0, page.Width.Point, 68);
            graphics.DrawString("BUILDMIND | PRELIMINARY REPORT", font, XBrushes.White, 36, 39);
            graphics.DrawString($"Page {document.PageCount}", small, XBrushes.Gray, 36, 815); y = 92;
        }
        void Line(string value)
        {
            token.ThrowIfCancellationRequested();
            if (value.Length > 10000 || value.Any(c => c > 0x7e || (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')))
                throw new ArgumentException("Text requires unsupported font or shaping.");
            // Hard wrapping prevents untrusted long tokens from escaping the page.
            foreach (var raw in value.Replace('\r', ' ').Replace('\t', ' ').Split('\n'))
            {
                var rest = raw;
                do
                {
                    if (y > 775) Page();
                    var count = Math.Min(85, rest.Length);
                    while (count > 1 && graphics!.MeasureString(rest[..count], font).Width > 515) count--;
                    graphics!.DrawString(rest[..count], font, XBrushes.Black, 36, y); y += 15;
                    rest = rest[count..];
                } while (rest.Length > 0);
            }
        }
        try
        {
            Page(); var s = data.Snapshot; var p = s.Plan; var r = s.Requirements;
            Line("Project and exact source versions");
            Line($"Project: {p.ProjectId}"); Line($"Plan: {p.PlanId}");
            Line($"Plan revision: {p.RevisionId} / version {p.VersionNumber}");
            Line($"Requirements revision: {r.Id}"); Line($"Land revision: {p.LandRevisionId}");
            Line($"Confirmation: {s.Confirmation.ConfirmationId}"); Line($"Geometry hash: {p.GeometryHash}");
            Line($"Snapshot UTC: {s.CapturedAtUtc:O}");
            Line($"Requirements schema: {r.SchemaVersion}; catalogue: {r.CatalogueVersion}");
            Line($"Requested floors: {r.Content.FloorCount}; design method: {r.Content.DesignMethod ?? "not selected"}");
            Line(FormattableString.Invariant($"All-floor sum of room polygon areas: {data.SumRoomAreaSquareMetres:F2} m2 (not gross building area)"));
            foreach (var room in r.Content.GroundFloorRooms) Line($"Requested {room.RoomType}: {room.Count}");
            foreach (var space in r.Content.AdditionalSpaces) Line($"Additional space: {space.Option} {space.OtherText}");
            foreach (var relation in r.Content.Relationships) Line($"Preference: {relation.SourceRoomType} / {relation.Kind} / {relation.TargetRoomType}");
            foreach (var floor in data.Floors)
            {
                Page(); Line($"Floor {floor.Level} | localCartesian metres | X right, Y up");
                var viewport = new ReportDrawingViewport(floor.Rooms.SelectMany(a => a.Polygon)
                    .Concat(floor.Walls.SelectMany(w => new[] { w.Start, w.End })), 510, 400, 12);
                XPoint Point(ReportPlotPoint point) { var q = viewport.Transform(point); return new(q.X + 36, q.Y + 120); }
                foreach (var room in floor.Rooms)
                {
                    token.ThrowIfCancellationRequested();
                    graphics!.DrawPolygon(new XPen(Teal, 0.8), new XSolidBrush(XColor.FromArgb(230, 246, 245)), room.Polygon.Select(Point).ToArray(), XFillMode.Winding);
                }
                foreach (var wall in floor.Walls)
                    graphics!.DrawLine(new XPen(Navy, Math.Max(0.5, wall.ThicknessMetres * viewport.Scale)), Point(wall.Start), Point(wall.End));
                foreach (var opening in floor.Openings)
                {
                    var wall = floor.Walls.Single(w => w.Id == opening.WallId);
                    var ends = ReportDrawingViewport.Opening(wall, opening);
                    graphics!.DrawLine(new XPen(XColors.White, Math.Max(2, wall.ThicknessMetres * viewport.Scale + 1)), Point(ends.Start), Point(ends.End));
                    graphics.DrawLine(new XPen(opening.Type.Equals("door", StringComparison.OrdinalIgnoreCase) ? XColors.Orange : Teal, 1.5), Point(ends.Start), Point(ends.End));
                }
                var occupied = new List<ReportLabelBox>();
                var omittedLabels = new List<string>();
                for (var roomIndex=0;roomIndex<floor.Rooms.Count;roomIndex++)
                {
                    token.ThrowIfCancellationRequested();
                    var room=floor.Rooms[roomIndex]; var label=$"R{roomIndex+1}: {room.Measurement.Name}";
                    if(label.Any(c=>c>0x7e || char.IsControl(c))) throw new ArgumentException("Unsupported label text.");
                    var points=room.Polygon.Select(p=>{var q=Point(p);return new ReportPlotPoint(q.X,q.Y);}).ToArray();
                    var size=graphics!.MeasureString(label,small);
                    var box=ReportRoomLabelPlacement.Find(points,size.Width+8,18,occupied);
                    if(box is null)
                    {
                        label=$"R{roomIndex+1}"; size=graphics.MeasureString(label,small);
                        box=ReportRoomLabelPlacement.Find(points,size.Width+8,18,occupied);
                    }
                    if(box is null){omittedLabels.Add($"R{roomIndex+1}");continue;}
                    occupied.Add(box);graphics.DrawRectangle(XBrushes.White,box.X,box.Y,box.Width,box.Height);
                    graphics.DrawString(label,small,new XSolidBrush(Navy),box.X+4,box.Y+12);
                }
                y = 548; Line("Legend: navy walls; orange doors; teal windows and room outlines.");
                if(omittedLabels.Count>0) Line("Room labels omitted where no safe interior fit exists: "+string.Join(", ",omittedLabels));
                Line("Room area schedule | bounds are axis-aligned extents, not wall lengths");
                for (var roomIndex=0;roomIndex<floor.Rooms.Count;roomIndex++)
                {
                    var room=floor.Rooms[roomIndex];
                    var m = room.Measurement;
                    Line(FormattableString.Invariant($"R{roomIndex+1}: {m.Name} | {m.Type} | {m.AreaSquareMetres:F2} m2 | bounds {m.BoundsWidthMetres:F2} x {m.BoundsHeightMetres:F2} m"));
                }
                Line(FormattableString.Invariant($"Sum of room polygon areas: {floor.SumRoomAreaSquareMetres:F2} m2 (not gross building area)"));
            }
            Page(); Line("Validation evidence and preliminary disclaimer");
            Line($"Validation: {p.Validation.ValidationId}; validator: {p.Validation.ValidatorVersion}");
            Line($"Geometry valid: {p.Validation.GeometryValid}; blocking issues: {p.Validation.HasBlockingIssues}");
            Line($"Requirements match: {p.Validation.RequirementsMatch?.ToString() ?? "UNKNOWN"}");
            Line($"Regulatory evidence: {p.Validation.RegulatoryEvidenceStatus}");
            Line($"Warnings acknowledged: {s.Confirmation.WarningAcknowledgement}");
            Line($"Disclaimer version: {s.Disclaimer.Version}"); Line(s.Disclaimer.Text); Line(data.Notice);
            graphics!.Dispose(); graphics = null; token.ThrowIfCancellationRequested();
            using var output = new BoundedOutput(_maximumOutputBytes, token); document.Save(output, false);
            if (output.Length > 10 * 1024 * 1024) throw new ArgumentException("Report output limit.");
            token.ThrowIfCancellationRequested(); return output.ToArray();
        }
        finally { graphics?.Dispose(); }
    }

    private sealed class NotoResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
            familyName == "BuildMind Noto" ? new("BuildMindNotoRegular") : throw new InvalidOperationException("Unsupported font.");
        public byte[] GetFont(string faceName)
        {
            using var stream = typeof(PreliminaryPdfRenderer).Assembly.GetManifestResourceStream("BuildMind.Infrastructure.Reports.Fonts.NotoSans-Regular.ttf")
                ?? throw new InvalidOperationException("Report font unavailable.");
            using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
        }
    }

    internal sealed class BoundedOutput(int limit, CancellationToken cancellationToken = default) : MemoryStream
    {
        private void Check(long count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count < 0 || Position > limit - count) throw new InvalidOperationException("Report output limit.");
        }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void SetLength(long value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (value < 0 || value > limit) throw new InvalidOperationException("Report output limit.");
            base.SetLength(value);
        }
    }
}

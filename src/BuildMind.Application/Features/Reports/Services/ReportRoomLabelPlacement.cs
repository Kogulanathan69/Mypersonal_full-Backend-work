namespace BuildMind.Application.Features.Reports.Services;

public sealed record ReportLabelBox(double X, double Y, double Width, double Height);

/// <summary>Conservative label placement: reject rather than cross room edges or other labels.</summary>
public static class ReportRoomLabelPlacement
{
    public static ReportLabelBox? Find(IReadOnlyList<ReportPlotPoint> polygon, double width, double height,
        IReadOnlyList<ReportLabelBox> occupied)
    {
        ArgumentNullException.ThrowIfNull(polygon); ArgumentNullException.ThrowIfNull(occupied);
        if (polygon.Count < 4 || polygon.Any(p=>p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y))
            || !double.IsFinite(width) || !double.IsFinite(height) || width<=0 || height<=0)
            throw new ArgumentException("Invalid label geometry.");
        var minX=polygon.Min(p=>p.X);var maxX=polygon.Max(p=>p.X);
        var minY=polygon.Min(p=>p.Y);var maxY=polygon.Max(p=>p.Y);
        // Bounded search, centre first, then a deterministic interior grid.
        foreach(var row in new[]{5,3,7,1,9}) foreach(var col in new[]{5,3,7,1,9})
        {
            var box=new ReportLabelBox(minX+(maxX-minX)*col/10-width/2,minY+(maxY-minY)*row/10-height/2,width,height);
            if (occupied.Any(b=>box.X<b.X+b.Width && box.X+width>b.X && box.Y<b.Y+b.Height && box.Y+height>b.Y)) continue;
            ReportPlotPoint[] corners=[new(box.X,box.Y),new(box.X+width,box.Y),new(box.X+width,box.Y+height),new(box.X,box.Y+height)];
            if(corners.All(Inside) && !Enumerable.Range(0,polygon.Count-1).Any(i=>CrossesBox(polygon[i],polygon[i+1],box))) return box;
        }
        return null;

        bool Inside(ReportPlotPoint p)
        {
            var inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {
                var a=polygon[i];var b=polygon[j];
                if((a.Y>p.Y)!=(b.Y>p.Y) && p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X) inside=!inside;
            }
            return inside;
        }
    }
    private static bool CrossesBox(ReportPlotPoint a,ReportPlotPoint b,ReportLabelBox r)
    {
        // Segment/slab clipping includes edge tangencies: labels retain clear separation.
        double lo=0,hi=1;
        bool Axis(double start,double delta,double min,double max)
        {
            if(delta==0) return start>=min && start<=max;
            var t1=(min-start)/delta;var t2=(max-start)/delta;
            lo=Math.Max(lo,Math.Min(t1,t2));hi=Math.Min(hi,Math.Max(t1,t2));return lo<=hi;
        }
        return Axis(a.X,b.X-a.X,r.X,r.X+r.Width) && Axis(a.Y,b.Y-a.Y,r.Y,r.Y+r.Height);
    }
}

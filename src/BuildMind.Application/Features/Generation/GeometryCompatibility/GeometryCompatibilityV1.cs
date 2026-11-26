using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BuildMind.Application.Features.Generation.GeometryCompatibility;

public sealed class GeometryCompatibilityException(string code) : ArgumentException(code)
{
    public string Code { get; } = code;
}

public sealed record GeometryPrecisionProfileV1(
    string Version = "m2-m6-metric-v1",
    decimal CoordinateQuantumM = 0.000001m,
    decimal CoordinateComparisonToleranceM = 0.000001m,
    decimal GoldenToleranceM = 0.001m,
    decimal RoundTripToleranceM = 0.001m,
    decimal MinimumClearanceM = 0.000001m,
    decimal SetbackMarginM = 0.00000001m,
    decimal ContactToleranceM = 0.000000001m)
{
    public void Validate()
    {
        if (this != new GeometryPrecisionProfileV1())
            throw new GeometryCompatibilityException("PRECISION_PROFILE_UNSUPPORTED");
    }
}

public sealed record TranslationAffineV1(decimal A, decimal B, decimal C, decimal D, decimal Tx, decimal Ty)
{
    public void Validate(decimal originX, decimal originY)
    {
        if ((A, B, C, D) != (1m, 0m, 0m, 1m))
            throw new GeometryCompatibilityException("LOCAL_AFFINE_UNSUPPORTED");
        if (Tx != -originX || Ty != -originY)
            throw new GeometryCompatibilityException("LOCAL_AFFINE_INCONSISTENT");
    }
}

public sealed record CompatibilityVertexV1(Guid VertexId, decimal X, decimal Y, Guid SourceVertexId);
public sealed record CompatibilityEdgeV1(Guid EdgeId, Guid StartVertexId, Guid EndVertexId,
    Guid SourceEdgeId, int NormalizedIndex);
public sealed record CompatibilityRingV1(Guid RingId, IReadOnlyList<CompatibilityVertexV1> Vertices,
    IReadOnlyList<CompatibilityEdgeV1> Edges);
public sealed record LocalFrameV1(decimal OriginX, decimal OriginY, TranslationAffineV1 Affine,
    CompatibilityRingV1 Exterior, IReadOnlyList<CompatibilityRingV1> Holes,
    string Type = "localCartesian", string Unit = "m", string XDirection = "right", string YDirection = "up");
public sealed record CrsProvenanceV1(string SourceCrs, string CalculationCrs, bool CalculationIsProjected,
    string AxisUnitX, string AxisUnitY, string TransformationPolicy, string TransformationEvidenceDigest,
    string EvidenceVersion = "m2-m6-transform-evidence/1.0", bool AlwaysXy = true,
    string SourceGeometryDigest = "", string ProjectedCoordinateDigest = "", string LocalCoordinateDigest = "",
    decimal OriginX = 0m, decimal OriginY = 0m, string OperationAuthority = "EPSG", string OperationCode = "unknown");
public sealed record LandGeometryHandoffV1(Guid ProjectId, Guid LandRevisionId, string CalculationCrs,
    string SourceGeometryDigest, CrsProvenanceV1 CrsProvenance, LocalFrameV1 LocalFrame, GeometryPrecisionProfileV1 Precision,
    string? GeometryDigest = null, string SchemaVersion = "m2-m6-land-handoff/1.0");
public sealed record StableEdgeMappingV1(Guid EdgeId, int ParcelEdgeIndex);

public static class GeometryCompatibilityV1
{
    public const string SchemaVersion = "m2-m6-land-handoff/1.0";
    public const string DigestDomain = "BuildMind.M2M6.LandGeometry.v1\0";
    public const string TransformationPolicy = "pyproj-always-xy-no-ballpark-only-best-v1";
    public const string TransformationEvidenceDomain = "BuildMind.M2M6.TransformEvidence.v1\0";
    private const decimal Quantum = 0.000001m;
    private const decimal MinimumClearance = 0.000001m;
    private const decimal MaximumCoordinate = 1_000_000m;

    public static (decimal X, decimal Y) ProjectedToLocal(
        decimal projectedX, decimal projectedY, decimal originX, decimal originY) =>
        (Canonical(decimal.Round(projectedX - Canonical(originX), 6, MidpointRounding.ToEven)),
         Canonical(decimal.Round(projectedY - Canonical(originY), 6, MidpointRounding.ToEven)));

    public static LandGeometryHandoffV1 Validate(LandGeometryHandoffV1 handoff,
        Guid expectedProjectId, Guid expectedLandRevisionId, bool requireDigest = true)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        if (handoff.SchemaVersion != SchemaVersion)
            throw new GeometryCompatibilityException("SCHEMA_VERSION_UNSUPPORTED");
        Id(handoff.ProjectId, "PROJECT_BINDING_INVALID");
        Id(handoff.LandRevisionId, "LAND_REVISION_INVALID");
        if (handoff.ProjectId != expectedProjectId)
            throw new GeometryCompatibilityException("PROJECT_BINDING_MISMATCH");
        if (handoff.LandRevisionId != expectedLandRevisionId)
            throw new GeometryCompatibilityException("LAND_REVISION_MISMATCH");
        ValidateCrs(handoff);
        Hash(handoff.SourceGeometryDigest, "SOURCE_GEOMETRY_DIGEST_INVALID");
        handoff.Precision.Validate();
        ValidateFrame(handoff.LocalFrame);
        var computed = ComputeDigest(handoff);
        if (requireDigest && !StringComparer.Ordinal.Equals(handoff.GeometryDigest, computed))
            throw new GeometryCompatibilityException("GEOMETRY_DIGEST_MISMATCH");
        return handoff;
    }

    public static string ComputeDigest(LandGeometryHandoffV1 handoff)
    {
        var bytes = new List<byte>(4096);
        bytes.AddRange(Encoding.ASCII.GetBytes(DigestDomain));
        Text(bytes, 1, handoff.SchemaVersion);
        Text(bytes, 2, handoff.ProjectId.ToString("D"));
        Text(bytes, 3, handoff.LandRevisionId.ToString("D"));
        Text(bytes, 4, handoff.CalculationCrs);
        Integer(bytes, 5, Micrometres(Canonical(handoff.LocalFrame.OriginX)));
        Integer(bytes, 6, Micrometres(Canonical(handoff.LocalFrame.OriginY)));
        Text(bytes, 7, handoff.Precision.Version);
        Field(bytes, 8, Convert.FromHexString(handoff.SourceGeometryDigest));
        Integer(bytes, 9, Micrometres(Canonical(handoff.LocalFrame.Affine.A)));
        Integer(bytes, 10, Micrometres(Canonical(handoff.LocalFrame.Affine.B)));
        Integer(bytes, 11, Micrometres(Canonical(handoff.LocalFrame.Affine.C)));
        Integer(bytes, 12, Micrometres(Canonical(handoff.LocalFrame.Affine.D)));
        Integer(bytes, 13, Micrometres(Canonical(handoff.LocalFrame.Affine.Tx)));
        Integer(bytes, 14, Micrometres(Canonical(handoff.LocalFrame.Affine.Ty)));
        Text(bytes, 15, handoff.CrsProvenance.SourceCrs);
        Text(bytes, 16, handoff.CrsProvenance.TransformationPolicy);
        Field(bytes, 17, Convert.FromHexString(handoff.CrsProvenance.TransformationEvidenceDigest));

        var rings = new List<(long Type, CompatibilityRingV1 Ring)> { (0, handoff.LocalFrame.Exterior) };
        rings.AddRange(handoff.LocalFrame.Holes.Select(ring => (1L, ring)));
        foreach (var (type, ring) in rings.OrderBy(item => item.Type).ThenBy(item => item.Ring.RingId.ToString("D"), StringComparer.Ordinal))
        {
            ValidateRing(ring);
            Integer(bytes, 20, type);
            Text(bytes, 21, ring.RingId.ToString("D"));
            foreach (var vertex in ring.Vertices.OrderBy(item => item.VertexId.ToString("D"), StringComparer.Ordinal))
            {
                Text(bytes, 22, vertex.VertexId.ToString("D"));
                Text(bytes, 23, vertex.SourceVertexId.ToString("D"));
                Integer(bytes, 24, Micrometres(Canonical(vertex.X)));
                Integer(bytes, 25, Micrometres(Canonical(vertex.Y)));
            }
            foreach (var edge in ring.Edges.OrderBy(item => item.EdgeId.ToString("D"), StringComparer.Ordinal))
            {
                var endpoints = new[] { edge.StartVertexId.ToString("D"), edge.EndVertexId.ToString("D") };
                Array.Sort(endpoints, StringComparer.Ordinal);
                Text(bytes, 26, edge.EdgeId.ToString("D"));
                Text(bytes, 27, edge.SourceEdgeId.ToString("D"));
                Text(bytes, 28, endpoints[0]);
                Text(bytes, 29, endpoints[1]);
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }

    public static IReadOnlyList<decimal> BindSetbacks(CompatibilityRingV1 exterior,
        IReadOnlyList<StableEdgeMappingV1> mappings, IReadOnlyDictionary<Guid, decimal> setbacks)
    {
        ValidateRing(exterior);
        if (mappings.Count != exterior.Edges.Count)
            throw new GeometryCompatibilityException("EDGE_REMAP_INCOMPLETE");
        var edgeIds = exterior.Edges.Select(item => item.EdgeId).ToHashSet();
        if (mappings.Select(item => item.EdgeId).Distinct().Count() != mappings.Count ||
            mappings.Select(item => item.ParcelEdgeIndex).Distinct().Count() != mappings.Count ||
            !mappings.Select(item => item.EdgeId).ToHashSet().SetEquals(edgeIds) ||
            !mappings.Select(item => item.ParcelEdgeIndex).ToHashSet().SetEquals(Enumerable.Range(0, exterior.Edges.Count)))
            throw new GeometryCompatibilityException("EDGE_REMAP_INVALID");
        if (!setbacks.Keys.ToHashSet().SetEquals(edgeIds))
            throw new GeometryCompatibilityException("SETBACK_EDGE_COVERAGE_MISSING");
        var result = new decimal[exterior.Edges.Count];
        foreach (var mapping in mappings)
        {
            var distance = Canonical(setbacks[mapping.EdgeId]);
            if (distance < 0) throw new GeometryCompatibilityException("SETBACK_INVALID");
            result[mapping.ParcelEdgeIndex] = distance;
        }
        return result;
    }

    public static string ComputeTransformationEvidenceDigest(CrsProvenanceV1 value)
    {
        var bytes = new List<byte>(512); bytes.AddRange(Encoding.ASCII.GetBytes(TransformationEvidenceDomain));
        Text(bytes, 1, value.EvidenceVersion); Text(bytes, 2, value.SourceCrs); Text(bytes, 3, value.CalculationCrs);
        Integer(bytes, 4, value.AlwaysXy ? 1 : 0); Field(bytes, 5, Convert.FromHexString(value.SourceGeometryDigest));
        Field(bytes, 6, Convert.FromHexString(value.ProjectedCoordinateDigest));
        Field(bytes, 7, Convert.FromHexString(value.LocalCoordinateDigest));
        Integer(bytes, 8, Micrometres(Canonical(value.OriginX))); Integer(bytes, 9, Micrometres(Canonical(value.OriginY)));
        Text(bytes, 10, value.TransformationPolicy); Text(bytes, 11, value.OperationAuthority); Text(bytes, 12, value.OperationCode);
        return Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray()));
    }

    public static double AreaTolerance(double areaM2, double perimeterM)
    {
        if (!double.IsFinite(areaM2) || !double.IsFinite(perimeterM) || areaM2 < 0 || perimeterM < 0)
            throw new GeometryCompatibilityException("AREA_TOLERANCE_INPUT_INVALID");
        var ulp = Math.BitIncrement(areaM2) - areaM2;
        return Math.Max(1e-10, Math.Max(32d * ulp, perimeterM * 1e-8));
    }

    private static void ValidateCrs(LandGeometryHandoffV1 handoff)
    {
        var value = handoff.CrsProvenance ?? throw new GeometryCompatibilityException("CRS_PROVENANCE_MISSING");
        if (string.IsNullOrWhiteSpace(handoff.CalculationCrs) || string.IsNullOrWhiteSpace(value.SourceCrs) ||
            !StringComparer.Ordinal.Equals(handoff.CalculationCrs, value.CalculationCrs) ||
            !value.CalculationIsProjected || value.AxisUnitX != "m" || value.AxisUnitY != "m" ||
            value.TransformationPolicy != TransformationPolicy || value.EvidenceVersion != "m2-m6-transform-evidence/1.0" ||
            !value.AlwaysXy || value.SourceGeometryDigest != handoff.SourceGeometryDigest ||
            value.OriginX != handoff.LocalFrame.OriginX || value.OriginY != handoff.LocalFrame.OriginY ||
            string.IsNullOrWhiteSpace(value.OperationAuthority) || string.IsNullOrWhiteSpace(value.OperationCode))
            throw new GeometryCompatibilityException("CRS_PROVENANCE_INVALID");
        Hash(value.SourceGeometryDigest, "CRS_PROVENANCE_DIGEST_INVALID");
        Hash(value.ProjectedCoordinateDigest, "CRS_PROVENANCE_DIGEST_INVALID");
        Hash(value.LocalCoordinateDigest, "CRS_PROVENANCE_DIGEST_INVALID");
        Hash(value.TransformationEvidenceDigest, "CRS_PROVENANCE_DIGEST_INVALID");
        if (!StringComparer.Ordinal.Equals(value.TransformationEvidenceDigest, ComputeTransformationEvidenceDigest(value)))
            throw new GeometryCompatibilityException("CRS_PROVENANCE_DIGEST_MISMATCH");
    }

    private static void ValidateFrame(LocalFrameV1 frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if ((frame.Type, frame.Unit, frame.XDirection, frame.YDirection) != ("localCartesian", "m", "right", "up"))
            throw new GeometryCompatibilityException("COORDINATE_SYSTEM_UNSUPPORTED");
        var originX = Canonical(frame.OriginX);
        var originY = Canonical(frame.OriginY);
        frame.Affine.Validate(originX, originY);
        ValidateRing(frame.Exterior);
        foreach (var hole in frame.Holes) ValidateRing(hole);
        ValidateTopology(frame.Exterior, frame.Holes);
        var rings = new[] { frame.Exterior }.Concat(frame.Holes).ToArray();
        if (rings.Select(item => item.RingId).Distinct().Count() != rings.Length)
            throw new GeometryCompatibilityException("RING_ID_DUPLICATE");
        if (rings.SelectMany(item => item.Vertices).Select(item => item.VertexId).Distinct().Count() != rings.Sum(item => item.Vertices.Count))
            throw new GeometryCompatibilityException("VERTEX_ID_DUPLICATE");
        if (rings.SelectMany(item => item.Edges).Select(item => item.EdgeId).Distinct().Count() != rings.Sum(item => item.Edges.Count))
            throw new GeometryCompatibilityException("EDGE_ID_DUPLICATE");
    }

    private static void ValidateRing(CompatibilityRingV1 ring)
    {
        ArgumentNullException.ThrowIfNull(ring);
        Id(ring.RingId, "RING_ID_INVALID");
        if (ring.Vertices.Count < 3 || ring.Edges.Count != ring.Vertices.Count)
            throw new GeometryCompatibilityException("RING_TOPOLOGY_INVALID");
        if (ring.Vertices.Select(item => item.VertexId).Distinct().Count() != ring.Vertices.Count ||
            ring.Vertices.Select(item => item.SourceVertexId).Distinct().Count() != ring.Vertices.Count)
            throw new GeometryCompatibilityException("VERTEX_ID_DUPLICATE");
        if (ring.Edges.Select(item => item.EdgeId).Distinct().Count() != ring.Edges.Count ||
            ring.Edges.Select(item => item.SourceEdgeId).Distinct().Count() != ring.Edges.Count)
            throw new GeometryCompatibilityException("EDGE_ID_DUPLICATE");
        foreach (var vertex in ring.Vertices)
        {
            Id(vertex.VertexId, "VERTEX_ID_INVALID");
            Id(vertex.SourceVertexId, "VERTEX_LINEAGE_INVALID");
            Canonical(vertex.X); Canonical(vertex.Y);
        }
        if (!ring.Edges.Select(item => item.NormalizedIndex).ToHashSet().SetEquals(Enumerable.Range(0, ring.Vertices.Count)))
            throw new GeometryCompatibilityException("EDGE_REMAP_INVALID");
        var byIndex = ring.Edges.ToDictionary(item => item.NormalizedIndex);
        for (var index = 0; index < ring.Vertices.Count; index++)
        {
            var edge = byIndex[index];
            Id(edge.EdgeId, "EDGE_ID_INVALID"); Id(edge.SourceEdgeId, "EDGE_LINEAGE_INVALID");
            var expected = new HashSet<Guid> { ring.Vertices[index].VertexId, ring.Vertices[(index + 1) % ring.Vertices.Count].VertexId };
            if (!expected.SetEquals(new[] { edge.StartVertexId, edge.EndVertexId }))
                throw new GeometryCompatibilityException("EDGE_REMAP_INVALID");
            var dx = ring.Vertices[index].X - ring.Vertices[(index + 1) % ring.Vertices.Count].X;
            var dy = ring.Vertices[index].Y - ring.Vertices[(index + 1) % ring.Vertices.Count].Y;
            if (Math.Sqrt((double)(dx * dx + dy * dy)) < (double)MinimumClearance)
                throw new GeometryCompatibilityException("PARCEL_PRECISION_UNSUPPORTED");
        }
    }

    private static void ValidateTopology(CompatibilityRingV1 exterior, IReadOnlyList<CompatibilityRingV1> holes)
    {
        var exteriorPoints = Points(exterior);
        if (SignedArea2(exteriorPoints) == 0 || SelfIntersects(exteriorPoints))
            throw new GeometryCompatibilityException("RING_TOPOLOGY_INVALID");
        foreach (var hole in holes)
        {
            var points = Points(hole);
            if (SignedArea2(points) == 0 || SelfIntersects(points))
                throw new GeometryCompatibilityException("RING_TOPOLOGY_INVALID");
            if (points.Any(point => !StrictlyInside(point, exteriorPoints)) || RingsIntersect(points, exteriorPoints))
                throw new GeometryCompatibilityException("HOLE_OUTSIDE_EXTERIOR");
        }
        for (var left = 0; left < holes.Count; left++)
        for (var right = left + 1; right < holes.Count; right++)
        {
            var a = Points(holes[left]); var b = Points(holes[right]);
            if (RingsIntersect(a, b) || StrictlyInside(a[0], b) || StrictlyInside(b[0], a))
                throw new GeometryCompatibilityException("HOLE_OVERLAP");
        }
    }

    private static (decimal X, decimal Y)[] Points(CompatibilityRingV1 ring) =>
        ring.Vertices.Select(vertex => (Canonical(vertex.X), Canonical(vertex.Y))).ToArray();
    private static decimal Cross((decimal X, decimal Y) a, (decimal X, decimal Y) b, (decimal X, decimal Y) c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static decimal SignedArea2((decimal X, decimal Y)[] points) =>
        Enumerable.Range(0, points.Length).Sum(i => points[i].X * points[(i + 1) % points.Length].Y - points[(i + 1) % points.Length].X * points[i].Y);
    private static bool OnSegment((decimal X, decimal Y) p, (decimal X, decimal Y) a, (decimal X, decimal Y) b) =>
        Cross(a, b, p) == 0 && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
        p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    private static bool SegmentsIntersect((decimal X, decimal Y) a, (decimal X, decimal Y) b,
        (decimal X, decimal Y) c, (decimal X, decimal Y) d)
    {
        var abC = Cross(a, b, c); var abD = Cross(a, b, d); var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
        return (abC == 0 && OnSegment(c, a, b)) || (abD == 0 && OnSegment(d, a, b)) ||
               (cdA == 0 && OnSegment(a, c, d)) || (cdB == 0 && OnSegment(b, c, d)) ||
               ((abC > 0) != (abD > 0) && (cdA > 0) != (cdB > 0));
    }
    private static bool SelfIntersects((decimal X, decimal Y)[] points)
    {
        for (var i = 0; i < points.Length; i++) for (var j = i + 1; j < points.Length; j++)
        {
            if (j == i || j == (i + 1) % points.Length || i == (j + 1) % points.Length) continue;
            if (SegmentsIntersect(points[i], points[(i + 1) % points.Length], points[j], points[(j + 1) % points.Length])) return true;
        }
        return false;
    }
    private static bool RingsIntersect((decimal X, decimal Y)[] a, (decimal X, decimal Y)[] b) =>
        Enumerable.Range(0, a.Length).Any(i => Enumerable.Range(0, b.Length).Any(j =>
            SegmentsIntersect(a[i], a[(i + 1) % a.Length], b[j], b[(j + 1) % b.Length])));
    private static bool StrictlyInside((decimal X, decimal Y) point, (decimal X, decimal Y)[] ring)
    {
        if (Enumerable.Range(0, ring.Length).Any(i => OnSegment(point, ring[i], ring[(i + 1) % ring.Length]))) return false;
        var inside = false;
        for (var i = 0; i < ring.Length; i++)
        {
            var a = ring[i]; var b = ring[(i + 1) % ring.Length];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static decimal Canonical(decimal value)
    {
        if (decimal.Round(value, 6, MidpointRounding.ToEven) != value)
            throw new GeometryCompatibilityException("COORDINATE_PRECISION_UNSUPPORTED");
        if (Math.Abs(value) > MaximumCoordinate)
            throw new GeometryCompatibilityException("COORDINATE_RANGE_UNSUPPORTED");
        return value;
    }

    private static long Micrometres(decimal value) => checked((long)(value / Quantum));
    private static void Id(Guid value, string code) { if (value == Guid.Empty) throw new GeometryCompatibilityException(code); }
    private static void Hash(string value, string code)
    {
        if (value is null || value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
            throw new GeometryCompatibilityException(code);
    }
    private static void Text(List<byte> output, ushort tag, string value) => Field(output, tag, Encoding.UTF8.GetBytes(value));
    private static void Integer(List<byte> output, ushort tag, long value)
    {
        Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); Field(output, tag, bytes);
    }
    private static void Field(List<byte> output, ushort tag, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[6]; BinaryPrimitives.WriteUInt16BigEndian(header, tag);
        BinaryPrimitives.WriteUInt32BigEndian(header[2..], checked((uint)payload.Length));
        output.AddRange(header); output.AddRange(payload);
    }
}

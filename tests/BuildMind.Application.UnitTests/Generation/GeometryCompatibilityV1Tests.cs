using BuildMind.Application.Features.Generation.GeometryCompatibility;
using System.Text.Json.Nodes;

namespace BuildMind.Application.UnitTests.Generation;

public sealed class GeometryCompatibilityV1Tests
{
    [Fact]
    public void Canonical_digest_matches_python_golden_fixture()
    {
        var handoff = Fixture();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BuildMind.sln"))) directory = directory.Parent;
        var vector = JsonNode.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "python-services", "tests",
            "m6_contract", "fixtures", "m2-m6-canonical-digest-vector.v1.json")))!;
        Assert.Equal(vector["expectedSha256"]!.GetValue<string>(), GeometryCompatibilityV1.ComputeDigest(handoff));
    }

    [Fact]
    public void Stable_edge_remap_preserves_unequal_setbacks()
    {
        var ring = Fixture().LocalFrame.Exterior;
        var mapping = ring.Edges.Select(edge => new StableEdgeMappingV1(edge.EdgeId, edge.NormalizedIndex)).ToArray();
        var setbacks = ring.Edges.ToDictionary(edge => edge.EdgeId, edge => (decimal)edge.NormalizedIndex + 1m);
        var bound = GeometryCompatibilityV1.BindSetbacks(ring, mapping, setbacks);
        Assert.All(ring.Edges, edge => Assert.Equal(setbacks[edge.EdgeId], bound[edge.NormalizedIndex]));
    }

    [Fact]
    public void Revision_and_digest_mismatches_fail_closed()
    {
        var value = WithDigest(Fixture());
        Assert.Equal("PROJECT_BINDING_MISMATCH", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.Validate(value, Guid.NewGuid(), value.LandRevisionId)).Code);
        Assert.Equal("LAND_REVISION_MISMATCH", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.Validate(value, value.ProjectId, Guid.NewGuid())).Code);
        Assert.Equal("GEOMETRY_DIGEST_MISMATCH", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.Validate(value with { GeometryDigest = new string('0', 64) }, value.ProjectId, value.LandRevisionId)).Code);
    }

    [Fact]
    public void Incorrect_affine_precision_and_crs_provenance_fail_closed()
    {
        var value = Fixture();
        Assert.Equal("LOCAL_AFFINE_UNSUPPORTED", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.Validate(value with { LocalFrame = value.LocalFrame with
                { Affine = value.LocalFrame.Affine with { A = 2 } } }, value.ProjectId, value.LandRevisionId, false)).Code);
        Assert.Equal("PRECISION_PROFILE_UNSUPPORTED", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.Validate(value with { Precision = value.Precision with { GoldenToleranceM = 0.01m } },
                value.ProjectId, value.LandRevisionId, false)).Code);
        Assert.Equal("CRS_PROVENANCE_INVALID", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.Validate(value with { CrsProvenance = value.CrsProvenance with { AxisUnitY = "ft" } },
                value.ProjectId, value.LandRevisionId, false)).Code);
    }

    [Fact]
    public void Duplicate_edges_and_missing_mapping_fail_closed()
    {
        var value = Fixture();
        var edges = value.LocalFrame.Exterior.Edges.ToArray();
        edges[1] = edges[1] with { EdgeId = edges[0].EdgeId };
        var duplicate = value.LocalFrame.Exterior with { Edges = edges };
        Assert.Equal("EDGE_ID_DUPLICATE", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.ComputeDigest(value with { LocalFrame = value.LocalFrame with { Exterior = duplicate } })).Code);
        Assert.Equal("EDGE_REMAP_INCOMPLETE", Assert.Throws<GeometryCompatibilityException>(() =>
            GeometryCompatibilityV1.BindSetbacks(value.LocalFrame.Exterior,
                value.LocalFrame.Exterior.Edges.Skip(1).Select(edge => new StableEdgeMappingV1(edge.EdgeId, edge.NormalizedIndex)).ToArray(),
                value.LocalFrame.Exterior.Edges.ToDictionary(edge => edge.EdgeId, _ => 1m))).Code);
    }

    [Fact]
    public void Digest_is_invariant_to_orientation_and_start_vertex()
    {
        var value = Fixture();
        Assert.Equal(GeometryCompatibilityV1.ComputeDigest(value), GeometryCompatibilityV1.ComputeDigest(
            value with { LocalFrame = value.LocalFrame with { Exterior = Reorder(value.LocalFrame.Exterior, 2, false) } }));
        Assert.Equal(GeometryCompatibilityV1.ComputeDigest(value), GeometryCompatibilityV1.ComputeDigest(
            value with { LocalFrame = value.LocalFrame with { Exterior = Reorder(value.LocalFrame.Exterior, 0, true) } }));
    }

    [Fact]
    public void Area_tolerance_uses_approved_maximum_policy()
    {
        Assert.Equal(1e-10, GeometryCompatibilityV1.AreaTolerance(0, 0));
        Assert.Equal(4e-7, GeometryCompatibilityV1.AreaTolerance(25, 40));
        var large = 1e300;
        Assert.True(GeometryCompatibilityV1.AreaTolerance(large, 0) >= 32 * (Math.BitIncrement(large) - large));
    }

    private static LandGeometryHandoffV1 WithDigest(LandGeometryHandoffV1 value) =>
        value with { GeometryDigest = GeometryCompatibilityV1.ComputeDigest(value) };

    private static CompatibilityRingV1 Reorder(CompatibilityRingV1 ring, int rotate, bool reverse)
    {
        var ordered = ring.Vertices.Skip(rotate).Concat(ring.Vertices.Take(rotate)).ToArray();
        if (reverse) Array.Reverse(ordered);
        var physical = ring.Edges.ToDictionary(edge => new HashSet<Guid> { edge.StartVertexId, edge.EndVertexId },
            edge => edge, HashSet<Guid>.CreateSetComparer());
        var edges = ordered.Select((vertex, index) =>
        {
            var next = ordered[(index + 1) % ordered.Length];
            var source = physical[new HashSet<Guid> { vertex.VertexId, next.VertexId }];
            return source with { StartVertexId = vertex.VertexId, EndVertexId = next.VertexId, NormalizedIndex = index };
        }).ToArray();
        return ring with { Vertices = ordered, Edges = edges };
    }

    private static LandGeometryHandoffV1 Fixture()
    {
        static Guid Id(string value) => Guid.Parse(value);
        var vertices = Enumerable.Range(1, 5).Select(index => Id($"40000000-0000-0000-0000-{index:000000000000}")).ToArray();
        var coordinates = new (decimal X, decimal Y)[] { (0, 0), (40, 5), (45, 35), (15, 50), (-10, 25) };
        var exteriorVertices = vertices.Select((id, index) => new CompatibilityVertexV1(id,
            coordinates[index].X, coordinates[index].Y, Id($"60000000-0000-0000-0000-{index + 1:000000000000}"))).ToArray();
        var exteriorEdges = vertices.Select((id, index) => new CompatibilityEdgeV1(
            Id($"50000000-0000-0000-0000-{index + 1:000000000000}"), id, vertices[(index + 1) % 5],
            Id($"70000000-0000-0000-0000-{index + 1:000000000000}"), index)).ToArray();
        var holeIds = Enumerable.Range(1, 3).Select(index => Id($"41000000-0000-0000-0000-{index:000000000000}")).ToArray();
        var holeCoordinates = new (decimal X, decimal Y)[] { (10, 15), (20, 17), (14, 25) };
        var holeVertices = holeIds.Select((id, index) => new CompatibilityVertexV1(id,
            holeCoordinates[index].X, holeCoordinates[index].Y, Id($"61000000-0000-0000-0000-{index + 1:000000000000}"))).ToArray();
        var holeEdges = holeIds.Select((id, index) => new CompatibilityEdgeV1(
            Id($"51000000-0000-0000-0000-{index + 1:000000000000}"), id, holeIds[(index + 1) % 3],
            Id($"71000000-0000-0000-0000-{index + 1:000000000000}"), index)).ToArray();
        var frame = new LocalFrameV1(370000m, 765000m,
            new TranslationAffineV1(1, 0, 0, 1, -370000m, -765000m),
            new CompatibilityRingV1(Id("30000000-0000-0000-0000-000000000001"), exteriorVertices, exteriorEdges),
            [new CompatibilityRingV1(Id("30000000-0000-0000-0000-000000000002"), holeVertices, holeEdges)]);
        var sourceDigest = string.Concat(Enumerable.Repeat("ab", 32));
        var provenance = new CrsProvenanceV1("EPSG:4326", "EPSG:32644", true, "m", "m",
            GeometryCompatibilityV1.TransformationPolicy, string.Concat(Enumerable.Repeat("00", 32)),
            SourceGeometryDigest: sourceDigest, ProjectedCoordinateDigest: string.Concat(Enumerable.Repeat("bc", 32)),
            LocalCoordinateDigest: string.Concat(Enumerable.Repeat("cd", 32)), OriginX: frame.OriginX,
            OriginY: frame.OriginY, OperationCode: "32644");
        provenance = provenance with { TransformationEvidenceDigest = GeometryCompatibilityV1.ComputeTransformationEvidenceDigest(provenance) };
        return new(Id("10000000-0000-0000-0000-000000000001"),
            Id("20000000-0000-0000-0000-000000000001"), "EPSG:32644", sourceDigest, provenance, frame,
            new GeometryPrecisionProfileV1());
    }
}

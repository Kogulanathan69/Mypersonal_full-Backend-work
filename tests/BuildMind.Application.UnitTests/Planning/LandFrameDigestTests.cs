using BuildMind.Application.Features.Planning;

namespace BuildMind.Application.UnitTests.Planning;

/// <summary>Cross-runtime parity with python-services app/internal/land_frame.py (same golden value as test_land_frame.py).</summary>
public sealed class LandFrameDigestTests
{
    private const string Golden = "c8293eff3a26fb435e77c5e28da5da25f821fb5bfb8ed019bcaf7aa0a60922a4";
    private static readonly (double, double)[] Ring = [(0, 0), (12.5, 0), (12.5, 8.25), (0, 8.25)];

    [Fact]
    public void Matches_the_python_golden_digest() =>
        Assert.Equal(Golden, LandFrameDigest.Compute("EPSG:5235", 500100, 500200, Ring));

    [Fact]
    public void Is_invariant_to_rotation_reversal_and_closing_vertex()
    {
        (double, double)[] rotated = [(12.5, 8.25), (0, 8.25), (0, 0), (12.5, 0)];
        (double, double)[] reversed = [(0, 8.25), (12.5, 8.25), (12.5, 0), (0, 0)];
        (double, double)[] closed = [(0, 0), (12.5, 0), (12.5, 8.25), (0, 8.25), (0, 0)];
        foreach (var ring in new[] { rotated, reversed, closed })
            Assert.Equal(Golden, LandFrameDigest.Compute("EPSG:5235", 500100, 500200, ring));
    }

    [Fact]
    public void Changes_with_geometry_crs_or_anchor()
    {
        Assert.NotEqual(Golden, LandFrameDigest.Compute("EPSG:5235", 500100, 500200, [(0, 0), (12.5, 0), (12.5, 8.26), (0, 8.25)]));
        Assert.NotEqual(Golden, LandFrameDigest.Compute("EPSG:32644", 500100, 500200, Ring));
        Assert.NotEqual(Golden, LandFrameDigest.Compute("EPSG:5235", 500101, 500200, Ring));
    }
}

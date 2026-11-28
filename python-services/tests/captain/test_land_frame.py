"""Captain coordinate frame: independent numeric references, CRS policy, digest/edge invariance, split lineage."""
import pytest

from app.geometry.land.validation import LandGeometryError
from app.internal.land_frame import build_frame, geometry_digest

# Golden digest shared with the .NET parity test (CaptainLandFrameDigestTests).
GOLDEN_RING = [(0.0, 0.0), (12.5, 0.0), (12.5, 8.25), (0.0, 8.25)]
GOLDEN_ANCHOR = (500100.0, 500200.0)


def square(x, y, d):
    return [[x, y], [x + d, y], [x + d, y + d], [x, y + d]]


def projected(frame, index=0):
    p = frame["localExterior"][index]
    return p["x"] + frame["frame"]["anchor"][0], p["y"] + frame["frame"]["anchor"][1]


def test_sld99_projection_origin_maps_to_the_grid_false_origin():
    # Reference: EPSG registry definition of EPSG:5235 (SLD99 / Sri Lanka Grid 1999): false easting/northing 500000/500000
    # at the natural origin 7°00'01.6975" N, 80°46'18.1671" E. (01.729" / 18.160" belong to the older Kandawala grid, EPSG:5234.)
    lat0, lon0 = 7 + 0 / 60 + 1.6975 / 3600, 80 + 46 / 60 + 18.1671 / 3600
    frame = build_frame("EPSG:5233", None, "EPSG:5235", square(lon0, lat0, 0.001))
    xs = [p["x"] + frame["frame"]["anchor"][0] for p in frame["localExterior"]]
    ys = [p["y"] + frame["frame"]["anchor"][1] for p in frame["localExterior"]]
    assert min(xs) == pytest.approx(500000.0, abs=0.01) and min(ys) == pytest.approx(500000.0, abs=0.01)
    assert frame["frame"]["calculationCrsSelection"] == "EXPLICIT" and frame["frame"]["georeferenced"] is True


def test_wgs84_central_meridian_on_the_equator_maps_to_utm_false_origin():
    frame = build_frame("EPSG:4326", None, "EPSG:32644", square(81.0, 0.0, 0.0005))
    xs = [p["x"] + frame["frame"]["anchor"][0] for p in frame["localExterior"]]
    ys = [p["y"] + frame["frame"]["anchor"][1] for p in frame["localExterior"]]
    assert min(xs) == pytest.approx(500000.0, abs=0.01) and min(ys) == pytest.approx(0.0, abs=0.01)


def test_sri_lanka_geographic_source_auto_selects_sld99_grid_and_records_it():
    frame = build_frame("EPSG:4326", None, None, square(79.86, 6.92, 0.0003))  # Colombo area
    assert frame["frame"]["crs"].endswith("5235") or "5235" in frame["frame"]["crs"]
    assert frame["frame"]["calculationCrsSelection"] == "AUTO_SELECTED_SRI_LANKA_GRID"
    assert frame["frame"]["transformation"]


def test_geographic_outside_sri_lanka_requires_an_explicit_calculation_crs():
    with pytest.raises(LandGeometryError, match="CALCULATION_CRS_REQUIRED"):
        build_frame("EPSG:4326", None, None, square(2.35, 48.85, 0.0003))


@pytest.mark.parametrize("crs,unit,code", [("EPSG:999999", None, "UNSUPPORTED_OR_INVALID_CRS"), ("LOCAL", None, "SOURCE_UNIT_REQUIRED"),
                                           ("", "m", "SOURCE_CRS_REQUIRED")])
def test_unknown_crs_and_missing_units_are_rejected(crs, unit, code):
    with pytest.raises(LandGeometryError, match=code):
        build_frame(crs, unit, None, square(0, 0, 10))


def test_local_feet_are_converted_and_never_claimed_georeferenced():
    frame = build_frame("LOCAL", "ft", None, square(0, 0, 10))
    assert frame["frame"]["kind"] == "LOCAL_ENGINEERING" and frame["frame"]["georeferenced"] is False
    assert all(e["lengthM"] == pytest.approx(3.048) for e in frame["edges"])


def test_asymmetric_polygon_with_nonzero_anchor_round_trips_to_projected_coordinates():
    ring = [[500123.7, 500456.2], [500141.3, 500456.2], [500141.3, 500470.9], [500130.0, 500478.4], [500123.7, 500470.0]]
    frame = build_frame("EPSG:5235", None, None, ring)
    assert frame["frame"]["anchor"] == [500123, 500456]
    assert frame["frame"]["calculationCrsSelection"] == "SOURCE_IS_PROJECTED"
    assert projected(frame) == pytest.approx((500123.7, 500456.2), abs=1e-6)


def test_digest_and_edge_ids_are_invariant_to_ring_rotation_and_reversal():
    ring = [[0, 0], [12, 0], [12, 9], [5, 14], [0, 9]]
    base = build_frame("LOCAL", "m", None, ring)
    rotated = build_frame("LOCAL", "m", None, ring[2:] + ring[:2])
    reversed_ring = build_frame("LOCAL", "m", None, list(reversed(ring)))
    assert base["geometryDigest"] == rotated["geometryDigest"] == reversed_ring["geometryDigest"]
    ids = lambda f: {e["edgeId"] for e in f["edges"]}  # noqa: E731
    assert ids(base) == ids(rotated) == ids(reversed_ring)
    assert build_frame("LOCAL", "m", None, [[0, 0], [12, 0], [12, 9.5], [5, 14], [0, 9]])["geometryDigest"] != base["geometryDigest"]


def test_split_edge_records_its_parent_and_unchanged_edges_keep_their_ids():
    before = build_frame("LOCAL", "m", None, square(0, 0, 10))
    previous = [{"edgeId": e["edgeId"], "start": e["start"], "end": e["end"]} for e in before["edges"]]
    after = build_frame("LOCAL", "m", None, [[0, 0], [4, 0], [10, 0], [10, 10], [0, 10]], previous)
    bottom = next(e["edgeId"] for e in before["edges"] if e["start"][1] == 0 and e["end"][1] == 0)
    children = [e for e in after["edges"] if e["start"][1] == 0 and e["end"][1] == 0]
    assert len(children) == 2 and all(c["parentEdgeId"] == bottom for c in children)
    unchanged = {e["edgeId"] for e in before["edges"]} & {e["edgeId"] for e in after["edges"]}
    assert len(unchanged) == 3  # the three untouched edges keep their identity


def test_self_intersecting_boundary_is_rejected_not_repaired():
    with pytest.raises(LandGeometryError):
        build_frame("LOCAL", "m", None, [[0, 0], [10, 10], [10, 0], [0, 10]])


def test_golden_digest_for_cross_runtime_parity():
    assert geometry_digest("EPSG:5235", GOLDEN_ANCHOR, GOLDEN_RING) == GOLDEN_DIGEST


def test_projected_anchor_shift_preserves_parent_edge_lineage():
    # Moving/splitting an edge near the bottom-left shifts the floored projected anchor (500123, 500456) -> (500121, 500454).
    # Unedited edges and split children on the shifted anchor must still preserve their parentEdgeId.
    ring = [[500123.7, 500456.2], [500141.3, 500456.2], [500141.3, 500470.9], [500123.7, 500470.9]]
    before = build_frame("EPSG:5235", None, None, ring)
    previous = [{"edgeId": e["edgeId"], "start": e["start"], "end": e["end"]} for e in before["edges"]]
    shifted = [[500121.4, 500454.3], [500141.3, 500456.2], [500141.3, 500463.0], [500141.3, 500470.9], [500123.7, 500470.9]]
    after = build_frame(
        "EPSG:5235", None, None, shifted,
        previous_edges=previous,
        previous_crs=before["frame"]["crs"],
        previous_anchor=before["frame"]["anchor"],
    )
    assert before["frame"]["anchor"] == [500123, 500456]
    assert after["frame"]["anchor"] == [500121, 500454]

    top_prev = next(e["edgeId"] for e in before["edges"] if abs(e["start"][1] - e["end"][1]) < 1e-6 and e["start"][1] > 10)
    top_after = next(e for e in after["edges"] if abs(e["start"][1] - e["end"][1]) < 1e-6 and e["start"][1] > 15)
    assert top_after["parentEdgeId"] == top_prev

    right_prev = next(e["edgeId"] for e in before["edges"] if abs(e["start"][0] - e["end"][0]) < 1e-6 and e["start"][0] > 10)
    right_children = [e for e in after["edges"] if abs(e["start"][0] - e["end"][0]) < 1e-6 and e["start"][0] > 15]
    assert len(right_children) == 2 and all(c["parentEdgeId"] == right_prev for c in right_children)


def test_different_previous_crs_disables_cross_crs_lineage():
    before = build_frame("LOCAL", "m", None, square(0, 0, 10))
    previous = [{"edgeId": e["edgeId"], "start": e["start"], "end": e["end"]} for e in before["edges"]]
    after = build_frame("LOCAL", "m", None, square(0, 0, 10), previous_edges=previous, previous_crs="EPSG:5235", previous_anchor=[0, 0])
    assert all(e["parentEdgeId"] is None for e in after["edges"])


GOLDEN_DIGEST = "c8293eff3a26fb435e77c5e28da5da25f821fb5bfb8ed019bcaf7aa0a60922a4"

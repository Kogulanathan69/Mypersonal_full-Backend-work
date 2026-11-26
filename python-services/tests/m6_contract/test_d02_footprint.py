from math import hypot, pi

import pytest

pytest.importorskip("shapely", minversion="2.1.2")

from shapely.geometry import Point, box

from app.geometry.footprint.parcel import MIN_CLEARANCE_M, validate_confirmed_parcel
from app.geometry.footprint.setbacks import FootprintError, build_footprint
from test_d02_parcel import LAND, PROJECT, RECTANGLE, confirmed, metric_land


def parcel(vertices=RECTANGLE, *, holes=()):
    return validate_confirmed_parcel(confirmed(metric_land(vertices, holes=holes)), PROJECT, LAND)


@pytest.mark.parametrize("distances, expected", [
    ((1, 1, 1, 1), box(1, 1, 9, 7)),
    ((1, 2, 3, 0.5), box(0.5, 1, 8, 5)),
    ((0, 0, 0, 0), box(0, 0, 10, 8)),
])
def test_rectangle_against_independent_analytic_region(distances, expected):
    p = parcel()
    result = build_footprint(p, distances)
    assert expected.covers(result.candidate)
    assert result.candidate.area == pytest.approx(expected.area, abs=0.031)
    assert result.upper_area_m2 == pytest.approx(expected.area, abs=1e-6)


def test_edge_setbacks_follow_original_clockwise_identity():
    p = parcel(list(reversed(RECTANGLE)))
    footprint = build_footprint(p, (2, 0, 0, 0)).candidate
    assert footprint.bounds[0] == pytest.approx(2, abs=0.001)
    assert footprint.bounds[1:] == (0, 10, 8)


def test_irregular_polygon_and_setbacks_satisfy_independent_predicates():
    p = parcel([(0, 0), (12, 0), (10, 6), (3, 8), (0, 0)])
    distances = (1, 0.5, 1.5, 2)
    footprint = build_footprint(p, distances).candidate
    # Shoelace cross terms: (12 * 6) + (10 * 8 - 6 * 3).
    assert p.polygon.area == 67
    assert footprint.is_valid and p.polygon.covers(footprint)
    assert 0 < footprint.area < p.polygon.area
    assert all(footprint.distance(edge) >= d for edge, d in zip(p.edges, distances))


@pytest.mark.parametrize("offset", [0, 800_000])
def test_mixed_setbacks_with_zero_distance_on_sloped_edges(offset):
    shell = [(0, 0), (11, 1), (9, 8), (1, 7), (0, 0)]
    p = parcel([(x + offset, y + offset) for x, y in shell])
    result = build_footprint(p, (0, 1, 0, 0))
    candidate = result.candidate
    assert 55 < candidate.area < 67
    assert p.polygon.covers(candidate)
    assert candidate.covers(Point(5 + offset, 4 + offset))
    # Independent projection onto the finite segment from (11, 1) to (9, 8).
    for x, y in candidate.exterior.coords:
        px, py = x - offset - 11, y - offset - 1
        projection = max(0, min(1, (-2 * px + 7 * py) / 53))
        assert hypot(px + 2 * projection, py - 7 * projection) >= 1


def test_concave_lot_against_analytic_notch_and_repeatability():
    p = parcel([(0, 0), (8, 0), (8, 3), (3, 3), (3, 8), (0, 8), (0, 0)])
    distances = (0.5,) * len(p.edges)
    result = build_footprint(p, distances)
    expected = 24 + 0.25 - pi / 16
    assert p.polygon.area == 39
    assert result.candidate.covers(Point(1, 6)) and result.candidate.covers(Point(6, 1))
    assert not result.candidate.covers(Point(2.8, 2.8))
    assert result.candidate.area == pytest.approx(expected, abs=0.015)
    assert result.candidate.area <= expected <= result.upper_area_m2
    assert p.polygon.covers(result.candidate)
    assert all(result.candidate.distance(edge) >= 0.5 for edge in p.edges)
    assert result.candidate.wkb == build_footprint(p, distances).candidate.wkb


@pytest.mark.parametrize("distance, empty", [(0.14, False), (0.16, True)])
def test_narrow_lot_and_consumed_lot(distance, empty):
    p = parcel([(0, 0), (10, 0), (10, 0.3), (0, 0.3), (0, 0)])
    result = build_footprint(p, (distance, 0, distance, 0))
    assert result.candidate.is_empty is empty
    if empty:
        assert result.candidate.area == result.upper_area_m2 == 0
    else:
        assert result.candidate.area == pytest.approx(0.2, abs=0.003)
        assert result.upper_area_m2 == pytest.approx(0.2, abs=1e-6)


def test_valid_parcel_hole_remains_excluded():
    p = parcel(holes=[[(3, 3), (4, 3), (4, 4), (3, 4), (3, 3)]])
    result = build_footprint(p, (1, 1, 1, 1))
    assert len(result.candidate.interiors) == 1
    assert not result.candidate.covers(Point(3.5, 3.5))
    assert result.candidate.area == pytest.approx(47, abs=0.02)


def test_disconnected_region_is_not_replaced_by_its_largest_component():
    p = parcel([(0, 0), (4, 0), (4, 2), (8, 2), (8, 0), (12, 0), (12, 5),
                (8, 5), (8, 3), (4, 3), (4, 5), (0, 5), (0, 0)])
    with pytest.raises(FootprintError, match="FOOTPRINT_DISCONNECTED"):
        build_footprint(p, (0.75,) * len(p.edges))


@pytest.mark.parametrize("distances, code", [
    ((1,), "SETBACK_EVIDENCE_MISSING"),
    ((-1, 0, 0, 0), "SETBACK_INVALID"),
    ((float("nan"), 0, 0, 0), "SETBACK_INVALID"),
    ((float("inf"), 0, 0, 0), "SETBACK_INVALID"),
    ((MIN_CLEARANCE_M / 2, 0, 0, 0), "SETBACK_PRECISION_UNSUPPORTED"),
    ((1_000_001, 0, 0, 0), "SETBACK_PRECISION_UNSUPPORTED"),
])
def test_distance_limits(distances, code):
    with pytest.raises(FootprintError, match=code):
        build_footprint(parcel(), distances)


def test_minimum_positive_distance_at_supported_origin():
    result = build_footprint(parcel(), (MIN_CLEARANCE_M, 0, 0, 0))
    assert result.candidate.bounds[1] >= MIN_CLEARANCE_M

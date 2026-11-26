from shapely.geometry import MultiPolygon, Polygon

from app.geometry.containment import (
    ContainmentStatus,
    MetricGeometryInput,
    assess_building_containment,
)


FRAME = "synthetic-frame-v1"


def _input(geometry, frame=FRAME):
    return MetricGeometryInput(frame, geometry)


def test_building_fully_within_parcel() -> None:
    result = assess_building_containment(
        _input(Polygon([(0, 0), (10, 0), (10, 10), (0, 10)])),
        _input(Polygon([(2, 2), (8, 2), (8, 8), (2, 8)])),
        authoritative_land_evidence=True,
    )
    assert result.status == ContainmentStatus.CONTAINED and result.contained
    assert result.geometric_only


def test_outside_and_partial_crossing_are_distinct() -> None:
    parcel = _input(Polygon([(0, 0), (10, 0), (10, 10), (0, 10)]))
    outside = assess_building_containment(
        parcel, _input(Polygon([(20, 20), (22, 20), (22, 22), (20, 22)])),
        authoritative_land_evidence=True,
    )
    crossing = assess_building_containment(
        parcel, _input(Polygon([(8, 8), (12, 8), (12, 12), (8, 12)])),
        authoritative_land_evidence=True,
    )
    assert outside.status == ContainmentStatus.OUTSIDE
    assert crossing.status == ContainmentStatus.CROSSES_BOUNDARY


def test_concave_parcel_refuses_building_across_notch() -> None:
    parcel = Polygon([(0, 0), (10, 0), (10, 4), (4, 4), (4, 10), (0, 10)])
    building = Polygon([(3, 3), (6, 3), (6, 6), (3, 6)])
    result = assess_building_containment(
        _input(parcel), _input(building), authoritative_land_evidence=True
    )
    assert result.status == ContainmentStatus.CROSSES_BOUNDARY


def test_polygon_hole_excludes_building() -> None:
    parcel = Polygon(
        [(0, 0), (10, 0), (10, 10), (0, 10)],
        [[(3, 3), (7, 3), (7, 7), (3, 7)]],
    )
    building = Polygon([(4, 4), (6, 4), (6, 6), (4, 6)])
    result = assess_building_containment(
        _input(parcel), _input(building), authoritative_land_evidence=True
    )
    assert result.status == ContainmentStatus.OUTSIDE


def test_invalid_parcel_and_building_are_distinct() -> None:
    invalid = Polygon([(0, 0), (5, 5), (5, 0), (0, 5)])
    valid = Polygon([(1, 1), (2, 1), (2, 2), (1, 2)])
    assert assess_building_containment(
        _input(invalid), _input(valid), authoritative_land_evidence=True
    ).status == ContainmentStatus.INVALID_PARCEL
    assert assess_building_containment(
        _input(Polygon([(0, 0), (10, 0), (10, 10), (0, 10)])), _input(invalid),
        authoritative_land_evidence=True,
    ).status == ContainmentStatus.INVALID_BUILDING


def test_multipolygon_is_explicitly_unsupported() -> None:
    multi = MultiPolygon([
        Polygon([(0, 0), (2, 0), (2, 2), (0, 2)]),
        Polygon([(4, 0), (6, 0), (6, 2), (4, 2)]),
    ])
    result = assess_building_containment(
        _input(multi), _input(Polygon([(0.5, 0.5), (1, 0.5), (1, 1), (0.5, 1)])),
        authoritative_land_evidence=True,
    )
    assert result.status == ContainmentStatus.UNSUPPORTED
    assert result.code == "MULTIPOLYGON_UNSUPPORTED"


def test_frame_mismatch_and_missing_authority_fail_closed() -> None:
    polygon = Polygon([(0, 0), (10, 0), (10, 10), (0, 10)])
    building = Polygon([(1, 1), (2, 1), (2, 2), (1, 2)])
    mismatch = assess_building_containment(
        _input(polygon), _input(building, "other-frame"), authoritative_land_evidence=True
    )
    missing = assess_building_containment(
        _input(polygon), _input(building), authoritative_land_evidence=False
    )
    assert mismatch.status == ContainmentStatus.FRAME_MISMATCH
    assert missing.status == ContainmentStatus.LAND_EVIDENCE_UNAVAILABLE


def test_truthy_non_boolean_authority_fails_closed() -> None:
    polygon = _input(Polygon([(0, 0), (10, 0), (10, 10), (0, 10)]))
    building = _input(Polygon([(1, 1), (2, 1), (2, 2), (1, 2)]))
    result = assess_building_containment(polygon, building, authoritative_land_evidence=1)
    assert result.status == ContainmentStatus.LAND_EVIDENCE_UNAVAILABLE


def test_empty_frame_and_z_dimension_are_rejected() -> None:
    parcel = _input(Polygon([(0, 0), (10, 0), (10, 10), (0, 10)]), " ")
    building = _input(Polygon([(1, 1, 2), (2, 1, 2), (2, 2, 2), (1, 1, 2)]), " ")
    assert assess_building_containment(
        parcel, building, authoritative_land_evidence=True
    ).status == ContainmentStatus.FRAME_MISMATCH
    valid_parcel = _input(Polygon([(0, 0), (10, 0), (10, 10), (0, 10)]))
    z_result = assess_building_containment(
        valid_parcel, _input(building.geometry), authoritative_land_evidence=True
    )
    assert z_result.status == ContainmentStatus.INVALID_BUILDING


def test_nonfinite_polygon_is_rejected_safely() -> None:
    parcel = _input(Polygon([(0, 0), (10, 0), (10, 10), (0, 10)]))
    malformed = _input(Polygon([(1, 1), (float("inf"), 1), (2, 2), (1, 1)]))
    result = assess_building_containment(parcel, malformed, authoritative_land_evidence=True)
    assert result.status == ContainmentStatus.INVALID_BUILDING

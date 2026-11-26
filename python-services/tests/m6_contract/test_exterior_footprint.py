import pytest
from shapely.geometry import Polygon, LineString, box

from app.geometry.containment import (
    ContainmentStatus,
    MetricGeometryInput,
    assess_building_containment,
)
from app.geometry.exterior_footprint import build_explicit_footprint


FRAME = "synthetic-frame-v1"


def metric(geometry, frame=FRAME):
    return MetricGeometryInput(frame, geometry)


def build(rooms=None, walls=None, frame=FRAME, limit=100):
    if rooms is None:
        rooms = (metric(box(1, 1, 9, 9)),)
    if walls is None:
        walls = (metric(box(0.8, 0.8, 9.2, 9.2)),)

    return build_explicit_footprint(
        rooms,
        walls,
        expected_frame_id=frame,
        max_components=limit,
    )


def test_explicit_footprint_includes_wall_area():
    result = build()
    assert result.computed
    assert result.footprint.geometry.equals(box(0.8, 0.8, 9.2, 9.2))


def test_explicit_footprint_inside_parcel():
    result = build()

    containment = assess_building_containment(
        metric(box(0, 0, 10, 10)),
        result.footprint,
        authoritative_land_evidence=True,
    )

    assert containment.contained
    assert containment.geometric_only


def test_room_inside_but_exterior_wall_crosses_land_boundary():
    room = metric(box(1, 1, 9.9, 9))
    wall = metric(box(9.8, 1, 10.1, 9))
    parcel = metric(box(0, 0, 10, 10))

    room_only = assess_building_containment(
        parcel, room, authoritative_land_evidence=True
    )

    assert room_only.contained

    result = build(rooms=(room,), walls=(wall,))
    assert result.computed

    complete = assess_building_containment(
        parcel, result.footprint, authoritative_land_evidence=True
    )

    assert not complete.contained
    assert complete.status == ContainmentStatus.CROSSES_BOUNDARY


def test_multiple_rooms_and_walls_are_combined():
    result = build(
        rooms=(
            metric(box(1, 1, 5, 9)),
            metric(box(5, 1, 9, 9)),
        ),
        walls=(
            metric(box(0.8, 1, 1.2, 9)),
            metric(box(4.9, 1, 5.1, 9)),
        ),
    )

    assert result.computed
    assert result.footprint.geometry.area == pytest.approx(65.6)


def test_missing_room_geometry_fails_closed():
    result = build(rooms=())
    assert not result.computed
    assert result.code == "ROOM_FOOTPRINT_REQUIRED"


def test_missing_wall_geometry_fails_closed():
    result = build(walls=())
    assert not result.computed
    assert result.code == "WALL_FOOTPRINT_REQUIRED"


def test_disconnected_wall_is_not_silently_discarded():
    result = build(walls=(metric(box(20, 20, 21, 21)),))
    assert not result.computed
    assert result.code == "DISCONNECTED_FOOTPRINT_UNSUPPORTED"


@pytest.mark.parametrize("component", ["room", "wall"])
def test_frame_mismatch_fails_closed(component):
    wrong = (metric(box(1, 1, 9, 9), "other-frame"),)

    result = build(**{component + "s": wrong})

    assert not result.computed
    assert result.code == "COORDINATE_FRAME_MISMATCH"


@pytest.mark.parametrize(
    "geometry",
    [
        Polygon(),
        Polygon([(0, 0), (2, 2), (0, 2), (2, 0)]),
        Polygon([(0, 0, 1), (1, 0, 1), (0, 1, 1)]),
        LineString([(0, 0), (2, 2)]),
    ],
)
def test_invalid_wall_polygon_is_rejected(geometry):
    result = build(walls=(metric(geometry),))
    assert not result.computed
    assert result.footprint is None


@pytest.mark.parametrize("limit", [0, -1, True, None])
def test_invalid_component_policy(limit):
    assert build(limit=limit).code == "INVALID_COMPONENT_POLICY"


def test_component_limit_enforced():
    assert build(limit=1).code == "FOOTPRINT_COMPONENT_LIMIT"


def test_exact_component_limit_accepted():
    assert build(limit=2).computed


def test_non_component_object_is_rejected():
    assert build(walls=(object(),)).code == "FOOTPRINT_COMPONENT_INVALID"


def test_missing_frame_is_rejected():
    assert build(frame=" ").code == "FRAME_ID_REQUIRED"


def test_footprint_does_not_claim_authority():
    result = build()

    assert result.computed
    assert not hasattr(result, "authoritative")
    assert not hasattr(result, "regulatory_compliance")
    assert not hasattr(result, "components_complete")

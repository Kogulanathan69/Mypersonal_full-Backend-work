from pathlib import Path
from uuid import UUID

import pytest
from pydantic import ValidationError

pytest.importorskip("shapely", minversion="2.1.2")

from app.geometry.footprint.contracts import MetricLand
from app.geometry.footprint.parcel import (
    ConfirmedMetricParcel,
    MAX_COORDINATE_M,
    MIN_CLEARANCE_M,
    ParcelError,
    validate_confirmed_parcel,
)
from app.plans.generation.contracts import GenerationInput
from app.validation.boundary import parse_contract_json

PROJECT = UUID("10000000-0000-4000-8000-000000000001")
LAND = UUID("10000000-0000-4000-8000-000000000002")
RECTANGLE = [(0, 0), (10, 0), (10, 8), (0, 8), (0, 0)]


def metric_land(exterior=RECTANGLE, *, holes=()):
    def ring(points):
        return [{"x": x, "y": y} for x, y in points]

    return MetricLand.model_validate({
        "schemaVersion": "metric-land/0.1-draft",
        "coordinateSystem": {
            "type": "localCartesian",
            "unit": "m",
            "origin": {"x": 0, "y": 0},
            "xDirection": "right",
            "yDirection": "up",
        },
        "boundary": {"exterior": ring(exterior), "holes": [ring(hole) for hole in holes]},
    })


def confirmed(land=None):
    return ConfirmedMetricParcel(projectId=PROJECT, landRevisionId=LAND, land=land or metric_land())


@pytest.mark.parametrize("vertices", [RECTANGLE, list(reversed(RECTANGLE))])
def test_rectangle_keeps_exact_caller_edge_ids(vertices):
    source = confirmed(metric_land(vertices))
    parcel = validate_confirmed_parcel(source, PROJECT, LAND)
    assert parcel.land == source.land
    assert parcel.polygon.area == 80
    assert tuple(list(edge.coords) for edge in parcel.edges) == tuple(
        [a, b] for a, b in zip(vertices, vertices[1:])
    )


def test_triangle_has_three_distinct_vertices():
    p = confirmed(metric_land([(0, 0), (4, 0), (0, 4), (0, 0)]))
    assert validate_confirmed_parcel(p, PROJECT, LAND).polygon.area == 8


@pytest.mark.parametrize("project,revision", [(LAND, LAND), (PROJECT, PROJECT), (None, LAND)])
def test_mismatched_revision_is_rejected(project, revision):
    with pytest.raises(ParcelError, match="INPUT_REVISION_MISMATCH"):
        validate_confirmed_parcel(confirmed(), project, revision)


@pytest.mark.parametrize("vertices", [
    [(0, 0), (10, 8), (0, 8), (10, 0), (0, 0)],
    [(0, 0), (10, 0), (10, 0), (0, 8), (0, 0)],
    [(0, 0), (10, 0), (0, 8), (10, 0), (0, 0)],
    [(0, 0), (1, 0), (2, 0), (0, 0)],
    [(0, 0), (1, 0), (0, 0), (0, 0)],
])
def test_invalid_shell_not_repaired(vertices):
    with pytest.raises(ParcelError, match="POLYGON_INVALID"):
        validate_confirmed_parcel(confirmed(metric_land(vertices)), PROJECT, LAND)


def test_original_hole_is_inside_and_excluded():
    hole = [(2, 2), (4, 2), (4, 4), (2, 4), (2, 2)]
    p = validate_confirmed_parcel(confirmed(metric_land(holes=[hole])), PROJECT, LAND)
    assert len(p.polygon.interiors) == 1
    assert p.polygon.area == 76
    assert len(p.edges) == 4


@pytest.mark.parametrize("holes", [
    [[(0, 2), (2, 2), (2, 3), (0, 3), (0, 2)]],
    [[(10, 2), (12, 2), (12, 3), (10, 3), (10, 2)]],
    [[(2, 2), (4, 4), (2, 4), (4, 2), (2, 2)]],
    [[(2, 2), (4, 2), (4, 4), (2, 4), (2, 2)], [(4, 4), (6, 4), (6, 6), (4, 6), (4, 4)]],
    [[(2, 2), (6, 2), (6, 6), (2, 6), (2, 2)], [(3, 3), (4, 3), (4, 4), (3, 4), (3, 3)]],
])
def test_invalid_hole_topology(holes):
    with pytest.raises(ParcelError, match="POLYGON_INVALID"):
        validate_confirmed_parcel(confirmed(metric_land(holes=holes)), PROJECT, LAND)


@pytest.mark.parametrize("coord", [float("nan"), float("inf"), float("-inf"), "1", True])
def test_bad_coordinate_rejected_by_d01_contract(coord):
    with pytest.raises(ValidationError):
        metric_land([(0, 0), (coord, 0), (1, 1), (0, 0)])


@pytest.mark.parametrize("vertices", [
    [(0, 0), (1, 0), (0, 1)],
    [(0, 0), (1, 0), (0, 0)],
])
def test_open_or_structurally_short_ring(vertices):
    with pytest.raises(ValidationError):
        metric_land(vertices)


def test_metric_frame_and_snapshot_uuid_are_revalidated():
    wrong_land = metric_land().model_copy(update={
        "coordinateSystem": metric_land().coordinateSystem.model_copy(update={"unit": "ft"})
    })
    with pytest.raises(ValidationError):
        validate_confirmed_parcel(confirmed().model_copy(update={"land": wrong_land}), PROJECT, LAND)
    with pytest.raises(ValidationError):
        validate_confirmed_parcel(confirmed().model_copy(update={"projectId": UUID(int=0)}), PROJECT, LAND)


@pytest.mark.parametrize("vertices", [
    [(0, 0), (MAX_COORDINATE_M + 1, 0), (1, 1), (0, 0)],
    [(0, 0), (10, 0), (10, MIN_CLEARANCE_M / 2), (0, MIN_CLEARANCE_M / 2), (0, 0)],
])
def test_unsupported_precision(vertices):
    with pytest.raises(ParcelError, match="PARCEL_PRECISION_UNSUPPORTED"):
        validate_confirmed_parcel(confirmed(metric_land(vertices)), PROJECT, LAND)


def test_complexity_guard_precedes_geos_topology():
    vertices = [(x, 0) for x in range(129)] + [(0, 1), (0, 0)]
    with pytest.raises(ParcelError, match="PARCEL_COMPLEXITY_UNSUPPORTED"):
        validate_confirmed_parcel(confirmed(metric_land(vertices)), PROJECT, LAND)


def test_d01_boundary_rejects_duplicate_key_and_wrong_units():
    path = Path(__file__).parent / "fixtures" / "generation-input.v0.1-draft.json"
    payload = path.read_text(encoding="utf-8")
    with pytest.raises(ValueError, match="Duplicate JSON property"):
        parse_contract_json(GenerationInput, payload.replace('"unit": "m"', '"unit": "m", "unit": "m"'))
    with pytest.raises(ValidationError):
        parse_contract_json(GenerationInput, payload.replace('"unit": "m"', '"unit": "ft"'))

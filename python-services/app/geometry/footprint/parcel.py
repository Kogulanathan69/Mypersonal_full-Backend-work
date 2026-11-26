"""Confirmed local metric parcel validation for the provisional D02 callable."""

from dataclasses import dataclass
from math import hypot
from typing import TYPE_CHECKING
from uuid import UUID

from app.geometry.footprint.contracts import ContractModel, CoordinatePoint, MetricLand, NonNilUUID

if TYPE_CHECKING:
    from shapely.geometry import LineString, Polygon

MAX_COORDINATE_M = 1_000_000
MAX_EXTERIOR_EDGES = 128
MAX_TOTAL_VERTICES = 512
MAX_HOLES = 16
MIN_CLEARANCE_M = 1e-6
MIN_AREA_M2 = 1e-12


class ConfirmedMetricParcel(ContractModel):
    """Caller-supplied snapshot from the trusted land boundary, or a synthetic test equivalent."""

    projectId: NonNilUUID
    landRevisionId: NonNilUUID
    land: MetricLand


class ParcelError(ValueError):
    """A fixed diagnostic code suitable for the local feasibility boundary."""


@dataclass(frozen=True)
class ValidatedParcel:
    land: MetricLand
    polygon: "Polygon"
    edges: tuple["LineString", ...]


def validate_confirmed_parcel(
    snapshot: ConfirmedMetricParcel, project_id: UUID, land_revision_id: UUID
) -> ValidatedParcel:
    """Check topology without repairing, snapping or changing the original edge order."""
    from shapely.geometry import LineString, Polygon

    parcel = ConfirmedMetricParcel.model_validate(snapshot)
    if (parcel.projectId, parcel.landRevisionId) != (project_id, land_revision_id):
        raise ParcelError("INPUT_REVISION_MISMATCH")

    land = parcel.land
    exterior, holes = land.boundary.exterior, land.boundary.holes
    if (
        len(exterior) - 1 > MAX_EXTERIOR_EDGES
        or len(holes) > MAX_HOLES
        or len(exterior) - 1 + sum(len(ring) - 1 for ring in holes) > MAX_TOTAL_VERTICES
    ):
        raise ParcelError("PARCEL_COMPLEXITY_UNSUPPORTED")

    origin = land.coordinateSystem.origin
    if max(abs(origin.x), abs(origin.y)) > MAX_COORDINATE_M:
        raise ParcelError("PARCEL_PRECISION_UNSUPPORTED")

    def ring_points(ring: list[CoordinatePoint]) -> list[tuple[float, float]]:
        points = [(point.x, point.y) for point in ring]
        if any(max(abs(x), abs(y)) > MAX_COORDINATE_M for x, y in points):
            raise ParcelError("PARCEL_PRECISION_UNSUPPORTED")
        if len(set(points[:-1])) < 3 or len(set(points[:-1])) != len(points) - 1:
            raise ParcelError("POLYGON_INVALID")
        if any(hypot(b[0] - a[0], b[1] - a[1]) < MIN_CLEARANCE_M
               for a, b in zip(points, points[1:])):
            raise ParcelError("PARCEL_PRECISION_UNSUPPORTED")
        return points

    shell = ring_points(exterior)
    interiors = [ring_points(ring) for ring in holes]
    outer = Polygon(shell)
    hole_polygons = [Polygon(ring) for ring in interiors]
    if not outer.is_valid or outer.area <= 0:
        raise ParcelError("POLYGON_INVALID")

    for index, hole in enumerate(hole_polygons):
        if (
            not hole.is_valid
            or hole.area <= 0
            or not outer.contains_properly(hole)
            or any(hole.intersects(other) for other in hole_polygons[:index])
        ):
            raise ParcelError("POLYGON_INVALID")

    polygon = Polygon(shell, interiors)
    if not polygon.is_valid or polygon.area <= 0:
        raise ParcelError("POLYGON_INVALID")
    if polygon.area < MIN_AREA_M2 or polygon.minimum_clearance < MIN_CLEARANCE_M:
        raise ParcelError("PARCEL_PRECISION_UNSUPPORTED")

    edges = tuple(LineString([start, end]) for start, end in zip(shell, shell[1:]))
    return ValidatedParcel(land, polygon, edges)

"""Geometric building-to-parcel containment in one explicit metric frame.

Containment is not setback compliance, legal boundary verification or proof of
the authority of the supplied land snapshot.
"""

from dataclasses import dataclass
from enum import StrEnum
from math import isfinite


class ContainmentStatus(StrEnum):
    CONTAINED = "CONTAINED"
    OUTSIDE = "OUTSIDE"
    CROSSES_BOUNDARY = "CROSSES_BOUNDARY"
    INVALID_PARCEL = "INVALID_PARCEL"
    INVALID_BUILDING = "INVALID_BUILDING"
    FRAME_MISMATCH = "FRAME_MISMATCH"
    LAND_EVIDENCE_UNAVAILABLE = "LAND_EVIDENCE_UNAVAILABLE"
    UNSUPPORTED = "UNSUPPORTED"


@dataclass(frozen=True)
class MetricGeometryInput:
    frame_id: str
    geometry: object


@dataclass(frozen=True)
class ContainmentResult:
    status: ContainmentStatus
    code: str
    contained: bool
    geometric_only: bool = True


def assess_building_containment(
    parcel: MetricGeometryInput | None,
    building: MetricGeometryInput,
    *,
    authoritative_land_evidence: bool,
) -> ContainmentResult:
    try:
        from shapely.geometry import MultiPolygon, Polygon
        from shapely.errors import ShapelyError
    except ImportError:
        return ContainmentResult(ContainmentStatus.UNSUPPORTED, "GEOMETRY_DEPENDENCY_UNAVAILABLE", False)

    if parcel is None or authoritative_land_evidence is not True:
        return ContainmentResult(
            ContainmentStatus.LAND_EVIDENCE_UNAVAILABLE, "CONFIRMED_LAND_EVIDENCE_UNAVAILABLE", False
        )
    if (
        not isinstance(parcel, MetricGeometryInput)
        or not isinstance(building, MetricGeometryInput)
        or not isinstance(parcel.frame_id, str)
        or not isinstance(building.frame_id, str)
        or not parcel.frame_id.strip()
        or not building.frame_id.strip()
        or len(parcel.frame_id) > 128
        or len(building.frame_id) > 128
        or parcel.frame_id != building.frame_id
    ):
        return ContainmentResult(ContainmentStatus.FRAME_MISMATCH, "COORDINATE_FRAME_MISMATCH", False)
    if isinstance(parcel.geometry, MultiPolygon) or isinstance(building.geometry, MultiPolygon):
        return ContainmentResult(ContainmentStatus.UNSUPPORTED, "MULTIPOLYGON_UNSUPPORTED", False)
    if not isinstance(parcel.geometry, Polygon):
        return ContainmentResult(ContainmentStatus.INVALID_PARCEL, "PARCEL_POLYGON_REQUIRED", False)
    if not isinstance(building.geometry, Polygon):
        return ContainmentResult(ContainmentStatus.INVALID_BUILDING, "BUILDING_POLYGON_REQUIRED", False)
    def valid_polygon(polygon) -> bool:
        if polygon.is_empty or polygon.has_z or getattr(polygon, "has_m", False):
            return False
        for ring in (polygon.exterior, *polygon.interiors):
            coordinates = tuple(ring.coords)
            if len(coordinates) < 4 or coordinates[0] != coordinates[-1]:
                return False
            if any(
                len(coordinate) != 2
                or any(isinstance(value, bool) or not isfinite(value) for value in coordinate)
                for coordinate in coordinates
            ):
                return False
        return polygon.is_valid and isfinite(polygon.area) and polygon.area > 0

    try:
        parcel_valid = valid_polygon(parcel.geometry)
        building_valid = valid_polygon(building.geometry)
    except (ShapelyError, ArithmeticError, ValueError, TypeError, OverflowError):
        return ContainmentResult(ContainmentStatus.UNSUPPORTED, "CONTAINMENT_CHECK_INCONCLUSIVE", False)

    if not parcel_valid:
        return ContainmentResult(ContainmentStatus.INVALID_PARCEL, "PARCEL_POLYGON_INVALID", False)
    if not building_valid:
        return ContainmentResult(ContainmentStatus.INVALID_BUILDING, "BUILDING_POLYGON_INVALID", False)
    try:
        if parcel.geometry.covers(building.geometry):
            return ContainmentResult(ContainmentStatus.CONTAINED, "BUILDING_CONTAINED", True)
        if parcel.geometry.intersects(building.geometry):
            return ContainmentResult(
                ContainmentStatus.CROSSES_BOUNDARY, "BUILDING_CROSSES_PARCEL_BOUNDARY", False
            )
        return ContainmentResult(ContainmentStatus.OUTSIDE, "BUILDING_OUTSIDE_PARCEL", False)
    except (ShapelyError, ArithmeticError, ValueError, TypeError, OverflowError):
        return ContainmentResult(ContainmentStatus.UNSUPPORTED, "CONTAINMENT_CHECK_INCONCLUSIVE", False)

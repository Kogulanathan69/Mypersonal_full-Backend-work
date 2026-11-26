"""Isolated footprint construction from explicit metric polygons.

The supplied polygons must already represent occupied room and wall
areas in the same coordinate frame. This helper does not infer wall
offsets, roof projections, ownership or component completeness.
It does not establish authoritative land or regulatory compliance.
"""

from dataclasses import dataclass
from math import isfinite

from app.geometry.containment import MetricGeometryInput


@dataclass(frozen=True)
class FootprintResult:
    computed: bool
    code: str
    footprint: MetricGeometryInput | None = None


def build_explicit_footprint(
    rooms: tuple[MetricGeometryInput, ...],
    walls: tuple[MetricGeometryInput, ...],
    *,
    expected_frame_id: str,
    max_components: int,
) -> FootprintResult:
    try:
        from shapely.geometry import Polygon
        from shapely.ops import unary_union
        from shapely.errors import ShapelyError
    except ImportError:
        return FootprintResult(False, "GEOMETRY_DEPENDENCY_UNAVAILABLE")

    if (
        not isinstance(expected_frame_id, str)
        or not expected_frame_id.strip()
        or len(expected_frame_id) > 128
    ):
        return FootprintResult(False, "FRAME_ID_REQUIRED")

    if (
        isinstance(max_components, bool)
        or not isinstance(max_components, int)
        or max_components <= 0
    ):
        return FootprintResult(False, "INVALID_COMPONENT_POLICY")

    if not isinstance(rooms, tuple) or not isinstance(walls, tuple):
        return FootprintResult(False, "FOOTPRINT_COMPONENTS_INVALID")

    if not rooms:
        return FootprintResult(False, "ROOM_FOOTPRINT_REQUIRED")

    if not walls:
        return FootprintResult(False, "WALL_FOOTPRINT_REQUIRED")

    if len(rooms) + len(walls) > max_components:
        return FootprintResult(False, "FOOTPRINT_COMPONENT_LIMIT")

    polygons = []

    try:
        for component in rooms + walls:
            if not isinstance(component, MetricGeometryInput):
                return FootprintResult(False, "FOOTPRINT_COMPONENT_INVALID")

            if component.frame_id != expected_frame_id:
                return FootprintResult(False, "COORDINATE_FRAME_MISMATCH")

            geometry = component.geometry

            if not isinstance(geometry, Polygon):
                return FootprintResult(False, "FOOTPRINT_POLYGON_REQUIRED")

            if geometry.is_empty or geometry.has_z:
                return FootprintResult(False, "FOOTPRINT_POLYGON_INVALID")

            rings = (geometry.exterior, *geometry.interiors)

            for ring in rings:
                for coordinate in ring.coords:
                    if len(coordinate) != 2 or not all(
                        isfinite(value) for value in coordinate
                    ):
                        return FootprintResult(
                            False, "FOOTPRINT_COORDINATE_INVALID"
                        )

            if (
                not geometry.is_valid
                or not isfinite(geometry.area)
                or geometry.area <= 0
            ):
                return FootprintResult(False, "FOOTPRINT_POLYGON_INVALID")

            polygons.append(geometry)

        merged = unary_union(polygons)

        if not isinstance(merged, Polygon):
            return FootprintResult(False, "DISCONNECTED_FOOTPRINT_UNSUPPORTED")

        if (
            merged.is_empty
            or not merged.is_valid
            or not isfinite(merged.area)
            or merged.area <= 0
        ):
            return FootprintResult(False, "FOOTPRINT_UNION_INVALID")

        if not all(merged.covers(polygon) for polygon in polygons):
            return FootprintResult(False, "FOOTPRINT_UNION_INCOMPLETE")

        return FootprintResult(
            True,
            "EXPLICIT_FOOTPRINT_COMPUTED",
            MetricGeometryInput(expected_frame_id, merged),
        )

    except (ShapelyError, ArithmeticError, ValueError, TypeError):
        return FootprintResult(False, "FOOTPRINT_COMPUTATION_FAILED")

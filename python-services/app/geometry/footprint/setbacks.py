"""Finite-edge setback construction with conservative inner and outer area bounds."""

from dataclasses import dataclass
from math import cos, isfinite, pi, ulp
from typing import TYPE_CHECKING

from app.geometry.footprint.parcel import MAX_COORDINATE_M, MIN_CLEARANCE_M, ValidatedParcel

if TYPE_CHECKING:
    from shapely.geometry import Polygon

QUADRANT_SEGMENTS = 32
DISTANCE_MARGIN_M = 1e-8


class FootprintError(ValueError):
    """A fixed diagnostic code from the local setback algorithm."""


@dataclass(frozen=True)
class FootprintBounds:
    candidate: "Polygon"
    upper_area_m2: float


def area_tolerance_m2(area: float, perimeter_m: float) -> float:
    return max(1e-10, 32 * ulp(area), perimeter_m * DISTANCE_MARGIN_M)


def build_footprint(parcel: ValidatedParcel, distances_m: tuple[float, ...]) -> FootprintBounds:
    """Subtract closed Euclidean neighborhoods of finite exterior segments.

    GEOS uses inscribed arc chords. Inflating the radius by sec(pi/(4*q))
    gives an enclosing exclusion at that chord angle. The nominal radius
    minus a roundoff margin gives an optimistic buildable area bound.
    Both paths preserve original holes. A zero setback excludes no area.
    """
    from shapely import union_all
    from shapely.geometry import Polygon

    if len(distances_m) != len(parcel.edges):
        raise FootprintError("SETBACK_EVIDENCE_MISSING")
    if any(not isfinite(d) or d < 0 for d in distances_m):
        raise FootprintError("SETBACK_INVALID")
    if any(d != 0 and not MIN_CLEARANCE_M <= d <= MAX_COORDINATE_M for d in distances_m):
        raise FootprintError("SETBACK_PRECISION_UNSUPPORTED")

    setbacks = [(edge, d) for edge, d in zip(parcel.edges, distances_m) if d]
    if not setbacks:
        return FootprintBounds(parcel.polygon, parcel.polygon.area)

    arc_factor = cos(pi / (4 * QUADRANT_SEGMENTS))
    enclosing = [
        edge.buffer((d + DISTANCE_MARGIN_M) / arc_factor, quad_segs=QUADRANT_SEGMENTS)
        for edge, d in setbacks
    ]
    enclosed = [
        edge.buffer(d - DISTANCE_MARGIN_M, quad_segs=QUADRANT_SEGMENTS)
        for edge, d in setbacks
    ]
    candidate = parcel.polygon.difference(union_all(enclosing))
    optimistic = parcel.polygon.difference(union_all(enclosed))
    if optimistic.geom_type not in ("Polygon", "MultiPolygon") or not optimistic.is_valid:
        raise FootprintError("FOOTPRINT_CALCULATION_FAILED")
    if optimistic.geom_type == "MultiPolygon" and len(optimistic.geoms) > 1:
        raise FootprintError("FOOTPRINT_DISCONNECTED")
    # Overlay can round a new point on an unbuffered sloped edge to the
    # outside. Inset only a valid computed single polygon, then recheck it.
    # This leaves the confirmed parcel and the optimistic bound untouched.
    if (
        candidate.geom_type == "Polygon"
        and not candidate.is_empty
        and candidate.is_valid
        and not parcel.polygon.covers(candidate)
    ):
        candidate = candidate.buffer(-DISTANCE_MARGIN_M, quad_segs=QUADRANT_SEGMENTS)
    if candidate.is_empty:
        candidate = Polygon()
    if candidate.geom_type == "MultiPolygon":
        raise FootprintError("FOOTPRINT_DISCONNECTED")
    if candidate.geom_type != "Polygon":
        raise FootprintError("FOOTPRINT_GEOMETRY_UNSUPPORTED")
    if not candidate.is_valid or (
        not candidate.is_empty and (
            candidate.area <= 0
            or not parcel.polygon.covers(candidate)
            or any(candidate.distance(edge) < d for edge, d in setbacks)
        )
    ):
        raise FootprintError("FOOTPRINT_CALCULATION_FAILED")

    land_area = parcel.polygon.area
    tolerance = area_tolerance_m2(land_area, parcel.polygon.length)
    if (
        not isfinite(candidate.area)
        or not isfinite(optimistic.area)
        or candidate.area > land_area
        or candidate.area > optimistic.area + tolerance
        or optimistic.area > land_area + tolerance
    ):
        raise FootprintError("FOOTPRINT_CALCULATION_FAILED")

    # The exact candidate is always a lower bound. Preserve that ordering
    # even when computed areas differ slightly because of floating point math.
    return FootprintBounds(candidate, min(land_area, max(candidate.area, optimistic.area)))

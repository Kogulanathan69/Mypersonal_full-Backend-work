"""Provisional M2 land-boundary geometry validation.

Input coordinates must already be expressed in a confirmed local metric
coordinate system. This module does not infer CRS or transform coordinates.
"""

from dataclasses import dataclass
from math import isfinite
from typing import Sequence

from shapely.geometry import Polygon
from shapely.geometry.polygon import orient
from shapely.validation import explain_validity


Point = tuple[float, float]
RingInput = Sequence[Sequence[float]]


class LandGeometryError(ValueError):
    """Validation failure with a stable, non-sensitive diagnostic code."""

    def __init__(self, code: str) -> None:
        self.code = code
        super().__init__(code)


@dataclass(frozen=True)
class LandGeometryResult:
    """Validated metric land geometry and derived measurements."""

    area_m2: float
    perimeter_m: float
    exterior: tuple[Point, ...]
    holes: tuple[tuple[Point, ...], ...]


# Provisional resource limits copied from the existing draft geometry contract.
# These require M2/M6 agreement before production use.
MAX_RING_POINTS = 10_000
MAX_HOLES = 100


def _normalise_ring(ring: RingInput) -> tuple[Point, ...]:
    if not isinstance(ring, (list, tuple)):
        raise LandGeometryError("INVALID_RING")

    if len(ring) < 4:
        raise LandGeometryError("RING_TOO_SHORT")

    if len(ring) > MAX_RING_POINTS:
        raise LandGeometryError("RING_LIMIT_EXCEEDED")

    points: list[Point] = []

    for point in ring:
        if not isinstance(point, (list, tuple)) or len(point) != 2:
            raise LandGeometryError("INVALID_COORDINATE")

        x, y = point

        if (
            isinstance(x, bool)
            or isinstance(y, bool)
            or not isinstance(x, (int, float))
            or not isinstance(y, (int, float))
        ):
            raise LandGeometryError("INVALID_COORDINATE")

        try:
            x_value = float(x)
            y_value = float(y)
        except OverflowError:
            raise LandGeometryError("INVALID_COORDINATE") from None

        if not isfinite(x_value) or not isfinite(y_value):
            raise LandGeometryError("NON_FINITE_COORDINATE")

        points.append((x_value, y_value))

    if points[0] != points[-1]:
        raise LandGeometryError("RING_NOT_CLOSED")

    for index in range(len(points) - 1):
        if points[index] == points[index + 1]:
            raise LandGeometryError("DUPLICATE_CONSECUTIVE_VERTEX")

    if len(set(points[:-1])) < 3:
        raise LandGeometryError("INSUFFICIENT_DISTINCT_VERTICES")

    return tuple(points)


def validate_land_boundary(
    exterior: RingInput,
    holes: Sequence[RingInput] = (),
) -> LandGeometryResult:
    """Validate a closed polygon and calculate area/perimeter in metric units.

    The caller is responsible for supplying coordinates in a confirmed
    local Cartesian CRS measured in metres.
    """
    if not isinstance(holes, (list, tuple)):
        raise LandGeometryError("INVALID_HOLES")

    if len(holes) > MAX_HOLES:
        raise LandGeometryError("HOLE_LIMIT_EXCEEDED")

    shell = _normalise_ring(exterior)
    interior_rings = tuple(_normalise_ring(ring) for ring in holes)

    try:
        polygon = Polygon(shell, interior_rings)
    except (TypeError, ValueError):
        raise LandGeometryError("INVALID_POLYGON") from None

    if not polygon.is_valid:
        reason = explain_validity(polygon)

        if "Self-intersection" in reason:
            raise LandGeometryError("SELF_INTERSECTION")

        raise LandGeometryError("INVALID_POLYGON_TOPOLOGY")

    normalized_polygon = orient(polygon, sign=1.0)

    area = float(normalized_polygon.area)
    perimeter = float(normalized_polygon.length)

    if not isfinite(area) or area <= 0:
        raise LandGeometryError("ZERO_OR_INVALID_AREA")

    if not isfinite(perimeter) or perimeter <= 0:
        raise LandGeometryError("ZERO_OR_INVALID_PERIMETER")

    return LandGeometryResult(
        area_m2=area,
        perimeter_m=perimeter,
        exterior=tuple((float(x), float(y)) for x, y in normalized_polygon.exterior.coords),
        holes=tuple(
            tuple((float(x), float(y)) for x, y in ring.coords)
            for ring in normalized_polygon.interiors
        ),
    )

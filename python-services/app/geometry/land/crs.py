"""Provisional M2 CRS normalization for land boundary geometry.

PyProj is imported lazily because the local Windows environment currently
blocks one of its native dependencies through Application Control.
"""

from dataclasses import dataclass
from math import isfinite
from typing import Sequence

from shapely.geometry import Polygon
from shapely.geometry.polygon import orient

from app.geometry.land.validation import (
    MAX_HOLES,
    MAX_RING_POINTS,
    LandGeometryError,
    LandGeometryResult,
    validate_land_boundary,
)


@dataclass(frozen=True)
class CRSNormalizationResult:
    """Source metadata, metric measurements and WGS84 GeoJSON output."""

    source_crs: str
    calculation_crs: str
    geometry_crs: str
    source_exterior: tuple[tuple[float, float], ...]
    source_holes: tuple[tuple[tuple[float, float], ...], ...]
    metric_geometry: LandGeometryResult
    geojson: dict


def _normalise_source_ring(ring: Sequence) -> tuple[tuple[float, float], ...]:
    if not isinstance(ring, (list, tuple)):
        raise LandGeometryError("INVALID_SOURCE_RING")

    if len(ring) < 4:
        raise LandGeometryError("RING_TOO_SHORT")

    if len(ring) > MAX_RING_POINTS:
        raise LandGeometryError("RING_LIMIT_EXCEEDED")

    points = []

    for point in ring:
        if not isinstance(point, (list, tuple)) or len(point) != 2:
            raise LandGeometryError("INVALID_SOURCE_COORDINATE")

        x, y = point

        if (
            isinstance(x, bool)
            or isinstance(y, bool)
            or not isinstance(x, (int, float))
            or not isinstance(y, (int, float))
        ):
            raise LandGeometryError("INVALID_SOURCE_COORDINATE")

        try:
            x, y = float(x), float(y)
        except OverflowError:
            raise LandGeometryError("INVALID_SOURCE_COORDINATE") from None

        if not isfinite(x) or not isfinite(y):
            raise LandGeometryError("NON_FINITE_SOURCE_COORDINATE")

        points.append((x, y))

    if points[0] != points[-1]:
        raise LandGeometryError("SOURCE_RING_NOT_CLOSED")

    for index in range(len(points) - 1):
        if points[index] == points[index + 1]:
            raise LandGeometryError("DUPLICATE_CONSECUTIVE_VERTEX")

    if len(set(points[:-1])) < 3:
        raise LandGeometryError("INSUFFICIENT_DISTINCT_VERTICES")

    return tuple(points)



def _validate_geographic_coordinates(rings):
    """Validate longitude/latitude ranges for geographic source CRS."""

    for ring in rings:
        for longitude, latitude in ring:
            if not -180 <= longitude <= 180:
                raise LandGeometryError("SOURCE_LONGITUDE_OUT_OF_RANGE")
            if not -90 <= latitude <= 90:
                raise LandGeometryError("SOURCE_LATITUDE_OUT_OF_RANGE")


def normalize_land_crs(
    exterior: Sequence,
    source_crs: str,
    calculation_crs: str,
    holes: Sequence = (),
) -> CRSNormalizationResult:
    """Transform source geometry to a projected metric CRS and WGS84.

    The caller must explicitly provide both CRS identifiers.
    Calculation CRS must be projected and use metres.
    GeoJSON coordinates are always WGS84 longitude, latitude.
    """

    if not isinstance(source_crs, str) or not source_crs.strip():
        raise LandGeometryError("SOURCE_CRS_REQUIRED")

    if not isinstance(calculation_crs, str) or not calculation_crs.strip():
        raise LandGeometryError("CALCULATION_CRS_REQUIRED")

    if not isinstance(holes, (list, tuple)):
        raise LandGeometryError("INVALID_HOLES")

    if len(holes) > MAX_HOLES:
        raise LandGeometryError("HOLE_LIMIT_EXCEEDED")

    source_shell = _normalise_source_ring(exterior)
    source_interiors = tuple(_normalise_source_ring(ring) for ring in holes)

    # Lazy import: avoids loading blocked native PyProj DLLs at module import.
    try:
        from pyproj import CRS, Transformer

        source = CRS.from_user_input(source_crs)
        calculation = CRS.from_user_input(calculation_crs)
        wgs84 = CRS.from_epsg(4326)
    except Exception:
        raise LandGeometryError("UNSUPPORTED_OR_INVALID_CRS") from None

    if not source.is_geographic and not source.is_projected:
        raise LandGeometryError("SOURCE_CRS_TYPE_UNSUPPORTED")

    if source.is_geographic:
        source_axes = source.axis_info[:2]
        if len(source_axes) != 2 or any(
            axis.unit_name.lower() not in {"degree", "degrees"}
            for axis in source_axes
        ):
            raise LandGeometryError("SOURCE_CRS_MUST_USE_DEGREES")

        _validate_geographic_coordinates(
            (source_shell, *source_interiors)
        )

    if not calculation.is_projected:
        raise LandGeometryError("CALCULATION_CRS_MUST_BE_PROJECTED")

    axes = calculation.axis_info[:2]
    if len(axes) != 2 or any(
        axis.unit_conversion_factor != 1.0 for axis in axes
    ):
        raise LandGeometryError("CALCULATION_CRS_MUST_USE_METRES")

    try:
        to_metric = Transformer.from_crs(
            source,
            calculation,
            always_xy=True,
            allow_ballpark=False,
            only_best=True,
        )
        to_wgs84 = Transformer.from_crs(
            calculation,
            wgs84,
            always_xy=True,
            allow_ballpark=False,
            only_best=True,
        )

        def transform_ring(ring, transformer):
            transformed = []
            for x, y in ring:
                tx, ty = transformer.transform(x, y, errcheck=True)
                if not isfinite(tx) or not isfinite(ty):
                    raise LandGeometryError("TRANSFORM_NON_FINITE_COORDINATE")
                transformed.append((float(tx), float(ty)))
            return tuple(transformed)

        metric_shell = transform_ring(source_shell, to_metric)
        metric_holes = tuple(
            transform_ring(ring, to_metric) for ring in source_interiors
        )

        metric_result = validate_land_boundary(metric_shell, metric_holes)

        metric_polygon = Polygon(
            metric_result.exterior,
            metric_result.holes,
        )

        wgs_shell = transform_ring(
            tuple(metric_polygon.exterior.coords), to_wgs84
        )
        wgs_holes = tuple(
            transform_ring(tuple(ring.coords), to_wgs84)
            for ring in metric_polygon.interiors
        )

        wgs_polygon = Polygon(wgs_shell, wgs_holes)

        if not wgs_polygon.is_valid or wgs_polygon.is_empty:
            raise LandGeometryError("INVALID_WGS84_GEOMETRY")

        wgs_polygon = orient(wgs_polygon, sign=1.0)

        for x, y in wgs_polygon.exterior.coords:
            if not (-180 <= x <= 180 and -90 <= y <= 90):
                raise LandGeometryError("WGS84_COORDINATE_OUT_OF_RANGE")

        for ring in wgs_polygon.interiors:
            for x, y in ring.coords:
                if not (-180 <= x <= 180 and -90 <= y <= 90):
                    raise LandGeometryError("WGS84_COORDINATE_OUT_OF_RANGE")

        geojson = {
            "type": "Polygon",
            "coordinates": [
                [[float(x), float(y)] for x, y in wgs_polygon.exterior.coords],
                *[
                    [[float(x), float(y)] for x, y in ring.coords]
                    for ring in wgs_polygon.interiors
                ],
            ],
        }

    except LandGeometryError:
        raise
    except Exception:
        raise LandGeometryError("CRS_TRANSFORMATION_FAILED") from None

    return CRSNormalizationResult(
        source_crs=source.to_string(),
        calculation_crs=calculation.to_string(),
        geometry_crs="EPSG:4326",
        source_exterior=source_shell,
        source_holes=source_interiors,
        metric_geometry=metric_result,
        geojson=geojson,
    )

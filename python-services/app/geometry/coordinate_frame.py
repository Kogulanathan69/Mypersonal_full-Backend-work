"""Explicit projected-CRS to local-metre frame mapping.

The caller supplies the projected CRS, immutable frame identity and projected
origin.  This module performs translation only; it never chooses a CRS or
infers an origin from geometry.
"""

from dataclasses import dataclass
from math import isfinite


class CoordinateFrameError(ValueError):
    def __init__(self, code: str):
        super().__init__(code)
        self.code = code


@dataclass(frozen=True)
class ProjectedMetricFrame:
    frame_id: str
    calculation_crs: str
    origin_easting_m: float
    origin_northing_m: float


def _number(value: object) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise CoordinateFrameError("COORDINATE_INVALID")
    try:
        result = float(value)
    except (OverflowError, ValueError):
        raise CoordinateFrameError("COORDINATE_NON_FINITE") from None
    if not isfinite(result):
        raise CoordinateFrameError("COORDINATE_NON_FINITE")
    return result


def create_projected_metric_frame(
    frame_id: str,
    calculation_crs: str,
    origin_easting_m: float,
    origin_northing_m: float,
) -> ProjectedMetricFrame:
    if not isinstance(frame_id, str) or not 1 <= len(frame_id) <= 128 or not frame_id.strip():
        raise CoordinateFrameError("FRAME_ID_REQUIRED")
    if not isinstance(calculation_crs, str) or not calculation_crs.strip():
        raise CoordinateFrameError("CALCULATION_CRS_REQUIRED")
    try:
        from pyproj import CRS

        crs = CRS.from_user_input(calculation_crs)
    except Exception:
        raise CoordinateFrameError("CALCULATION_CRS_INVALID") from None
    if not crs.is_projected:
        raise CoordinateFrameError("CALCULATION_CRS_NOT_PROJECTED")
    axes = crs.axis_info[:2]
    if len(axes) != 2 or any(axis.unit_conversion_factor != 1.0 for axis in axes):
        raise CoordinateFrameError("CALCULATION_CRS_NOT_METRIC")
    return ProjectedMetricFrame(
        frame_id.strip(), crs.to_string(), _number(origin_easting_m), _number(origin_northing_m)
    )


def _require_frame(
    frame: ProjectedMetricFrame, expected_frame_id: str, expected_calculation_crs: str
) -> None:
    if not isinstance(frame, ProjectedMetricFrame):
        raise CoordinateFrameError("FRAME_INVALID")

    validated = create_projected_metric_frame(
        frame.frame_id,
        frame.calculation_crs,
        frame.origin_easting_m,
        frame.origin_northing_m,
    )

    if validated.frame_id != frame.frame_id:
        raise CoordinateFrameError("FRAME_ID_INVALID")

    if frame.frame_id != expected_frame_id:
        raise CoordinateFrameError("FRAME_ID_MISMATCH")
    try:
        from pyproj import CRS

        expected = CRS.from_user_input(expected_calculation_crs)
        actual = CRS.from_user_input(frame.calculation_crs)
    except Exception:
        raise CoordinateFrameError("CALCULATION_CRS_INVALID") from None
    if not actual.equals(expected):
        raise CoordinateFrameError("CALCULATION_CRS_MISMATCH")


def projected_to_local(
    point: tuple[float, float],
    frame: ProjectedMetricFrame,
    *,
    expected_frame_id: str,
    expected_calculation_crs: str,
) -> tuple[float, float]:
    _require_frame(frame, expected_frame_id, expected_calculation_crs)
    if not isinstance(point, (tuple, list)) or len(point) != 2:
        raise CoordinateFrameError("COORDINATE_INVALID")
    easting, northing = _number(point[0]), _number(point[1])
    local = easting - frame.origin_easting_m, northing - frame.origin_northing_m
    if not all(isfinite(value) for value in local):
        raise CoordinateFrameError("COORDINATE_NON_FINITE")
    return local


def local_to_projected(
    point: tuple[float, float],
    frame: ProjectedMetricFrame,
    *,
    expected_frame_id: str,
    expected_calculation_crs: str,
) -> tuple[float, float]:
    _require_frame(frame, expected_frame_id, expected_calculation_crs)
    if not isinstance(point, (tuple, list)) or len(point) != 2:
        raise CoordinateFrameError("COORDINATE_INVALID")
    x, y = _number(point[0]), _number(point[1])
    projected = frame.origin_easting_m + x, frame.origin_northing_m + y
    if not all(isfinite(value) for value in projected):
        raise CoordinateFrameError("COORDINATE_NON_FINITE")
    return projected

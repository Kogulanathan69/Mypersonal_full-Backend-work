"""M2 land metric and reported-area comparison."""

from dataclasses import dataclass
from math import isfinite

from app.survey.contracts import ExtractedArea
from app.geometry.land.validation import LandGeometryResult


SQ_FT_TO_SQ_M = 0.09290304


@dataclass(frozen=True)
class LandMetrics:
    """Calculated metric measurements and reported-area comparison."""

    area_m2: float
    perimeter_m: float
    reported_area: ExtractedArea | None
    reported_area_m2: float | None
    difference_m2: float | None
    review_status: str


def build_land_metrics(
    geometry: LandGeometryResult,
    reported_area: ExtractedArea | None = None,
    tolerance_m2: float | None = None,
) -> LandMetrics:
    """Build metrics without overwriting the source-reported survey area.

    No default discrepancy tolerance is assumed. A tolerance must be
    explicitly supplied by an approved caller before comparison can
    return WITHIN_TOLERANCE or DISCREPANCY_REVIEW_REQUIRED.
    """
    if not isfinite(geometry.area_m2) or geometry.area_m2 <= 0:
        raise ValueError("Calculated area must be finite and positive")

    if not isfinite(geometry.perimeter_m) or geometry.perimeter_m <= 0:
        raise ValueError("Calculated perimeter must be finite and positive")

    if tolerance_m2 is not None:
        if not isfinite(tolerance_m2) or tolerance_m2 < 0:
            raise ValueError("Tolerance must be finite and non-negative")

    if reported_area is None:
        return LandMetrics(
            area_m2=geometry.area_m2,
            perimeter_m=geometry.perimeter_m,
            reported_area=None,
            reported_area_m2=None,
            difference_m2=None,
            review_status="NO_REPORTED_AREA",
        )

    if reported_area.unit == "m2":
        reported_area_m2 = float(reported_area.value)
    elif reported_area.unit == "ft2":
        reported_area_m2 = float(reported_area.value) * SQ_FT_TO_SQ_M
    else:
        return LandMetrics(
            area_m2=geometry.area_m2,
            perimeter_m=geometry.perimeter_m,
            reported_area=reported_area,
            reported_area_m2=None,
            difference_m2=None,
            review_status="UNKNOWN_REPORTED_AREA_UNIT_REVIEW_REQUIRED",
        )

    difference_m2 = abs(geometry.area_m2 - reported_area_m2)

    if tolerance_m2 is None:
        review_status = "TOLERANCE_UNSET_REVIEW_REQUIRED"
    elif difference_m2 <= tolerance_m2:
        review_status = "WITHIN_TOLERANCE"
    else:
        review_status = "DISCREPANCY_REVIEW_REQUIRED"

    return LandMetrics(
        area_m2=geometry.area_m2,
        perimeter_m=geometry.perimeter_m,
        reported_area=reported_area,
        reported_area_m2=reported_area_m2,
        difference_m2=difference_m2,
        review_status=review_status,
    )

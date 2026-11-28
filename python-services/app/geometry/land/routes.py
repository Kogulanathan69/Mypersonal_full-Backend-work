"""M2 land geometry validation route.

Returns structured VALID/INVALID results. Geometry validation does not
establish ownership, survey authenticity or legal boundary confirmation.
"""

from fastapi import APIRouter

from app.geometry.land.contracts import (
    LandGeometryMeasurements,
    LandGeometryValidationRequest,
    LandGeometryValidationResponse,
)
from app.geometry.land.validation import LandGeometryError
from app.geometry.land.crs import normalize_land_crs


router = APIRouter(
    prefix="/geometry/land",
    tags=["Land geometry"],
)


@router.post(
    "/validate",
    response_model=LandGeometryValidationResponse,
)
def validate_land_geometry(
    request: LandGeometryValidationRequest,
) -> LandGeometryValidationResponse:
    """Validate a candidate boundary using explicitly supplied CRS values."""

    try:
        result = normalize_land_crs(
            exterior=request.exterior,
            source_crs=request.source_crs,
            calculation_crs=request.calculation_crs,
            holes=request.holes,
        )
    except LandGeometryError as exc:
        return LandGeometryValidationResponse(
            validation_status="INVALID",
            source_crs=request.source_crs,
            calculation_crs=request.calculation_crs,
            geometry_crs="EPSG:4326",
            diagnostic_code=getattr(
                exc, "code", "LAND_GEOMETRY_INVALID"
            ),
            legal_boundary_verified=False,
        )

    metric_geometry = result.metric_geometry

    return LandGeometryValidationResponse(
        validation_status="VALID",
        source_crs=result.source_crs,
        calculation_crs=result.calculation_crs,
        geometry_crs="EPSG:4326",
        measurements=LandGeometryMeasurements(
            area_m2=metric_geometry.area_m2,
            perimeter_m=metric_geometry.perimeter_m,
        ),
        normalized_metric_exterior=[
            [x, y] for x, y in metric_geometry.exterior
        ],
        normalized_metric_holes=[
            [[x, y] for x, y in ring]
            for ring in metric_geometry.holes
        ],
        geojson=result.geojson,
        legal_boundary_verified=False,
    )

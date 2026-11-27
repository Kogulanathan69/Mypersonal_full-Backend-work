"""Provisional M2 land geometry validation API contracts.

These contracts validate geometry only. They do not establish land ownership,
survey authenticity, cadastral accuracy or legal boundary confirmation.
Cross-member approval is required before production use.
"""

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field


FiniteNumber = Annotated[float, Field(allow_inf_nan=False)]
Coordinate = Annotated[list[FiniteNumber], Field(min_length=2, max_length=2)]
LinearRing = Annotated[list[Coordinate], Field(min_length=4)]


class LandValidationContract(BaseModel):
    model_config = ConfigDict(
        extra="forbid",
        strict=True,
    )


class LandGeometryValidationRequest(LandValidationContract):
    """Candidate boundary and explicit CRS information."""

    source_crs: str = Field(min_length=1, max_length=100)
    calculation_crs: str = Field(min_length=1, max_length=100)
    exterior: LinearRing
    holes: list[LinearRing] = Field(default_factory=list, max_length=100)


class LandGeometryMeasurements(LandValidationContract):
    """Server-calculated measurements in metres."""

    area_m2: FiniteNumber = Field(gt=0)
    perimeter_m: FiniteNumber = Field(gt=0)


class LandGeometryValidationResponse(LandValidationContract):
    """Geometry validation result, not legal confirmation."""

    validation_status: Literal["VALID", "INVALID"]
    source_crs: str
    calculation_crs: str
    geometry_crs: Literal["EPSG:4326"]
    measurements: LandGeometryMeasurements | None = None
    normalized_metric_exterior: list[Coordinate] | None = None
    normalized_metric_holes: list[LinearRing] | None = None
    geojson: dict | None = None
    diagnostic_code: str | None = None
    legal_boundary_verified: Literal[False] = False

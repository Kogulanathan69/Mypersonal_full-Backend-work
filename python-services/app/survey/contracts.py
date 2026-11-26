"""Provisional M2 survey and land contracts.

These schemas preserve source measurements and uncertainty. They do not
infer coordinate systems, units, georeferencing or legal survey validity.
Cross-member approval is required before production use.
"""

from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field


class SurveyContract(BaseModel):
    model_config = ConfigDict(extra="forbid", strict=True)


NonEmptyText = str
MeasurementUnit = Literal["m", "ft", "unknown"]
CoordinateUnit = Literal["m", "ft", "degree", "unknown"]
AreaUnit = Literal["m2", "ft2", "unknown"]
ExtractionStatus = Literal[
    "PENDING",
    "PROCESSING",
    "COMPLETED",
    "NEEDS_REVIEW",
    "FAILED",
]
ReviewStatus = Literal["UNREVIEWED", "REVIEW_REQUIRED", "REVIEWED"]
FiniteMeasurement = Annotated[float, Field(allow_inf_nan=False)]


class SurveyDocument(SurveyContract):
    """Metadata for a privately stored survey document."""

    file_id: UUID
    original_file_name: NonEmptyText = Field(min_length=1, max_length=255)
    media_type: Literal["application/pdf", "image/jpeg", "image/png"]
    file_size_bytes: int = Field(gt=0)
    sha256: str = Field(pattern=r"^[a-fA-F0-9]{64}$")
    uploaded_at_utc: str = Field(min_length=1, max_length=40)


class SourceReference(SurveyContract):
    """Reference to evidence in the original survey document."""

    file_id: UUID
    page_number: int | None = Field(default=None, ge=1)
    reference_text: str | None = Field(default=None, max_length=500)


class ExtractedMeasurement(SurveyContract):
    """A reported measurement; it is not automatically a verified dimension."""

    measurement_name: str = Field(min_length=1, max_length=100)
    value: FiniteMeasurement | None = None
    unit: MeasurementUnit
    confidence: float | None = Field(default=None, ge=0, le=1)
    source: SourceReference
    review_status: ReviewStatus = "UNREVIEWED"


class ExtractedTextValue(SurveyContract):
    """A text value extracted from a source document."""

    value: str = Field(min_length=1, max_length=100)
    source: SourceReference
    review_status: ReviewStatus = "UNREVIEWED"


class ExtractedArea(SurveyContract):
    """A source-reported land area, not a calculated area."""

    value: FiniteMeasurement = Field(gt=0)
    unit: AreaUnit
    source: SourceReference
    review_status: ReviewStatus = "UNREVIEWED"


class SurveyExtraction(SurveyContract):
    """Extraction state and evidence produced from a survey document."""

    extraction_id: UUID
    file_id: UUID
    status: ExtractionStatus
    measurements: list[ExtractedMeasurement] = Field(default_factory=list)
    lot_reference: ExtractedTextValue | None = None
    reported_area: ExtractedArea | None = None
    diagnostics: list[str] = Field(default_factory=list)
    completed_at_utc: str | None = Field(default=None, max_length=40)


class BoundaryMeasurement(SurveyContract):
    """One source-reported boundary segment measurement."""

    segment_index: int = Field(ge=0)
    length: FiniteMeasurement | None = Field(default=None, gt=0)
    unit: MeasurementUnit
    bearing_text: str | None = Field(default=None, max_length=100)
    source: SourceReference
    confidence: float | None = Field(default=None, ge=0, le=1)
    review_status: ReviewStatus = "UNREVIEWED"


class LandBoundaryRevision(SurveyContract):
    """Draft source-measurement revision, not confirmed metric geometry."""

    revision_id: UUID
    project_id: UUID
    revision_number: int = Field(ge=1)
    coordinate_reference: str | None = Field(default=None, max_length=100)
    coordinate_unit: CoordinateUnit
    measurements: list[BoundaryMeasurement] = Field(default_factory=list)
    reported_area: FiniteMeasurement | None = Field(default=None, gt=0)
    reported_area_unit: AreaUnit
    source_file_ids: list[UUID] = Field(default_factory=list)
    review_status: ReviewStatus = "UNREVIEWED"

from uuid import uuid4

import pytest
from pydantic import ValidationError

from app.survey.contracts import (
    BoundaryMeasurement,
    ExtractedMeasurement,
    LandBoundaryRevision,
    SourceReference,
    SurveyDocument,
    SurveyExtraction,
)


def test_missing_measurement_is_preserved_as_none():
    item = ExtractedMeasurement(
        measurement_name="north_boundary",
        value=None,
        unit="unknown",
        source=SourceReference(file_id=uuid4()),
    )
    assert item.value is None
    assert item.unit == "unknown"


def test_extracted_measurement_preserves_source_and_confidence():
    file_id = uuid4()
    item = ExtractedMeasurement(
        measurement_name="east_boundary",
        value=12.5,
        unit="m",
        confidence=0.92,
        source=SourceReference(file_id=file_id, page_number=2),
    )
    assert item.value == 12.5
    assert item.source.file_id == file_id
    assert item.source.page_number == 2


def test_invalid_confidence_is_rejected():
    with pytest.raises(ValidationError):
        ExtractedMeasurement(
            measurement_name="east_boundary",
            value=12.5,
            unit="m",
            confidence=1.5,
            source=SourceReference(file_id=uuid4()),
        )


def test_invalid_unit_is_rejected():
    with pytest.raises(ValidationError):
        ExtractedMeasurement(
            measurement_name="east_boundary",
            value=12.5,
            unit="yards",
            source=SourceReference(file_id=uuid4()),
        )


def test_survey_document_requires_sha256():
    with pytest.raises(ValidationError):
        SurveyDocument(
            file_id=uuid4(),
            original_file_name="survey.pdf",
            media_type="application/pdf",
            file_size_bytes=100,
            sha256="invalid",
            uploaded_at_utc="2026-11-21T10:00:00Z",
        )


def test_extraction_requires_valid_file_reference():
    with pytest.raises(ValidationError):
        SurveyExtraction(
            extraction_id=uuid4(),
            file_id="not-a-uuid",
            status="PENDING",
        )


def test_boundary_revision_preserves_reported_area_separately():
    file_id = uuid4()
    revision = LandBoundaryRevision(
        revision_id=uuid4(),
        project_id=uuid4(),
        revision_number=1,
        coordinate_reference=None,
        coordinate_unit="unknown",
        measurements=[
            BoundaryMeasurement(
                segment_index=0,
                length=None,
                unit="unknown",
                source=SourceReference(file_id=file_id),
            )
        ],
        reported_area=None,
        reported_area_unit="unknown",
        source_file_ids=[file_id],
    )
    assert revision.reported_area is None
    assert revision.measurements[0].length is None
    assert revision.review_status == "UNREVIEWED"


def test_extra_fields_are_rejected():
    with pytest.raises(ValidationError):
        SourceReference(
            file_id=uuid4(),
            unexpected_field="not-allowed",
        )


def test_completed_extraction_does_not_require_invented_measurements():
    extraction = SurveyExtraction(
        extraction_id=uuid4(),
        file_id=uuid4(),
        status="COMPLETED",
    )
    assert extraction.status == "COMPLETED"
    assert extraction.measurements == []


@pytest.mark.parametrize("invalid_value", [float("nan"), float("inf"), float("-inf")])
def test_non_finite_measurements_are_rejected(invalid_value):
    with pytest.raises(ValidationError):
        ExtractedMeasurement(
            measurement_name="north_boundary",
            value=invalid_value,
            unit="m",
            source=SourceReference(file_id=uuid4()),
        )


def test_degree_is_rejected_as_boundary_distance_unit():
    with pytest.raises(ValidationError):
        BoundaryMeasurement(
            segment_index=0,
            length=10.0,
            unit="degree",
            source=SourceReference(file_id=uuid4()),
        )


def test_square_metre_is_accepted_for_reported_area():
    revision = LandBoundaryRevision(
        revision_id=uuid4(),
        project_id=uuid4(),
        revision_number=1,
        coordinate_unit="unknown",
        reported_area=120.0,
        reported_area_unit="m2",
    )
    assert revision.reported_area == 120.0
    assert revision.reported_area_unit == "m2"

def test_survey_extraction_preserves_lot_reference_and_source():
    from uuid import uuid4

    from app.survey.contracts import (
        ExtractedTextValue,
        SourceReference,
        SurveyExtraction,
    )

    file_id = uuid4()
    extraction = SurveyExtraction(
        extraction_id=uuid4(),
        file_id=file_id,
        status="COMPLETED",
        lot_reference=ExtractedTextValue(
            value="Lot 12",
            source=SourceReference(
                file_id=file_id,
                page_number=2,
                reference_text="Lot No: 12",
            ),
        ),
    )

    assert extraction.lot_reference.value == "Lot 12"
    assert extraction.lot_reference.source.page_number == 2


def test_survey_extraction_preserves_reported_area_and_unit():
    from uuid import uuid4

    from app.survey.contracts import (
        ExtractedArea,
        SourceReference,
        SurveyExtraction,
    )

    file_id = uuid4()
    extraction = SurveyExtraction(
        extraction_id=uuid4(),
        file_id=file_id,
        status="COMPLETED",
        reported_area=ExtractedArea(
            value=10.5,
            unit="m2",
            source=SourceReference(
                file_id=file_id,
                page_number=3,
                reference_text="Extent: 10.5 m2",
            ),
        ),
    )

    assert extraction.reported_area.value == 10.5
    assert extraction.reported_area.unit == "m2"
    assert extraction.reported_area.source.page_number == 3


def test_survey_extraction_allows_missing_lot_and_area():
    from uuid import uuid4

    from app.survey.contracts import SurveyExtraction

    extraction = SurveyExtraction(
        extraction_id=uuid4(),
        file_id=uuid4(),
        status="NEEDS_REVIEW",
    )

    assert extraction.lot_reference is None
    assert extraction.reported_area is None

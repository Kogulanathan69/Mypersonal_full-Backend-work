from uuid import uuid4

from app.survey.extraction_pipeline import extract_survey_document
from tests.test_survey_pdf_text_extractor import make_text_pdf


def test_pipeline_extracts_survey_information_from_pdf():
    file_id = uuid4()
    extraction_id = uuid4()

    pdf_bytes = make_text_pdf([
        "Lot No: 12\n"
        "North boundary length: 15 m\n"
        "Extent: 10.5 m2"
    ])

    result = extract_survey_document(
        file_id=file_id,
        extraction_id=extraction_id,
        pdf_bytes=pdf_bytes,
    )

    assert result.status == "COMPLETED"
    assert result.lot_reference is not None
    assert result.lot_reference.value == "12"
    assert result.reported_area is not None
    assert result.reported_area.value == 10.5
    assert len(result.measurements) == 1
    assert result.measurements[0].value == 15
    assert result.measurements[0].source.page_number == 1


def test_pipeline_marks_blank_pdf_for_manual_review():
    pdf_bytes = make_text_pdf([""])

    result = extract_survey_document(
        file_id=uuid4(),
        extraction_id=uuid4(),
        pdf_bytes=pdf_bytes,
    )

    assert result.status == "NEEDS_REVIEW"
    assert result.measurements == []
    assert result.lot_reference is None
    assert result.reported_area is None
    assert any("OCR" in item for item in result.diagnostics)


def test_pipeline_rejects_corrupt_pdf():
    result = extract_survey_document(
        file_id=uuid4(),
        extraction_id=uuid4(),
        pdf_bytes=b"not a valid PDF",
    )

    assert result.status == "FAILED"
    assert result.diagnostics


def test_pipeline_marks_unrecognised_document_for_review():
    pdf_bytes = make_text_pdf(["This is a survey document without labelled values."])

    result = extract_survey_document(
        file_id=uuid4(),
        extraction_id=uuid4(),
        pdf_bytes=pdf_bytes,
    )

    assert result.status == "NEEDS_REVIEW"
    assert result.measurements == []

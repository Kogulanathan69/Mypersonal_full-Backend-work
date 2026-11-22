"""Coordinate readable PDF text and conservative survey extraction."""

from uuid import UUID

from app.survey.contracts import SurveyExtraction
from app.survey.measurement_extractor import extract_boundary_lengths
from app.survey.metadata_extractor import (
    extract_lot_reference,
    extract_reported_area,
)
from app.survey.pdf_text_extractor import (
    PdfTextExtractionError,
    extract_pdf_page_text,
)


def extract_survey_document(
    file_id: UUID,
    extraction_id: UUID,
    pdf_bytes: bytes,
) -> SurveyExtraction:
    """Extract explicitly reported survey information from a readable PDF."""

    try:
        pages = extract_pdf_page_text(pdf_bytes)
    except (PdfTextExtractionError, ValueError) as exc:
        return SurveyExtraction(
            extraction_id=extraction_id,
            file_id=file_id,
            status="FAILED",
            diagnostics=[str(exc)],
        )

    measurements = extract_boundary_lengths(file_id, pages)
    lot_reference = extract_lot_reference(file_id, pages)
    reported_area = extract_reported_area(file_id, pages)

    has_readable_text = any(any(char.isprintable() and not char.isspace() for char in page.text) for page in pages)

    if not has_readable_text:
        status = "NEEDS_REVIEW"
        diagnostics = [
            "No readable text was found. Scanned documents require OCR or manual review."
        ]
    elif not measurements and lot_reference is None and reported_area is None:
        status = "NEEDS_REVIEW"
        diagnostics = [
            "No supported labelled survey measurements or metadata were found. OCR may be required for scanned documents; otherwise manual review is needed."
        ]
    else:
        status = "COMPLETED"
        diagnostics = []

        if not measurements:
            diagnostics.append("No labelled boundary measurements were found.")

        if lot_reference is None:
            diagnostics.append("Lot reference was not found.")

        if reported_area is None:
            diagnostics.append("Reported area was not found.")

    return SurveyExtraction(
        extraction_id=extraction_id,
        file_id=file_id,
        status=status,
        measurements=measurements,
        lot_reference=lot_reference,
        reported_area=reported_area,
        diagnostics=diagnostics,
    )

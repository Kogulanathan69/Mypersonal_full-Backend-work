"""Captain survey extraction route over the existing M2 extraction pipeline and extractors.

1. Integrity: the decoded bytes must match the declared SHA-256 and size; media type must be PDF/PNG/JPEG.
2. Readable PDFs use the existing pdf-text pipeline (unchanged).
3. If no readable text is found (or the file is an image), local OCR (Tesseract) supplies page text and the existing
   extractors run on it. OCR-derived results are ALWAYS returned as NEEDS_REVIEW with REVIEW_REQUIRED values.
4. Nothing is inferred: no coordinates, bearings, CRS, units or polygon are invented; the result is evidence for
   review, not a legal survey determination.
"""
import base64
import binascii
import hashlib
import re
from typing import Annotated, Literal

from pydantic import Field, StrictInt

from app.geometry.footprint.contracts import ContractModel, NonNilUUID
from app.survey import ocr
from app.survey.contracts import SurveyExtraction
from app.survey.extraction_pipeline import extract_survey_document
from app.survey.measurement_extractor import extract_boundary_lengths
from app.survey.metadata_extractor import extract_lot_reference, extract_reported_area

SCHEMA = "captain-survey-extract/1"
MAX_FILE_BYTES = 6 * 1024 * 1024
MAX_MEASUREMENTS = 200
MAX_DIAGNOSTICS = 20


class SurveyExtractRequest(ContractModel):
    schemaVersion: Literal["captain-survey-extract/1"]
    extractionId: NonNilUUID
    fileId: NonNilUUID
    mediaType: Literal["application/pdf", "image/jpeg", "image/png"]
    byteLength: Annotated[StrictInt, Field(gt=0, le=MAX_FILE_BYTES)]
    sha256: Annotated[str, Field(pattern=r"^[0-9a-f]{64}$")]
    contentBase64: Annotated[str, Field(min_length=4, max_length=(MAX_FILE_BYTES * 4) // 3 + 8)]


class IntegrityRejected(ValueError):
    pass


def _decode(request: SurveyExtractRequest) -> bytes:
    try:
        content = base64.b64decode(request.contentBase64, validate=True)
    except (binascii.Error, ValueError):
        raise IntegrityRejected("CONTENT_ENCODING_INVALID") from None
    if len(content) != request.byteLength or hashlib.sha256(content).hexdigest() != request.sha256:
        raise IntegrityRejected("CONTENT_INTEGRITY_MISMATCH")
    return content


def _review_required(model):
    return model.model_copy(update={"review_status": "REVIEW_REQUIRED"}) if model is not None else None


def _ocr_extraction(request, content: bytes, prior: list[str]) -> SurveyExtraction:
    try:
        pages = ocr.ocr_pages(content, request.mediaType)
    except ocr.OcrUnavailable:
        return SurveyExtraction(extraction_id=request.extractionId, file_id=request.fileId, status="NEEDS_REVIEW",
                                diagnostics=prior + ["Local OCR is not available; manual review is required."])
    except ocr.OcrFailed as error:
        return SurveyExtraction(extraction_id=request.extractionId, file_id=request.fileId, status="NEEDS_REVIEW",
                                diagnostics=prior + [str(error), "Manual review is required."])
    measurements = [_review_required(m) for m in extract_boundary_lengths(request.fileId, pages)][:MAX_MEASUREMENTS]
    lot = _review_required(extract_lot_reference(request.fileId, pages))
    area = _review_required(extract_reported_area(request.fileId, pages))
    found = bool(measurements) or lot is not None or area is not None
    return SurveyExtraction(
        extraction_id=request.extractionId, file_id=request.fileId, status="NEEDS_REVIEW",
        measurements=measurements, lot_reference=lot, reported_area=area,
        diagnostics=prior + [
            "Values were read by OCR and require manual review before use." if found
            else "OCR found no supported labelled survey measurements or metadata; manual review is required."
        ],
    )


def extract(request: SurveyExtractRequest) -> dict:
    content = _decode(request)
    method = "PDF_TEXT"
    if request.mediaType == "application/pdf":
        result = extract_survey_document(request.fileId, request.extractionId, content)
        if result.status == "NEEDS_REVIEW" and not result.measurements and result.lot_reference is None \
                and result.reported_area is None:
            method = "OCR"
            result = _ocr_extraction(request, content, ["No readable PDF text; OCR was attempted."])
    else:
        method = "OCR"
        result = _ocr_extraction(request, content, [])
    payload = result.model_dump(mode="json")
    payload["measurements"] = payload["measurements"][:MAX_MEASUREMENTS]
    payload["diagnostics"] = [re.sub(r"[\x00-\x1f\x7f]", " ", d)[:300] for d in payload["diagnostics"][:MAX_DIAGNOSTICS]]
    return {"schemaVersion": SCHEMA, "method": method, "ocrLanguages": _reported_languages() if method == "OCR" else None,
            "extraction": payload}


def _reported_languages() -> str | None:
    """Installed OCR languages actually used (None when OCR is unavailable)."""
    try:
        return ocr.languages()
    except ocr.OcrUnavailable:
        return None

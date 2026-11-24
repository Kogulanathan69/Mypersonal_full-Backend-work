"""Captain survey extraction route: integrity, readable PDF path, OCR fallback (fake OCR seam), review semantics.

Synthetic documents only. Real Tesseract runs live in tests_local/test_survey_ocr_local.py (not collected in CI).
"""
import base64
import hashlib
import json
from uuid import UUID

from fastapi.testclient import TestClient

from app.internal.auth import AUTH_HEADER, InternalAuth
from app.main import create_app
from app.survey import ocr
from app.survey.page_text import PageText
from tests.test_survey_pdf_text_extractor import make_text_pdf

TOKEN = "synthetic-python-internal-token-0123456789abcdef"
EXTRACTION = "00000000-0000-4000-8000-0000000000e1"
FILE = "00000000-0000-4000-8000-0000000000f1"


def client():
    return TestClient(create_app(InternalAuth.from_tokens([TOKEN])), raise_server_exceptions=False)


def body(content: bytes, media="application/pdf", **overrides):
    payload = {
        "schemaVersion": "captain-survey-extract/1", "extractionId": EXTRACTION, "fileId": FILE, "mediaType": media,
        "byteLength": len(content), "sha256": hashlib.sha256(content).hexdigest(),
        "contentBase64": base64.b64encode(content).decode(),
    }
    payload.update(overrides)
    return payload


def post(payload, token=TOKEN):
    headers = {"Content-Type": "application/json"}
    if token:
        headers[AUTH_HEADER] = token
    return client().post("/internal/v1/survey/extract", content=json.dumps(payload), headers=headers)


def test_readable_pdf_uses_text_pipeline_and_returns_unreviewed_evidence():
    pdf = make_text_pdf("North boundary length: 12 m Lot No: 42 Land area: 450 m2")
    response = post(body(pdf))
    assert response.status_code == 200
    result = response.json()
    assert result["method"] == "PDF_TEXT" and result["ocrLanguages"] is None
    extraction = result["extraction"]
    assert extraction["status"] == "COMPLETED"
    assert extraction["measurements"][0]["value"] == 12 and extraction["measurements"][0]["unit"] == "m"
    assert extraction["lot_reference"]["value"] == "42"
    assert extraction["reported_area"]["value"] == 450
    assert extraction["measurements"][0]["source"]["page_number"] == 1


def test_integrity_mismatch_and_bad_encoding_are_rejected_without_processing():
    pdf = make_text_pdf("North boundary length: 12 m")
    assert post(body(pdf, sha256="0" * 64)).json() == {"code": "CONTENT_INTEGRITY_MISMATCH"}
    assert post(body(pdf, byteLength=len(pdf) + 1)).status_code == 422
    assert post(body(pdf, contentBase64="@@@@")).json() == {"code": "CONTENT_ENCODING_INVALID"}


def test_requires_internal_token_and_strict_schema():
    pdf = make_text_pdf("North boundary length: 12 m")
    assert post(body(pdf), token=None).status_code == 401
    assert post(body(pdf, mediaType="image/gif")).status_code == 422
    assert post({**body(pdf), "extra": 1}).status_code == 422


def test_unreadable_pdf_falls_back_to_ocr_and_is_always_needs_review(monkeypatch):
    monkeypatch.setattr(ocr, "ocr_pages", lambda content, media: (PageText(1, "East boundary length: 9.5 m"),))
    result = post(body(make_text_pdf(""))).json()
    extraction = result["extraction"]
    assert result["method"] == "OCR"
    assert extraction["status"] == "NEEDS_REVIEW"
    assert extraction["measurements"][0]["value"] == 9.5
    assert extraction["measurements"][0]["review_status"] == "REVIEW_REQUIRED"
    assert any("OCR" in d for d in extraction["diagnostics"])


def test_images_use_ocr_and_missing_ocr_degrades_to_manual_review(monkeypatch):
    def unavailable(content, media):
        raise ocr.OcrUnavailable("Local OCR is not installed.")

    monkeypatch.setattr(ocr, "ocr_pages", unavailable)
    png = b"\x89PNG\r\n\x1a\n" + b"\x00" * 32
    extraction = post(body(png, media="image/png")).json()["extraction"]
    assert extraction["status"] == "NEEDS_REVIEW" and extraction["measurements"] == []
    assert "manual review" in " ".join(extraction["diagnostics"]).lower()


def test_ocr_failures_never_leak_document_content(monkeypatch):
    def failing(content, media):
        raise ocr.OcrFailed("OCR could not read a page.")

    monkeypatch.setattr(ocr, "ocr_pages", failing)
    extraction = post(body(b"\xff\xd8\xff" + b"secret-survey-content", media="image/jpeg")).json()["extraction"]
    assert extraction["status"] == "NEEDS_REVIEW"
    assert "secret-survey-content" not in json.dumps(extraction)


def test_oversized_payload_is_rejected_before_parsing():
    big = {"schemaVersion": "x", "contentBase64": "A" * (10 * 1024 * 1024)}
    assert post(big).status_code == 413
    assert UUID(EXTRACTION)

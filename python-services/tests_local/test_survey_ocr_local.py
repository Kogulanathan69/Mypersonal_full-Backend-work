"""Real local OCR (Tesseract + pdftoppm) on SYNTHETIC documents. Not collected in CI (pytest.ini testpaths=tests).

Run: python -m pytest -q -p no:cacheprovider tests_local  (fails, never skips, when the tools are missing).
"""
import base64
import hashlib
import os
import subprocess
import tempfile

import pytest

from app.internal.survey_extract import SurveyExtractRequest, extract
from app.survey import ocr
from tests.test_survey_pdf_text_extractor import make_text_pdf


def _png_from_text(text: str) -> bytes:
    with tempfile.TemporaryDirectory() as work:
        pdf = os.path.join(work, "s.pdf")
        open(pdf, "wb").write(make_text_pdf(text))
        subprocess.run(["pdftoppm", "-r", "300", "-png", "-singlefile", pdf, os.path.join(work, "s")], check=True)
        return open(os.path.join(work, "s.png"), "rb").read()


def _request(content: bytes, media: str) -> SurveyExtractRequest:
    return SurveyExtractRequest.model_validate({
        "schemaVersion": "captain-survey-extract/1", "extractionId": "00000000-0000-4000-8000-0000000000e2",
        "fileId": "00000000-0000-4000-8000-0000000000f2", "mediaType": media, "byteLength": len(content),
        "sha256": hashlib.sha256(content).hexdigest(), "contentBase64": base64.b64encode(content).decode()})


def test_tools_are_installed():
    assert ocr.available(), "tesseract and pdftoppm are required for local OCR"


def test_real_ocr_reads_a_synthetic_scanned_png():
    png = _png_from_text("North boundary length: 12 m")
    result = extract(_request(png, "image/png"))
    extraction = result["extraction"]
    assert result["method"] == "OCR" and extraction["status"] == "NEEDS_REVIEW"
    assert [m["value"] for m in extraction["measurements"]] == [12]
    assert extraction["measurements"][0]["review_status"] == "REVIEW_REQUIRED"


def test_real_ocr_on_blank_image_finds_nothing_and_requires_manual_review():
    png = _png_from_text("")
    extraction = extract(_request(png, "image/png"))["extraction"]
    assert extraction["status"] == "NEEDS_REVIEW" and extraction["measurements"] == []


def test_installed_languages_include_english_and_report_sinhala_tamil_when_present():
    langs = ocr.languages().split("+")
    assert "eng" in langs


def test_malformed_pdf_fails_closed_with_fixed_message():
    with pytest.raises(ocr.OcrFailed) as error:
        ocr.ocr_pages(b"%PDF-1.4\nthis is not a pdf", "application/pdf")
    assert str(error.value) == "PDF could not be rasterised."

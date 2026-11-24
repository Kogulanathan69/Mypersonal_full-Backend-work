"""Local OCR for scanned survey documents (Tesseract CLI; PDFs rasterised with Poppler's pdftoppm).

Processing is entirely local: no network, no external provider. Both tools run as separate processes with bounded
page counts, resolution and per-page time limits, in a private temporary directory that is always removed.
OCR text is evidence for human review only; callers must never treat OCR-derived values as verified.
"""
import os
import shutil
import subprocess
import tempfile

from app.survey.page_text import PageText

MAX_PAGES = 10
DPI = 200
PAGE_TIMEOUT_SECONDS = 60
LANGUAGES = "eng"


class OcrUnavailable(RuntimeError):
    """Tesseract or pdftoppm is not installed."""


class OcrFailed(RuntimeError):
    """A fixed-message OCR failure (never contains document content)."""


def available() -> bool:
    return shutil.which("tesseract") is not None and shutil.which("pdftoppm") is not None


def _tesseract(image_path: str) -> str:
    try:
        result = subprocess.run(
            ["tesseract", image_path, "stdout", "-l", LANGUAGES, "--psm", "3"],
            capture_output=True, timeout=PAGE_TIMEOUT_SECONDS, check=False,
            env={"PATH": os.environ.get("PATH", "/usr/bin:/bin"), "OMP_THREAD_LIMIT": "1"},
        )
    except subprocess.TimeoutExpired:
        raise OcrFailed("OCR timed out for a page.") from None
    if result.returncode != 0:
        raise OcrFailed("OCR could not read a page.")
    return result.stdout.decode("utf-8", errors="replace")


def ocr_pages(content: bytes, media_type: str) -> tuple[PageText, ...]:
    if not available():
        raise OcrUnavailable("Local OCR is not installed.")
    with tempfile.TemporaryDirectory(prefix="bm-ocr-") as work:
        if media_type in ("image/png", "image/jpeg"):
            source = os.path.join(work, "page.png" if media_type == "image/png" else "page.jpg")
            with open(source, "wb") as handle:
                handle.write(content)
            return (PageText(page_number=1, text=_tesseract(source)),)
        if media_type != "application/pdf":
            raise OcrFailed("Unsupported media type for OCR.")
        pdf = os.path.join(work, "document.pdf")
        with open(pdf, "wb") as handle:
            handle.write(content)
        try:
            rendered = subprocess.run(
                ["pdftoppm", "-r", str(DPI), "-f", "1", "-l", str(MAX_PAGES), "-png", pdf, os.path.join(work, "page")],
                capture_output=True, timeout=PAGE_TIMEOUT_SECONDS * 2, check=False,
                env={"PATH": os.environ.get("PATH", "/usr/bin:/bin")},
            )
        except subprocess.TimeoutExpired:
            raise OcrFailed("PDF rasterisation timed out.") from None
        if rendered.returncode != 0:
            raise OcrFailed("PDF could not be rasterised.")
        images = sorted(name for name in os.listdir(work) if name.startswith("page") and name.endswith(".png"))
        return tuple(PageText(page_number=index + 1, text=_tesseract(os.path.join(work, name)))
                     for index, name in enumerate(images[:MAX_PAGES]))

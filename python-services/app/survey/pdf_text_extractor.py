"""Readable PDF text extraction with source page preservation."""

from io import BytesIO

from pypdf import PdfReader
from pypdf.errors import PdfReadError

from app.survey.page_text import PageText, preserve_page_text


class PdfTextExtractionError(Exception):
    """Raised when a PDF cannot be read for text extraction."""


def extract_pdf_page_text(pdf_bytes: bytes) -> tuple[PageText, ...]:
    """Extract readable text while preserving original one-based page numbers."""

    if not isinstance(pdf_bytes, bytes) or not pdf_bytes:
        raise ValueError("PDF content must be non-empty bytes")

    try:
        reader = PdfReader(BytesIO(pdf_bytes), strict=False)

        if reader.is_encrypted:
            raise PdfTextExtractionError(
                "Encrypted PDFs are not supported for extraction"
            )

        pages = tuple(
            PageText(
                page_number=page_number,
                text=page.extract_text() or "",
            )
            for page_number, page in enumerate(reader.pages, start=1)
        )

        return preserve_page_text(pages)

    except PdfTextExtractionError:
        raise
    except (PdfReadError, ValueError, TypeError) as exc:
        raise PdfTextExtractionError(
            "The PDF could not be read"
        ) from exc

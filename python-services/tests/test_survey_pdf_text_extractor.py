from io import BytesIO

import pytest
from pypdf import PdfWriter

from app.survey.pdf_text_extractor import (
    PdfTextExtractionError,
    extract_pdf_page_text,
)


def make_text_pdf(text: str) -> bytes:
    content = (
        f"BT /F1 12 Tf 72 720 Td ({text}) Tj ET"
    ).encode("ascii")

    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R "
            b"/MediaBox [0 0 612 792] "
            b"/Resources << /Font << /F1 5 0 R >> >> "
            b"/Contents 4 0 R >>"
        ),
        (
            b"<< /Length " + str(len(content)).encode()
            + b" >>\nstream\n" + content + b"\nendstream"
        ),
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]

    pdf = bytearray(b"%PDF-1.4\n")
    offsets = [0]

    for index, obj in enumerate(objects, start=1):
        offsets.append(len(pdf))
        pdf.extend(f"{index} 0 obj\n".encode())
        pdf.extend(obj)
        pdf.extend(b"\nendobj\n")

    xref_offset = len(pdf)
    pdf.extend(f"xref\n0 {len(objects) + 1}\n".encode())
    pdf.extend(b"0000000000 65535 f \n")

    for offset in offsets[1:]:
        pdf.extend(f"{offset:010d} 00000 n \n".encode())

    pdf.extend(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
            f"startxref\n{xref_offset}\n%%EOF"
        ).encode()
    )

    return bytes(pdf)


def test_extracts_readable_pdf_text_with_page_number():
    result = extract_pdf_page_text(
        make_text_pdf("North boundary length: 12 m")
    )

    assert len(result) == 1
    assert result[0].page_number == 1
    assert "North boundary length: 12 m" in result[0].text


def test_preserves_page_with_no_extractable_text():
    writer = PdfWriter()
    writer.add_blank_page(width=612, height=792)

    output = BytesIO()
    writer.write(output)

    result = extract_pdf_page_text(output.getvalue())

    assert len(result) == 1
    assert result[0].page_number == 1
    assert result[0].text == ""


def test_rejects_corrupt_pdf():
    with pytest.raises(PdfTextExtractionError):
        extract_pdf_page_text(b"This is not a PDF")


def test_rejects_empty_input():
    with pytest.raises(ValueError):
        extract_pdf_page_text(b"")


def test_rejects_encrypted_pdf():
    writer = PdfWriter()
    writer.add_blank_page(width=612, height=792)
    writer.encrypt("test-password")

    output = BytesIO()
    writer.write(output)

    with pytest.raises(PdfTextExtractionError):
        extract_pdf_page_text(output.getvalue())

"""Independent check of the journey's downloaded report with pypdf (run with the python-services venv).

Verifies the PDF parses, and that its extracted text carries the preliminary notice, the non-approval disclaimer and
the synthetic-data statement. Usage: check_report_pdf.py <pdf>
"""
import re
import sys

from pypdf import PdfReader

reader = PdfReader(sys.argv[1])
text = re.sub(r"\s+", " ", " ".join(page.extract_text() or "" for page in reader.pages))
required = {
    "preliminary notice": "Preliminary planning report",
    "not a construction drawing": "not a construction drawing",
    "not a regulatory approval": "regulatory approval",
    "synthetic data statement": "synthetic",
}
missing = [name for name, needle in required.items() if needle.lower() not in text.lower()]
print(f"pages={len(reader.pages)} chars={len(text)} missing={missing}")
sys.exit(1 if missing or not reader.pages else 0)

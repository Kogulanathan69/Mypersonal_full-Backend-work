from uuid import uuid4

from app.survey.measurement_extractor import extract_boundary_lengths
from app.survey.pdf_text_extractor import extract_pdf_page_text


def test_pdf_text_flows_into_boundary_measurement_extraction():
    file_id = uuid4()

    # PDF fixture is generated in memory; no real survey document is used.
    from tests.test_survey_pdf_text_extractor import make_text_pdf

    pdf_bytes = make_text_pdf("North boundary length: 12 m")

    pages = extract_pdf_page_text(pdf_bytes)
    measurements = extract_boundary_lengths(file_id, pages)

    assert len(measurements) == 1
    assert measurements[0].measurement_name == "north_boundary"
    assert measurements[0].value == 12
    assert measurements[0].unit == "m"
    assert measurements[0].source.file_id == file_id
    assert measurements[0].source.page_number == 1

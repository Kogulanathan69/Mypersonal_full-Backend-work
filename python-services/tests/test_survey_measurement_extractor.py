from uuid import uuid4

from app.survey.measurement_extractor import extract_boundary_lengths
from app.survey.page_text import PageText


def test_extracts_north_boundary_with_source_page() -> None:
    file_id = uuid4()
    pages = (
        PageText(1, "North boundary length: 12.5 m"),
    )

    result = extract_boundary_lengths(file_id, pages)

    assert len(result) == 1
    assert result[0].measurement_name == "north_boundary"
    assert result[0].value == 12.5
    assert result[0].unit == "m"
    assert result[0].confidence is None
    assert result[0].source.file_id == file_id
    assert result[0].source.page_number == 1
    assert result[0].source.reference_text == "North boundary length: 12.5 m"


def test_extracts_feet_unit() -> None:
    result = extract_boundary_lengths(
        uuid4(),
        (PageText(2, "Boundary distance: 40 ft"),),
    )

    assert len(result) == 1
    assert result[0].value == 40.0
    assert result[0].unit == "ft"
    assert result[0].source.page_number == 2


def test_ignores_unlabelled_numbers() -> None:
    result = extract_boundary_lengths(
        uuid4(),
        (PageText(1, "The plot measures approximately 12.5 m"),),
    )

    assert result == []


def test_ignores_reported_area() -> None:
    result = extract_boundary_lengths(
        uuid4(),
        (PageText(1, "Reported area: 120 m2"),),
    )

    assert result == []


def test_preserves_multiple_boundary_measurements() -> None:
    result = extract_boundary_lengths(
        uuid4(),
        (
            PageText(
                1,
                "North boundary length: 12 m\n"
                "South boundary distance: 11.5 m",
            ),
        ),
    )

    assert len(result) == 2
    assert result[0].measurement_name == "north_boundary"
    assert result[1].measurement_name == "south_boundary"


def test_empty_document_text_returns_no_measurements() -> None:
    result = extract_boundary_lengths(
        uuid4(),
        (PageText(1, ""),),
    )

    assert result == []

def test_ignores_zero_boundary_length() -> None:
    result = extract_boundary_lengths(
        uuid4(),
        (PageText(1, "North boundary length: 0 m"),),
    )

    assert result == []

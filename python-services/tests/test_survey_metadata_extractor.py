from uuid import uuid4

from app.survey.metadata_extractor import (
    extract_lot_reference,
    extract_reported_area,
)
from app.survey.page_text import PageText


def test_extracts_lot_reference_with_page_and_source():
    file_id = uuid4()
    pages = (
        PageText(page_number=1, text="Survey Plan"),
        PageText(page_number=2, text="Lot No: 12"),
    )

    result = extract_lot_reference(file_id, pages)

    assert result is not None
    assert result.value == "12"
    assert result.source.file_id == file_id
    assert result.source.page_number == 2
    assert result.source.reference_text == "Lot No: 12"


def test_extracts_block_number():
    result = extract_lot_reference(
        uuid4(),
        (PageText(page_number=1, text="Block Number: B-12"),),
    )

    assert result is not None
    assert result.value == "B-12"


def test_does_not_infer_unlabelled_lot_number():
    result = extract_lot_reference(
        uuid4(),
        (PageText(page_number=1, text="12, 15, 20"),),
    )

    assert result is None


def test_extracts_reported_area_in_square_metres():
    file_id = uuid4()
    pages = (
        PageText(page_number=1, text="Extent: 10.5 m2"),
    )

    result = extract_reported_area(file_id, pages)

    assert result is not None
    assert result.value == 10.5
    assert result.unit == "m2"
    assert result.source.file_id == file_id
    assert result.source.page_number == 1


def test_extracts_land_area_in_square_feet():
    result = extract_reported_area(
        uuid4(),
        (PageText(page_number=1, text="Land Area: 1200 sq ft"),),
    )

    assert result is not None
    assert result.value == 1200
    assert result.unit == "ft2"


def test_does_not_infer_unlabelled_area():
    result = extract_reported_area(
        uuid4(),
        (PageText(page_number=1, text="1200 sq ft"),),
    )

    assert result is None


def test_ignores_zero_area():
    result = extract_reported_area(
        uuid4(),
        (PageText(page_number=1, text="Extent: 0 m2"),),
    )

    assert result is None


def test_missing_metadata_returns_none():
    pages = (PageText(page_number=1, text="Survey Plan"),)

    assert extract_lot_reference(uuid4(), pages) is None
    assert extract_reported_area(uuid4(), pages) is None

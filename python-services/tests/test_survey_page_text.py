import pytest

from app.survey.page_text import PageText, preserve_page_text


def test_preserves_page_numbers_and_original_text() -> None:
    pages = [
        PageText(page_number=1, text="Survey Plan"),
        PageText(page_number=2, text="Boundary length: 12 m"),
    ]

    result = preserve_page_text(pages)

    assert len(result) == 2
    assert result[0].page_number == 1
    assert result[0].text == "Survey Plan"
    assert result[1].page_number == 2
    assert result[1].text == "Boundary length: 12 m"


def test_rejects_page_number_zero() -> None:
    with pytest.raises(ValueError):
        PageText(page_number=0, text="Invalid page")


def test_rejects_duplicate_page_numbers() -> None:
    pages = [
        PageText(page_number=1, text="Page one"),
        PageText(page_number=1, text="Duplicate page"),
    ]

    with pytest.raises(ValueError):
        preserve_page_text(pages)


def test_rejects_out_of_order_pages() -> None:
    pages = [
        PageText(page_number=2, text="Page two"),
        PageText(page_number=1, text="Page one"),
    ]

    with pytest.raises(ValueError):
        preserve_page_text(pages)


def test_empty_page_collection_is_allowed() -> None:
    assert preserve_page_text([]) == ()

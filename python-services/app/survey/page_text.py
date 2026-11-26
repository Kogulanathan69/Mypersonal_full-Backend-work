"""Dependency-independent page text models for survey extraction."""

from dataclasses import dataclass
from collections.abc import Iterable


@dataclass(frozen=True, slots=True)
class PageText:
    """Text extracted from one source document page."""

    page_number: int
    text: str

    def __post_init__(self) -> None:
        if self.page_number < 1:
            raise ValueError("page_number must be at least 1")
        if not isinstance(self.text, str):
            raise TypeError("page text must be a string")


def preserve_page_text(
    pages: Iterable[PageText],
) -> tuple[PageText, ...]:
    """Preserve page order and evidence without modifying source text."""

    result = tuple(pages)
    previous_page_number = 0

    for page in result:
        if page.page_number <= previous_page_number:
            raise ValueError(
                "pages must have unique, strictly increasing page numbers"
            )
        previous_page_number = page.page_number

    return result

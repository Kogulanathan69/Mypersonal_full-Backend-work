"""Conservative extraction of explicitly labelled survey metadata."""

import math
import re
from uuid import UUID

from app.survey.contracts import (
    ExtractedArea,
    ExtractedTextValue,
    SourceReference,
)
from app.survey.page_text import PageText


_LOT_PATTERN = re.compile(
    r"\b(?:lot|block)\s*(?:number|no\.?|#)\s*[:=-]?\s*"
    r"([A-Za-z0-9][A-Za-z0-9/-]{0,49})\b",
    re.IGNORECASE,
)

_AREA_PATTERN = re.compile(
    r"\b(?:reported\s+)?(?:land\s+)?(?:area|extent)\s*[:=-]?\s*"
    r"(\d+(?:\.\d+)?)\s*"
    r"(m2|m²|sq\.?\s*m|square\s+met(?:er|re)s?|"
    r"ft2|ft²|sq\.?\s*ft|square\s+feet)\b",
    re.IGNORECASE,
)


def extract_lot_reference(
    file_id: UUID,
    pages: tuple[PageText, ...],
) -> ExtractedTextValue | None:
    """Extract the first explicitly labelled lot or block reference."""

    for page in pages:
        for line in page.text.splitlines():
            match = _LOT_PATTERN.search(line)

            if match is not None:
                return ExtractedTextValue(
                    value=match.group(1),
                    source=SourceReference(
                        file_id=file_id,
                        page_number=page.page_number,
                        reference_text=line[:500],
                    ),
                )

    return None


def extract_reported_area(
    file_id: UUID,
    pages: tuple[PageText, ...],
) -> ExtractedArea | None:
    """Extract the first explicitly labelled positive reported area."""

    for page in pages:
        for line in page.text.splitlines():
            match = _AREA_PATTERN.search(line)

            if match is None:
                continue

            value = float(match.group(1))

            if not math.isfinite(value) or value <= 0:
                continue

            raw_unit = re.sub(r"\s+", " ", match.group(2).lower()).strip()

            unit = (
                "m2"
                if raw_unit in {
                    "m2", "m²", "sq m", "sq. m",
                    "square meter", "square meters",
                    "square metre", "square metres",
                }
                else "ft2"
            )

            return ExtractedArea(
                value=value,
                unit=unit,
                source=SourceReference(
                    file_id=file_id,
                    page_number=page.page_number,
                    reference_text=line[:500],
                ),
            )

    return None

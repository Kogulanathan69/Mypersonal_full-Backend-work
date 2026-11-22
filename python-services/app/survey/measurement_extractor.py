"""Conservative extraction of explicitly labelled boundary lengths."""

import math
import re
from uuid import UUID

from app.survey.contracts import (
    ExtractedMeasurement,
    SourceReference,
)
from app.survey.page_text import PageText


_BOUNDARY_LENGTH_PATTERN = re.compile(
    r"(?P<label>(?:(?:north|south|east|west)\s+)?boundary\s+"
    r"(?:length|distance))\s*[:=]\s*"
    r"(?P<value>\d+(?:\.\d+)?)\s*"
    r"(?P<unit>m|metres?|meters?|ft|feet)\b",
    re.IGNORECASE,
)


def extract_boundary_lengths(
    file_id: UUID,
    pages: tuple[PageText, ...],
) -> list[ExtractedMeasurement]:
    """Extract only explicitly labelled, positive boundary lengths."""

    measurements: list[ExtractedMeasurement] = []

    for page in pages:
        for line in page.text.splitlines():
            match = _BOUNDARY_LENGTH_PATTERN.search(line)

            if match is None:
                continue

            value = float(match.group("value"))

            if not math.isfinite(value) or value <= 0:
                continue

            label = match.group("label").lower()
            raw_unit = match.group("unit").lower()

            unit = (
                "m"
                if raw_unit in {"m", "metre", "metres", "meter", "meters"}
                else "ft"
            )

            direction = next(
                (
                    name
                    for name in ("north", "south", "east", "west")
                    if label.startswith(name)
                ),
                None,
            )

            measurement_name = (
                f"{direction}_boundary"
                if direction
                else "boundary_length"
            )

            measurements.append(
                ExtractedMeasurement(
                    measurement_name=measurement_name,
                    value=value,
                    unit=unit,
                    confidence=None,
                    source=SourceReference(
                        file_id=file_id,
                        page_number=page.page_number,
                        reference_text=line[:500],
                    ),
                )
            )

    return measurements

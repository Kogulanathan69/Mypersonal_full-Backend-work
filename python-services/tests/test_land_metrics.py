from uuid import UUID

from app.geometry.land.metrics import build_land_metrics
from app.geometry.land.validation import validate_land_boundary
from app.survey.contracts import ExtractedArea


def square_geometry():
    return validate_land_boundary([
        [0, 0], [20, 0], [20, 10], [0, 10], [0, 0]
    ])


def reported_area(value, unit):
    return ExtractedArea(
        value=value,
        unit=unit,
        source={
            "file_id": UUID("12345678-1234-5678-1234-567812345678"),
            "reference_text": "Survey plan area",
        },
    )


def test_rectangle_calculates_area_and_perimeter():
    result = build_land_metrics(square_geometry())

    assert result.area_m2 == 200
    assert result.perimeter_m == 60
    assert result.review_status == "NO_REPORTED_AREA"


def test_reported_area_is_preserved_separately():
    source_area = reported_area(195, "m2")
    result = build_land_metrics(square_geometry(), source_area)

    assert result.area_m2 == 200
    assert result.reported_area == source_area
    assert result.reported_area_m2 == 195
    assert result.difference_m2 == 5
    assert result.review_status == "TOLERANCE_UNSET_REVIEW_REQUIRED"


def test_square_feet_are_converted_to_square_metres():
    result = build_land_metrics(
        square_geometry(),
        reported_area(1000, "ft2"),
    )

    assert result.reported_area_m2 == 92.90304


def test_unknown_reported_area_unit_requires_review():
    result = build_land_metrics(
        square_geometry(),
        reported_area(200, "unknown"),
    )

    assert result.reported_area_m2 is None
    assert result.review_status == "UNKNOWN_REPORTED_AREA_UNIT_REVIEW_REQUIRED"


def test_explicit_tolerance_controls_comparison():
    result = build_land_metrics(
        square_geometry(),
        reported_area(195, "m2"),
        tolerance_m2=5,
    )

    assert result.review_status == "WITHIN_TOLERANCE"


def test_difference_above_explicit_tolerance_requires_review():
    result = build_land_metrics(
        square_geometry(),
        reported_area(190, "m2"),
        tolerance_m2=5,
    )

    assert result.review_status == "DISCREPANCY_REVIEW_REQUIRED"

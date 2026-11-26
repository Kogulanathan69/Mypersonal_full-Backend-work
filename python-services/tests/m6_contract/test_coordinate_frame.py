from math import isclose

import pytest

from app.geometry.coordinate_frame import (
    CoordinateFrameError,
    ProjectedMetricFrame,
    create_projected_metric_frame,
    local_to_projected,
    projected_to_local,
)


CRS = "EPSG:32644"
FRAME_ID = "synthetic-frame-v1"


def _frame():
    return create_projected_metric_frame(FRAME_ID, CRS, 500_000.25, 1_000_000.5)


def test_projected_to_local_and_round_trip_preserve_explicit_origin() -> None:
    frame = _frame()
    assert projected_to_local(
        (frame.origin_easting_m, frame.origin_northing_m), frame,
        expected_frame_id=FRAME_ID, expected_calculation_crs=CRS,
    ) == (0.0, 0.0)
    projected = (500_012.75, 999_980.125)
    local = projected_to_local(
        projected, frame, expected_frame_id=FRAME_ID, expected_calculation_crs=CRS
    )
    restored = local_to_projected(
        local, frame, expected_frame_id=FRAME_ID, expected_calculation_crs=CRS
    )
    assert all(isclose(left, right, abs_tol=1e-9) for left, right in zip(projected, restored))


@pytest.mark.parametrize("point", [(float("nan"), 1), (1, float("inf")), (True, 1), (1,), "1,2"])
def test_invalid_or_nonfinite_coordinates_are_rejected(point) -> None:
    with pytest.raises(CoordinateFrameError) as error:
        projected_to_local(
            point, _frame(), expected_frame_id=FRAME_ID, expected_calculation_crs=CRS
        )
    assert error.value.code in {"COORDINATE_INVALID", "COORDINATE_NON_FINITE"}


def test_geographic_degrees_cannot_become_editor_metres() -> None:
    with pytest.raises(CoordinateFrameError) as error:
        create_projected_metric_frame(FRAME_ID, "EPSG:4326", 80.0, 7.0)
    assert error.value.code == "CALCULATION_CRS_NOT_PROJECTED"


def test_frame_identity_and_crs_mismatch_fail_closed() -> None:
    with pytest.raises(CoordinateFrameError, match="FRAME_ID_MISMATCH"):
        projected_to_local(
            (500_001, 1_000_001), _frame(),
            expected_frame_id="other-frame", expected_calculation_crs=CRS,
        )
    with pytest.raises(CoordinateFrameError, match="CALCULATION_CRS_MISMATCH"):
        projected_to_local(
            (500_001, 1_000_001), _frame(),
            expected_frame_id=FRAME_ID, expected_calculation_crs="EPSG:32643",
        )


def test_translation_overflow_is_rejected() -> None:
    frame = create_projected_metric_frame(FRAME_ID, CRS, -1e308, 0)
    with pytest.raises(CoordinateFrameError, match="COORDINATE_NON_FINITE"):
        projected_to_local(
            (1e308, 0), frame, expected_frame_id=FRAME_ID, expected_calculation_crs=CRS
        )


def _signed_area(points):
    return sum(
        points[index][0] * points[index + 1][1] - points[index + 1][0] * points[index][1]
        for index in range(len(points) - 1)
    ) / 2


def test_translation_preserves_ring_orientation_and_precision() -> None:
    frame = _frame()
    projected = [
        (500_000.25, 1_000_000.5), (500_010.25, 1_000_000.5),
        (500_010.25, 1_000_005.5), (500_000.25, 1_000_005.5),
        (500_000.25, 1_000_000.5),
    ]
    local = [projected_to_local(
        point, frame, expected_frame_id=FRAME_ID, expected_calculation_crs=CRS
    ) for point in projected]
    assert _signed_area(projected) > 0 and _signed_area(local) > 0
    assert local == [(0.0, 0.0), (10.0, 0.0), (10.0, 5.0), (0.0, 5.0), (0.0, 0.0)]


@pytest.mark.parametrize(
    "frame,expected_code",
    [
        (
            ProjectedMetricFrame(
                FRAME_ID, "EPSG:4326", 80.0, 7.0
            ),
            "CALCULATION_CRS_NOT_PROJECTED",
        ),
        (
            ProjectedMetricFrame(
                FRAME_ID, "EPSG:2263", 1.0, 1.0
            ),
            "CALCULATION_CRS_NOT_METRIC",
        ),
        (
            ProjectedMetricFrame(
                FRAME_ID, CRS, float("inf"), 0.0
            ),
            "COORDINATE_NON_FINITE",
        ),
        (
            ProjectedMetricFrame(
                FRAME_ID, CRS, True, 0.0
            ),
            "COORDINATE_INVALID",
        ),
        (
            ProjectedMetricFrame(
                " ", CRS, 0.0, 0.0
            ),
            "FRAME_ID_REQUIRED",
        ),
    ],
)
@pytest.mark.parametrize(
    "operation",
    [projected_to_local, local_to_projected],
)
def test_direct_constructor_cannot_bypass_frame_validation(
    frame, expected_code, operation
):
    with pytest.raises(CoordinateFrameError) as error:
        operation(
            (1.0, 1.0),
            frame,
            expected_frame_id=frame.frame_id,
            expected_calculation_crs=frame.calculation_crs,
        )
    assert error.value.code == expected_code


@pytest.mark.parametrize(
    "operation",
    [projected_to_local, local_to_projected],
)
def test_huge_integer_coordinate_returns_controlled_failure(operation):
    with pytest.raises(
        CoordinateFrameError,
        match="COORDINATE_NON_FINITE",
    ):
        operation(
            (10 ** 400, 0),
            _frame(),
            expected_frame_id=FRAME_ID,
            expected_calculation_crs=CRS,
        )


def test_huge_integer_origin_returns_controlled_failure():
    with pytest.raises(
        CoordinateFrameError,
        match="COORDINATE_NON_FINITE",
    ):
        create_projected_metric_frame(
            FRAME_ID, CRS, 10 ** 400, 0
        )

import pytest

from app.geometry.land.validation import (
    LandGeometryError,
    validate_land_boundary,
)


def test_valid_square_returns_area_and_perimeter():
    result = validate_land_boundary([
        [0, 0], [10, 0], [10, 10], [0, 10], [0, 0]
    ])

    assert result.area_m2 == 100
    assert result.perimeter_m == 40


def test_valid_square_with_hole():
    result = validate_land_boundary(
        [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]],
        [[[2, 2], [4, 2], [4, 4], [2, 4], [2, 2]]],
    )

    assert result.area_m2 == 96
    assert result.perimeter_m == 48


def test_unclosed_boundary_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary([
            [0, 0], [10, 0], [10, 10], [0, 10]
        ])

    assert error.value.code == "RING_NOT_CLOSED"


def test_self_intersecting_boundary_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary([
            [0, 0], [10, 10], [0, 10], [10, 0], [0, 0]
        ])

    assert error.value.code == "SELF_INTERSECTION"


def test_zero_area_boundary_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary([
            [0, 0], [2, 0], [4, 0], [0, 0]
        ])

    assert error.value.code in {
        "ZERO_OR_INVALID_AREA",
        "INVALID_POLYGON_TOPOLOGY",
        "SELF_INTERSECTION",
    }


def test_non_finite_coordinate_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary([
            [0, 0], [10, 0], [10, float("nan")],
            [0, 10], [0, 0]
        ])

    assert error.value.code == "NON_FINITE_COORDINATE"


def test_hole_outside_boundary_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary(
            [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]],
            [[[20, 20], [22, 20], [22, 22], [20, 22], [20, 20]]],
        )

    assert error.value.code == "INVALID_POLYGON_TOPOLOGY"





def test_duplicate_vertices_are_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary([
            [0, 0],
            [10, 0],
            [10, 0],
            [10, 10],
            [0, 10],
            [0, 0],
        ])

    assert error.value.code == "DUPLICATE_CONSECUTIVE_VERTEX"


def test_boolean_coordinate_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary([
            [0, 0], [True, 0], [10, 10], [0, 10], [0, 0]
        ])

    assert error.value.code == "INVALID_COORDINATE"


def test_coordinate_outside_float_range_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        validate_land_boundary([
            [0, 0],
            [10**1000, 0],
            [10**1000, 10],
            [0, 10],
            [0, 0],
        ])

    assert error.value.code == "INVALID_COORDINATE"


def _signed_ring_area(ring):
    return sum(
        ring[index][0] * ring[index + 1][1]
        - ring[index + 1][0] * ring[index][1]
        for index in range(len(ring) - 1)
    ) / 2


def test_exterior_ring_is_normalized_counter_clockwise():
    result = validate_land_boundary([
        [0, 0], [0, 10], [10, 10], [10, 0], [0, 0]
    ])

    assert _signed_ring_area(result.exterior) > 0


def test_interior_ring_is_normalized_clockwise():
    result = validate_land_boundary(
        [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]],
        [[[2, 2], [4, 2], [4, 4], [2, 4], [2, 2]]],
    )

    assert _signed_ring_area(result.holes[0]) < 0

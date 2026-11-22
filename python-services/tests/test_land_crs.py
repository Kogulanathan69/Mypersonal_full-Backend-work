import pytest

from app.geometry.land.crs import (
    _normalise_source_ring,
    _validate_geographic_coordinates,
    normalize_land_crs,
)
from app.geometry.land.validation import LandGeometryError


def test_missing_source_crs_is_rejected_before_pyproj_import():
    with pytest.raises(LandGeometryError) as error:
        normalize_land_crs(
            [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]],
            "",
            "EPSG:32644",
        )

    assert error.value.code == "SOURCE_CRS_REQUIRED"


def test_missing_calculation_crs_is_rejected_before_pyproj_import():
    with pytest.raises(LandGeometryError) as error:
        normalize_land_crs(
            [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]],
            "EPSG:4326",
            "",
        )

    assert error.value.code == "CALCULATION_CRS_REQUIRED"


def test_unclosed_source_ring_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        _normalise_source_ring([
            [0, 0], [10, 0], [10, 10], [0, 10]
        ])

    assert error.value.code == "SOURCE_RING_NOT_CLOSED"


def test_non_finite_source_coordinate_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        _normalise_source_ring([
            [0, 0], [10, 0], [10, float("nan")],
            [0, 10], [0, 0]
        ])

    assert error.value.code == "NON_FINITE_SOURCE_COORDINATE"


def test_boolean_source_coordinate_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        _normalise_source_ring([
            [0, 0], [True, 0], [10, 10], [0, 10], [0, 0]
        ])

    assert error.value.code == "INVALID_SOURCE_COORDINATE"


def test_invalid_holes_container_is_rejected_before_pyproj_import():
    with pytest.raises(LandGeometryError) as error:
        normalize_land_crs(
            [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]],
            "EPSG:4326",
            "EPSG:32644",
            holes="invalid",
        )

    assert error.value.code == "INVALID_HOLES"



def test_valid_geographic_coordinates_are_accepted():
    _validate_geographic_coordinates((
        ((79.8, 9.6), (79.9, 9.6), (79.9, 9.7), (79.8, 9.6)),
    ))


def test_longitude_outside_wgs84_range_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        _validate_geographic_coordinates((
            ((181, 9), (179, 9), (179, 10), (181, 9)),
        ))

    assert error.value.code == "SOURCE_LONGITUDE_OUT_OF_RANGE"


def test_latitude_outside_wgs84_range_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        _validate_geographic_coordinates((
            ((79, 91), (80, 91), (80, 90), (79, 91)),
        ))

    assert error.value.code == "SOURCE_LATITUDE_OUT_OF_RANGE"



def test_duplicate_consecutive_source_vertices_are_rejected():
    with pytest.raises(LandGeometryError) as error:
        _normalise_source_ring([
            [0, 0], [10, 0], [10, 0], [10, 10], [0, 0]
        ])

    assert error.value.code == "DUPLICATE_CONSECUTIVE_VERTEX"


def test_source_ring_with_insufficient_distinct_vertices_is_rejected():
    with pytest.raises(LandGeometryError) as error:
        _normalise_source_ring([
            [0, 0], [10, 0], [0, 0], [10, 0], [0, 0]
        ])

    assert error.value.code == "INSUFFICIENT_DISTINCT_VERTICES"


def test_source_ring_point_limit_is_enforced():
    ring = [[float(index), 0] for index in range(10001)]
    ring[-1] = ring[0]

    with pytest.raises(LandGeometryError) as error:
        _normalise_source_ring(ring)

    assert error.value.code == "RING_LIMIT_EXCEEDED"


def test_source_hole_limit_is_enforced_before_pyproj_import():
    with pytest.raises(LandGeometryError) as error:
        normalize_land_crs(
            [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]],
            "EPSG:4326",
            "EPSG:32644",
            holes=[[] for _ in range(101)],
        )

    assert error.value.code == "HOLE_LIMIT_EXCEEDED"


def test_normalize_land_crs_with_mocked_pyproj(monkeypatch):
    from types import SimpleNamespace
    import sys

    class FakeCRS:
        def __init__(self, name):
            self.name = name
            self.is_geographic = name == "EPSG:4326"
            self.is_projected = name == "EPSG:32644"
            unit = "degree" if self.is_geographic else "metre"
            factor = 0.017453292519943295 if self.is_geographic else 1.0
            self.axis_info = [
                SimpleNamespace(unit_name=unit, unit_conversion_factor=factor),
                SimpleNamespace(unit_name=unit, unit_conversion_factor=factor),
            ]

        @classmethod
        def from_user_input(cls, value):
            if value not in {"EPSG:4326", "EPSG:32644"}:
                raise ValueError("Unsupported CRS")
            return cls(value)

        @classmethod
        def from_epsg(cls, value):
            return cls(f"EPSG:{value}")

        def to_string(self):
            return self.name

    class FakeTransformer:
        @classmethod
        def from_crs(cls, source, target, **kwargs):
            assert kwargs == {
                "always_xy": True,
                "allow_ballpark": False,
                "only_best": True,
            }

            class Transformer:
                def transform(self, x, y, errcheck=False):
                    assert errcheck is True
                    if source.is_geographic:
                        return x * 1000, y * 1000
                    return x / 1000, y / 1000

            return Transformer()

    monkeypatch.setitem(
        sys.modules,
        "pyproj",
        SimpleNamespace(CRS=FakeCRS, Transformer=FakeTransformer),
    )

    result = normalize_land_crs(
        [[79.8, 9.6], [79.8, 9.61], [79.81, 9.61],
         [79.81, 9.6], [79.8, 9.6]],
        "EPSG:4326",
        "EPSG:32644",
    )

    assert result.source_crs == "EPSG:4326"
    assert result.source_exterior[0] == pytest.approx((79.8, 9.6))
    assert result.calculation_crs == "EPSG:32644"
    assert result.geometry_crs == "EPSG:4326"
    assert result.metric_geometry.area_m2 == pytest.approx(100)
    assert result.metric_geometry.perimeter_m == pytest.approx(40)

    def signed_area(ring):
        return sum(
            ring[index][0] * ring[index + 1][1]
            - ring[index + 1][0] * ring[index][1]
            for index in range(len(ring) - 1)
        ) / 2

    assert signed_area(result.metric_geometry.exterior) > 0
    assert result.geojson["type"] == "Polygon"
    assert result.geojson["coordinates"][0][0] == pytest.approx([79.8, 9.6])
    assert signed_area(result.geojson["coordinates"][0]) > 0

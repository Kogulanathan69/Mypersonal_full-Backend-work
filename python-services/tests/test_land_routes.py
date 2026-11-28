from fastapi import FastAPI
from fastapi.testclient import TestClient

from app.geometry.land.routes import router


app = FastAPI()
app.include_router(router)
client = TestClient(app)


def valid_payload() -> dict:
    return {
        "source_crs": "EPSG:4326",
        "calculation_crs": "EPSG:32644",
        "exterior": [
            [79.8, 9.6],
            [79.8, 9.61],
            [79.81, 9.61],
            [79.81, 9.6],
            [79.8, 9.6],
        ],
        "holes": [],
    }


def test_valid_boundary_returns_calculated_measurements() -> None:
    response = client.post(
        "/geometry/land/validate",
        json=valid_payload(),
    )

    assert response.status_code == 200

    body = response.json()
    assert body["validation_status"] == "VALID"
    assert body["measurements"]["area_m2"] > 0
    assert body["measurements"]["perimeter_m"] > 0
    assert body["legal_boundary_verified"] is False


def test_self_intersecting_boundary_returns_invalid_result() -> None:
    payload = valid_payload()
    payload["exterior"] = [
        [79.8, 9.6],
        [79.81, 9.61],
        [79.8, 9.61],
        [79.81, 9.6],
        [79.8, 9.6],
    ]

    response = client.post(
        "/geometry/land/validate",
        json=payload,
    )

    assert response.status_code == 200

    body = response.json()
    assert body["validation_status"] == "INVALID"
    assert body["diagnostic_code"]
    assert body["measurements"] is None
    assert body["legal_boundary_verified"] is False


def test_invalid_source_crs_returns_invalid_result() -> None:
    payload = valid_payload()
    payload["source_crs"] = "NOT_A_REAL_CRS"

    response = client.post(
        "/geometry/land/validate",
        json=payload,
    )

    assert response.status_code == 200

    body = response.json()
    assert body["validation_status"] == "INVALID"
    assert body["diagnostic_code"] == "UNSUPPORTED_OR_INVALID_CRS"
    assert body["measurements"] is None
    assert body["legal_boundary_verified"] is False


def test_invalid_calculation_crs_returns_invalid_result() -> None:
    payload = valid_payload()
    payload["calculation_crs"] = "NOT_A_REAL_CRS"

    response = client.post("/geometry/land/validate", json=payload)

    assert response.status_code == 200
    body = response.json()
    assert body["validation_status"] == "INVALID"
    assert body["diagnostic_code"] == "UNSUPPORTED_OR_INVALID_CRS"
    assert body["measurements"] is None
    assert body["legal_boundary_verified"] is False


def test_out_of_range_geographic_coordinate_returns_invalid_result() -> None:
    payload = valid_payload()
    payload["exterior"] = [
        [181.0, 9.6],
        [181.0, 9.61],
        [179.9, 9.61],
        [179.9, 9.6],
        [181.0, 9.6],
    ]

    response = client.post("/geometry/land/validate", json=payload)

    assert response.status_code == 200
    body = response.json()
    assert body["validation_status"] == "INVALID"
    assert body["diagnostic_code"] == "SOURCE_LONGITUDE_OUT_OF_RANGE"
    assert body["measurements"] is None
    assert body["legal_boundary_verified"] is False

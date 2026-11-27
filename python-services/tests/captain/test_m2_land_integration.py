import json

import pytest
from fastapi.testclient import TestClient

from app.internal.auth import AUTH_HEADER, InternalAuth
from app.main import create_app

TOKEN = "m2-integration-test-token-000000000001"
PATH = "/geometry/land/validate"
HEADERS = {AUTH_HEADER: TOKEN}

client = TestClient(
    create_app(InternalAuth.from_tokens([TOKEN])),
    raise_server_exceptions=False,
)


def payload():
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


def test_authenticated_valid_geometry():
    response = client.post(PATH, json=payload(), headers=HEADERS)
    assert response.status_code == 200
    result = response.json()
    assert result["validation_status"] == "VALID"
    assert result["measurements"]["area_m2"] > 0
    assert result["legal_boundary_verified"] is False


@pytest.mark.parametrize("headers", [
    {},
    {AUTH_HEADER: "invalid-token"},
])
def test_unauthenticated(headers):
    response = client.post(PATH, json=payload(), headers=headers)
    assert response.status_code == 401


def test_unconfigured_auth_fails_closed():
    c = TestClient(create_app(InternalAuth((), True)))
    assert c.post(PATH, json=payload()).status_code == 503


def test_duplicate_json_rejected():
    raw = json.dumps(payload()).replace(
        '"source_crs":',
        '"source_crs": "EPSG:4326", "source_crs":',
        1,
    )
    response = client.post(
        PATH,
        content=raw,
        headers={**HEADERS, "Content-Type": "application/json"},
    )
    assert response.status_code == 422


def test_oversized_body_rejected():
    response = client.post(
        PATH,
        content=b"x" * (2 * 1024 * 1024 + 1),
        headers={**HEADERS, "Content-Type": "application/json"},
    )
    assert response.status_code == 413


def test_streamed_oversized_body_rejected():
    def chunks():
        for _ in range(3):
            yield b"x" * (1024 * 1024)

    response = client.post(
        PATH,
        content=chunks(),
        headers={**HEADERS, "Content-Type": "application/json"},
    )
    assert response.status_code == 413


def test_wrong_content_type_rejected():
    response = client.post(
        PATH,
        content=json.dumps(payload()),
        headers={**HEADERS, "Content-Type": "text/plain"},
    )
    assert response.status_code == 415
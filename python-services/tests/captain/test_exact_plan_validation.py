"""BM-INT-01: exact geometry validation binding."""

import hashlib
import json

import pytest

from test_internal_api import (
    client, post, land, _generated_geometry, PROJECT, LAND,
)

ROUTE = "/internal/v2/plans/validate"


def digest(value):
    return "sha256:" + hashlib.sha256(value.encode("utf-8")).hexdigest()


def request_body():
    geometry_json = json.dumps(_generated_geometry(), ensure_ascii=False)
    return {
        "schemaVersion": "captain-plan-validation/2",
        "projectId": PROJECT,
        "landRevisionId": LAND,
        "land": land(),
        "geometryJson": geometry_json,
        "geometryHash": digest(geometry_json),
    }


def test_exact_geometry_is_validated_and_hash_returned():
    body = request_body()
    response = post(client(), ROUTE, body)
    assert response.status_code == 200
    result = response.json()
    assert result["schemaVersion"] == "captain-plan-validation/2"
    assert result["geometryValid"] is True
    assert result["geometryHash"] == body["geometryHash"]


@pytest.mark.parametrize("mode", [
    "missing_hash",
    "wrong_hash",
    "malformed_hash",
    "modified_geometry",
    "duplicate_property",
])
def test_invalid_geometry_binding_is_rejected(mode):
    body = request_body()

    if mode == "missing_hash":
        del body["geometryHash"]
    elif mode == "wrong_hash":
        body["geometryHash"] = "sha256:" + "0" * 64
    elif mode == "malformed_hash":
        body["geometryHash"] = "invalid"
    elif mode == "modified_geometry":
        body["geometryJson"] += " "
    elif mode == "duplicate_property":
        body["geometryJson"] = '{"schemaVersion":"invalid",' + body["geometryJson"][1:]
        body["geometryHash"] = digest(body["geometryJson"])

    response = post(client(), ROUTE, body)
    assert response.status_code == 422


def test_exact_representation_is_preserved():
    body = request_body()
    body["geometryJson"] = json.dumps(
        json.loads(body["geometryJson"]), indent=2
    )
    body["geometryHash"] = digest(body["geometryJson"])

    response = post(client(), ROUTE, body)
    assert response.status_code == 200
    assert response.json()["geometryHash"] == body["geometryHash"]


def test_v2_requires_authentication():
    response = post(client(), ROUTE, {}, token=None)
    assert response.status_code == 401
    assert response.json() == {"code": "UNAUTHENTICATED"}


def test_v2_rejects_ambiguous_geometry():
    body = request_body()
    body["geometry"] = None
    response = post(client(), ROUTE, body)
    assert response.status_code == 422


@pytest.mark.parametrize("raw", [
    '{"x":NaN}',
    '{"x":Infinity}',
    '{"x":-Infinity}',
    '{"x":1e999}',
    '{"outer":{"x":1,"x":2}}',
    '[' + '0,' * 10 + '0]',
    '{"x":' + '[' * 80 + '0' + ']' * 80 + '}',
])
def test_v2_rejects_invalid_inner_json(raw):
    body = request_body()
    body["geometryJson"] = raw
    body["geometryHash"] = digest(raw)

    response = post(client(), ROUTE, body)
    assert response.status_code == 422
    assert response.json() == {"code": "VALIDATION_FAILED"}


def test_v2_rejects_oversized_geometry():
    body = request_body()
    raw = body["geometryJson"]
    raw += " " * (1048577 - len(raw))
    body["geometryJson"] = raw
    body["geometryHash"] = digest(raw)

    response = post(client(), ROUTE, body)
    assert response.status_code == 422


def test_matching_hash_does_not_make_invalid_geometry_valid():
    body = request_body()
    geometry = json.loads(body["geometryJson"])
    geometry["floors"][0]["level"] = "0"

    body["geometryJson"] = json.dumps(geometry)
    body["geometryHash"] = digest(body["geometryJson"])

    response = post(client(), ROUTE, body)
    assert response.status_code in (200, 422)

    if response.status_code == 200:
        assert response.json()["geometryValid"] is False


@pytest.mark.parametrize("raw", [
    r'{"x":"\ud800"}',
    r'{"x":"\udfff"}',
    r'{"\ud800":"value"}',
    r'{"outer":{"x":"\ud800"}}',
])
def test_v2_rejects_unpaired_unicode_surrogate(raw):
    body = request_body()
    body["geometryJson"] = raw
    body["geometryHash"] = digest(raw)

    response = post(client(), ROUTE, body)

    assert response.status_code == 422
    assert response.json() == {"code": "VALIDATION_FAILED"}


@pytest.mark.parametrize("depth", [64, 65])
def test_exact_json_depth_boundary(depth):
    from app.internal.exact_plan_validation import (
        _check_json_depth,
        GeometryBindingError,
    )

    raw = '{"x":' + '[' * (depth - 1) + '0' + ']' * (depth - 1) + '}'

    if depth == 64:
        _check_json_depth(raw)
    else:
        with pytest.raises(GeometryBindingError):
            _check_json_depth(raw)


def test_json_depth_ignores_escaped_string_content():
    from app.internal.exact_plan_validation import _check_json_depth

    raw = json.dumps({
        "x": ('[{' * 100) + '"quoted"' + '\\' + ('}]' * 100)
    })

    _check_json_depth(raw)


def test_v2_rejects_multibyte_geometry_over_byte_limit():
    from app.internal.exact_plan_validation import (
        ExactPlanValidationRequest,
        GeometryBindingError,
        validate_exact_plan,
    )

    body = request_body()
    raw = json.dumps({"x": "\u00e9" * 524288}, ensure_ascii=False)

    assert len(raw) < 1048576
    assert len(raw.encode("utf-8")) > 1048576

    body["geometryJson"] = raw
    body["geometryHash"] = digest(raw)

    request = ExactPlanValidationRequest.model_validate_json(
        json.dumps(body), strict=True
    )

    with pytest.raises(GeometryBindingError):
        validate_exact_plan(request)



@pytest.mark.parametrize("ensure_ascii", [True, False])
def test_v2_preserves_valid_unicode_geometry(ensure_ascii):
    body = request_body()
    geometry = json.loads(body["geometryJson"])

    geometry["floors"][0]["rooms"][0]["name"] = (
        "\u0ba4\u0bae\u0bbf\u0bb4\u0bcd "
        "\u0dc3\u0dd2\u0d82\u0dc4\u0dbd "
        "\u00e9 \U0001f600"
    )

    raw = json.dumps(geometry, ensure_ascii=ensure_ascii)
    body["geometryJson"] = raw
    body["geometryHash"] = digest(raw)

    response = post(client(), ROUTE, body)

    assert response.status_code == 200
    result = response.json()
    assert result["geometryValid"] is True
    assert result["geometryHash"] == body["geometryHash"]
    assert result["validatorVersion"] == "captain-plan-validation/2"

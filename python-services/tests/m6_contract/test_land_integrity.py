from hashlib import sha256
import json
from uuid import UUID, uuid4

import pytest

from app.geometry.land_integrity import prepare_verified_land_geometry, verify_land_geometry_bytes


PAYLOAD = b'{"type":"Polygon","coordinates":[]}'
HASH = "sha256:" + sha256(PAYLOAD).hexdigest()


def verify(payload=PAYLOAD, expected_hash=HASH, limit=1024):
    return verify_land_geometry_bytes(
        payload,
        expected_hash,
        max_payload_bytes=limit,
    )


def test_exact_bytes_are_verified():
    result = verify()
    assert result.verified is True
    assert result.code == "LAND_GEOMETRY_BYTES_VERIFIED"


@pytest.mark.parametrize(
    "payload",
    [
        PAYLOAD + b" ",
        PAYLOAD + b"\\n",
        b'{"coordinates":[],"type":"Polygon"}',
        b'{"type": "Polygon","coordinates":[]}',
    ],
)
def test_no_json_normalization_or_reordering(payload):
    result = verify(payload)
    assert result.verified is False
    assert result.code == "LAND_GEOMETRY_HASH_MISMATCH"


@pytest.mark.parametrize(
    "expected_hash",
    [
        None,
        "",
        "b" * 64,
        "sha256:" + "A" * 64,
        "sha256:" + "g" * 64,
        "sha256:" + "b" * 63,
        HASH + "\\n",
    ],
)
def test_malformed_hash_is_rejected(expected_hash):
    assert verify(expected_hash=expected_hash).code == (
        "LAND_GEOMETRY_HASH_INVALID"
    )


@pytest.mark.parametrize(
    "payload",
    [None, b"", "geometry", bytearray(PAYLOAD)],
)
def test_payload_must_be_nonempty_immutable_bytes(payload):
    assert verify(payload=payload).code == (
        "LAND_GEOMETRY_BYTES_REQUIRED"
    )


@pytest.mark.parametrize("limit", [0, -1, True, None, 1.5])
def test_invalid_size_policy_is_rejected(limit):
    assert verify(limit=limit).code == "INVALID_SIZE_POLICY"


def test_exact_payload_limit_is_accepted():
    assert verify(limit=len(PAYLOAD)).verified


def test_payload_over_limit_is_rejected():
    assert verify(limit=len(PAYLOAD) - 1).code == (
        "LAND_GEOMETRY_TOO_LARGE"
    )


def test_invalid_utf8_is_rejected_even_with_matching_hash():
    payload = bytes([255, 254])
    expected = "sha256:" + sha256(payload).hexdigest()
    result = verify(payload, expected)
    assert not result.verified
    assert result.code == "LAND_GEOMETRY_UTF8_INVALID"


def test_unicode_exact_utf8_bytes():
    payload = '{"name":"\\u0ba8\\u0bbf\\u0bb2\\u0bae\\u0bcd"}'.encode("utf-8")
    expected = "sha256:" + sha256(payload).hexdigest()
    assert verify(payload, expected).verified


def test_hash_verification_does_not_claim_geometry_validity():
    payload = b"not a geometry document"
    expected = "sha256:" + sha256(payload).hexdigest()
    result = verify(payload, expected)
    assert result.verified
    assert result.code == "LAND_GEOMETRY_BYTES_VERIFIED"
    assert not hasattr(result, "geometry_valid")
    assert not hasattr(result, "authoritative")


PROJECT_ID = UUID("10000000-0000-4000-8000-000000000001")
REVISION_ID = UUID("10000000-0000-4000-8000-000000000002")
FRAME_ID = "synthetic-frame-v1"


def _land_payload(*, project_id=PROJECT_ID, revision_id=REVISION_ID, frame=FRAME_ID, geometry=None):
    document = {
        "projectId": str(project_id),
        "landRevisionId": str(revision_id),
        "coordinateFrameId": frame,
        "geometry": geometry or {
            "type": "Polygon",
            "coordinates": [[[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]]],
        },
    }
    return json.dumps(document, separators=(",", ":")).encode()


def _prepare(payload=None, *, project_id=PROJECT_ID, revision_id=REVISION_ID, frame=FRAME_ID):
    payload = payload or _land_payload()
    return prepare_verified_land_geometry(
        payload,
        "sha256:" + sha256(payload).hexdigest(),
        max_payload_bytes=4096,
        expected_project_id=project_id,
        expected_land_revision_id=revision_id,
        expected_coordinate_frame_id=frame,
    )


def test_verified_payload_is_the_only_source_of_parcel_geometry():
    payload = _land_payload()
    unrelated = {"type": "Polygon", "coordinates": [[[20, 20], [21, 20], [21, 21], [20, 20]]]}
    result = _prepare(payload)
    assert result.prepared
    assert result.land.parcel_geometry.bounds == (0.0, 0.0, 10.0, 10.0)
    assert unrelated not in result.land.__dict__.values()


@pytest.mark.parametrize(
    "expected",
    [
        {"project_id": uuid4()},
        {"revision_id": uuid4()},
        {"frame": "wrong-frame"},
    ],
)
def test_land_identity_binding_mismatch_is_rejected(expected):
    assert _prepare(**expected).code == "LAND_GEOMETRY_BINDING_MISMATCH"


def test_duplicate_json_property_is_rejected_after_hash_verification():
    payload = _land_payload()[:-1] + b',"projectId":"10000000-0000-4000-8000-000000000001"}'
    assert _prepare(payload).code == "LAND_GEOMETRY_JSON_INVALID"


@pytest.mark.parametrize(
    "geometry",
    [
        {"type": "MultiPolygon", "coordinates": []},
        {"type": "Polygon", "coordinates": [[[0, 0], [1, 1], [1, 0], [0, 1], [0, 0]]]},
        {"type": "Polygon", "coordinates": [[[0, 0, 1], [1, 0, 1], [0, 1, 1], [0, 0, 1]]]},
    ],
)
def test_unsupported_or_invalid_land_geometry_is_rejected(geometry):
    result = _prepare(_land_payload(geometry=geometry))
    assert not result.prepared


def test_nonfinite_land_coordinate_is_rejected():
    payload = _land_payload().replace(b"[10,0]", b"[1e400,0]", 1)
    assert _prepare(payload).code == "LAND_COORDINATE_INVALID"

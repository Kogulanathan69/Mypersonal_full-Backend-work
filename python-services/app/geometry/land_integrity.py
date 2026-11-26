"""Isolated exact-byte land geometry integrity verification.

The expected hash must come from an independently trusted land
revision provider. This module verifies byte integrity only.
It does not establish confirmation, ownership, revision authority,
coordinate-frame identity or geometric validity.
"""

from dataclasses import dataclass
from hashlib import sha256
from hmac import compare_digest
import json
from math import isfinite
import re
from typing import Any
from uuid import UUID


_HASH_PATTERN = re.compile(r"sha256:[0-9a-f]{64}")


@dataclass(frozen=True)
class LandIntegrityResult:
    verified: bool
    code: str


@dataclass(frozen=True)
class PreparedLandGeometry:
    """Provisional, unregistered parcel decoded from the verified bytes."""

    project_id: UUID
    land_revision_id: UUID
    coordinate_frame_id: str
    geometry_hash: str
    parcel_geometry: object


@dataclass(frozen=True)
class LandGeometryPreparationResult:
    prepared: bool
    code: str
    land: PreparedLandGeometry | None = None


class _DuplicateProperty(ValueError):
    pass


def _strict_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateProperty(key)
        result[key] = value
    return result


def _reject_constant(_: str) -> None:
    raise ValueError("non-finite JSON number")


def verify_land_geometry_bytes(
    payload: bytes,
    expected_hash: str,
    *,
    max_payload_bytes: int,
) -> LandIntegrityResult:
    """Verify exact UTF-8 bytes without normalizing JSON."""

    if (
        isinstance(max_payload_bytes, bool)
        or not isinstance(max_payload_bytes, int)
        or max_payload_bytes <= 0
    ):
        return LandIntegrityResult(False, "INVALID_SIZE_POLICY")

    if not isinstance(payload, bytes) or not payload:
        return LandIntegrityResult(False, "LAND_GEOMETRY_BYTES_REQUIRED")

    if len(payload) > max_payload_bytes:
        return LandIntegrityResult(False, "LAND_GEOMETRY_TOO_LARGE")

    if (
        not isinstance(expected_hash, str)
        or _HASH_PATTERN.fullmatch(expected_hash) is None
    ):
        return LandIntegrityResult(False, "LAND_GEOMETRY_HASH_INVALID")

    try:
        payload.decode("utf-8", errors="strict")
    except UnicodeDecodeError:
        return LandIntegrityResult(False, "LAND_GEOMETRY_UTF8_INVALID")

    actual_hash = "sha256:" + sha256(payload).hexdigest()

    if not compare_digest(actual_hash, expected_hash):
        return LandIntegrityResult(False, "LAND_GEOMETRY_HASH_MISMATCH")

    return LandIntegrityResult(True, "LAND_GEOMETRY_BYTES_VERIFIED")


def prepare_verified_land_geometry(
    payload: bytes,
    expected_hash: str,
    *,
    max_payload_bytes: int,
    expected_project_id: UUID,
    expected_land_revision_id: UUID,
    expected_coordinate_frame_id: str,
) -> LandGeometryPreparationResult:
    """Verify, strictly decode and bind a provisional parcel payload.

    This is deliberately not an M2 serialization contract and is not mounted
    in a production composition root.  The parcel returned here is always
    constructed from ``payload`` after its exact bytes have been verified.
    """

    integrity = verify_land_geometry_bytes(
        payload, expected_hash, max_payload_bytes=max_payload_bytes
    )
    if not integrity.verified:
        return LandGeometryPreparationResult(False, integrity.code)

    if (
        not isinstance(expected_project_id, UUID)
        or not isinstance(expected_land_revision_id, UUID)
        or not isinstance(expected_coordinate_frame_id, str)
        or not expected_coordinate_frame_id.strip()
        or len(expected_coordinate_frame_id) > 128
    ):
        return LandGeometryPreparationResult(False, "LAND_GEOMETRY_EXPECTATION_INVALID")

    try:
        document = json.loads(
            payload.decode("utf-8", errors="strict"),
            object_pairs_hook=_strict_object,
            parse_constant=_reject_constant,
        )
    except (_DuplicateProperty, UnicodeDecodeError, json.JSONDecodeError, ValueError, TypeError):
        return LandGeometryPreparationResult(False, "LAND_GEOMETRY_JSON_INVALID")

    if not isinstance(document, dict) or set(document) != {
        "projectId", "landRevisionId", "coordinateFrameId", "geometry"
    }:
        return LandGeometryPreparationResult(False, "LAND_GEOMETRY_CONTRACT_INVALID")

    try:
        project_id = UUID(document["projectId"])
        revision_id = UUID(document["landRevisionId"])
    except (ValueError, TypeError, AttributeError):
        return LandGeometryPreparationResult(False, "LAND_GEOMETRY_IDENTITY_INVALID")

    frame_id = document["coordinateFrameId"]
    if (
        project_id != expected_project_id
        or revision_id != expected_land_revision_id
        or frame_id != expected_coordinate_frame_id
    ):
        return LandGeometryPreparationResult(False, "LAND_GEOMETRY_BINDING_MISMATCH")

    geometry = document["geometry"]
    if not isinstance(geometry, dict) or set(geometry) != {"type", "coordinates"}:
        return LandGeometryPreparationResult(False, "LAND_GEOMETRY_CONTRACT_INVALID")
    if geometry["type"] != "Polygon" or not isinstance(geometry["coordinates"], list):
        return LandGeometryPreparationResult(False, "LAND_GEOMETRY_UNSUPPORTED")

    try:
        from shapely.geometry import Polygon
        from shapely.errors import ShapelyError

        rings = geometry["coordinates"]
        if not rings or any(not isinstance(ring, list) for ring in rings):
            return LandGeometryPreparationResult(False, "LAND_POLYGON_INVALID")
        for ring in rings:
            if len(ring) < 4 or ring[0] != ring[-1]:
                return LandGeometryPreparationResult(False, "LAND_POLYGON_INVALID")
            for point in ring:
                if (
                    not isinstance(point, list)
                    or len(point) != 2
                    or any(
                        isinstance(value, bool) or not isinstance(value, (int, float))
                        or not isfinite(value)
                        for value in point
                    )
                ):
                    return LandGeometryPreparationResult(False, "LAND_COORDINATE_INVALID")
        parcel = Polygon(rings[0], rings[1:])
        if (
            parcel.is_empty
            or parcel.has_z
            or getattr(parcel, "has_m", False)
            or not parcel.is_valid
            or not parcel.area > 0
        ):
            return LandGeometryPreparationResult(False, "LAND_POLYGON_INVALID")
    except (ImportError, ShapelyError, ArithmeticError, ValueError, TypeError, OverflowError):
        return LandGeometryPreparationResult(False, "LAND_POLYGON_INVALID")

    return LandGeometryPreparationResult(
        True,
        "LAND_GEOMETRY_PREPARED_PROVISIONALLY",
        PreparedLandGeometry(project_id, revision_id, frame_id, expected_hash, parcel),
    )

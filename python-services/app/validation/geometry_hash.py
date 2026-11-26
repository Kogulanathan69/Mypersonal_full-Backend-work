"""Exact M3 geometry-byte SHA-256 compatibility.

This is deliberately not BM-JCS-1 and performs no JSON normalization.  The
UTF-8 bytes of the supplied geometry JSON string are the protected value.
Whitespace, member order and numeric spelling therefore remain significant.
"""

import hashlib
import hmac

PREFIX = "sha256:"
DIGEST_LENGTH = len(PREFIX) + 64


def compute_geometry_hash(geometry_json: str) -> str:
    if not isinstance(geometry_json, str):
        raise TypeError("geometry_json must be a string")
    return PREFIX + hashlib.sha256(geometry_json.encode("utf-8", errors="strict")).hexdigest()


def is_geometry_hash(value: object) -> bool:
    if not isinstance(value, str) or len(value) != DIGEST_LENGTH or not value.startswith(PREFIX):
        return False
    return all(character in "0123456789abcdef" for character in value[len(PREFIX):])


def matches_geometry_hash(geometry_json: str, expected: str) -> bool:
    return is_geometry_hash(expected) and hmac.compare_digest(
        compute_geometry_hash(geometry_json), expected
    )

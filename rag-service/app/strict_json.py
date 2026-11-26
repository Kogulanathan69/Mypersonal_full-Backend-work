"""Strict raw-JSON request parsing (M7 correction, M6 review): validation BEFORE model deserialization.

Rejects, without echoing input:
- bodies over the size limit, including chunked bodies without Content-Length (counted while streaming) -> 413;
- a non-JSON content type -> 415;
- malformed JSON, invalid UTF-8, duplicate object keys at any depth, NaN/Infinity, a non-object root, or nesting
  deeper than MAX_DEPTH -> 400 MALFORMED_REQUEST.
Schema/contract violations are left to the Pydantic models (extra="forbid") -> 422 VALIDATION_FAILED.
"""
import json
from typing import Any

from starlette.requests import Request

MAX_BODY_BYTES = 16 * 1024
MAX_DEPTH = 32


class RequestRejected(Exception):
    def __init__(self, status: int, code: str):
        super().__init__(code)
        self.status = status
        self.code = code


def _no_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate key")  # the key itself is never reported
        result[key] = value
    return result


def _reject_constant(_: str) -> None:
    raise ValueError("non-finite number")


def _depth(value: Any, level: int = 1) -> int:
    if level > MAX_DEPTH:
        return level
    if isinstance(value, dict):
        return max([level] + [_depth(v, level + 1) for v in value.values()])
    if isinstance(value, list):
        return max([level] + [_depth(v, level + 1) for v in value])
    return level


async def read_json_object(request: Request) -> dict[str, Any]:
    content_type = request.headers.get("content-type", "").split(";")[0].strip().lower()
    if content_type != "application/json":
        raise RequestRejected(415, "UNSUPPORTED_MEDIA_TYPE")

    chunks: list[bytes] = []
    size = 0
    async for chunk in request.stream():
        size += len(chunk)
        if size > MAX_BODY_BYTES:
            raise RequestRejected(413, "PAYLOAD_TOO_LARGE")
        chunks.append(chunk)

    try:
        text = b"".join(chunks).decode("utf-8")
        value = json.loads(text, object_pairs_hook=_no_duplicates, parse_constant=_reject_constant)
    except (UnicodeDecodeError, ValueError, RecursionError):
        raise RequestRejected(400, "MALFORMED_REQUEST") from None

    if not isinstance(value, dict) or _depth(value) > MAX_DEPTH:
        raise RequestRejected(400, "MALFORMED_REQUEST")
    return value

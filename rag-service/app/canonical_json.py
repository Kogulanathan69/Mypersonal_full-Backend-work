"""BuildMind canonical JSON v1 ("BM-JCS-1"): RFC 8785 (JCS) restricted to a number domain where Python and .NET agree.

Shared with the C# implementation (BuildMind.Infrastructure.Integration.CanonicalJson); both are checked against the
same vectors in rag-service/contract/canonical_json_vectors.v1.json.

Rules:
- Objects: keys sorted by UTF-16 code units (RFC 8785), no whitespace, no duplicate keys.
- Strings: UTF-8; escape only '"', '\\' and U+0000..U+001F (\\b \\t \\n \\f \\r short forms, otherwise \\u00xx
  lowercase).
- Literals: true, false, null.
- Numbers (restricted, deterministic in both languages): finite only; integral values with |x| < 1e15 as plain
  integers ("3", "-0" becomes "0"); non-integral values with 1e-4 <= |x| < 1e15 as the shortest round-trip decimal
  ("0.5", "12.25"). Any other number is REJECTED, so the two implementations can never silently diverge.
"""
import math
from typing import Any

ALGORITHM = "SHA-256/BM-JCS-1"
_MAX = 1e15
_MIN_FRACTION = 1e-4
_SHORT_ESCAPES = {'"': '\\"', "\\": "\\\\", "\b": "\\b", "\t": "\\t", "\n": "\\n", "\f": "\\f", "\r": "\\r"}


class CanonicalizationError(ValueError):
    pass


def _number(value: float | int) -> str:
    if isinstance(value, bool):
        raise CanonicalizationError("boolean is not a number")
    x = float(value)
    if not math.isfinite(x) or abs(x) >= _MAX:
        raise CanonicalizationError("number outside the canonical domain")
    if x == int(x):
        return str(int(x))  # also maps -0.0 to "0"
    if abs(x) < _MIN_FRACTION:
        raise CanonicalizationError("number outside the canonical domain")
    text = repr(x)
    if "e" in text or "E" in text:
        raise CanonicalizationError("number outside the canonical domain")
    return text


def _string(value: str) -> str:
    out = ['"']
    for ch in value:
        if ch in _SHORT_ESCAPES:
            out.append(_SHORT_ESCAPES[ch])
        elif ord(ch) < 0x20:
            out.append(f"\\u{ord(ch):04x}")
        elif 0xD800 <= ord(ch) <= 0xDFFF:
            raise CanonicalizationError("lone surrogate")
        else:
            out.append(ch)
    out.append('"')
    return "".join(out)


def _utf16_key(key: str) -> list[int]:
    data = key.encode("utf-16-be")
    return [int.from_bytes(data[i:i + 2], "big") for i in range(0, len(data), 2)]


def canonicalize(value: Any) -> str:
    if value is None:
        return "null"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if isinstance(value, (int, float)):
        return _number(value)
    if isinstance(value, str):
        return _string(value)
    if isinstance(value, (list, tuple)):
        return "[" + ",".join(canonicalize(v) for v in value) + "]"
    if isinstance(value, dict):
        if not all(isinstance(k, str) for k in value):
            raise CanonicalizationError("object keys must be strings")
        items = sorted(value.items(), key=lambda kv: _utf16_key(kv[0]))
        return "{" + ",".join(_string(k) + ":" + canonicalize(v) for k, v in items) + "}"
    raise CanonicalizationError(f"unsupported type {type(value).__name__}")


def digest(value: Any) -> str:
    import hashlib
    return hashlib.sha256(canonicalize(value).encode("utf-8")).hexdigest()

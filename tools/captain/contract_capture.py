"""Live contract capture for the smoke tools: structure only, never values.

When BM_CAPTURE=<file.jsonl> is set, every smoke-tool HTTP call appends one line:
  {"method", "path" (concrete, query stripped), "status", "contentType", "request": <schema|null>, "response": <schema|null>}
Schemas are inferred JSON-Schema fragments (type, properties, required, items, format). String VALUES, tokens, ids,
emails and passwords are never written, only their type and a coarse format (uuid, date-time, email, uri, sha256).
tools/captain/openapi_enrich.py merges captures into the OpenAPI document and the API acceptance matrix.
"""
import json
import os
import re

_FORMATS = [
    ("uuid", re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")),
    ("date-time", re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})?$")),
    ("date", re.compile(r"^\d{4}-\d{2}-\d{2}$")),
    ("email", re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")),
    ("uri", re.compile(r"^(https?://|/api/)")),
]


def infer(value, depth=0):
    if depth > 12:
        return {}
    if value is None:
        return {"type": "null"}
    if isinstance(value, bool):
        return {"type": "boolean"}
    if isinstance(value, int):
        return {"type": "integer"}
    if isinstance(value, float):
        return {"type": "number"}
    if isinstance(value, str):
        for name, pattern in _FORMATS:
            if pattern.match(value):
                return {"type": "string", "format": name}
        return {"type": "string"}
    if isinstance(value, list):
        items = None
        for element in value[:50]:
            items = merge(items, infer(element, depth + 1))
        return {"type": "array", "items": items or {}}
    if isinstance(value, dict):
        return {"type": "object", "properties": {k: infer(v, depth + 1) for k, v in value.items()}, "required": sorted(value)}
    return {}


def _types(schema):
    t = schema.get("type")
    return set(t) if isinstance(t, list) else ({t} if t else set())


def merge(a, b):
    """Union of two inferred schemas (properties unioned, required intersected, types unioned)."""
    if a is None:
        return b
    if b is None:
        return a
    types = _types(a) | _types(b)
    out = {}
    if types:
        out["type"] = sorted(types)[0] if len(types) == 1 else sorted(types)
    if "object" in types:
        pa, pb = a.get("properties", {}), b.get("properties", {})
        out["properties"] = {k: merge(pa.get(k), pb.get(k)) for k in sorted(set(pa) | set(pb))}
        ra = set(a.get("required", pa)) if "object" in _types(a) else None
        rb = set(b.get("required", pb)) if "object" in _types(b) else None
        req = ra & rb if ra is not None and rb is not None else (ra if rb is None else rb)
        out["required"] = sorted(req or [])
    if "array" in types:
        out["items"] = merge(a.get("items"), b.get("items")) or {}
    fa, fb = a.get("format"), b.get("format")
    if fa and fa == fb:
        out["format"] = fa
    return out


def record(method, path, status, request_body, response_payload, content_type):
    target = os.environ.get("BM_CAPTURE")
    if not target:
        return
    if isinstance(response_payload, (bytes, bytearray)):
        response = {"type": "string", "format": "binary"} if response_payload else None
    else:
        response = infer(response_payload) if response_payload is not None else None
    line = {"method": method, "path": path.split("?", 1)[0], "status": status, "contentType": (content_type or "").split(";")[0],
            "request": infer(request_body) if request_body is not None else None, "response": response}
    with open(target, "a", encoding="utf-8") as stream:
        stream.write(json.dumps(line, separators=(",", ":")) + "\n")

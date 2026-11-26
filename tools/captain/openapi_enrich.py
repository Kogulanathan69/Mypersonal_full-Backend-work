"""Merges live contract captures (contract_capture.py) into the exported OpenAPI document and writes the
endpoint-by-endpoint acceptance matrix from the same runtime evidence.

Usage: python3 openapi_enrich.py <openapi.json> <out-openapi.json> <out-matrix.md> <capture.jsonl>...

- Response schemas: for each (operation, status) observed live, the merged inferred schema of the actual bodies is set
  as the response content (x-buildmind-schema-source: observed-live). Problem responses use ProblemDetails.
  Declared (code-generated) schemas are never replaced.
- Request schemas: only from calls that succeeded (2xx), so deliberately invalid negative-test bodies never shape the
  contract; set only where the document declares no request body.
- Acceptance: per operation, the statuses observed against the running API, split into positive (2xx/3xx), negative
  (4xx) and authentication (401 without a token) evidence. Nothing here comes from static route matching.
"""
import json
import re
import sys

sys.path.insert(0, __file__.rsplit("/", 1)[0] if "/" in __file__ else ".")
from contract_capture import merge  # noqa: E402

METHODS = ("get", "post", "put", "patch", "delete")
PROBLEM = {
    "type": "object", "additionalProperties": True,
    "required": ["status", "code"],
    "properties": {"type": {"type": "string"}, "title": {"type": "string"}, "status": {"type": "integer"},
                   "detail": {"type": "string"}, "instance": {"type": "string"}, "code": {"type": "string"},
                   "traceId": {"type": "string"},
                   "errors": {"type": "object", "additionalProperties": {"type": "array", "items": {"type": "string"}}}},
}


def template_regex(path):
    return re.compile("^" + re.sub(r"\\\{[^}]*\\\}", "[^/]+", re.escape(path)) + "$")


def to_openapi(schema):
    """Inferred schema -> OpenAPI 3.0 schema (type lists become nullable / oneOf)."""
    if not schema:
        return {}
    out = {}
    types = schema.get("type")
    types = types if isinstance(types, list) else ([types] if types else [])
    nullable = "null" in types
    types = [t for t in types if t != "null"]
    if len(types) > 1:
        out["oneOf"] = [to_openapi({**schema, "type": t}) for t in types]
    elif types:
        out["type"] = types[0]
    if nullable:
        out["nullable"] = True
    if "object" in types and len(types) == 1:
        out["properties"] = {k: to_openapi(v) for k, v in schema.get("properties", {}).items()}
        if schema.get("required"):
            out["required"] = schema["required"]
    if "array" in types and len(types) == 1:
        out["items"] = to_openapi(schema.get("items") or {})
    if schema.get("format") and len(types) == 1 and types[0] == "string":
        out["format"] = schema["format"]
    return out


def main(src, out_doc, out_md, captures):
    doc = json.load(open(src, encoding="utf-8"))
    ops = []
    for path, item in doc["paths"].items():
        for method in METHODS:
            if method in item:
                ops.append((template_regex(path), path.count("{"), path, method))
    ops.sort(key=lambda o: o[1])  # literal segments win over parameters
    evidence, unmatched = {}, 0
    for capture in captures:
        for line in open(capture, encoding="utf-8"):
            r = json.loads(line)
            match = next((o for o in ops if o[3] == r["method"].lower() and o[0].match(r["path"])), None)
            if match is None:
                unmatched += 1
                continue
            e = evidence.setdefault((match[2], match[3]), {"statuses": {}, "responses": {}, "request": None, "media": {}})
            status = str(r["status"])
            e["statuses"][status] = e["statuses"].get(status, 0) + 1
            if r["response"] is not None:
                e["responses"][status] = merge(e["responses"].get(status), r["response"])
                e["media"][status] = r["contentType"] or "application/json"
            if r["request"] is not None and 200 <= r["status"] < 300:
                e["request"] = merge(e["request"], r["request"])

    doc.setdefault("components", {}).setdefault("schemas", {}).setdefault("ProblemDetails", PROBLEM)
    enriched_responses = enriched_requests = 0
    for (path, method), e in evidence.items():
        op = doc["paths"][path][method]
        responses = op.setdefault("responses", {})
        # Preserve observed bodyless success statuses without inventing schemas.
        for status in e["statuses"]:
            if status.startswith("2") and status not in e["responses"]:
                responses.setdefault(status, {
                    "description": f"Observed live ({status})",
                    "x-buildmind-status-source": "observed-live",
                })
        for status, schema in e["responses"].items():
            media = e["media"][status]
            entry = responses.setdefault(status, {"description": f"Observed live ({status})"})
            content = entry.setdefault("content", {})
            if any(c.get("schema") for c in content.values()):
                continue  # declared in code: keep
            if "problem" in media or (status.startswith(("4", "5")) and "json" in media):
                content.clear()
                content["application/problem+json"] = {"schema": {"$ref": "#/components/schemas/ProblemDetails"}}
            elif "pdf" in media or (schema.get("format") == "binary"):
                content.clear()
                content[media or "application/octet-stream"] = {"schema": {"type": "string", "format": "binary"}}
            else:
                content.clear()
                content["application/json"] = {"schema": to_openapi(schema)}
                entry["x-buildmind-schema-source"] = "observed-live"
                enriched_responses += 1
        if e["request"] is not None and not (op.get("requestBody") or {}).get("content"):
            op["requestBody"] = {"required": True, "x-buildmind-schema-source": "observed-live",
                                 "content": {"application/json": {"schema": to_openapi(e["request"])}}}
            enriched_requests += 1
    json.dump(doc, open(out_doc, "w", encoding="utf-8", newline="\n"), indent=2)

    rows, live, neg, auth = [], 0, 0, 0
    for path in sorted(doc["paths"]):
        for method in METHODS:
            op = doc["paths"][path].get(method)
            if op is None:
                continue
            e = evidence.get((path, method), {"statuses": {}})
            st = e["statuses"]
            positive = sorted(s for s in st if s[0] in "23")
            negative = sorted(s for s in st if s[0] == "4")
            secured = bool(op.get("security", doc.get("security")))
            authenticated = "401" in st or not secured
            verdict = "LIVE_VERIFIED" if positive else ("NEGATIVE_ONLY" if negative else "NOT_EXERCISED")
            live += verdict == "LIVE_VERIFIED"
            neg += bool(negative)
            auth += "401" in st
            rows.append(f"| {method.upper()} | `{path}` | {(op.get('tags') or ['-'])[0]} | {'bearer' if secured else 'anonymous'} "
                        f"| {', '.join(positive) or '-'} | {', '.join(negative) or '-'} | {'401 observed' if '401' in st else ('n/a' if not secured else '-')} "
                        f"| {sum(st.values())} | {verdict} |")
    total = len(rows)
    head = [
        "# Final API acceptance matrix (live runtime evidence)",
        "",
        "Generated by `tools/captain/openapi_enrich.py` from live contract captures of the smoke tools run against the "
        "running API (`BM_CAPTURE`). Each row lists the HTTP statuses actually returned for that operation. Static route "
        "matching is not used.",
        "",
        f"**{total} operations: {live} LIVE_VERIFIED (a successful live call observed), {neg} with negative-path evidence "
        f"(4xx observed), {auth} with an observed 401 for a missing/invalid token.** Unmatched captured calls: {unmatched}.",
        "",
        "Positive = 2xx/3xx observed; Negative = 4xx observed (validation, ownership 404, conflict 409, rate 429, ...); "
        "Auth = 401 observed for that operation. Authorization (owner/role) evidence is in the journey check names.",
        "",
        "| Method | Path | Tag | Security | Positive | Negative | Auth | Calls | Result |",
        "|---|---|---|---|---|---|---|---|---|",
    ]
    open(out_md, "w", encoding="utf-8", newline="\n").write("\n".join(head + rows) + "\n")
    print(f"operations={total} live_verified={live} negative={neg} auth401={auth} enriched_responses={enriched_responses} "
          f"enriched_requests={enriched_requests} unmatched={unmatched}")


if __name__ == "__main__":
    if len(sys.argv) < 5:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4:])

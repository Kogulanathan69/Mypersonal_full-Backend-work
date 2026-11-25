"""Generates TypeScript contract declarations from the exported OpenAPI document (no third-party dependencies).

Usage: python3 openapi_to_ts.py <openapi.json> <out.d.ts>
Emits: one interface per components.schemas entry, and an `ApiOperations` map keyed by "METHOD /path" with typed
path/query parameters, the request body type and the success response type. Bodies the API does not declare in
OpenAPI are typed `unknown` (reported in the summary line) rather than guessed.
"""
import json
import re
import sys

PRIMITIVES = {"string": "string", "integer": "number", "number": "number", "boolean": "boolean"}


def ts_type(schema, schemas):
    if not schema:
        return "unknown"
    if schema.get("x-binary"):
        return "Blob"
    if "$ref" in schema:
        return schema["$ref"].rsplit("/", 1)[-1]
    if "oneOf" in schema or "anyOf" in schema:
        base = " | ".join(ts_type(s, schemas) for s in schema.get("oneOf") or schema.get("anyOf"))
        return f"({base} | null)" if schema.get("nullable") else f"({base})"
    if "enum" in schema:
        base = " | ".join(json.dumps(v) for v in schema["enum"])
    elif schema.get("type") == "array":
        base = f"Array<{ts_type(schema.get('items'), schemas)}>"
    elif schema.get("type") == "object" or "properties" in schema:
        props = schema.get("properties", {})
        if not props:
            extra = schema.get("additionalProperties")
            base = f"Record<string, {ts_type(extra, schemas) if isinstance(extra, dict) else 'unknown'}>"
        else:
            required = set(schema.get("required", []))
            fields = [f"{json.dumps(k)}{'' if k in required else '?'}: {ts_type(v, schemas)}" for k, v in props.items()]
            base = "{ " + "; ".join(fields) + " }"
    else:
        base = PRIMITIVES.get(schema.get("type"), "unknown")
    return f"({base} | null)" if schema.get("nullable") else base


def json_body(container):
    content = (container or {}).get("content", {})
    for media in ("application/json", "text/json", "application/problem+json"):
        if media in content:
            return content[media].get("schema")
    for media, entry in content.items():  # binary downloads (e.g. application/pdf)
        if (entry.get("schema") or {}).get("format") == "binary":
            return {"x-binary": True}
    return None


def main(src, out):
    doc = json.load(open(src, encoding="utf-8"))
    schemas = doc.get("components", {}).get("schemas", {})
    lines = ["// GENERATED from docs/captain-development/openapi.captain-dev.json by tools/captain/openapi_to_ts.py.",
             "// Do not edit. Bodies not declared in OpenAPI are `unknown`; see FINAL-API-ENDPOINT-INVENTORY.md.", ""]
    for name, schema in sorted(schemas.items()):
        lines.append(f"export type {name} = {ts_type(schema, schemas)};")
    if "ProblemDetails" not in schemas:
        lines += ["", "export interface ProblemDetails { type?: string; title?: string; status?: number; detail?: string;"
                  " instance?: string; code?: string; traceId?: string; errors?: Record<string, string[]> }"]
    lines += ["", "export interface ApiOperations {"]
    total = typed_req = typed_res = 0
    for path, item in sorted(doc.get("paths", {}).items()):
        shared = item.get("parameters", [])
        for method, op in sorted(item.items()):
            if method not in ("get", "post", "put", "patch", "delete"):
                continue
            total += 1
            params = shared + op.get("parameters", [])
            path_params = [p for p in params if p.get("in") == "path"]
            query = [p for p in params if p.get("in") == "query"]
            req = json_body(op.get("requestBody"))
            ok = next((json_body(r) for code, r in sorted(op.get("responses", {}).items())
                       if code.startswith("2") and json_body(r) is not None), None)
            no_content = ok is None and any(code == "204" for code in op.get("responses", {}))
            typed_req += req is not None
            typed_res += ok is not None
            pp = "{ " + "; ".join(f"{json.dumps(p['name'])}: string" for p in path_params) + " }" if path_params else "{}"
            qp = "{ " + "; ".join(f"{json.dumps(p['name'])}{'' if p.get('required') else '?'}: {ts_type(p.get('schema'), schemas)}"
                                  for p in query) + " }" if query else "{}"
            body = ts_type(req, schemas) if req is not None else ("unknown" if op.get("requestBody") else "never")
            lines.append(f"  {json.dumps(method.upper() + ' ' + path)}: {{ path: {pp}; query: {qp}; body: {body}; "
                         f"response: {ts_type(ok, schemas) if ok is not None else ('void' if no_content else 'unknown')} }};")
    lines += ["}", "", "export type OperationKey = keyof ApiOperations;", ""]
    open(out, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    print(f"operations={total} schemas={len(schemas)} declared_request_bodies={typed_req} declared_success_bodies={typed_res}")


if __name__ == "__main__":
    if len(sys.argv) != 3 or not re.search(r"\.json$", sys.argv[1]):
        sys.exit("usage: openapi_to_ts.py <openapi.json> <out.d.ts>")
    main(sys.argv[1], sys.argv[2])

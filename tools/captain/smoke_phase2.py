"""Captain live smoke (Phase 2): API + MySQL readiness, auth, projects, requirements, surveys, cross-user denial.

Synthetic users only; tokens and passwords are never printed. Exit code 0 only when every check passes.
Usage: python3 smoke_phase2.py [base_url]
"""
import json
import secrets
import sys
import urllib.error
import urllib.request
import uuid

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:5180"
results = []


def call(method, path, body=None, token=None, raw=None, content_type=None):
    data = None
    headers = {}
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    if raw is not None:
        data, headers["Content-Type"] = raw, content_type
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            text = response.read().decode() or "null"
            return response.status, json.loads(text) if text.strip()[:1] in "{[n" else text
    except urllib.error.HTTPError as error:
        text = error.read().decode()
        try:
            return error.code, json.loads(text) if text else None
        except json.JSONDecodeError:
            return error.code, text


def check(name, condition, detail=""):
    results.append((name, bool(condition)))
    print(("PASS " if condition else "FAIL ") + name + (f"  [{detail}]" if detail and not condition else ""))


def user():
    email = f"captain-{uuid.uuid4().hex[:12]}@example.invalid"
    password = "Synthetic-" + secrets.token_urlsafe(18) + "9a!"
    status, _ = call("POST", "/api/v1/auth/register", {"email": email, "password": password})
    check("register " + email.split("@")[0][:14], status in (200, 201), status)
    status, body = call("POST", "/api/v1/auth/login", {"email": email, "password": password})
    check("login", status == 200 and isinstance(body, dict) and body.get("accessToken"), status)
    return body


def multipart(field, filename, content_type, payload):
    boundary = "----captain" + uuid.uuid4().hex
    parts = (
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"{field}\"; filename=\"{filename}\"\r\n"
        f"Content-Type: {content_type}\r\n\r\n"
    ).encode() + payload + f"\r\n--{boundary}--\r\n".encode()
    return parts, "multipart/form-data; boundary=" + boundary


def main():
    status, body = call("GET", "/health")
    check("liveness /health", status == 200, status)
    status, body = call("GET", "/health/ready")
    print("     readiness body: " + json.dumps(body))
    check("readiness /health/ready mysql Healthy", status == 200 and "Healthy" in json.dumps(body), body)

    alice = user()
    bob = user()
    a, b = alice["accessToken"], bob["accessToken"]

    status, _ = call("GET", "/api/v1/projects", token="invalid.jwt.value")
    check("invalid JWT rejected (401)", status == 401, status)

    status, project = call("POST", "/api/v1/projects", {"name": "Captain synthetic house", "projectType": "Residential", "locationReference": "Synthetic plot"}, token=a)
    check("create project", status in (200, 201) and isinstance(project, dict) and project.get("id"), status)
    pid = project["id"]

    status, _ = call("GET", f"/api/v1/projects/{pid}", token=b)
    check("cross-user project read denied (404)", status == 404, status)

    status, _ = call("GET", f"/api/v1/projects/{pid}/requirements", token=a)
    check("requirements empty before save (204)", status == 204, status)
    snapshot = {
        "expectedRevisionId": None, "catalogueVersion": "synthetic-fixture-v1", "floorCount": 1,
        "groundFloorRooms": [{"roomType": "bedroom", "count": 2}, {"roomType": "livingRoom", "count": 1},
                             {"roomType": "kitchen", "count": 1}, {"roomType": "bathroom", "count": 1}],
        "additionalSpaces": [], "relationships": [{"sourceRoomType": "kitchen", "targetRoomType": "livingRoom", "kind": "adjacent"}],
        "designMethod": "AI_GENERATED",
    }
    status, saved = call("PUT", f"/api/v1/projects/{pid}/requirements", snapshot, token=a)
    check("save requirements", status == 200 and isinstance(saved, dict), saved)
    status, _ = call("PUT", f"/api/v1/projects/{pid}/requirements", snapshot, token=a)
    check("stale requirement revision rejected (409)", status == 409, status)
    status, _ = call("PUT", f"/api/v1/projects/{pid}/requirements", snapshot, token=b)
    check("cross-user requirement write denied (404)", status == 404, status)
    status, again = call("GET", f"/api/v1/projects/{pid}/requirements", token=a)
    check("requirements read back", status == 200 and again.get("id") == saved.get("id"), status)

    pdf = b"%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"
    raw, ctype = multipart("file", "synthetic-survey.pdf", "application/pdf", pdf)
    status, upload = call("POST", f"/api/v1/projects/{pid}/surveys", raw=raw, content_type=ctype, token=a)
    check("survey upload accepted (202)", status == 202 and isinstance(upload, dict), upload)
    if status == 202:
        sid = upload.get("surveyId") or upload.get("id")
        status, state = call("GET", f"/api/v1/projects/{pid}/surveys/{sid}", token=a)
        check("survey status Queued", status == 200 and state.get("status") == "Queued", state)
        status, _ = call("GET", f"/api/v1/projects/{pid}/surveys/{sid}", token=b)
        check("cross-user survey read denied (404)", status == 404, status)
    raw, ctype = multipart("file", "synthetic-survey.pdf", "application/pdf", pdf)
    status, _ = call("POST", f"/api/v1/projects/{pid}/surveys", raw=raw, content_type=ctype, token=b)
    check("cross-user survey upload denied (404)", status == 404, status)

    status, _ = call("GET", f"/api/v1/projects/{pid}/plans/{uuid.uuid4()}/versions", token=a)
    check("revision history for unknown plan (404)", status == 404, status)

    failed = [name for name, ok in results if not ok]
    print(f"\nSUMMARY: {len(results) - len(failed)} passed, {len(failed)} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

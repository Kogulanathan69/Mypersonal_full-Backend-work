"""Captain live customer journey against the real local stack (API + MySQL + python-services [+ rag-service]).

Synthetic users and data only; tokens/passwords are never printed. Exit code 0 only when every check passes.
Usage: python3 smoke_journey.py [base_url] [--phases 2,3,4,5,6]
"""
import json
import secrets
import sys
import urllib.error
import urllib.request
import uuid

BASE = next((a for a in sys.argv[1:] if a.startswith("http")), "http://127.0.0.1:5180")
PHASES = {int(p) for p in (sys.argv[sys.argv.index("--phases") + 1].split(",") if "--phases" in sys.argv else "2,3,4,5,6".split(","))}
results = []
state = {}


def call(method, path, body=None, token=None, raw=None, content_type=None, headers=None):
    data, hdrs = None, dict(headers or {})
    if body is not None:
        data, hdrs["Content-Type"] = json.dumps(body).encode(), "application/json"
    if raw is not None:
        data, hdrs["Content-Type"] = raw, content_type
    if token:
        hdrs["Authorization"] = "Bearer " + token
    request = urllib.request.Request(BASE + path, data=data, method=method, headers=hdrs)
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            payload = response.read()
            kind = response.headers.get("Content-Type", "")
            if "json" in kind:
                return response.status, json.loads(payload or b"null"), response.headers
            return response.status, payload, response.headers
    except urllib.error.HTTPError as error:
        text = error.read()
        try:
            return error.code, json.loads(text) if text else None, error.headers
        except json.JSONDecodeError:
            return error.code, text, error.headers


def check(name, condition, detail=""):
    results.append((name, bool(condition)))
    print(("PASS " if condition else "FAIL ") + name + (f"  [{str(detail)[:400]}]" if detail and not condition else ""))
    return bool(condition)


def code_of(body):
    return body.get("code") if isinstance(body, dict) else None


def new_user(label):
    email = f"captain-{label}-{uuid.uuid4().hex[:10]}@example.invalid"
    password = "Synthetic-" + secrets.token_urlsafe(18) + "9a!"
    status, _, _ = call("POST", "/api/v1/auth/register", {"email": email, "password": password})
    check(f"register {label}", status in (200, 201), status)
    status, body, _ = call("POST", "/api/v1/auth/login", {"email": email, "password": password})
    check(f"login {label} returns access + refresh tokens", status == 200 and body.get("accessToken") and body.get("refreshToken"), status)
    return {"email": email, "password": password, "access": body["accessToken"], "refresh": body.get("refreshToken")}


def multipart(field, filename, content_type, payload):
    boundary = "----captain" + uuid.uuid4().hex
    head = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"{field}\"; filename=\"{filename}\"\r\n"
            f"Content-Type: {content_type}\r\n\r\n").encode()
    return head + payload + f"\r\n--{boundary}--\r\n".encode(), "multipart/form-data; boundary=" + boundary


def rectangle(width, height):
    return [{"x": x, "y": y} for x, y in [(0, 0), (width, 0), (width, height), (0, height)]]


def phase2():
    status, body, _ = call("GET", "/health/ready")
    checks = {c["name"]: c for c in body.get("checks", [])} if isinstance(body, dict) else {}
    check("readiness: mysql Healthy/reachable (exact entry)",
          checks.get("mysql", {}).get("status") == "Healthy" and checks.get("mysql", {}).get("reason") == "reachable", body)
    check("readiness: python Healthy when configured", checks.get("python", {}).get("status") == "Healthy", checks.get("python"))
    a, b = new_user("alice"), new_user("bob")
    state.update(alice=a, bob=b)
    status, _, _ = call("GET", "/api/v1/projects", token="invalid.jwt.value")
    check("invalid JWT rejected (401)", status == 401, status)
    status, project, _ = call("POST", "/api/v1/projects", {"name": "Captain synthetic house", "projectType": "Residential",
                                                           "locationReference": "Synthetic plot"}, token=a["access"])
    check("create project", status in (200, 201) and project.get("id"), status)
    state["project"] = pid = project["id"]
    status, _, _ = call("GET", f"/api/v1/projects/{pid}", token=b["access"])
    check("cross-user project read denied (404)", status == 404, status)
    snapshot = {
        "expectedRevisionId": None, "catalogueVersion": "synthetic-fixture-v1", "floorCount": 1,
        "groundFloorRooms": [{"roomType": "bedroom", "count": 2}, {"roomType": "livingRoom", "count": 1},
                             {"roomType": "kitchen", "count": 1}, {"roomType": "bathroom", "count": 1}],
        "additionalSpaces": [], "relationships": [{"sourceRoomType": "kitchen", "targetRoomType": "livingRoom", "kind": "adjacent"}],
        "designMethod": "AI_GENERATED",
    }
    status, saved, _ = call("PUT", f"/api/v1/projects/{pid}/requirements", snapshot, token=a["access"])
    check("save requirements", status == 200 and saved.get("revisionId"), saved)
    state["requirements"] = saved["revisionId"]
    status, _, _ = call("PUT", f"/api/v1/projects/{pid}/requirements", snapshot, token=a["access"])
    check("stale requirement revision rejected (409)", status == 409, status)
    pdf = b"%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"
    raw, ctype = multipart("file", "synthetic-survey.pdf", "application/pdf", pdf)
    status, upload, _ = call("POST", f"/api/v1/projects/{pid}/surveys", raw=raw, content_type=ctype, token=a["access"])
    check("survey upload accepted (202)", status == 202, upload)
    state["survey"] = upload.get("surveyId") if isinstance(upload, dict) else None


def phase3():
    a, b, pid = state["alice"]["access"], state["bob"]["access"], state["project"]
    bowtie = [{"x": 0, "y": 0}, {"x": 10, "y": 10}, {"x": 10, "y": 0}, {"x": 0, "y": 10}]
    status, body, _ = call("PUT", f"/api/v1/projects/{pid}/land", {"exterior": bowtie, "expectedCurrentLandRevisionId": None}, token=a)
    check("self-intersecting land rejected (422 POLYGON_INVALID)", status == 422 and code_of(body) == "POLYGON_INVALID", body)
    status, land, _ = call("PUT", f"/api/v1/projects/{pid}/land",
                           {"exterior": rectangle(14, 12), "expectedCurrentLandRevisionId": None, "surveyId": state.get("survey")}, token=a)
    check("land boundary validated and saved (201, area 168)", status == 201 and land.get("areaM2") == 168.0, land)
    state["land"] = land["landRevisionId"]
    status, _, _ = call("PUT", f"/api/v1/projects/{pid}/land", {"exterior": rectangle(14, 12), "expectedCurrentLandRevisionId": None}, token=a)
    check("stale land revision rejected (409)", status == 409, status)
    status, _, _ = call("PUT", f"/api/v1/projects/{pid}/land", {"exterior": rectangle(14, 12), "expectedCurrentLandRevisionId": None}, token=b)
    check("cross-user land write denied (404)", status == 404, status)

    request = {"idempotencyKey": "journey-" + uuid.uuid4().hex[:12], "expectedLandRevisionId": state["land"],
               "expectedRequirementsRevisionId": state["requirements"], "seed": 7}
    status, job, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs", request, token=a)
    ok = check("generation succeeds with three candidates (201)", status == 201 and len(job.get("candidates", [])) == 3, job)
    if not ok:
        return
    state["job"] = job["jobId"]
    state["candidates"] = [c["candidateId"] for c in job["candidates"]]
    check("candidates are distinct and ranked 1..3",
          len({c["geometryHash"] for c in job["candidates"]}) == 3 and [c["rank"] for c in job["candidates"]] == [1, 2, 3])
    check("candidates are labelled synthetic + access not evaluated",
          all(c["validation"]["regulatoryEvidenceStatus"] == "SYNTHETIC_FIXTURE"
              and {"SYNTHETIC_REGULATORY_EVIDENCE", "PHYSICAL_ACCESS_NOT_EVALUATED"} <= {d["code"] for d in c["validation"]["diagnostics"]}
              for c in job["candidates"]))
    check("preliminary notice present", "not construction-ready" in job.get("notice", ""))
    check("every candidate has 5 rooms and no openings",
          all(len(c["geometry"]["floors"][0]["rooms"]) == 5 and c["geometry"]["floors"][0]["openings"] == [] for c in job["candidates"]))
    status, replay, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs", request, token=a)
    check("idempotent replay returns the same job", status == 201 and replay.get("jobId") == state["job"], status)
    status, body, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs", {**request, "seed": 8}, token=a)
    check("idempotency key reuse with other inputs rejected (409)", status == 409 and code_of(body) == "IDEMPOTENCY_KEY_REUSED", body)
    status, body, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs",
                           {**request, "idempotencyKey": "journey-stale-" + uuid.uuid4().hex[:6], "expectedLandRevisionId": str(uuid.uuid4())}, token=a)
    check("stale input revisions rejected (409)", status == 409 and code_of(body) == "STALE_INPUT_REVISIONS", body)
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs", {**request, "idempotencyKey": "journey-bob-0001"}, token=b)
    check("cross-user generation denied (404)", status == 404, status)
    status, fetched, _ = call("GET", f"/api/v1/projects/{pid}/generation-jobs/{state['job']}", token=a)
    check("generation job readable by owner", status == 200 and fetched.get("status") == "Succeeded", status)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/generation-jobs/{state['job']}", token=b)
    check("generation job hidden from other users (404)", status == 404, status)

    status, plan, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs/{state['job']}/candidates/{state['candidates'][0]}/save",
                           {"changeSummary": "Selected alternative 1"}, token=a)
    check("selected candidate saved as plan revision 1 (201)", status == 201 and plan.get("versionNumber") == 1, plan)
    state["plan"], state["revision"] = plan.get("planId"), plan.get("revisionId")
    check("saved plan cites server-issued synthetic evidence",
          plan.get("validation", {}).get("regulatoryEvidenceStatus") == "SYNTHETIC_FIXTURE"
          and plan.get("validation", {}).get("hasBlockingIssues") is False, plan.get("validation"))
    status, body, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs/{state['job']}/candidates/{state['candidates'][0]}/save",
                           None, token=a)
    check("saving the same candidate twice rejected (409)", status == 409 and code_of(body) == "PLAN_ALREADY_SAVED", body)
    status, history, _ = call("GET", f"/api/v1/projects/{pid}/plans/{state['plan']}/versions", token=a)
    check("revision history lists version 1", status == 200 and [r["versionNumber"] for r in history.get("revisions", [])] == [1], history)

    # Truthful failure: a parcel too small for the room program after synthetic setbacks.
    status, small, _ = call("POST", "/api/v1/projects", {"name": "Tiny plot", "projectType": "Residential"}, token=a)
    spid = small["id"]
    status, req, _ = call("PUT", f"/api/v1/projects/{spid}/requirements", {
        "expectedRevisionId": None, "catalogueVersion": "synthetic-fixture-v1", "floorCount": 1,
        "groundFloorRooms": [{"roomType": "bedroom", "count": 2}, {"roomType": "livingRoom", "count": 1},
                             {"roomType": "kitchen", "count": 1}, {"roomType": "bathroom", "count": 1}],
        "additionalSpaces": [], "relationships": [], "designMethod": None}, token=a)
    status, tiny, _ = call("PUT", f"/api/v1/projects/{spid}/land", {"exterior": rectangle(6, 5), "expectedCurrentLandRevisionId": None}, token=a)
    status, body, _ = call("POST", f"/api/v1/projects/{spid}/generation-jobs", {
        "idempotencyKey": "journey-tiny-" + uuid.uuid4().hex[:6], "expectedLandRevisionId": tiny["landRevisionId"],
        "expectedRequirementsRevisionId": req["revisionId"]}, token=a)
    check("impossible requirements fail truthfully (422, INFEASIBLE, no candidates)",
          status == 422 and code_of(body) == "GENERATION_UNSUCCESSFUL" and body.get("job", {}).get("outcome") == "INFEASIBLE"
          and body["job"]["candidates"] == [], body)


def phase4():
    a, b, pid, plan = state["alice"]["access"], state["bob"]["access"], state["project"], state.get("plan")
    if not check("phase 4 prerequisite: saved plan exists", plan):
        return
    status, plans, _ = call("GET", f"/api/v1/projects/{pid}/plans", token=a)
    check("project plan list shows the saved plan", status == 200 and any(p["planId"] == plan for p in plans.get("plans", [])), plans)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/plans", token=b)
    check("cross-user plan list denied (404)", status == 404, status)
    status, head, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}", token=a)
    check("current plan readable (version 1)", status == 200 and head.get("versionNumber") == 1, head)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}", token=b)
    check("cross-user plan read denied (404)", status == 404, status)
    floor = head["geometry"]["floors"][0]
    room = floor["rooms"][0]
    xs, ys = [p["x"] for p in room["polygon"]], [p["y"] for p in room["polygon"]]

    def edit(command_type, target, payload, base=None):
        return call("POST", f"/api/v1/projects/{pid}/plans/{plan}/candidates", {
            "schemaVersion": "0.1-draft", "projectId": pid, "planId": plan, "baseRevisionId": base or head["revisionId"],
            "command": {"commandId": str(uuid.uuid4()), "commandType": command_type, "floorId": floor["id"],
                        "targetId": target, "payload": payload}}, token=a)

    status, body, _ = edit("moveRoom", room["id"], {"deltaX": 100, "deltaY": 0})
    check("edit moving a room outside the parcel rejected by server validation (422 VALIDATION_EVIDENCE_INVALID)",
          status == 422 and code_of(body) == "VALIDATION_EVIDENCE_INVALID", body)
    status, body, _ = edit("resizeRoom", room["id"], {"scaleX": 0.9, "scaleY": 1}, base=str(uuid.uuid4()))
    check("edit against a stale/unknown base revision rejected (409/404)", status in (404, 409), body)
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/candidates", {
        "schemaVersion": "0.1-draft", "projectId": pid, "planId": plan, "baseRevisionId": head["revisionId"],
        "command": {"commandId": str(uuid.uuid4()), "commandType": "resizeRoom", "floorId": floor["id"], "targetId": room["id"],
                    "payload": {"scaleX": 0.9, "scaleY": 1}}}, token=b)
    check("cross-user edit denied (404)", status == 404, status)
    status, candidate, _ = edit("resizeRoom", room["id"], {"scaleX": 0.9, "scaleY": 1, "anchorX": min(xs), "anchorY": min(ys)})
    if not check("valid edit creates a validated candidate (201)", status == 201 and candidate.get("validation", {}).get("geometryValid") is True, candidate):
        return
    cid = candidate["candidateId"]
    apply_path = f"/api/v1/projects/{pid}/plans/{plan}/candidates/{cid}/apply"
    status, body, _ = call("POST", apply_path, {"acknowledgedWarnings": False}, token=a)
    check("apply without acknowledging synthetic-evidence warnings refused (422)", status == 422, body)
    status, _, _ = call("POST", apply_path, {"acknowledgedWarnings": True}, token=b)
    check("cross-user apply denied (404)", status == 404, status)
    status, applied, _ = call("POST", apply_path, {"acknowledgedWarnings": True}, token=a)
    check("apply creates revision 2 (200)", status == 200 and applied.get("revision", {}).get("versionNumber") == 2, applied)
    state["revision2"] = applied.get("revision", {}).get("revisionId")
    status, body, _ = call("POST", apply_path, {"acknowledgedWarnings": True}, token=a)
    check("duplicate apply rejected (409)", status == 409, body)
    status, history, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}/versions", token=a)
    check("revision history lists versions 2,1", status == 200 and [r["versionNumber"] for r in history["revisions"]] == [2, 1], history)
    status, body, _ = edit("resizeRoom", room["id"], {"scaleX": 0.9, "scaleY": 1})
    check("edit against superseded revision 1 rejected (409)", status == 409, body)
    status, rejected_candidate, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/candidates", {
        "schemaVersion": "0.1-draft", "projectId": pid, "planId": plan, "baseRevisionId": state["revision2"],
        "command": {"commandId": str(uuid.uuid4()), "commandType": "resizeRoom", "floorId": floor["id"], "targetId": room["id"],
                    "payload": {"scaleX": 0.95, "scaleY": 1, "anchorX": min(xs), "anchorY": min(ys)}}}, token=a)
    if status == 201:
        status, body, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/candidates/{rejected_candidate['candidateId']}/reject", None, token=a)
        check("explicit candidate rejection (200, Rejected)", status == 200 and body.get("status") == "Rejected", body)
    else:
        check("second candidate for rejection test created", False, rejected_candidate)

    status, head2, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}", token=a)
    confirm = f"/api/v1/projects/{pid}/plans/{plan}/confirmation"
    status, body, _ = call("POST", confirm, {"revisionId": head2["revisionId"], "expectedGeometryHash": "sha256:" + "0" * 64,
                                             "acknowledgedWarnings": True}, token=a)
    check("confirmation with a wrong geometry hash rejected (409)", status == 409 and code_of(body) == "GEOMETRY_HASH_MISMATCH", body)
    status, body, _ = call("POST", confirm, {"revisionId": state["revision"], "expectedGeometryHash": head["geometryHash"],
                                             "acknowledgedWarnings": True}, token=a)
    check("confirmation of a superseded revision rejected (409)", status == 409 and code_of(body) == "NOT_CURRENT_REVISION", body)
    status, body, _ = call("POST", confirm, {"revisionId": head2["revisionId"], "expectedGeometryHash": head2["geometryHash"],
                                             "acknowledgedWarnings": False}, token=a)
    check("confirmation without acknowledging warnings refused (422)", status == 422 and code_of(body) == "WARNING_ACKNOWLEDGEMENT_REQUIRED", body)
    status, _, _ = call("POST", confirm, {"revisionId": head2["revisionId"], "expectedGeometryHash": head2["geometryHash"],
                                          "acknowledgedWarnings": True}, token=b)
    check("cross-user confirmation denied (404)", status == 404, status)
    status, confirmed, _ = call("POST", confirm, {"revisionId": head2["revisionId"], "expectedGeometryHash": head2["geometryHash"],
                                                  "acknowledgedWarnings": True}, token=a)
    check("final plan confirmed (201) with non-approval notice", status == 201 and "not an architectural approval" in confirmed.get("notice", ""), confirmed)
    state["confirmation"] = confirmed.get("confirmationId")
    status, body, _ = call("POST", confirm, {"revisionId": head2["revisionId"], "expectedGeometryHash": head2["geometryHash"],
                                             "acknowledgedWarnings": True}, token=a)
    check("second confirmation rejected (409 ALREADY_CONFIRMED)", status == 409 and code_of(body) == "ALREADY_CONFIRMED", body)
    status, plans, _ = call("GET", f"/api/v1/projects/{pid}/plans", token=a)
    check("plan list reports the confirmation",
          any(p["planId"] == plan and p.get("confirmation", {}) and p["confirmation"]["confirmedRevisionId"] == head2["revisionId"]
              for p in plans.get("plans", [])), plans)


def phase5():
    global BASE
    a = state["alice"]["access"]
    status, body, _ = call("GET", "/health/ready")
    rag = {c["name"]: c for c in body.get("checks", [])}.get("rag", {}) if isinstance(body, dict) else {}
    print(f"     rag readiness entry: {rag}")
    check("readiness reports rag truthfully (not Healthy without a corpus)", rag.get("status") in ("Degraded", "Unhealthy"), rag)
    status, _, _ = call("POST", "/api/v1/regulations/retrieve", {"query": "rear setback"})
    check("regulation retrieval requires authentication (401)", status == 401, status)
    status, body, _ = call("POST", "/api/v1/regulations/retrieve", {"query": "rear setback", "maxResults": 3}, token=a)
    check("retrieval fails closed without a verified corpus (503, no passages)",
          status == 503 and not (isinstance(body, dict) and body.get("passages")), body)

    # Second API instance: generation policy evidence mode "rag-constraints".
    saved_base, BASE = BASE, "http://127.0.0.1:5181"
    try:
        user = new_user("ragmode")
        t = user["access"]
        status, project, _ = call("POST", "/api/v1/projects", {"name": "RAG mode plot", "projectType": "Residential"}, token=t)
        pid = project["id"]
        status, req, _ = call("PUT", f"/api/v1/projects/{pid}/requirements", {
            "expectedRevisionId": None, "catalogueVersion": "synthetic-fixture-v1", "floorCount": 1,
            "groundFloorRooms": [{"roomType": "bedroom", "count": 1}, {"roomType": "livingRoom", "count": 1},
                                 {"roomType": "kitchen", "count": 1}, {"roomType": "bathroom", "count": 1}],
            "additionalSpaces": [], "relationships": [], "designMethod": None}, token=t)
        check("rag-mode requirements saved", status == 200 and isinstance(req, dict) and req.get("revisionId"), req)
        status, land, _ = call("PUT", f"/api/v1/projects/{pid}/land", {"exterior": rectangle(14, 12), "expectedCurrentLandRevisionId": None}, token=t)
        key = "journey-rag-" + uuid.uuid4().hex[:8]
        status, body, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs", {
            "idempotencyKey": key, "expectedLandRevisionId": land["landRevisionId"],
            "expectedRequirementsRevisionId": req["revisionId"]}, token=t)
        check("rag-constraints generation fails closed when the constraint service is unavailable (503)",
              status == 503 and code_of(body) == "DEPENDENCY_UNAVAILABLE", body)
        status, again, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs", {
            "idempotencyKey": key, "expectedLandRevisionId": land["landRevisionId"],
            "expectedRequirementsRevisionId": req["revisionId"]}, token=t)
        check("no job or candidates were published for the failed attempt (still 503, not a replay)", status == 503, again)
    finally:
        BASE = saved_base


ARCHITECT = "a7c1e0de-0000-4000-8000-0000000000a1"  # synthetic directory entry
POLICY = "captain-sharing-policy-dev-1"


def phase6():
    a, b, pid, plan = state["alice"]["access"], state["bob"]["access"], state["project"], state.get("plan")
    if not check("phase 6 prerequisite: confirmed plan", state.get("confirmation")):
        return
    status, head, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}", token=a)
    request = {"planId": plan, "planRevisionId": head["revisionId"], "confirmationId": state["confirmation"],
               "geometryHash": head["geometryHash"], "reportId": str(uuid.uuid4())}
    status, body, _ = call("POST", f"/api/v1/projects/{pid}/reports", {**request, "reportId": str(uuid.uuid4()),
                                                                     "geometryHash": "sha256:" + "1" * 64}, token=a)
    check("report for a mismatched geometry hash refused (404)", status == 404, body)
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/reports", request, token=b)
    check("cross-user report generation denied (404)", status == 404, status)
    status, report, _ = call("POST", f"/api/v1/projects/{pid}/reports", request, token=a)
    check("preliminary PDF report generated and Ready (201)", status == 201 and report.get("status") == "Ready", report)
    rid = request["reportId"]
    state["report"] = rid
    status, again, _ = call("POST", f"/api/v1/projects/{pid}/reports", request, token=a)
    check("repeated report request is idempotent (200 existing)", status == 200 and again.get("existing") is True, again)
    status, st, _ = call("GET", f"/api/v1/projects/{pid}/reports/{rid}", token=a)
    check("report status readable by owner", status == 200 and st.get("status") == "Ready", st)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/reports/{rid}", token=b)
    check("cross-user report status denied (404)", status == 404, status)
    status, pdf, headers = call("GET", f"/api/v1/projects/{pid}/reports/{rid}/download", token=a)
    check("authorized download returns a PDF attachment (no-store)",
          status == 200 and isinstance(pdf, bytes) and pdf.startswith(b"%PDF-")
          and "application/pdf" in headers.get("Content-Type", "") and "attachment" in headers.get("Content-Disposition", "")
          and "no-store" in headers.get("Cache-Control", ""), (status, headers.get("Content-Type")))
    state["pdf_bytes"] = len(pdf) if isinstance(pdf, bytes) else 0
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/reports/{rid}/download", token=b)
    check("cross-user report download denied (404)", status == 404, status)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/reports/{uuid.uuid4()}/download", token=a)
    check("unknown report download hidden (404)", status == 404, status)

    path = f"/api/v1/projects/{pid}/architect-requests"
    base = {"recipientId": ARCHITECT, "reportId": rid, "documentIds": [], "explicitConsent": True, "acknowledgedPolicyVersion": POLICY}
    status, body, _ = call("POST", path, {**base, "explicitConsent": False}, token=a)
    check("architect request without explicit consent refused (422)", status == 422 and code_of(body) == "EXPLICIT_CONSENT_REQUIRED", body)
    status, body, _ = call("POST", path, {**base, "recipientId": str(uuid.uuid4())}, token=a)
    check("unknown architect recipient not eligible (422)", status == 422 and code_of(body) == "NOT_ELIGIBLE", body)
    status, body, _ = call("POST", path, {**base, "acknowledgedPolicyVersion": "old-policy"}, token=a)
    check("stale sharing-policy acknowledgement refused (409)", status == 409, body)
    status, _, _ = call("POST", path, base, token=b)
    check("cross-user architect request denied (404)", status == 404, status)
    status, created, _ = call("POST", path, base, token=a)
    check("architect request recorded with explicit consent (201 Pending, not dispatched)",
          status == 201 and created.get("status") == "Pending" and "No project data has been sent" in created.get("delivery", ""), created)
    req_id = created.get("requestId")
    status, listing, _ = call("GET", path, token=a)
    check("architect requests listed for owner", status == 200 and any(r["requestId"] == req_id for r in listing.get("requests", [])), listing)
    status, _, _ = call("GET", f"{path}/{req_id}", token=b)
    check("cross-user architect request read denied (404)", status == 404, status)
    status, withdrawn, _ = call("POST", f"{path}/{req_id}/withdraw", {"expectedStateVersion": 1}, token=a)
    check("consent withdrawn (200 Withdrawn)", status == 200 and withdrawn.get("status") == "Withdrawn", withdrawn)
    status, body, _ = call("POST", f"{path}/{req_id}/withdraw", {"expectedStateVersion": 1}, token=a)
    check("second withdrawal rejected (409)", status == 409, body)


EXTRA_PHASES = [(4, phase4), (5, phase5), (6, phase6)]


def main():
    for number, phase in ((2, phase2), (3, phase3)):
        if number in PHASES or number == 2:
            print(f"--- phase {number}")
            phase()
    extra = globals().get("EXTRA_PHASES", [])
    for number, phase in extra:
        if number in PHASES:
            print(f"--- phase {number}")
            phase()
    failed = [name for name, ok in results if not ok]
    print(f"\nSUMMARY: {len(results) - len(failed)} passed, {len(failed)} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

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
PHASES = {int(p) for p in (sys.argv[sys.argv.index("--phases") + 1].split(",") if "--phases" in sys.argv else "2,3,4,5,6,7,8,9".split(","))}
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
    if isinstance(pdf, bytes):
        import os
        target = os.path.expanduser("~/bm-captain/results/journey-report.pdf")
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with open(target, "wb") as handle:
            handle.write(pdf)
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


def _b64(data):
    import base64
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def _jwt(claims, key):
    import hashlib
    import hmac
    head = _b64(json.dumps({"alg": "HS256", "typ": "JWT"}, separators=(",", ":")).encode())
    body = _b64(json.dumps(claims, separators=(",", ":")).encode())
    sig = hmac.new(key, f"{head}.{body}".encode(), hashlib.sha256).digest()
    return f"{head}.{body}.{_b64(sig)}"


def _claims(token):
    import base64
    payload = token.split(".")[1]
    return json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))


def phase7():
    import time
    time.sleep(61)  # fresh auth-strict window: phase 7 performs several logins and ends with the limiter test
    import base64
    import os
    import subprocess
    import time
    alice, bob, pid = state["alice"], state["bob"], state["project"]
    status, dash, _ = call("GET", "/api/v1/dashboard/summary", token=alice["access"])
    check("dashboard summary for owner (200)", status == 200, dash)
    status, bob_dash, _ = call("GET", "/api/v1/dashboard/summary", token=bob["access"])
    check("dashboard never shows another user's project", status == 200 and pid not in json.dumps(bob_dash), bob_dash)

    # Forged tokens with the real local key: expired, and with the wrong key.
    key = base64.b64decode(open(os.path.expanduser("~/bm-captain/secrets/jwt-signing.key")).read().strip())
    claims = _claims(alice["access"])
    now = int(time.time())
    expired = _jwt({**claims, "iat": now - 7200, "nbf": now - 7200, "exp": now - 3600}, key)
    status, _, _ = call("GET", "/api/v1/projects", token=expired)
    check("expired JWT rejected (401)", status == 401, status)
    wrong = _jwt({**claims, "exp": now + 600}, os.urandom(32))
    status, _, _ = call("GET", "/api/v1/projects", token=wrong)
    check("JWT signed with another key rejected (401)", status == 401, status)
    tampered = alice["access"][:-4] + ("AAAA" if not alice["access"].endswith("AAAA") else "BBBB")
    status, _, _ = call("GET", "/api/v1/projects", token=tampered)
    check("tampered JWT signature rejected (401)", status == 401, status)

    # Refresh rotation, then replay of the old refresh token revokes the session (token version bump).
    status, rotated, _ = call("POST", "/api/v1/auth/refresh", {"refreshToken": alice["refresh"]})
    check("refresh rotates the session (200, new tokens)",
          status == 200 and rotated.get("accessToken") and rotated.get("refreshToken") != alice["refresh"], status)
    new_access = rotated.get("accessToken")
    status, _, _ = call("GET", "/api/v1/projects", token=new_access)
    check("rotated access token works", status == 200, status)
    status, _, _ = call("POST", "/api/v1/auth/refresh", {"refreshToken": alice["refresh"]})
    check("replayed refresh token rejected (401)", status == 401, status)
    status, _, _ = call("GET", "/api/v1/projects", token=new_access)
    check("replay detection revokes issued access tokens (401)", status == 401, status)
    status, _, _ = call("POST", "/api/v1/auth/refresh", {"refreshToken": rotated.get("refreshToken")})
    check("whole refresh family revoked after replay (401)", status == 401, status)

    # Fresh login, logout (one session), then logout-all (every access token).
    status, login, _ = call("POST", "/api/v1/auth/login", {"email": alice["email"], "password": alice["password"]})
    check("re-login after revocation (200)", status == 200 and login.get("accessToken"), status)
    status, _, _ = call("POST", "/api/v1/auth/logout", {"refreshToken": login["refreshToken"]}, token=login["accessToken"])
    check("logout (204)", status == 204, status)
    status, _, _ = call("POST", "/api/v1/auth/refresh", {"refreshToken": login["refreshToken"]})
    check("logged-out refresh token rejected (401)", status == 401, status)
    status, login2, _ = call("POST", "/api/v1/auth/login", {"email": alice["email"], "password": alice["password"]})
    status, _, _ = call("POST", "/api/v1/auth/logout-all", token=login2["accessToken"])
    check("logout-all (204)", status == 204, status)
    status, _, _ = call("GET", "/api/v1/projects", token=login2["accessToken"])
    check("access token rejected after logout-all (401)", status == 401, status)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}", token=bob["access"])
    check("other users' sessions unaffected; cross-user still denied (404)", status == 404, status)

    # Invalid requests and safe errors.
    status, body, _ = call("POST", "/api/v1/projects", raw=b'{"name":', content_type="application/json", token=bob["access"])
    check("malformed JSON rejected (400)", status == 400, status)
    leaky = json.dumps(body) if body is not None else ""
    check("error body leaks no stack trace or SQL",
          not any(s in leaky for s in ("Exception", "   at ", "SELECT ", "MySql", "/home/")), leaky[:200])
    status, _, _ = call("PUT", f"/api/v1/projects/{pid}/land", raw=b"x=1", content_type="text/plain", token=bob["access"])
    check("unsupported media type rejected (415 or 404 for non-owner)", status in (404, 415), status)
    status, _, _ = call("POST", "/api/v1/projects", raw=b'{"name":"' + b"a" * (2 * 1024 * 1024) + b'"}',
                        content_type="application/json", token=bob["access"])
    check("oversized body rejected (413)", status == 413, status)

    # No secret material in service logs.
    secrets_used = [alice["password"], bob["password"], alice["access"], alice["refresh"], bob["access"], login["accessToken"]]
    leaked = []
    for log in ("~/bm-captain/logs/api.log", "~/bm-captain/logs/api-rag.log", "~/bm-captain/logs/python.log", "~/bm-captain/logs/rag.log"):
        path = os.path.expanduser(log)
        if os.path.exists(path):
            text = open(path, encoding="utf-8", errors="replace").read()
            leaked += [log for s in secrets_used if s and s in text]
            for f in ("mysql-runtime.password", "python-internal.token", "rag-internal.token", "jwt-signing.key"):
                value = open(os.path.expanduser(f"~/bm-captain/secrets/{f}")).read().strip()
                if value and value in text:
                    leaked.append(f"{log}:{f}")
    check("no passwords, tokens or keys in service logs", not leaked, leaked)

    # Dependency failure: stop python-services; land validation must fail closed (503) and readiness must say so.
    subprocess.run(["pkill", "-f", "uvicorn app.main:app --host 127.0.0.1 --port 8091"], check=False)
    time.sleep(2)
    status, body, _ = call("PUT", f"/api/v1/projects/{pid}/land", {"exterior": rectangle(14, 12),
                                                                  "expectedCurrentLandRevisionId": state["land"]}, token=bob["access"])
    check("non-owner still denied while python is down (404)", status == 404, status)
    status, login3, _ = call("POST", "/api/v1/auth/login", {"email": alice["email"], "password": alice["password"]})
    status, body, _ = call("PUT", f"/api/v1/projects/{pid}/land", {"exterior": rectangle(14, 12),
                                                                  "expectedCurrentLandRevisionId": state["land"]}, token=login3["accessToken"])
    check("python dependency down -> land validation fails closed (503)", status == 503 and code_of(body) == "DEPENDENCY_UNAVAILABLE", body)
    status, ready, _ = call("GET", "/health/ready")
    py = {c["name"]: c for c in ready.get("checks", [])}.get("python", {}) if isinstance(ready, dict) else {}
    check("readiness reports python not Healthy while down", py.get("status") != "Healthy", py)

    # Auth rate limiting (auth-strict policy) - last, so it cannot affect earlier logins.
    codes = [call("POST", "/api/v1/auth/login", {"email": f"nobody-{i}@example.invalid", "password": "Wrong-password-123!"})[0]
             for i in range(14)]
    check("auth rate limiting engages (429) and never errors (no 5xx)", 429 in codes and not any(c >= 500 for c in codes), codes)


def text_pdf(text):
    """Minimal synthetic single-page PDF with a readable text layer (no real survey data)."""
    content = f"BT /F1 12 Tf 72 720 Td ({text}) Tj ET".encode("ascii")
    objects = [b"<< /Type /Catalog /Pages 2 0 R >>", b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
               b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
               b"<< /Length " + str(len(content)).encode() + b" >>\nstream\n" + content + b"\nendstream",
               b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"]
    pdf, offsets = bytearray(b"%PDF-1.4\n"), [0]
    for index, obj in enumerate(objects, start=1):
        offsets.append(len(pdf))
        pdf.extend(f"{index} 0 obj\n".encode() + obj + b"\nendobj\n")
    xref = len(pdf)
    pdf.extend(f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode())
    for offset in offsets[1:]:
        pdf.extend(f"{offset:010d} 00000 n \n".encode())
    pdf.extend(f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode())
    return bytes(pdf)


def phase8():
    """V3 register routes: /users/me, survey extraction, land validate/confirm, alternatives/select, POST /plans,
    edits/validate, versions, confirm alias, AI proposals (no provider) and report listing."""
    import time
    a, b = state["alice"]["access"], state["bob"]["access"]
    status, me, _ = call("GET", "/api/v1/users/me", token=a)
    check("GET /users/me returns the caller's profile", status == 200 and me.get("email") == state["alice"]["email"], me)
    status, _, _ = call("GET", "/api/v1/users/me")
    check("GET /users/me without a token rejected (401)", status == 401, status)

    status, project, _ = call("POST", "/api/v1/projects", {"name": "Captain V3 routes", "projectType": "Residential"}, token=a)
    pid = project["id"]
    status, req, _ = call("PUT", f"/api/v1/projects/{pid}/requirements", {
        "expectedRevisionId": None, "catalogueVersion": "synthetic-fixture-v1", "floorCount": 1,
        "groundFloorRooms": [{"roomType": "bedroom", "count": 2}, {"roomType": "livingRoom", "count": 1},
                             {"roomType": "kitchen", "count": 1}, {"roomType": "bathroom", "count": 1}],
        "additionalSpaces": [], "relationships": [], "designMethod": "AI_GENERATED"}, token=a)

    # Survey: upload -> durable worker extraction -> unverified evidence for customer review.
    raw, ctype = multipart("file", "synthetic-text-survey.pdf", "application/pdf",
                           text_pdf("North boundary length: 14 m Lot No: 77 Land area: 168 m2"))
    status, upload, _ = call("POST", f"/api/v1/projects/{pid}/surveys", raw=raw, content_type=ctype, token=a)
    sid = upload.get("surveyId") if isinstance(upload, dict) else None
    check("text survey upload accepted (202)", status == 202 and sid, upload)
    extraction = None
    for _ in range(60):
        status, extraction, _ = call("GET", f"/api/v1/projects/{pid}/surveys/{sid}/extraction", token=a)
        if status == 200 and extraction.get("processingStatus") in ("Completed", "Failed"):
            break
        time.sleep(1)
    evidence = ((extraction or {}).get("extraction") or {}).get("evidence") or {}
    check("worker completes extraction (Completed, PDF_TEXT)",
          extraction.get("processingStatus") == "Completed" and extraction["extraction"]["method"] == "PDF_TEXT", extraction)
    check("extracted length 14 m is returned as unreviewed evidence",
          any(m.get("value") == 14 and m.get("unit") == "m" for m in evidence.get("measurements", [])), evidence)
    check("extraction carries the unverified-evidence notice", "not a legal survey" in (extraction or {}).get("notice", ""))
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/surveys/{sid}/extraction", token=b)
    check("cross-user extraction read denied (404)", status == 404, status)

    # Customer review of extracted values: versioned, unit-normalised, never becomes land geometry.
    eid = ((extraction or {}).get("extraction") or {}).get("extractionId")
    reviews_path = f"/api/v1/projects/{pid}/surveys/{sid}/extraction/reviews"
    status, body, _ = call("POST", reviews_path, {"extractionId": eid, "expectedVersion": 0,
                                                  "decisions": [{"measurementIndex": 99, "action": "CONFIRM"}]}, token=a)
    check("review of a non-existent measurement refused (422)", status == 422, body)
    status, r1, _ = call("POST", reviews_path, {"extractionId": eid, "expectedVersion": 0,
                                                "decisions": [{"measurementIndex": 0, "action": "CONFIRM"}]}, token=a)
    check("customer confirms the extracted 14 m (review v1, 14 m)", status == 201 and r1["review"]["version"] == 1
          and r1["review"]["measurements"][0]["valueMetres"] == 14, r1)
    status, r2, _ = call("POST", reviews_path, {"extractionId": eid, "expectedVersion": 1, "note": "Surveyor's figure is in feet",
                                                "decisions": [{"measurementIndex": 0, "action": "CORRECT", "correctedValue": 46, "correctedUnit": "ft"}]},
                         token=a)
    check("correction in feet normalised to metres (v2, 14.0208 m)", status == 201 and r2["review"]["measurements"][0]["valueMetres"] == 14.0208, r2)
    status, body, _ = call("POST", reviews_path, {"extractionId": eid, "expectedVersion": 1,
                                                  "decisions": [{"measurementIndex": 0, "action": "REJECT"}]}, token=a)
    check("review against a superseded version refused (409)", status == 409, body)
    status, _, _ = call("POST", reviews_path, {"extractionId": eid, "expectedVersion": 2,
                                               "decisions": [{"measurementIndex": 0, "action": "REJECT"}]}, token=b)
    check("cross-user review denied (404)", status == 404, status)
    status, history, _ = call("GET", reviews_path, token=a)
    check("review history keeps both versions", status == 200 and [r["version"] for r in history["reviews"]] == [2, 1], history)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/land", token=a)
    check("reviewing survey values does not create land (204)", status == 204, status)

    # Land: dry-run validation persists nothing; confirmation is an explicit customer action.
    status, dry, _ = call("POST", f"/api/v1/projects/{pid}/land/validate", {"exterior": rectangle(14, 12)}, token=a)
    check("land/validate dry run (200, valid, persisted=false)", status == 200 and dry.get("valid") is True and dry.get("persisted") is False, dry)
    status, bad, _ = call("POST", f"/api/v1/projects/{pid}/land/validate",
                          {"exterior": [{"x": 0, "y": 0}, {"x": 10, "y": 10}, {"x": 10, "y": 0}, {"x": 0, "y": 10}]}, token=a)
    check("land/validate reports an invalid polygon without saving", status in (200, 422) and (bad.get("valid") is False or code_of(bad) == "POLYGON_INVALID"), bad)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/land", token=a)
    check("dry run left no land revision (204 no content)", status == 204, status)
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/land/validate", {"exterior": rectangle(14, 12)}, token=b)
    check("cross-user land/validate denied (404)", status == 404, status)
    status, land, _ = call("POST", f"/api/v1/projects/{pid}/land/confirm",
                           {"exterior": rectangle(14, 12), "expectedCurrentLandRevisionId": None, "surveyId": sid}, token=a)
    check("land/confirm saves the customer-confirmed boundary (201)", status == 201 and land.get("areaM2") == 168.0, land)
    status, lc, _ = call("GET", f"/api/v1/projects/{pid}/lifecycle", token=a)
    check("lifecycle derived: GENERATION_READY after land + requirements", status == 200 and lc.get("stage") == "GENERATION_READY"
          and lc.get("nextActions") == ["START_GENERATION"], lc)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/lifecycle", token=b)
    check("cross-user lifecycle denied (404)", status == 404, status)

    # Generation alternatives, selection and plan creation.
    status, job, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs", {
        "idempotencyKey": "v3-" + uuid.uuid4().hex[:12], "expectedLandRevisionId": land.get("landRevisionId"),
        "expectedRequirementsRevisionId": req.get("revisionId"), "seed": 11}, token=a)
    jid = job.get("jobId")
    status, alts, _ = call("GET", f"/api/v1/projects/{pid}/generation-jobs/{jid}/alternatives", token=a)
    candidates = alts.get("candidates", []) if isinstance(alts, dict) else []
    check("alternatives lists three distinct alternatives", status == 200 and len({c["geometryHash"] for c in candidates}) == 3, alts)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}/generation-jobs/{jid}/alternatives", token=b)
    check("cross-user alternatives denied (404)", status == 404, status)
    if len(candidates) < 3:
        return
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs/{jid}/select", {"alternativeId": str(uuid.uuid4())}, token=a)
    check("selecting an unknown alternative rejected (404)", status == 404, status)
    status, selected, _ = call("POST", f"/api/v1/projects/{pid}/generation-jobs/{jid}/select",
                               {"alternativeId": candidates[0]["candidateId"]}, token=a)
    check("select creates plan revision 1 (201)", status == 201 and selected.get("versionNumber") == 1, selected)
    check("lifecycle derived: PLAN_SELECTED", call("GET", f"/api/v1/projects/{pid}/lifecycle", token=a)[1].get("stage") == "PLAN_SELECTED")
    status, created, _ = call("POST", f"/api/v1/projects/{pid}/plans",
                              {"jobId": jid, "alternativeId": candidates[1]["candidateId"], "changeSummary": "Alternative 2"}, token=a)
    check("POST /plans creates a plan from another alternative (201)", status == 201 and created.get("planId") not in (None, selected.get("planId")), created)
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/plans", {"jobId": jid, "alternativeId": candidates[2]["candidateId"]}, token=b)
    check("cross-user POST /plans denied (404)", status == 404, status)

    plan, base = selected["planId"], selected["revisionId"]
    status, head, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}", token=a)
    floor, room = head["geometry"]["floors"][0], head["geometry"]["floors"][0]["rooms"][0]
    xs, ys = [p["x"] for p in room["polygon"]], [p["y"] for p in room["polygon"]]
    edit = {"schemaVersion": "0.1-draft", "projectId": pid, "planId": plan, "baseRevisionId": base,
            "command": {"commandId": str(uuid.uuid4()), "commandType": "resizeRoom", "floorId": floor["id"], "targetId": room["id"],
                        "payload": {"scaleX": 0.9, "scaleY": 1, "anchorX": min(xs), "anchorY": min(ys)}}}
    status, validated, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/edits/validate", edit, token=a)
    check("edits/validate returns validated geometry without saving",
          status == 200 and validated.get("persisted") is False and validated.get("validation", {}).get("geometryValid") is True, validated)
    status, history, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}/versions", token=a)
    check("edits/validate created no revision", [r["versionNumber"] for r in history.get("revisions", [])] == [1], history)
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/edits/validate", edit, token=b)
    check("cross-user edits/validate denied (404)", status == 404, status)

    geometry = validated.get("candidateGeometry")
    status, saved, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/versions",
                            {"expectedBaseRevisionId": base, "geometry": geometry, "changeSummary": "Narrower first room"}, token=a)
    check("versions saves a server-validated revision 2 (201)", status == 201 and saved.get("versionNumber") == 2, saved)
    status, body, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/versions",
                           {"expectedBaseRevisionId": base, "geometry": geometry}, token=a)
    check("versions against a superseded base rejected (409)", status == 409, body)
    outside = json.loads(json.dumps(geometry))
    for point in outside["floors"][0]["rooms"][0]["polygon"]:
        point["x"] += 100
    status, body, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/versions",
                           {"expectedBaseRevisionId": saved.get("revisionId"), "geometry": outside}, token=a)
    check("versions rejects geometry that fails server validation (422)", status == 422, body)
    status, _, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/versions",
                        {"expectedBaseRevisionId": saved.get("revisionId"), "geometry": geometry}, token=b)
    check("cross-user versions denied (404)", status == 404, status)

    ai_path = f"/api/v1/projects/{pid}/plans/{plan}/ai-edit/proposals"
    status, body, _ = call("POST", ai_path, {"expectedBaseRevisionId": saved.get("revisionId"), "instruction": "Paint the walls blue"}, token=a)
    check("unsupported natural-language instruction refused (422; M3 reports AI_PROPOSAL_INVALID)",
          status == 422 and code_of(body) == "AI_PROPOSAL_INVALID", body)
    status, body, _ = call("POST", ai_path, {"expectedBaseRevisionId": saved.get("revisionId"), "instruction": "Make the bedroom larger"}, token=a)
    check("ambiguous room reference refused (422 AI_PROPOSAL_INVALID)", status == 422 and code_of(body) == "AI_PROPOSAL_INVALID", body)
    status, _, _ = call("POST", ai_path, {"expectedBaseRevisionId": saved.get("revisionId"), "instruction": "Make the kitchen larger by 5%"}, token=b)
    check("cross-user AI proposal denied (404)", status == 404, status)
    status, proposal, _ = call("POST", ai_path, {"expectedBaseRevisionId": saved.get("revisionId"),
                                                 "instruction": "Make the kitchen larger by 5%"}, token=a)
    check("rule-based proposal becomes a validated, unapplied candidate (201, labelled RULE_BASED_LOCAL)",
          status == 201 and proposal.get("proposalSource") == "RULE_BASED_LOCAL" and proposal.get("applied") is False
          and proposal.get("candidate", {}).get("candidateId"), proposal)
    history = call("GET", f"/api/v1/projects/{pid}/plans/{plan}/versions", token=a)[1]
    check("a proposal never mutates the plan by itself", [r["versionNumber"] for r in history["revisions"]][0] == 2, history)
    cand = (proposal or {}).get("candidate", {}).get("candidateId")
    status, applied, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/candidates/{cand}/apply", {"acknowledgedWarnings": True}, token=a)
    check("customer explicitly applies the proposal (revision 3)", status == 200 and applied.get("revision", {}).get("versionNumber") == 3, applied)

    status, head2, _ = call("GET", f"/api/v1/projects/{pid}/plans/{plan}", token=a)
    status, confirmed, _ = call("POST", f"/api/v1/projects/{pid}/plans/{plan}/confirm",
                                {"revisionId": head2["revisionId"], "expectedGeometryHash": head2["geometryHash"],
                                 "acknowledgedWarnings": True}, token=a)
    check("confirm alias confirms the exact current revision (201)", status == 201 and confirmed.get("confirmationId"), confirmed)
    lc = call("GET", f"/api/v1/projects/{pid}/lifecycle", token=a)[1]
    check("lifecycle derived: PLAN_CONFIRMED, nothing stale", lc.get("stage") == "PLAN_CONFIRMED" and lc["stale"]["supersededConfirmations"] == 0, lc)
    status, land2, _ = call("POST", f"/api/v1/projects/{pid}/land/confirm", {"exterior": rectangle(15, 12),
                            "expectedCurrentLandRevisionId": land.get("landRevisionId")}, token=a)
    lc = call("GET", f"/api/v1/projects/{pid}/lifecycle", token=a)[1]
    check("new land revision makes plans and confirmation stale (back to GENERATION_READY)",
          status == 201 and lc.get("stage") == "GENERATION_READY" and lc["stale"]["stalePlans"] == 2 and lc["stale"]["supersededConfirmations"] == 1, lc)

    if state.get("project") and 6 in PHASES:
        status, reports, _ = call("GET", f"/api/v1/projects/{state['project']}/reports", token=a)
        check("report list shows the phase 6 report", status == 200 and len(reports.get("reports", [])) >= 1, reports)
        status, _, _ = call("GET", f"/api/v1/projects/{state['project']}/reports", token=b)
        check("cross-user report list denied (404)", status == 404, status)


def phase9():
    """Architect directory + portal, notifications/outbox and requirement catalogue (needs the bootstrapped admin)."""
    import os
    import time
    import urllib.request as ur
    time.sleep(61)  # fresh auth-strict window for the logins below
    secrets_dir = os.path.expanduser("~/bm-captain/secrets")
    if not check("phase 9 prerequisite: admin credentials and the phase 6 report",
                 os.path.exists(secrets_dir + "/captain-admin.email") and state.get("report")):
        return
    status, body, _ = call("POST", "/api/v1/auth/login", {"email": open(secrets_dir + "/captain-admin.email").read().strip(),
                                                         "password": open(secrets_dir + "/captain-admin.password").read().strip()})
    admin = body["accessToken"]
    a, b, pid, rid = state["alice"]["access"], state["bob"]["access"], state["project"], state["report"]

    arch = new_user("architect")
    arch_id = call("GET", "/api/v1/admin/customers?search=" + arch["email"].split("@")[0], token=admin)[1]["items"][0]["userId"]
    status, _, _ = call("POST", f"/api/v1/admin/accounts/{arch_id}/roles", {"role": "ARCHITECT", "granted": True}, token=admin)
    check("admin grants the architect role", status == 200, status)
    atok = call("POST", "/api/v1/auth/login", {"email": arch["email"], "password": arch["password"]})[1]["accessToken"]
    status, body, _ = call("GET", "/api/v1/architect/dashboard", token=atok)
    check("architect without a directory profile refused (403)", status == 403 and code_of(body) == "ARCHITECT_PROFILE_MISSING", body)
    status, body, _ = call("GET", "/api/v1/architect/dashboard", token=b)
    check("customer cannot use the architect portal (403)", status == 403, status)
    status, prof, _ = call("POST", "/api/v1/admin/architects", {"userId": arch_id, "displayName": "Synthetic Architect",
                                                               "registrationNumber": "SYN-0001"}, token=admin)
    check("admin creates an UNVERIFIED, unlisted directory profile", status == 201 and prof.get("verificationStatus") == "UNVERIFIED"
          and prof.get("isListed") is False, prof)
    rec = prof["recipientId"]
    status, body, _ = call("PUT", f"/api/v1/admin/architects/{rec}/listing", {"isListed": True, "acceptingRequests": True, "expectedRowVersion": 1},
                           token=admin)
    check("unverified architect cannot be listed (422)", status == 422 and code_of(body) == "ARCHITECT_NOT_VERIFIED", body)
    status, body, _ = call("POST", f"/api/v1/admin/architects/{rec}/verification", {"status": "VERIFIED", "expectedRowVersion": 1}, token=admin)
    check("verification without an evidence reference refused (422)", status == 422, body)
    status, body, _ = call("POST", f"/api/v1/admin/architects/{rec}/verification", {"status": "VERIFIED", "reference": "synthetic-evidence-ref-001",
                                                                                 "expectedRowVersion": 1}, token=admin)
    check("admin records verification with an evidence reference", status == 200 and body.get("verificationStatus") == "VERIFIED", body)
    call("PUT", f"/api/v1/admin/architects/{rec}/listing", {"isListed": True, "acceptingRequests": True, "expectedRowVersion": 2}, token=admin)
    status, directory, _ = call("GET", "/api/v1/architects", token=a)
    check("customer directory lists the verified architect (no contact data)",
          any(e["recipientId"] == rec and set(e) == {"recipientId", "displayName", "verificationStatus"} for e in directory.get("architects", [])),
          directory)

    path = f"/api/v1/projects/{pid}/architect-requests"
    status, created, _ = call("POST", path, {"recipientId": rec, "reportId": rid, "documentIds": [], "explicitConsent": True,
                                             "acknowledgedPolicyVersion": POLICY}, token=a)
    check("customer shares the report with the verified architect (201)", status == 201, created)
    req = created.get("requestId")
    status, listing, _ = call("GET", "/api/v1/architect/requests", token=atok)
    check("architect sees the request addressed to them", any(r["requestId"] == req for r in listing.get("requests", [])), listing)
    status, pdf, headers = call("GET", f"/api/v1/architect/requests/{req}/report", token=atok)
    check("architect downloads exactly the consented report (PDF)", status == 200 and isinstance(pdf, bytes) and pdf[:4] == b"%PDF", status)
    status, _, _ = call("GET", f"/api/v1/architect/requests/{req}", token=b)
    check("customer cannot read the architect's request view (403)", status == 403, status)
    status, body, _ = call("POST", f"/api/v1/architect/requests/{req}/feedback", {"kind": "FEEDBACK", "body": "Early note"}, token=atok)
    check("feedback before acceptance refused (409)", status == 409, body)
    status, body, _ = call("POST", f"/api/v1/architect/requests/{req}/respond", {"decision": "ACCEPTED", "note": "Happy to review"}, token=atok)
    check("architect accepts the request", status == 200 and body.get("decision") == "ACCEPTED", body)
    status, _, _ = call("POST", f"/api/v1/architect/requests/{req}/respond", {"decision": "DECLINED"}, token=atok)
    check("second response refused (409)", status == 409, status)
    status, body, _ = call("POST", f"/api/v1/architect/requests/{req}/feedback", {"kind": "REVISION_REQUEST",
                                                                                "body": "Please widen the kitchen by 0.5 m."}, token=atok)
    check("architect records a revision request (201)", status == 201, body)
    status, conv, _ = call("GET", f"{path}/{req}/conversation", token=a)
    check("customer sees the response and feedback", status == 200 and conv["request"]["decision"] == "ACCEPTED" and len(conv["feedback"]) == 1, conv)
    status, _, _ = call("GET", f"{path}/{req}/conversation", token=b)
    check("cross-user conversation denied (404)", status == 404, status)
    status, unread, _ = call("GET", "/api/v1/notifications/unread-count", token=a)
    check("customer has unread notifications (response + feedback)", status == 200 and unread.get("unread", 0) >= 2, unread)
    status, items, _ = call("GET", "/api/v1/notifications?unreadOnly=true", token=a)
    check("notifications list the architect response", any(n["kind"] == "ARCHITECT_RESPONSE" for n in items.get("items", [])), items)
    call("POST", "/api/v1/notifications/read-all", token=a)
    check("mark-all-read clears the unread count", call("GET", "/api/v1/notifications/unread-count", token=a)[1].get("unread") == 0)
    status, anote, _ = call("GET", "/api/v1/notifications", token=atok)
    check("architect was notified of the new request", any(n["kind"] == "ARCHITECT_REQUEST_RECEIVED" for n in anote.get("items", [])), anote)
    delivered = False
    for _ in range(30):
        with ur.urlopen("http://127.0.0.1:8025/api/v1/search?query=" + ur.quote("to:" + state["alice"]["email"]), timeout=10) as r:
            delivered = any("architect" in m.get("Subject", "") for m in json.loads(r.read()).get("messages", []))
        if delivered:
            break
        time.sleep(1)
    check("outbox delivered the notification email to the local mail capture", delivered)

    status, _, _ = call("POST", f"{path}/{req}/withdraw", {"expectedStateVersion": 1}, token=a)
    check("customer withdraws consent", status == 200, status)
    status, _, _ = call("GET", f"/api/v1/architect/requests/{req}/report", token=atok)
    check("withdrawal revokes the architect's report access (404)", status == 404, status)

    # Requirement catalogue: persistent, versioned, admin-published (product preferences, not regulations).
    sample = json.load(open(os.path.expanduser("~/bm-captain/src/docs/captain-development/samples/requirement-catalogue.synthetic.json")))
    version = "captain-dev-" + uuid.uuid4().hex[:8]
    sample["version"] = version
    status, body, _ = call("POST", "/api/v1/admin/catalogues", {"version": version + "x", "provenance": "DEVELOPMENT_SYNTHETIC", "document": sample},
                           token=admin)
    check("catalogue whose document version differs from its key refused (422)", status == 422, body)
    status, draft, _ = call("POST", "/api/v1/admin/catalogues", {"version": version, "provenance": "DEVELOPMENT_SYNTHETIC", "document": sample},
                            token=admin)
    check("admin imports a DRAFT catalogue", status == 201 and draft.get("status") == "DRAFT", draft)
    status, _, _ = call("POST", "/api/v1/admin/catalogues", {"version": version, "provenance": "DEVELOPMENT_SYNTHETIC", "document": sample}, token=admin)
    check("duplicate catalogue version refused (409)", status == 409, status)
    status, pub, _ = call("POST", f"/api/v1/admin/catalogues/{version}/publish", {"expectedRowVersion": 1}, token=admin)
    check("admin publishes the catalogue", status == 200 and pub.get("status") == "PUBLISHED", pub)
    status, ret, _ = call("POST", f"/api/v1/admin/catalogues/{version}/retire", {"expectedRowVersion": 2}, token=admin)
    check("admin retires it (historical version kept)", status == 200 and ret.get("status") == "RETIRED", ret)
    status, got, _ = call("GET", f"/api/v1/admin/catalogues/{version}", token=admin)
    check("historical catalogue lookup returns the exact document", status == 200 and got["document"]["version"] == version, status)
    status, _, _ = call("GET", "/api/v1/admin/catalogues", token=b)
    check("customer cannot manage catalogues (403)", status == 403, status)


# Phase 8 runs before phase 7, which stops python-services and exercises rate limiting.
EXTRA_PHASES = [(4, phase4), (5, phase5), (6, phase6), (8, phase8), (9, phase9), (7, phase7)]


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

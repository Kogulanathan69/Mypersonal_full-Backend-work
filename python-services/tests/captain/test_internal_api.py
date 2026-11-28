"""Captain internal API: fail-closed auth, land validation, generation envelopes and plan validation.

All data is synthetic (labelled fixtures); nothing here represents a real parcel, rule or reviewer.
"""
import json
from uuid import UUID

import pytest
from fastapi.testclient import TestClient

from app.internal.openings import validate_openings
from app.internal.auth import AUTH_HEADER, InternalAuth
from app.main import create_app
from app.plans.generation.contracts import GenerationResult

TOKEN = "synthetic-python-internal-token-0123456789abcdef"
PROJECT = "00000000-0000-4000-8000-0000000000a1"
LAND = "00000000-0000-4000-8000-0000000000a2"
REQUIREMENTS = "00000000-0000-4000-8000-0000000000a3"
REVIEWER = "00000000-0000-4000-8000-0000000000a4"
FRAME = {"type": "localCartesian", "unit": "m", "origin": {"x": 0, "y": 0}, "xDirection": "right", "yDirection": "up"}


def client(auth=None):
    return TestClient(create_app(auth or InternalAuth.from_tokens([TOKEN])), raise_server_exceptions=False)


def post(c, path, body, token=TOKEN):
    headers = {"Content-Type": "application/json"}
    if token is not None:
        headers[AUTH_HEADER] = token
    return c.post(path, content=json.dumps(body), headers=headers)


def land(width=12, height=10):
    ring = [{"x": x, "y": y} for x, y in [(0, 0), (width, 0), (width, height), (0, height), (0, 0)]]
    return {"schemaVersion": "metric-land/0.1-draft", "coordinateSystem": FRAME, "boundary": {"exterior": ring, "holes": []}}


def setback(edge, distance):
    return {
        "ruleId": f"synthetic-edge-{edge}", "type": "setback",
        "source": {"sourceVersion": "synthetic-source-v1", "sourceLocator": f"fixture/edge/{edge}",
                   "jurisdiction": "Synthetic fixture only"},
        "parcelEdgeIndex": edge, "distanceM": distance,
        "applicability": {"status": "APPLICABLE", "basis": "Synthetic scenario"},
        "reviewedBy": REVIEWER, "reviewReference": "synthetic-review-v1",
    }


def generation_input(width=12, height=10, *, evidence="REVIEWED", rooms=None):
    rooms = rooms or [("bedroom", 3, 3), ("livingRoom", 4, 3), ("kitchen", 2.5, 2.5), ("bathroom", 2, 2)]
    regulatory = (
        {"status": "REVIEWED", "ruleSetVersion": "synthetic-captain-v1",
         "constraints": [setback(i, 1) for i in range(4)], "gaps": []}
        if evidence == "REVIEWED"
        else {"status": "MISSING", "ruleSetVersion": None, "constraints": [], "gaps": [{"code": "NOT_INTEGRATED"}]}
    )
    return {
        "schemaVersion": "m6-generation-input/0.1-draft",
        "projectId": PROJECT, "landRevisionId": LAND, "requirementsRevisionId": REQUIREMENTS,
        "land": land(width, height),
        "roomProgram": {"level": 0, "catalogueVersion": "synthetic-fixture-v1", "rooms": [
            {"slotId": str(UUID(int=0x100 + i)), "roomType": t, "dimensions": {"minWidthM": w, "minDepthM": d}}
            for i, (t, w, d) in enumerate(rooms)]},
        "adjacencyPreferences": [],
        "regulatoryEvidence": regulatory,
        "seed": 7,
    }


def run(c, gi, synthetic=True):
    return post(c, "/internal/v1/generation/run",
                {"schemaVersion": "captain-generation-run/1", "input": gi, "syntheticReviewedSetbacks": synthetic})


# --- authentication (fail closed) ---------------------------------------------------------------------------

def test_health_is_open_and_protected_routes_need_the_token():
    c = client()
    assert c.get("/health").status_code == 200
    assert post(c, "/internal/v1/land/validate", {}, token=None).json() == {"code": "UNAUTHENTICATED"}
    assert post(c, "/internal/v1/land/validate", {}, token=None).status_code == 401
    assert post(c, "/internal/v1/land/validate", {}, token="x" * 40).status_code == 401
    assert post(c, "/internal/v1/land/validate", {}, token=TOKEN).status_code == 422  # authenticated, then validated


def test_unconfigured_service_fails_closed_with_503():
    c = TestClient(create_app(InternalAuth((), True)))
    response = post(c, "/internal/v1/generation/run", {})
    assert response.status_code == 503 and response.json() == {"code": "AUTH_NOT_CONFIGURED"}


def test_repeated_token_header_is_rejected():
    c = client()
    response = c.post("/internal/v1/land/validate", content="{}",
                      headers=[("Content-Type", "application/json"), (AUTH_HEADER, TOKEN), (AUTH_HEADER, TOKEN)])
    assert response.status_code == 401


def test_disabling_auth_requires_explicit_development_environment(monkeypatch):
    monkeypatch.setenv("BUILDMIND_PY_AUTH", "disabled")
    monkeypatch.delenv("BUILDMIND_PY_ENVIRONMENT", raising=False)
    with pytest.raises(RuntimeError):
        InternalAuth.from_environment()
    monkeypatch.setenv("BUILDMIND_PY_ENVIRONMENT", "production")
    with pytest.raises(RuntimeError):
        InternalAuth.from_environment()
    monkeypatch.setenv("BUILDMIND_PY_ENVIRONMENT", "development")
    assert InternalAuth.from_environment().required is False


def test_errors_never_reflect_submitted_values():
    response = post(client(), "/internal/v1/generation/run", {"schemaVersion": "secret-value-xyz"})
    assert response.status_code == 422 and "secret-value-xyz" not in response.text


def test_non_json_content_type_is_rejected():
    c = client()
    response = c.post("/internal/v1/land/validate", content="{}",
                      headers={"Content-Type": "text/plain", AUTH_HEADER: TOKEN})
    assert response.status_code == 415


# --- land validation -----------------------------------------------------------------------------------------

def test_land_validation_accepts_a_simple_parcel_and_reports_area():
    body = {"schemaVersion": "captain-land-validation/1", "projectId": PROJECT, "landRevisionId": LAND, "land": land()}
    result = post(client(), "/internal/v1/land/validate", body).json()
    assert result == {"schemaVersion": "captain-land-validation/1", "valid": True, "code": "LAND_VALID",
                      "areaM2": 120.0, "edgeCount": 4}


def test_land_validation_rejects_a_self_intersecting_parcel():
    bowtie = land()
    bowtie["boundary"]["exterior"] = [{"x": x, "y": y} for x, y in [(0, 0), (10, 10), (10, 0), (0, 10), (0, 0)]]
    body = {"schemaVersion": "captain-land-validation/1", "projectId": PROJECT, "landRevisionId": LAND, "land": bowtie}
    result = post(client(), "/internal/v1/land/validate", body).json()
    assert result["valid"] is False and result["code"] == "POLYGON_INVALID"


# --- generation envelopes ------------------------------------------------------------------------------------

def test_success_returns_three_distinct_contract_valid_alternatives():
    response = run(client(), generation_input())
    assert response.status_code == 200
    result = response.json()
    # Regression: the envelope must round-trip through the unchanged contract in JSON mode.
    GenerationResult.model_validate_json(json.dumps(result))
    assert result["outcome"] == "SUCCESS" and result["reasonCode"] is None
    assert len(result["alternatives"]) == 3
    assert len({a["geometry"]["planId"] for a in result["alternatives"]}) == 3
    assert len({a["diversitySignature"]["value"] for a in result["alternatives"]}) == 3
    for alternative in result["alternatives"]:
        floor = alternative["geometry"]["floors"][0]
        assert sorted(r["type"] for r in floor["rooms"]) == ["bathroom", "bedroom", "kitchen", "livingRoom"]
        doors = [o for o in floor["openings"] if o["type"] == "door"]
        windows = [o for o in floor["openings"] if o["type"] == "window"]
        assert len(doors) >= len(floor["rooms"]) and windows  # entrance + one door per other room; windows present
        assert not [c for c, s in validate_openings(floor) if s == "CRITICAL" or c in {"ROOM_NOT_REACHABLE", "NO_ENTRANCE"}]
        codes = {d["code"] for d in alternative["validation"]["diagnostics"]}
        assert {"SYNTHETIC_REGULATORY_EVIDENCE", "OPENING_DIMENSIONS_DESIGN_PREFERENCE"} <= codes
        assert "PHYSICAL_ACCESS_NOT_EVALUATED" not in codes
        assert alternative["validation"]["hasBlockingIssues"] is False
        UUID(alternative["validation"]["validationId"])


def test_success_is_deterministic_for_the_same_input_except_timestamps():
    def strip(result):
        for alternative in result["alternatives"]:
            alternative["validation"]["validatedAtUtc"] = None
        return result
    c = client()
    assert strip(run(c, generation_input()).json()) == strip(run(c, generation_input()).json())


@pytest.mark.parametrize("kwargs,synthetic,outcome,reason", [
    ({"evidence": "MISSING"}, True, "NEEDS_REVIEW", "REGULATORY_EVIDENCE_MISSING"),
    ({}, False, "NEEDS_REVIEW", "REVIEWED_SETBACK_SNAPSHOT_MISSING"),
    ({"width": 5, "height": 5}, True, "INFEASIBLE", "BUILDABLE_AREA_INSUFFICIENT"),
])
def test_unsuccessful_generation_is_truthful_and_has_no_alternatives(kwargs, synthetic, outcome, reason):
    result = run(client(), generation_input(**kwargs), synthetic).json()
    GenerationResult.model_validate_json(json.dumps(result))
    assert result["outcome"] == outcome and result["reasonCode"] == reason
    assert result["alternatives"] == []
    assert any(d["severity"] == "CRITICAL" for d in result["diagnostics"])


def test_invalid_generation_input_is_rejected_without_detail():
    gi = generation_input()
    gi["roomProgram"]["rooms"][0]["roomType"] = "garage"
    response = run(client(), gi)
    assert response.status_code == 422 and response.json() == {"code": "VALIDATION_FAILED"}


# --- plan validation -----------------------------------------------------------------------------------------

def _plan_body(geometry, **extra):
    return {"schemaVersion": "captain-plan-validation/1", "projectId": PROJECT, "landRevisionId": LAND,
            "land": land(), "geometry": geometry, **extra}


def _generated_geometry():
    return run(client(), generation_input()).json()["alternatives"][0]["geometry"]


def test_generated_geometry_passes_plan_validation_with_matching_counts():
    counts = {"bedroom": 1, "livingRoom": 1, "kitchen": 1, "bathroom": 1}
    result = post(client(), "/internal/v1/plans/validate",
                  _plan_body(_generated_geometry(), setbackDistancesM=[1, 1, 1, 1], expectedRoomCounts=counts)).json()
    assert result["geometryValid"] is True and result["requirementsMatch"] is True and result["diagnostics"] == []
    assert len(result["geometryHash"]) == 64


def test_overlap_outside_parcel_and_count_mismatch_are_reported():
    geometry = _generated_geometry()
    rooms = geometry["floors"][0]["rooms"]
    rooms[1]["polygon"] = [dict(p) for p in rooms[0]["polygon"]]  # duplicate footprint -> overlap
    rooms[2]["polygon"] = [{"x": x, "y": y} for x, y in [(20, 20), (22, 20), (22, 22), (20, 22), (20, 20)]]
    result = post(client(), "/internal/v1/plans/validate",
                  _plan_body(geometry, expectedRoomCounts={"bedroom": 2})).json()
    codes = {d["code"] for d in result["diagnostics"]}
    assert result["geometryValid"] is False and result["requirementsMatch"] is False
    assert {"ROOMS_OVERLAP", "ROOM_OUTSIDE_PARCEL", "ROOM_COUNTS_MISMATCH"} <= codes


def test_plan_schema_violations_are_reported_not_coerced():
    geometry = _generated_geometry()
    geometry["floors"][0]["level"] = "0"  # string, not an integer: no coercion
    result = post(client(), "/internal/v1/plans/validate", _plan_body(geometry)).json()
    assert result["geometryValid"] is False and result["diagnostics"][0]["code"] == "PLAN_SCHEMA_INVALID"


# --- edit preview (existing M3 operations engine) ------------------------------------------------------------

def _edit_body(geometry, command_type, target, payload):
    return {"schemaVersion": "captain-plan-edit-preview/1", "baseRevisionId": str(UUID(int=0xBA5E)),
            "sourceGeometry": geometry,
            "command": {"commandId": str(UUID(int=0xC0DE)), "commandType": command_type,
                        "floorId": geometry["floors"][0]["id"], "targetId": target, "payload": payload}}


def test_edit_preview_moves_a_room_and_reports_changed_entities():
    geometry = _generated_geometry()
    room = geometry["floors"][0]["rooms"][0]
    result = post(client(), "/internal/v1/plans/edit-preview",
                  _edit_body(geometry, "moveRoom", room["id"], {"deltaX": 0.5, "deltaY": 0})).json()
    assert result["status"] == "accepted" and result["validation"] is None
    moved = next(r for r in result["candidateGeometry"]["floors"][0]["rooms"] if r["id"] == room["id"])
    assert moved["polygon"][0]["x"] == room["polygon"][0]["x"] + 0.5
    assert room["id"] in result["changedEntityIds"]


def test_edit_preview_rejects_unknown_targets_without_geometry():
    geometry = _generated_geometry()
    result = post(client(), "/internal/v1/plans/edit-preview",
                  _edit_body(geometry, "moveRoom", str(UUID(int=0xDEAD)), {"deltaX": 1, "deltaY": 0})).json()
    assert result["status"] == "rejected" and result["candidateGeometry"] is None
    assert result["diagnostics"][0]["code"] == "ROOM_NOT_FOUND"


def test_edit_preview_requires_token_and_strict_json():
    geometry = _generated_geometry()
    body = _edit_body(geometry, "moveRoom", geometry["floors"][0]["rooms"][0]["id"], {"deltaX": 1, "deltaY": 0})
    assert post(client(), "/internal/v1/plans/edit-preview", body, token=None).status_code == 401
    body["command"]["commandId"] = 123  # not a UUID string: no coercion
    assert post(client(), "/internal/v1/plans/edit-preview", body).status_code == 422
    duplicate = client().post("/internal/v1/plans/edit-preview", content='{"schemaVersion":"a","schemaVersion":"b"}',
                              headers={"Content-Type": "application/json", AUTH_HEADER: TOKEN})
    assert duplicate.status_code == 422


def test_vastu_ranking_remains_internal_nonblocking_and_hidden_from_response():
    response = run(client(), generation_input())
    assert response.status_code == 200
    text = response.text.lower()
    assert "vastu" not in text
    assert len(response.json()["alternatives"]) == 3

import pytest
from pydantic import ValidationError

from app.geometry.footprint.contracts import Diagnostic, FeasibilityResult

REVISIONS = {
    "projectId": "10000000-0000-4000-8000-000000000001",
    "landRevisionId": "10000000-0000-4000-8000-000000000002",
    "requirementsRevisionId": "10000000-0000-4000-8000-000000000003",
}
CRITICAL = {"code": "UNCHECKED", "severity": "CRITICAL", "message": "Check unavailable."}
COORDINATE_SYSTEM = {
    "type": "localCartesian", "unit": "m", "origin": {"x": 0, "y": 0},
    "xDirection": "right", "yDirection": "up",
}


@pytest.fixture
def feasibility():
    return {
        **REVISIONS,
        "schemaVersion": "m6-feasibility-result/0.1-draft",
        "status": "FEASIBLE",
        "buildableFootprint": {
            "schemaVersion": "metric-land/0.1-draft",
            "coordinateSystem": COORDINATE_SYSTEM,
            "boundary": {"exterior": [
                {"x": 0, "y": 0}, {"x": 5, "y": 0}, {"x": 5, "y": 4},
                {"x": 0, "y": 4}, {"x": 0, "y": 0},
            ]},
        },
        "landAreaM2": 30,
        "buildableAreaM2": 20,
        "requiredAreaM2": 20,
        "regulatoryEvidenceStatus": "REVIEWED",
        "ruleSetVersion": "synthetic",
        "feasibilityPolicyVersion": "synthetic",
        "diagnostics": [],
    }


def test_feasible_round_trip(feasibility):
    result = FeasibilityResult.model_validate(feasibility)
    assert FeasibilityResult.model_validate_json(result.model_dump_json()) == result


@pytest.mark.parametrize("status, area", [("INFEASIBLE", 0), ("NEEDS_REVIEW", None), ("UNSUPPORTED", None)])
def test_empty_vs_unchecked(feasibility, status, area):
    result = FeasibilityResult.model_validate({
        **feasibility, "status": status, "buildableFootprint": None,
        "buildableAreaM2": area, "landAreaM2": 30 if area == 0 else None,
        "requiredAreaM2": 20 if area == 0 else None,
        "regulatoryEvidenceStatus": "MISSING", "ruleSetVersion": None,
        "diagnostics": [CRITICAL],
    })
    assert result.model_dump(mode="json")["buildableAreaM2"] == area


@pytest.mark.parametrize("changes", [
    {"schemaVersion": "unsupported"}, {"status": "PASS"}, {"newField": True},
    {"projectId": "00000000-0000-0000-0000-000000000000"},
    {"landRevisionId": "10000000000040008000000000000002"},
    {"requirementsRevisionId": None}, {"requiredAreaM2": None},
    {"landAreaM2": None}, {"buildableAreaM2": 31}, {"buildableAreaM2": 0},
    {"buildableFootprint": None}, {"requiredAreaM2": 21}, {"requiredAreaM2": "20"},
    {"landAreaM2": True}, {"regulatoryEvidenceStatus": "MISSING"},
    {"regulatoryEvidenceStatus": "UNVERIFIED"}, {"regulatoryEvidenceStatus": "CONFLICTING"},
    {"diagnostics": [CRITICAL]}, {"ruleSetVersion": None},
    {"status": "NEEDS_REVIEW"}, {"diagnostics": [CRITICAL] * 1_001},
])
def test_invalid_feasibility(feasibility, changes):
    with pytest.raises(ValidationError):
        FeasibilityResult.model_validate({**feasibility, **changes})


@pytest.mark.parametrize("field", ["landAreaM2", "buildableAreaM2", "requiredAreaM2"])
@pytest.mark.parametrize("value", [-1, float("inf"), float("-inf"), float("nan")])
def test_invalid_area(feasibility, field, value):
    with pytest.raises(ValidationError):
        FeasibilityResult.model_validate({**feasibility, field: value})


@pytest.mark.parametrize("pointer", ["", "/floors/0/~0~1"])
def test_diagnostic_pointer(pointer):
    diagnostic = Diagnostic.model_validate({
        **CRITICAL, "path": pointer, "relatedEntityIds": [REVISIONS["projectId"]],
        "sourceReferenceIds": ["source-v1"],
    })
    assert diagnostic.path == pointer


@pytest.mark.parametrize("changes", [
    {"severity": "ERROR"}, {"code": "lowercase"}, {"message": " "},
    {"message": "raw\ntrace"}, {"message": "x" * 501}, {"path": "/bad~"},
    {"path": "floors"}, {"sourceReferenceIds": ["r"] * 101},
    {"relatedEntityIds": [REVISIONS["projectId"]] * 101},
    {"relatedEntityIds": ["00000000-0000-0000-0000-000000000000"]},
])
def test_invalid_diagnostic(changes):
    with pytest.raises(ValidationError):
        Diagnostic.model_validate({**CRITICAL, **changes})

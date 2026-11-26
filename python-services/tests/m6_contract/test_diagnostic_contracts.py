import pytest
from pydantic import ValidationError

from app.geometry.footprint.contracts import Diagnostic
from app.plans.generation.job_contracts import DIAGNOSTIC_REGISTRY, JobFailure, RegisteredDiagnostic

EXPECTED_CODES = {
    "INVALID_IDENTIFIER", "SCHEMA_VERSION_UNSUPPORTED", "METRIC_UNITS_REQUIRED",
    "COORDINATE_SYSTEM_UNSUPPORTED", "INPUT_REVISION_MISMATCH",
    "POLYGON_INVALID", "ROOM_OVERLAP", "OUTSIDE_BUILDABLE_FOOTPRINT", "WALL_TOPOLOGY_INVALID",
    "OPENING_WALL_REFERENCE_INVALID", "OPENING_OUTSIDE_WALL", "OPENING_COLLISION", "ROOM_INACCESSIBLE",
    "ROOM_REQUIREMENT_UNMET", "SETBACK_EVIDENCE_MISSING", "REGULATORY_EVIDENCE_CONFLICT",
    "REQUIRED_AREA_POLICY_UNAVAILABLE", "VALIDATION_EVIDENCE_STALE",
    "VALIDATION_UNAVAILABLE", "GENERATION_UNAVAILABLE", "SEARCH_EXHAUSTED",
    "INSUFFICIENT_DISTINCT_ALTERNATIVES", "BUILDABLE_AREA_INSUFFICIENT",
    "GENERATION_TIMEOUT", "GENERATION_FAILED",
}
SYNTHETIC = {
    "code": "GENERATION_FAILED", "severity": "CRITICAL", "message": "Synthetic diagnostic.",
}


def test_registry_is_complete_and_read_only():
    assert set(DIAGNOSTIC_REGISTRY) == EXPECTED_CODES
    with pytest.raises(TypeError):
        DIAGNOSTIC_REGISTRY["GENERATION_FAILED"] = "WARNING"


@pytest.mark.parametrize("code", sorted(EXPECTED_CODES))
def test_registered_classification(code):
    assert DIAGNOSTIC_REGISTRY[code] == "CRITICAL"
    result = RegisteredDiagnostic.model_validate({**SYNTHETIC, "code": code})
    assert isinstance(result, Diagnostic)
    assert RegisteredDiagnostic.model_validate_json(result.model_dump_json()) == result
    with pytest.raises(ValidationError, match="code and severity must match the registry"):
        RegisteredDiagnostic.model_validate({**SYNTHETIC, "code": code, "severity": "WARNING"})


@pytest.mark.parametrize("changes", [
    {"code": "CUSTOM_DIAGNOSTIC"}, {"severity": "ERROR"}, {"message": "x" * 501},
    {"message": "Unsafe multi-line\nmessage"}, {"path": "/invalid~x"},
    {"sourceReferenceIds": ["source"] * 101}, {"unrecognizedField": True},
])
def test_registry_retains_diagnostic_shape(changes):
    with pytest.raises(ValidationError):
        RegisteredDiagnostic.model_validate({**SYNTHETIC, **changes})


def test_d01c_diagnostics_keep_open_codes_and_severity():
    warning = Diagnostic.model_validate({**SYNTHETIC, "severity": "WARNING"})
    custom = Diagnostic.model_validate({**SYNTHETIC, "code": "CUSTOM_DIAGNOSTIC"})
    assert warning.severity == "WARNING"
    assert custom.code == "CUSTOM_DIAGNOSTIC"


@pytest.mark.parametrize("changes", [
    {"reasonCode": "SEARCH_EXHAUSTED"}, {"diagnostics": []}, {"diagnostics": (SYNTHETIC,)},
    {"diagnostics": [SYNTHETIC] * 1_001},
])
def test_failure_diagnostics_must_support_the_reason(changes):
    data = {"reasonCode": SYNTHETIC["code"], "diagnostics": [SYNTHETIC], **changes}
    with pytest.raises(ValidationError):
        JobFailure.model_validate(data)

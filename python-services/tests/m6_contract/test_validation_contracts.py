from datetime import timezone

import pytest
from pydantic import ValidationError

from app.validation.contracts import ValidationResult
from test_feasibility_contracts import CRITICAL, REVISIONS


def synthetic_validation():
    return {
        **REVISIONS,
        "validationId": "10000000-0000-4000-8000-000000000010",
        "candidateId": "10000000-0000-4000-8000-000000000011",
        "geometryHash": "a" * 64,
        "geometryValid": True,
        "requirementsMatch": True,
        "regulatoryEvidenceStatus": "REVIEWED",
        "hasBlockingIssues": False,
        "validatorVersion": "synthetic-validator-v1",
        "ruleSetVersion": "synthetic-rules-v1",
        "validatedAtUtc": "2026-11-19T12:00:00+05:30",
        "diagnostics": [],
    }


def test_validation_serialization():
    result = ValidationResult.model_validate(synthetic_validation())
    assert result.validatedAtUtc.tzinfo is timezone.utc
    assert result.model_dump(mode="json")["validatedAtUtc"] == "2026-11-19T06:30:00Z"
    assert ValidationResult.model_validate_json(result.model_dump_json()) == result


@pytest.mark.parametrize("changes", [
    {"geometryValid": None}, {"geometryValid": False},
    {"requirementsMatch": None}, {"requirementsMatch": False},
    {"regulatoryEvidenceStatus": "MISSING", "ruleSetVersion": None},
    {"regulatoryEvidenceStatus": "CONFLICTING"},
    {"regulatoryEvidenceStatus": "UNVERIFIED"},
])
def test_incomplete_or_failed_requires_blocking(changes):
    data = {**synthetic_validation(), **changes}
    with pytest.raises(ValidationError, match="requires a critical diagnostic"):
        ValidationResult.model_validate(data)
    result = ValidationResult.model_validate({
        **data, "hasBlockingIssues": True, "diagnostics": [CRITICAL],
    })
    assert result.hasBlockingIssues
    for field, value in changes.items():
        assert getattr(result, field) == value


@pytest.mark.parametrize("changes", [
    {"hasBlockingIssues": True}, {"diagnostics": [CRITICAL]},
    {"geometryValid": "true"}, {"requirementsMatch": 1}, {"hasBlockingIssues": 0},
    {"validatorVersion": " "}, {"ruleSetVersion": None},
    {"regulatoryEvidenceStatus": "VERIFIED"},
    {"geometryHash": "A" * 64}, {"geometryHash": "g" * 64}, {"geometryHash": "a" * 63},
    {"geometryHash": "sha256:" + "a" * 64}, {"geometryHash": "a" * 64 + "\n"},
    {"validatedAtUtc": "2026-11-19T12:00:00"}, {"validatedAtUtc": "2026-02-30T00:00:00Z"},
    {"validatedAtUtc": "2026-11-19T12:00:00+00:60"},
    {"validatedAtUtc": "0001-01-01T00:00:00+01:00"}, {"validatedAtUtc": 0},
    {"validationId": "00000000-0000-0000-0000-000000000000"}, {"candidateId": "bad"},
    {"landRevisionId": ""}, {"diagnostics": ()},
])
def test_invalid_validation_result(changes):
    with pytest.raises(ValidationError):
        ValidationResult.model_validate({**synthetic_validation(), **changes})


def test_unchecked_field_is_required():
    data = synthetic_validation()
    del data["geometryValid"]
    with pytest.raises(ValidationError, match="Field required"):
        ValidationResult.model_validate(data)

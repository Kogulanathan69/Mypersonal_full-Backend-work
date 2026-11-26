import json
from pathlib import Path
from uuid import UUID

import pytest
from pydantic import ValidationError

from app.plan_editor.schemas.geometry import PlanGeometry
from app.plans.generation.contracts import GenerationResult
from test_feasibility_contracts import COORDINATE_SYSTEM, CRITICAL, REVISIONS
from test_validation_contracts import synthetic_validation

FIXTURES = Path(__file__).parent / "fixtures"


def synthetic_id(number):
    return str(UUID(int=number))


@pytest.fixture
def synthetic_success():
    """Structural contract data only. Empty floors represent no real floor plan."""
    alternatives = []
    for index in (100, 110, 120):
        candidate_id = synthetic_id(index + 1)
        alternatives.append({
            **REVISIONS,
            "alternativeId": synthetic_id(index),
            "algorithmVersion": "synthetic-v1",
            "geometry": {
                **REVISIONS,
                "schemaVersion": "0.1-draft",
                "planId": candidate_id,
                "coordinateSystem": COORDINATE_SYSTEM,
                "floors": [{"id": synthetic_id(index + 2), "level": 0}],
            },
            "metrics": {"floorAreaM2": 1, "footprintAreaM2": 1, "roomCount": 0},
            "validation": {
                **synthetic_validation(),
                "candidateId": candidate_id,
                "validationId": synthetic_id(index + 3),
            },
            "diversitySignature": {"version": "synthetic-v1", "value": str(index)},
        })
    return {
        **REVISIONS,
        "schemaVersion": "m6-generation-result/0.1-draft",
        "outcome": "SUCCESS",
        "reasonCode": None,
        "algorithmVersion": "synthetic-v1",
        "seed": 42,
        "alternatives": alternatives,
        "diagnostics": [],
    }


def test_success_serializes_m3_geometry(synthetic_success):
    result = GenerationResult.model_validate(synthetic_success)
    first = result.alternatives[0]
    assert type(first.geometry) is PlanGeometry
    assert PlanGeometry.model_validate_json(first.geometry.model_dump_json()) == first.geometry
    assert GenerationResult.model_validate_json(result.model_dump_json()) == result
    assert GenerationResult.model_validate(result.model_dump(mode="json")) == result


def test_unavailable_fixture():
    source = json.loads((FIXTURES / "generation-input.v0.1-draft.json").read_text())
    result = GenerationResult.model_validate_json(
        (FIXTURES / "generation-unavailable.v0.1-draft.json").read_bytes()
    ).model_dump(mode="json")
    assert {key: result[key] for key in REVISIONS} == {key: source[key] for key in REVISIONS}
    assert result["seed"] == source["seed"] == 42
    assert result["outcome"] == "UNAVAILABLE"
    assert result["reasonCode"] == "GENERATION_UNAVAILABLE"
    assert result["algorithmVersion"] is None
    assert result["alternatives"] == []
    assert result["diagnostics"][0]["severity"] == "CRITICAL"
    assert "synthetic" in result["diagnostics"][0]["message"]


@pytest.mark.parametrize("count", [0, 1, 2, 4])
def test_success_requires_exactly_three(synthetic_success, count):
    synthetic_success["alternatives"] = (synthetic_success["alternatives"] * 2)[:count]
    with pytest.raises(ValidationError):
        GenerationResult.model_validate(synthetic_success)


@pytest.mark.parametrize("outcome", ["UNAVAILABLE", "FAILED", "INFEASIBLE", "NEEDS_REVIEW", "UNSUPPORTED"])
def test_non_success_forbids_alternatives(synthetic_success, outcome):
    synthetic_success.update(outcome=outcome, reasonCode="GENERATION_UNAVAILABLE", diagnostics=[CRITICAL])
    with pytest.raises(ValidationError, match="requires no alternatives"):
        GenerationResult.model_validate(synthetic_success)


@pytest.mark.parametrize("changes", [
    {"schemaVersion": "0.1-draft"}, {"outcome": "COMPLETED"},
    {"reasonCode": "UNAVAILABLE"}, {"algorithmVersion": None}, {"diagnostics": [CRITICAL]},
    {"seed": -1}, {"seed": True}, {"seed": "42"}, {"seed": 2**31},
    {"projectId": synthetic_id(999)}, {"landRevisionId": synthetic_id(999)},
    {"requirementsRevisionId": synthetic_id(999)}, {"extra": "unexpected"},
])
def test_invalid_success_envelope(synthetic_success, changes):
    with pytest.raises(ValidationError):
        GenerationResult.model_validate({**synthetic_success, **changes})


@pytest.mark.parametrize("field", ["alternativeId", "diversitySignature", "geometry", "validation"])
def test_duplicates(synthetic_success, field):
    first, second, _ = synthetic_success["alternatives"]
    if field == "geometry":
        second["geometry"]["planId"] = first["geometry"]["planId"]
        second["validation"]["candidateId"] = first["geometry"]["planId"]
    elif field == "validation":
        second["validation"]["validationId"] = first["validation"]["validationId"]
    else:
        second[field] = first[field]
    with pytest.raises(ValidationError, match="distinct"):
        GenerationResult.model_validate(synthetic_success)


@pytest.mark.parametrize("container,field,value", [
    ("geometry", "schemaVersion", "v2"),
    ("geometry", "planId", synthetic_id(0)),
    ("geometry", "planId", UUID(int=101).hex),
    ("geometry", "landRevisionId", synthetic_id(999)),
    ("geometry", "floors", []),
    ("geometry", "floors", [{"id": synthetic_id(1), "level": "0"}]),
    ("geometry", "floors", [{"id": synthetic_id(1), "level": 0}] * 101),
    ("geometry", "floors", [{"id": synthetic_id(0), "level": 0}]),
    ("geometry", "floors", [{"id": synthetic_id(101), "level": 0}]),
    ("geometry", "floors", [{
        "id": synthetic_id(1), "level": 0, "openings": [{
            "id": synthetic_id(2), "wallId": synthetic_id(3), "type": "door", "offset": 0, "width": 1,
        }],
    }]),
    ("metrics", "floorAreaM2", float("nan")),
    ("metrics", "footprintAreaM2", float("inf")),
    ("metrics", "footprintAreaM2", -1),
    ("metrics", "roomCount", 100_001),
    ("metrics", "roomCount", True),
    ("validation", "candidateId", synthetic_id(999)),
    ("validation", "requirementsRevisionId", synthetic_id(999)),
    ("validation", "ruleSetVersion", "stale-rules"),
    ("diversitySignature", "value", "x" * 129),
    ("diversitySignature", "version", "incompatible"),
])
def test_invalid_alternative(synthetic_success, container, field, value):
    synthetic_success["alternatives"][0][container][field] = value
    with pytest.raises(ValidationError):
        GenerationResult.model_validate(synthetic_success)


@pytest.mark.parametrize("field,value", [
    ("geometryValid", None), ("geometryValid", False),
    ("requirementsMatch", False), ("regulatoryEvidenceStatus", "MISSING"),
])
def test_success_forbids_individually_blocking_validation(synthetic_success, field, value):
    validation = synthetic_success["alternatives"][0]["validation"]
    validation.update({field: value}, hasBlockingIssues=True, diagnostics=[CRITICAL])
    with pytest.raises(ValidationError, match="nonblocking validation"):
        GenerationResult.model_validate(synthetic_success)


def test_warning_allows_success(synthetic_success):
    warning = {**CRITICAL, "severity": "WARNING"}
    synthetic_success["diagnostics"] = [warning]
    synthetic_success["alternatives"][0]["validation"]["diagnostics"] = [warning]
    assert GenerationResult.model_validate(synthetic_success).outcome == "SUCCESS"


@pytest.mark.parametrize("invalid_wall_reference", [False, True])
def test_m3_geometry_wall_references(synthetic_success, invalid_wall_reference):
    geometry = synthetic_success["alternatives"][0]["geometry"]
    floor = geometry["floors"][0]
    points = [{"x": 0, "y": 0}, {"x": 1, "y": 0}, {"x": 0, "y": 1}]
    wall_ids = [synthetic_id(n) for n in (201, 202, 203)]
    floor["walls"] = [
        {"id": wall_id, "start": points[index], "end": points[(index + 1) % 3], "thickness": 0.1}
        for index, wall_id in enumerate(wall_ids)
    ]
    floor["rooms"] = [{
        "id": synthetic_id(204), "name": "Synthetic fixture room", "type": "bedroom",
        "polygon": [*points, points[0]],
        "wallIds": [*wall_ids[:2], synthetic_id(999)] if invalid_wall_reference else wall_ids,
    }]
    m3_geometry = PlanGeometry.model_validate(geometry)
    synthetic_success["alternatives"][0]["geometry"] = m3_geometry
    if invalid_wall_reference:
        with pytest.raises(ValidationError, match="room must reference distinct walls on its floor"):
            GenerationResult.model_validate(synthetic_success)
    else:
        result = GenerationResult.model_validate(synthetic_success)
        assert result.alternatives[0].geometry == m3_geometry

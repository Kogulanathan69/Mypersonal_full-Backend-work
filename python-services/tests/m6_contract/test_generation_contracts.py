"""D01-B schema acceptance tests; all data and dimensions are synthetic.

The fixture is a 10 m x 8 m plot INPUT, never a generated plan or legal rule.
These tests establish structural validation only, not geometry/compliance.
"""

from copy import deepcopy
import json
from pathlib import Path
from uuid import UUID

import pytest
from pydantic import ValidationError

from app.plans.generation.contracts import GenerationInput


FIXTURE = Path(__file__).parent / "fixtures" / "generation-input.v0.1-draft.json"
SECOND_SLOT = "10000000-0000-4000-8000-000000000005"
NIL = "00000000-0000-0000-0000-000000000000"


@pytest.fixture
def request_data():
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


def put(data, path, value):
    parts = path.split(".")
    for part in parts[:-1]:
        data = data[int(part)] if isinstance(data, list) else data[part]
    key = int(parts[-1]) if isinstance(data, list) else parts[-1]
    data[key] = value


def reject(data, path, value):
    put(data, path, value)
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(data)
    # Ensure the JSON boundary enforces the same rules for JSON values.
    if not isinstance(value, (bytes, tuple, UUID)):
        with pytest.raises(ValidationError):
            GenerationInput.model_validate_json(json.dumps(data))


def two_rooms(data):
    room = deepcopy(data["roomProgram"]["rooms"][0])
    room["slotId"] = SECOND_SLOT
    room["roomType"] = "kitchen"
    data["roomProgram"]["rooms"].append(room)
    return {
        "fromSlotId": data["roomProgram"]["rooms"][0]["slotId"],
        "toSlotId": SECOND_SLOT,
        "relationship": "adjacent",
    }


def reviewed(data):
    # Invented source identifiers for structural tests ONLY, no actual law.
    data["regulatoryEvidence"] = {
        "status": "REVIEWED",
        "ruleSetVersion": "synthetic-rules-v1",
        "constraints": [{
            "ruleId": "SYNTHETIC-SETBACK",
            "type": "setback",
            "source": {
                "sourceVersion": "synthetic-source-v1",
                "sourceLocator": "synthetic:page-1",
                "jurisdiction": "synthetic-test-jurisdiction",
            },
            "parcelEdgeIndex": 0,
            "distanceM": 1,
            "applicability": {"status": "APPLICABLE", "basis": "synthetic review"},
            "reviewedBy": "10000000-0000-4000-8000-000000000006",
            "reviewReference": "synthetic-review-1",
        }],
        "gaps": [],
    }


def test_valid_synthetic_request(request_data):
    result = GenerationInput.model_validate(request_data)
    assert result.seed == 42
    assert result.roomProgram.catalogueVersion == "synthetic-fixture-v1"
    assert [(p.x, p.y) for p in result.land.boundary.exterior] == [
        (0, 0), (10, 0), (10, 8), (0, 8), (0, 0)
    ]


def test_exact_revision_preservation(request_data):
    for key in ("landRevisionId", "requirementsRevisionId"):
        request_data[key] = "aabbccdd-1234-4321-8765-abcdef123456"
        result = GenerationInput.model_validate(request_data)
        assert result.model_dump(mode="json")[key] == request_data[key]


@pytest.mark.parametrize("field", [
    "schemaVersion", "projectId", "landRevisionId", "requirementsRevisionId",
    "land", "roomProgram", "adjacencyPreferences", "regulatoryEvidence", "seed",
])
def test_required_generation_fields(request_data, field):
    del request_data[field]
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


@pytest.mark.parametrize("path", [
    "projectId", "landRevisionId", "requirementsRevisionId", "roomProgram.rooms.0.slotId",
])
@pytest.mark.parametrize("value", ["bad", NIL, 12, True, None, "10000000000040008000000000000001"])
def test_invalid_identifiers(request_data, path, value):
    reject(request_data, path, value)


@pytest.mark.parametrize(("path", "value"), [
    ("schemaVersion", "0.1"), ("land.schemaVersion", "metric-land/1"),
    ("land.coordinateSystem.type", "EPSG:4326"),
    ("land.coordinateSystem.unit", "ft"), ("land.coordinateSystem.unit", "metres"),
    ("land.coordinateSystem.xDirection", "east"),
    ("land.coordinateSystem.yDirection", "down"),
    ("land.coordinateSystem.origin", None),
    ("land.coordinateSystem.origin.x", "0"),
    ("land.boundary.exterior.0.x", True),
    ("land.boundary.exterior.0.y", "0"),
    ("land.boundary.exterior.0", [0, 0]),
    ("land.boundary.exterior.0", {"x": 0}),
    ("land.boundary.exterior", []),
    ("land.boundary.exterior", [{"x": 0, "y": 0}] * 3),
    ("land.boundary.exterior.4", {"x": 0, "y": 1e-12}),
    ("land.boundary.holes", [[{"x": 0, "y": 0}] * 3]),
    ("land.boundary.holes", [[{"x": 0, "y": 0}, {"x": 1, "y": 0},
                              {"x": 1, "y": 1}, {"x": 0, "y": 1}]]),
    ("land.boundary.holes", None),
    ("roomProgram.rooms", []), ("roomProgram.level", 1),
    ("roomProgram.level", -1), ("roomProgram.level", False),
    ("roomProgram.level", 0.0), ("roomProgram.level", "0"),
    ("roomProgram.catalogueVersion", "legal-standard"),
    ("roomProgram.rooms.0.roomType", "garage"),
    ("roomProgram.rooms.0.dimensions.maxWidthM", 2),
    ("roomProgram.rooms.0.dimensions.maxDepthM", 2),
])
def test_invalid_structure_and_scope(request_data, path, value):
    reject(request_data, path, value)


@pytest.mark.parametrize("path", [
    "land.boundary.exterior.0.x", "land.boundary.exterior.0.y",
    "land.coordinateSystem.origin.x", "land.coordinateSystem.origin.y",
])
@pytest.mark.parametrize("value", [float("inf"), float("-inf"), float("nan")])
def test_nonfinite_coordinates(request_data, path, value):
    reject(request_data, path, value)


@pytest.mark.parametrize("dimension", ["minWidthM", "minDepthM", "maxWidthM", "maxDepthM"])
@pytest.mark.parametrize("value", [-1, 0, float("inf"), float("-inf"), float("nan"), "3", True])
def test_invalid_dimensions(request_data, dimension, value):
    reject(request_data, f"roomProgram.rooms.0.dimensions.{dimension}", value)


def test_duplicate_room_slot(request_data):
    request_data["roomProgram"]["rooms"] *= 2
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


@pytest.mark.parametrize("room_type", ["bedroom", "livingRoom", "kitchen", "bathroom"])
def test_supported_room_types_and_equal_bounds(request_data, room_type):
    request_data["roomProgram"]["rooms"][0]["roomType"] = room_type
    dimensions = request_data["roomProgram"]["rooms"][0]["dimensions"]
    dimensions.update(maxWidthM=3, maxDepthM=3)
    assert GenerationInput.model_validate(request_data).roomProgram.rooms[0].dimensions.maxWidthM == 3


@pytest.mark.parametrize("relationship", ["adjacent", "separated"])
def test_supported_adjacency(request_data, relationship):
    preference = two_rooms(request_data)
    preference["relationship"] = relationship
    request_data["adjacencyPreferences"] = [preference]
    assert len(GenerationInput.model_validate(request_data).adjacencyPreferences) == 1


@pytest.mark.parametrize("case", ["missing", "dangling", "self", "duplicate", "reverse", "contradiction", "type", "nil"])
def test_invalid_adjacency(request_data, case):
    preference = two_rooms(request_data)
    request_data["adjacencyPreferences"] = [preference]
    if case == "missing":
        del preference["toSlotId"]
    elif case == "dangling":
        preference["toSlotId"] = "10000000-0000-4000-8000-000000000099"
    elif case == "self":
        preference["toSlotId"] = preference["fromSlotId"]
    elif case in ("duplicate", "reverse", "contradiction"):
        other = deepcopy(preference)
        if case == "reverse":
            other["fromSlotId"], other["toSlotId"] = other["toSlotId"], other["fromSlotId"]
        if case == "contradiction":
            other["relationship"] = "separated"
        request_data["adjacencyPreferences"].append(other)
    elif case == "type":
        preference["relationship"] = "connectedByDoor"
    else:
        preference["fromSlotId"] = NIL
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


def test_explicit_missing_evidence(request_data):
    evidence = GenerationInput.model_validate(request_data).regulatoryEvidence
    assert evidence.status == "MISSING"
    assert evidence.ruleSetVersion is None
    assert evidence.constraints == []
    assert evidence.gaps[0].code == "SETBACK_EVIDENCE_MISSING"


@pytest.mark.parametrize("status", ["UNVERIFIED", "CONFLICTING"])
def test_explicit_evidence_gaps(request_data, status):
    request_data["regulatoryEvidence"]["status"] = status
    assert GenerationInput.model_validate(request_data).regulatoryEvidence.status == status


@pytest.mark.parametrize(("path", "value"), [
    ("status", "VERIFIED"), ("status", "COMPLIANT"), ("status", "REVIEWED"),
    ("ruleSetVersion", "synthetic-v1"), ("gaps", []),
    ("gaps.0.code", " "), ("gaps.0.code", 1),
    ("constraints", [{"distanceM": 1}]),
])
def test_missing_evidence_contradictions(request_data, path, value):
    reject(request_data, f"regulatoryEvidence.{path}", value)


def test_reviewed_metadata_round_trip_is_not_compliance(request_data):
    reviewed(request_data)
    model = GenerationInput.model_validate(request_data)
    assert model.regulatoryEvidence.status == "REVIEWED"
    assert "compliant" not in model.model_dump_json().lower()
    assert GenerationInput.model_validate_json(model.model_dump_json()) == model


@pytest.mark.parametrize("field", [
    "ruleId", "source", "parcelEdgeIndex", "distanceM", "applicability", "reviewedBy", "reviewReference",
])
def test_reviewed_constraint_requires_metadata(request_data, field):
    reviewed(request_data)
    del request_data["regulatoryEvidence"]["constraints"][0][field]
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


@pytest.mark.parametrize("field", ["sourceVersion", "sourceLocator", "jurisdiction"])
def test_source_provenance_required(request_data, field):
    reviewed(request_data)
    del request_data["regulatoryEvidence"]["constraints"][0]["source"][field]
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


@pytest.mark.parametrize(("path", "value"), [
    ("ruleSetVersion", None), ("constraints", []),
    ("gaps", [{"code": "CONFLICTING_SOURCES"}]),
    ("constraints.0.applicability.status", "UNKNOWN"),
    ("constraints.0.applicability.basis", ""),
    ("constraints.0.reviewedBy", NIL),
    ("constraints.0.source.sourceLocator", ""),
    ("constraints.0.parcelEdgeIndex", 4), ("constraints.0.parcelEdgeIndex", -1),
    ("constraints.0.parcelEdgeIndex", True), ("constraints.0.parcelEdgeIndex", "0"),
    ("constraints.0.distanceM", -1), ("constraints.0.distanceM", "1"),
    ("constraints.0.distanceM", True), ("constraints.0.distanceM", float("nan")),
    ("constraints.0.distanceM", float("inf")),
])
def test_reviewed_evidence_invalid(request_data, path, value):
    reviewed(request_data)
    reject(request_data, f"regulatoryEvidence.{path}", value)


@pytest.mark.parametrize("status", ["UNVERIFIED", "CONFLICTING"])
def test_uncertain_status_requires_gaps(request_data, status):
    reviewed(request_data)
    request_data["regulatoryEvidence"]["status"] = status
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


def test_duplicate_rule_edge_requires_explicit_gap(request_data):
    reviewed(request_data)
    request_data["regulatoryEvidence"]["constraints"] *= 2
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


def test_zero_setback_metadata_and_last_edge(request_data):
    reviewed(request_data)
    constraint = request_data["regulatoryEvidence"]["constraints"][0]
    constraint.update(distanceM=0, parcelEdgeIndex=3)
    assert GenerationInput.model_validate(request_data).regulatoryEvidence.constraints[0].distanceM == 0


@pytest.mark.parametrize("seed", [-1, 2**31, True, 42.0, "42", None, float("inf")])
def test_invalid_seed(request_data, seed):
    reject(request_data, "seed", seed)


@pytest.mark.parametrize("seed", [0, 2**31 - 1])
def test_seed_boundaries(request_data, seed):
    request_data["seed"] = seed
    assert GenerationInput.model_validate(request_data).seed == seed


@pytest.mark.parametrize("path", [
    "", "land", "land.coordinateSystem", "land.coordinateSystem.origin",
    "land.boundary", "land.boundary.exterior.0", "roomProgram", "roomProgram.rooms.0",
    "roomProgram.rooms.0.dimensions", "regulatoryEvidence", "regulatoryEvidence.gaps.0",
])
def test_extra_fields_forbidden_at_every_fixture_level(request_data, path):
    reject(request_data, f"{path}.unexpected" if path else "unexpected", "no")


@pytest.mark.parametrize("path", [
    "constraints.0", "constraints.0.source", "constraints.0.applicability",
])
def test_extra_review_metadata_forbidden(request_data, path):
    reviewed(request_data)
    reject(request_data, f"regulatoryEvidence.{path}.verified", True)


def test_extra_adjacency_fields_forbidden(request_data):
    request_data["adjacencyPreferences"] = [two_rooms(request_data)]
    reject(request_data, "adjacencyPreferences.0.score", 1)


def test_json_serialization_round_trip(request_data):
    model = GenerationInput.model_validate_json(FIXTURE.read_text(encoding="utf-8"))
    dumped = model.model_dump_json()
    assert GenerationInput.model_validate_json(dumped) == model
    assert GenerationInput.model_validate(model.model_dump()) == model
    assert json.loads(dumped)["landRevisionId"] == request_data["landRevisionId"]
    assert GenerationInput.model_validate(request_data).model_dump_json() == dumped


def test_optional_holes_and_dimensions(request_data):
    del request_data["land"]["boundary"]["holes"]
    model = GenerationInput.model_validate(request_data)
    assert model.land.boundary.holes == []
    assert model.roomProgram.rooms[0].dimensions.maxWidthM is None


def test_hole_coordinates_preserved(request_data):
    hole = [{"x": 1, "y": 1}, {"x": 2, "y": 1}, {"x": 2, "y": 2}, {"x": 1, "y": 1}]
    request_data["land"]["boundary"]["holes"] = [hole]
    model = GenerationInput.model_validate(request_data)
    assert model.model_dump(mode="json")["land"]["boundary"]["holes"] == [hole]


def test_no_crs_inference_or_coordinate_repair(request_data):
    request_data["land"]["coordinateSystem"]["origin"] = {"x": 79.8, "y": 6.9}
    model = GenerationInput.model_validate(request_data)
    assert model.land.coordinateSystem.type == "localCartesian"
    assert model.land.coordinateSystem.origin.x == 79.8
    assert request_data["land"]["boundary"]["exterior"][0] == {"x": 0, "y": 0}


def test_topological_validation_is_deferred(request_data):
    # Closed self-crossing ring is structurally accepted; no validity claim.
    ring = [{"x": 0, "y": 0}, {"x": 2, "y": 2}, {"x": 0, "y": 2},
            {"x": 2, "y": 0}, {"x": 0, "y": 0}]
    request_data["land"]["boundary"]["exterior"] = ring
    assert GenerationInput.model_validate(request_data).model_dump(mode="json")["land"]["boundary"]["exterior"] == ring


def test_python_tuple_is_not_silently_coerced(request_data):
    request_data["land"]["boundary"]["exterior"] = tuple(request_data["land"]["boundary"]["exterior"])
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)


@pytest.mark.parametrize("case", ["rooms", "ring", "holes", "gaps", "text"])
def test_resource_limits(request_data, case):
    if case == "rooms":
        request_data["roomProgram"]["rooms"] *= 101
    elif case == "ring":
        request_data["land"]["boundary"]["exterior"] = [{"x": 0, "y": 0}] * 10_001
    elif case == "holes":
        ring = request_data["land"]["boundary"]["exterior"]
        request_data["land"]["boundary"]["holes"] = [ring] * 101
    elif case == "gaps":
        request_data["regulatoryEvidence"]["gaps"] *= 1_001
    else:
        request_data["regulatoryEvidence"]["gaps"][0]["code"] = "X" * 501
    with pytest.raises(ValidationError):
        GenerationInput.model_validate(request_data)

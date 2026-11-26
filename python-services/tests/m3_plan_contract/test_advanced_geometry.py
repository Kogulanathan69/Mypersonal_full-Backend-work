"""Executable structural checks for INT-05.

Pending spatial fixtures are deliberately not parameterized as passing tests.
M2/M6 canonical contracts and an approved spatial dependency are still needed.
"""

from copy import deepcopy
import json
from pathlib import Path
from uuid import uuid4

import pytest
from pydantic import ValidationError

from app.plan_editor.operations import EditOperationError, apply_command, validate_candidate_structure
from app.plan_editor.schemas import PlanEditCommand, PlanGeometry


FIXTURE = Path(__file__).parent / "fixtures" / "advanced_geometry_cases.json"


def _pack() -> dict[str, object]:
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


def _mutate(plan: dict[str, object], mutation: str) -> None:
    floor = plan["floors"][0]
    if mutation == "none":
        return
    if mutation == "duplicateRoomId":
        floor["rooms"][1]["id"] = floor["rooms"][0]["id"]
    elif mutation == "missingRoomWall":
        floor["rooms"][0]["wallIds"][0] = str(uuid4())
    elif mutation == "zeroLengthWall":
        floor["walls"][0]["end"] = deepcopy(floor["walls"][0]["start"])
    elif mutation == "openingExceedsWall":
        floor["openings"][0]["offset"] = 2.5
        floor["openings"][0]["width"] = 1.0
    elif mutation == "openRoomRing":
        floor["rooms"][0]["polygon"][-1] = {"x": 1, "y": 1}
    elif mutation == "negativeOpeningOffset":
        floor["openings"][0]["offset"] = -0.1
    elif mutation == "nonFiniteCoordinate":
        floor["walls"][0]["start"]["x"] = float("nan")
    else:
        raise AssertionError(f"Unknown fixture mutation: {mutation}")


@pytest.mark.parametrize(
    "case",
    _pack()["executableCases"],
    ids=lambda case: case["name"],
)
def test_advanced_structural_fixture_has_truthful_result(case: dict[str, object]) -> None:
    plan_data = deepcopy(_pack()["baseSharedWallPlan"])
    _mutate(plan_data, str(case["mutation"]))

    if case.get("expected") == "SCHEMA_REJECTED":
        with pytest.raises(ValidationError):
            PlanGeometry.model_validate(plan_data)
        return

    plan = PlanGeometry.model_validate(plan_data)
    if "expectedCode" in case:
        with pytest.raises(EditOperationError) as captured:
            validate_candidate_structure(plan)
        assert captured.value.code == case["expectedCode"]
    else:
        validate_candidate_structure(plan)


@pytest.mark.parametrize("command_type", ["moveRoom", "resizeRoom"])
def test_shared_wall_transform_is_rejected_without_mutating_source(command_type: str) -> None:
    source = PlanGeometry.model_validate(_pack()["baseSharedWallPlan"])
    before = source.model_dump(mode="json")
    floor = source.floors[0]
    payload = {"deltaX": 1, "deltaY": 0} if command_type == "moveRoom" else {
        "scaleX": 1.2,
        "scaleY": 1,
        "anchorX": 0,
        "anchorY": 0,
    }
    command = PlanEditCommand(
        commandId=uuid4(),
        commandType=command_type,
        floorId=floor.id,
        targetId=floor.rooms[0].id,
        payload=payload,
    )

    with pytest.raises(EditOperationError) as captured:
        apply_command(source, command)

    assert captured.value.code == "UNSUPPORTED_TOPOLOGY"
    assert source.model_dump(mode="json") == before


def test_valid_opening_move_preserves_stable_entity_ids_and_source() -> None:
    source = PlanGeometry.model_validate(_pack()["baseSharedWallPlan"])
    before = source.model_dump(mode="json")
    floor = source.floors[0]
    original_ids = {
        *(room.id for room in floor.rooms),
        *(wall.id for wall in floor.walls),
        *(opening.id for opening in floor.openings),
    }
    opening = floor.openings[0]
    command = PlanEditCommand(
        commandId=uuid4(), commandType="moveOpening", floorId=floor.id,
        targetId=opening.id, payload={"offset": 1.25},
    )

    candidate, changed = apply_command(source, command)
    candidate_floor = candidate.floors[0]
    candidate_ids = {
        *(room.id for room in candidate_floor.rooms),
        *(wall.id for wall in candidate_floor.walls),
        *(item.id for item in candidate_floor.openings),
    }

    assert candidate_ids == original_ids
    assert changed == [opening.id, opening.wallId]
    assert candidate_floor.openings[0].offset == 1.25
    assert source.model_dump(mode="json") == before


def test_opening_move_beyond_wall_fails_structural_check_and_preserves_source() -> None:
    source = PlanGeometry.model_validate(_pack()["baseSharedWallPlan"])
    before = source.model_dump(mode="json")
    floor = source.floors[0]
    opening = floor.openings[0]
    command = PlanEditCommand(
        commandId=uuid4(), commandType="moveOpening", floorId=floor.id,
        targetId=opening.id, payload={"offset": 2.5},
    )

    with pytest.raises(EditOperationError) as captured:
        apply_command(source, command)

    assert captured.value.code == "INVALID_OPENING_POSITION"
    assert source.model_dump(mode="json") == before

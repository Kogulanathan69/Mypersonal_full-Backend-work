from copy import deepcopy
from uuid import UUID, uuid4

import pytest
from pydantic import ValidationError

from app.plan_editor.operations import EditOperationError, apply_command, validate_candidate_structure
from app.plan_editor.schemas import PlanEditCommand, PlanGeometry


def make_command(geometry: PlanGeometry, command_type: str, target_id: UUID | None, payload: dict[str, object]) -> PlanEditCommand:
    return PlanEditCommand(
        commandId=uuid4(),
        commandType=command_type,
        floorId=geometry.floors[0].id,
        targetId=target_id,
        payload=payload,
    )


def test_move_room_returns_candidate_without_mutating_original(geometry: PlanGeometry) -> None:
    original = geometry.model_dump(mode="json")
    room = geometry.floors[0].rooms[0]
    candidate, changed = apply_command(
        geometry, make_command(geometry, "moveRoom", room.id, {"deltaX": 2, "deltaY": -1})
    )
    assert geometry.model_dump(mode="json") == original
    assert candidate.floors[0].rooms[0].polygon[0].x == room.polygon[0].x + 2
    assert room.id in changed


def test_missing_room_rejects_and_preserves_original(geometry: PlanGeometry) -> None:
    original = geometry.model_dump(mode="json")
    with pytest.raises(EditOperationError) as raised:
        apply_command(geometry, make_command(geometry, "moveRoom", uuid4(), {"deltaX": 1, "deltaY": 1}))
    assert raised.value.code == "ROOM_NOT_FOUND"
    assert geometry.model_dump(mode="json") == original


def test_remove_room_removes_exclusive_walls_and_orphan_openings(geometry: PlanGeometry) -> None:
    room = geometry.floors[0].rooms[0]
    candidate, changed = apply_command(
        geometry, make_command(geometry, "removeRoom", room.id, {"removeOrphanOpenings": True})
    )
    assert candidate.floors[0].rooms == []
    assert candidate.floors[0].walls == []
    assert candidate.floors[0].openings == []
    assert room.id in changed


def test_remove_room_can_reject_orphan_opening_policy(geometry: PlanGeometry) -> None:
    room = geometry.floors[0].rooms[0]
    with pytest.raises(EditOperationError) as raised:
        apply_command(
            geometry, make_command(geometry, "removeRoom", room.id, {"removeOrphanOpenings": False})
        )
    assert raised.value.code == "INVALID_GEOMETRY_REFERENCE"


def test_add_room_success_and_duplicate_id_rejection(geometry: PlanGeometry) -> None:
    floor = geometry.floors[0]
    room = floor.rooms[0].model_copy(update={"id": uuid4(), "name": "Bedroom 2"})
    walls = [wall.model_copy(update={"id": uuid4()}) for wall in floor.walls]
    room = room.model_copy(update={"wallIds": [wall.id for wall in walls]})
    candidate, _ = apply_command(
        geometry,
        make_command(
            geometry,
            "addRoom",
            None,
            {"room": room.model_dump(mode="json"), "walls": [wall.model_dump(mode="json") for wall in walls]},
        ),
    )
    assert len(candidate.floors[0].rooms) == 2

    duplicate_room = room.model_copy(update={"id": floor.rooms[0].id})
    with pytest.raises(EditOperationError) as raised:
        apply_command(
            geometry,
            make_command(
                geometry,
                "addRoom",
                None,
                {"room": duplicate_room.model_dump(mode="json"), "walls": [wall.model_dump(mode="json") for wall in walls]},
            ),
        )
    assert raised.value.code == "DUPLICATE_ENTITY_ID"


def test_resize_room_scales_geometry_and_openings(geometry: PlanGeometry) -> None:
    room = geometry.floors[0].rooms[0]
    candidate, changed = apply_command(
        geometry, make_command(geometry, "resizeRoom", room.id, {"scaleX": 2, "scaleY": 1})
    )
    assert candidate.floors[0].rooms[0].polygon[1].x == 10
    assert candidate.floors[0].openings[0].width == 0.9
    assert candidate.floors[0].openings[1].width == 1.5
    assert room.id in changed


def test_zero_resize_dimension_is_rejected_by_command_model(geometry: PlanGeometry) -> None:
    with pytest.raises(ValidationError):
        make_command(geometry, "resizeRoom", geometry.floors[0].rooms[0].id, {"scaleX": 0, "scaleY": 1})


def test_shared_wall_move_is_rejected_without_neighbor_policy(geometry: PlanGeometry) -> None:
    floor = geometry.floors[0]
    shared_room = floor.rooms[0].model_copy(update={"id": uuid4(), "name": "Neighbor"})
    floor.rooms.append(shared_room)
    with pytest.raises(EditOperationError) as raised:
        apply_command(
            geometry,
            make_command(geometry, "moveRoom", floor.rooms[0].id, {"deltaX": 1, "deltaY": 0}),
        )
    assert raised.value.code == "UNSUPPORTED_TOPOLOGY"


def test_opening_add_move_remove_and_fit_checks(geometry: PlanGeometry) -> None:
    wall = geometry.floors[0].walls[0]
    opening_id = uuid4()
    added, _ = apply_command(
        geometry,
        make_command(
            geometry,
            "addOpening",
            None,
            {
                "opening": {
                    "id": str(opening_id),
                    "type": "window",
                    "wallId": str(wall.id),
                    "offset": 2.5,
                    "width": 1,
                }
            },
        ),
    )
    moved, _ = apply_command(added, make_command(added, "moveOpening", opening_id, {"offset": 2.75}))
    assert next(item for item in moved.floors[0].openings if item.id == opening_id).offset == 2.75
    removed, _ = apply_command(moved, make_command(moved, "removeOpening", opening_id, {}))
    assert all(item.id != opening_id for item in removed.floors[0].openings)

    with pytest.raises(EditOperationError) as raised:
        apply_command(added, make_command(added, "moveOpening", opening_id, {"offset": 3.5}))
    assert raised.value.code == "INVALID_OPENING_POSITION"


def test_dangling_wall_reference_is_detected(geometry: PlanGeometry) -> None:
    geometry.floors[0].walls.pop()
    with pytest.raises(EditOperationError) as raised:
        validate_candidate_structure(geometry)
    assert raised.value.code == "INVALID_GEOMETRY_REFERENCE"


def test_referenced_wall_endpoint_edit_rejects_and_rolls_back(geometry: PlanGeometry) -> None:
    original = geometry.model_dump(mode="json")
    wall = geometry.floors[0].walls[0]
    with pytest.raises(EditOperationError) as raised:
        apply_command(geometry, make_command(geometry, "updateWall", wall.id, {"startX": 3}))
    assert raised.value.code == "UNSUPPORTED_TOPOLOGY"
    assert geometry.model_dump(mode="json") == original


def test_wall_thickness_edit_remains_supported(geometry: PlanGeometry) -> None:
    wall = geometry.floors[0].walls[0]
    candidate, changed = apply_command(
        geometry, make_command(geometry, "updateWall", wall.id, {"thickness": 0.25})
    )
    assert candidate.floors[0].walls[0].thickness == 0.25
    assert changed == [wall.id]


def test_numeric_overflow_rejects_and_preserves_original(geometry: PlanGeometry) -> None:
    original = geometry.model_dump(mode="json")
    room = geometry.floors[0].rooms[0]
    command = make_command(geometry, "resizeRoom", room.id, {"scaleX": 1e308, "scaleY": 1})
    with pytest.raises(EditOperationError) as raised:
        apply_command(geometry, command)
    assert raised.value.code == "INVALID_EDIT_PAYLOAD"
    assert geometry.model_dump(mode="json") == original


def test_shared_wall_resize_is_rejected(geometry: PlanGeometry) -> None:
    floor = geometry.floors[0]
    floor.rooms.append(floor.rooms[0].model_copy(update={"id": uuid4(), "name": "Neighbor"}))
    with pytest.raises(EditOperationError) as raised:
        apply_command(
            geometry,
            make_command(geometry, "resizeRoom", floor.rooms[0].id, {"scaleX": 1.2, "scaleY": 1}),
        )
    assert raised.value.code == "UNSUPPORTED_TOPOLOGY"


def test_add_opening_rejects_missing_parent_wall_and_rolls_back(geometry: PlanGeometry) -> None:
    original = geometry.model_dump(mode="json")
    opening_id = uuid4()
    with pytest.raises(EditOperationError) as raised:
        apply_command(
            geometry,
            make_command(
                geometry,
                "addOpening",
                None,
                {"opening": {"id": opening_id, "type": "door", "wallId": uuid4(), "offset": 0, "width": 0.9}},
            ),
        )
    assert raised.value.code == "INVALID_GEOMETRY_REFERENCE"
    assert geometry.model_dump(mode="json") == original


def test_candidate_transform_is_deterministic(geometry: PlanGeometry) -> None:
    room = geometry.floors[0].rooms[0]
    command = make_command(geometry, "moveRoom", room.id, {"deltaX": 1.5, "deltaY": -0.25})
    first, first_changed = apply_command(geometry, command)
    second, second_changed = apply_command(geometry, command)
    assert first.model_dump(mode="json") == second.model_dump(mode="json")
    assert first_changed == second_changed

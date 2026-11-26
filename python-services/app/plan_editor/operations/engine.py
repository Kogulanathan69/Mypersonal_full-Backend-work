"""Pure candidate transformations for the provisional M3 plan editor.

These checks protect referential integrity and obvious degeneracy. They are not
the M6 authoritative architectural, boundary, overlap, or regulatory validator.
"""

from __future__ import annotations

from math import hypot
from uuid import UUID

from pydantic import ValidationError

from ..schemas.commands import (
    AddOpeningPayload,
    AddRoomPayload,
    MoveOpeningPayload,
    MoveRoomPayload,
    PlanEditCommand,
    RemoveRoomPayload,
    ResizeRoomPayload,
    UpdateWallPayload,
)
from ..schemas.geometry import CoordinatePoint, Floor, PlanGeometry, Room, Wall

EPSILON = 1e-9


class EditOperationError(ValueError):
    def __init__(self, code: str, message: str, entity_id: UUID | None = None) -> None:
        super().__init__(message)
        self.code = code
        self.entity_id = entity_id


def _find_floor(plan: PlanGeometry, floor_id: UUID) -> Floor:
    floor = next((item for item in plan.floors if item.id == floor_id), None)
    if floor is None:
        raise EditOperationError("FLOOR_NOT_FOUND", "The requested floor does not exist.", floor_id)
    return floor


def _wall_length(wall: Wall) -> float:
    return hypot(wall.end.x - wall.start.x, wall.end.y - wall.start.y)


def _assert_unique_ids(plan: PlanGeometry) -> None:
    seen: set[UUID] = set()
    for floor in plan.floors:
        for entity in (*floor.rooms, *floor.walls, *floor.openings):
            if entity.id in seen:
                raise EditOperationError("DUPLICATE_ENTITY_ID", "Entity identifiers must be unique.", entity.id)
            seen.add(entity.id)


def _assert_floor_references(floor: Floor) -> None:
    wall_ids = {wall.id for wall in floor.walls}
    for room in floor.rooms:
        missing = next((wall_id for wall_id in room.wallIds if wall_id not in wall_ids), None)
        if missing is not None:
            raise EditOperationError(
                "INVALID_GEOMETRY_REFERENCE", "A room references a missing wall.", missing
            )
    for opening in floor.openings:
        wall = next((candidate for candidate in floor.walls if candidate.id == opening.wallId), None)
        if wall is None:
            raise EditOperationError(
                "INVALID_GEOMETRY_REFERENCE", "An opening references a missing wall.", opening.id
            )
        if opening.offset + opening.width > _wall_length(wall) + EPSILON:
            raise EditOperationError(
                "INVALID_OPENING_POSITION", "The opening does not fit on its parent wall.", opening.id
            )
    for wall in floor.walls:
        if _wall_length(wall) <= EPSILON:
            raise EditOperationError("INVALID_GEOMETRY_REFERENCE", "A wall cannot be degenerate.", wall.id)


def validate_candidate_structure(plan: PlanGeometry) -> None:
    """Run bounded structural checks without claiming authoritative validity."""

    _assert_unique_ids(plan)
    floor_ids: set[UUID] = set()
    for floor in plan.floors:
        if floor.id in floor_ids:
            raise EditOperationError("DUPLICATE_ENTITY_ID", "Floor identifiers must be unique.", floor.id)
        floor_ids.add(floor.id)
        _assert_floor_references(floor)


def _room_wall_usage(floor: Floor, room: Room) -> dict[UUID, int]:
    return {
        wall_id: sum(wall_id in candidate.wallIds for candidate in floor.rooms)
        for wall_id in room.wallIds
    }


def _translate_point(point: CoordinatePoint, delta_x: float, delta_y: float) -> CoordinatePoint:
    return CoordinatePoint(x=point.x + delta_x, y=point.y + delta_y)


def _replace_room(floor: Floor, room: Room) -> None:
    floor.rooms = [room if existing.id == room.id else existing for existing in floor.rooms]


def _move_room(floor: Floor, target_id: UUID, payload: MoveRoomPayload) -> list[UUID]:
    room = next((item for item in floor.rooms if item.id == target_id), None)
    if room is None:
        raise EditOperationError("ROOM_NOT_FOUND", "The requested room does not exist.", target_id)
    usage = _room_wall_usage(floor, room)
    if any(count > 1 for count in usage.values()):
        raise EditOperationError(
            "UNSUPPORTED_TOPOLOGY", "Moving a room with shared walls requires an approved neighbor policy.", target_id
        )
    moved = room.model_copy(
        update={"polygon": [_translate_point(point, payload.deltaX, payload.deltaY) for point in room.polygon]}
    )
    _replace_room(floor, moved)
    changed: list[UUID] = [room.id]
    for index, wall in enumerate(floor.walls):
        if wall.id in usage:
            floor.walls[index] = wall.model_copy(
                update={
                    "start": _translate_point(wall.start, payload.deltaX, payload.deltaY),
                    "end": _translate_point(wall.end, payload.deltaX, payload.deltaY),
                }
            )
            changed.append(wall.id)
    return changed


def _resize_point(point: CoordinatePoint, anchor_x: float, anchor_y: float, scale_x: float, scale_y: float) -> CoordinatePoint:
    return CoordinatePoint(
        x=anchor_x + (point.x - anchor_x) * scale_x,
        y=anchor_y + (point.y - anchor_y) * scale_y,
    )


def _resize_room(floor: Floor, target_id: UUID, payload: ResizeRoomPayload) -> list[UUID]:
    room = next((item for item in floor.rooms if item.id == target_id), None)
    if room is None:
        raise EditOperationError("ROOM_NOT_FOUND", "The requested room does not exist.", target_id)
    usage = _room_wall_usage(floor, room)
    if any(count > 1 for count in usage.values()):
        raise EditOperationError(
            "UNSUPPORTED_TOPOLOGY", "Resizing shared-wall topology requires an approved neighbor policy.", target_id
        )
    anchor_x = payload.anchorX if payload.anchorX is not None else min(point.x for point in room.polygon)
    anchor_y = payload.anchorY if payload.anchorY is not None else min(point.y for point in room.polygon)
    resized = room.model_copy(
        update={
            "polygon": [
                _resize_point(point, anchor_x, anchor_y, payload.scaleX, payload.scaleY)
                for point in room.polygon
            ]
        }
    )
    _replace_room(floor, resized)
    changed: list[UUID] = [room.id]
    for index, wall in enumerate(floor.walls):
        if wall.id in usage:
            old_length = _wall_length(wall)
            resized_wall = wall.model_copy(
                update={
                    "start": _resize_point(wall.start, anchor_x, anchor_y, payload.scaleX, payload.scaleY),
                    "end": _resize_point(wall.end, anchor_x, anchor_y, payload.scaleX, payload.scaleY),
                }
            )
            floor.walls[index] = resized_wall
            new_length = _wall_length(resized_wall)
            for opening_index, opening in enumerate(floor.openings):
                if opening.wallId == wall.id and old_length > EPSILON:
                    ratio = new_length / old_length
                    floor.openings[opening_index] = opening.model_copy(
                        update={"offset": opening.offset * ratio}
                    )
                    changed.append(opening.id)
            changed.append(wall.id)
    return changed


def apply_command(source: PlanGeometry, command: PlanEditCommand) -> tuple[PlanGeometry, list[UUID]]:
    """Return a detached candidate, normalizing numeric/model failures."""

    try:
        return _apply_command(source, command)
    except EditOperationError:
        raise
    except (ArithmeticError, ValidationError, ValueError) as error:
        raise EditOperationError("INVALID_EDIT_PAYLOAD", "The edit produced invalid numeric geometry.") from error


def _apply_command(source: PlanGeometry, command: PlanEditCommand) -> tuple[PlanGeometry, list[UUID]]:
    """Return a fully detached candidate and affected IDs, or raise a structured error."""

    candidate = source.model_copy(deep=True)
    floor = _find_floor(candidate, command.floorId)
    payload = command.typed_payload()
    changed: list[UUID]

    if command.commandType == "addRoom":
        assert isinstance(payload, AddRoomPayload)
        existing = {entity.id for entity in (*floor.rooms, *floor.walls, *floor.openings)}
        additions = [payload.room.id, *(wall.id for wall in payload.walls), *(item.id for item in payload.openings)]
        duplicate = next((entity_id for entity_id in additions if entity_id in existing), None)
        if duplicate is not None or len(additions) != len(set(additions)):
            raise EditOperationError("DUPLICATE_ENTITY_ID", "An added entity ID already exists.", duplicate)
        supplied_walls = {wall.id for wall in payload.walls}
        if not set(payload.room.wallIds).issubset(supplied_walls):
            raise EditOperationError("INVALID_GEOMETRY_REFERENCE", "The new room references an unsupplied wall.")
        floor.rooms.append(payload.room)
        floor.walls.extend(payload.walls)
        floor.openings.extend(payload.openings)
        changed = additions
    elif command.commandType == "removeRoom":
        assert isinstance(payload, RemoveRoomPayload)
        target = next((room for room in floor.rooms if room.id == command.targetId), None)
        if target is None:
            raise EditOperationError("ROOM_NOT_FOUND", "The requested room does not exist.", command.targetId)
        floor.rooms = [room for room in floor.rooms if room.id != target.id]
        still_used = {wall_id for room in floor.rooms for wall_id in room.wallIds}
        removed_walls = set(target.wallIds) - still_used
        orphan_openings = [item for item in floor.openings if item.wallId in removed_walls]
        if orphan_openings and not payload.removeOrphanOpenings:
            raise EditOperationError(
                "INVALID_GEOMETRY_REFERENCE", "Removing the room would orphan openings.", orphan_openings[0].id
            )
        floor.walls = [wall for wall in floor.walls if wall.id not in removed_walls]
        floor.openings = [item for item in floor.openings if item.wallId not in removed_walls]
        changed = [target.id, *removed_walls, *(item.id for item in orphan_openings)]
    elif command.commandType == "moveRoom":
        assert isinstance(payload, MoveRoomPayload) and command.targetId is not None
        changed = _move_room(floor, command.targetId, payload)
    elif command.commandType == "resizeRoom":
        assert isinstance(payload, ResizeRoomPayload) and command.targetId is not None
        changed = _resize_room(floor, command.targetId, payload)
    elif command.commandType == "updateWall":
        assert isinstance(payload, UpdateWallPayload) and command.targetId is not None
        index = next((i for i, item in enumerate(floor.walls) if item.id == command.targetId), None)
        if index is None:
            raise EditOperationError("WALL_NOT_FOUND", "The requested wall does not exist.", command.targetId)
        wall = floor.walls[index]
        changes_endpoints = any(
            value is not None
            for value in (payload.startX, payload.startY, payload.endX, payload.endY)
        )
        if changes_endpoints and any(wall.id in room.wallIds for room in floor.rooms):
            raise EditOperationError(
                "UNSUPPORTED_TOPOLOGY",
                "Referenced wall endpoints cannot be changed without an approved room-topology policy.",
                wall.id,
            )
        floor.walls[index] = wall.model_copy(
            update={
                "start": CoordinatePoint(
                    x=wall.start.x if payload.startX is None else payload.startX,
                    y=wall.start.y if payload.startY is None else payload.startY,
                ),
                "end": CoordinatePoint(
                    x=wall.end.x if payload.endX is None else payload.endX,
                    y=wall.end.y if payload.endY is None else payload.endY,
                ),
                "thickness": wall.thickness if payload.thickness is None else payload.thickness,
            }
        )
        changed = [wall.id]
    elif command.commandType == "addOpening":
        assert isinstance(payload, AddOpeningPayload)
        if any(item.id == payload.opening.id for item in floor.openings):
            raise EditOperationError("DUPLICATE_ENTITY_ID", "The opening ID already exists.", payload.opening.id)
        floor.openings.append(payload.opening)
        changed = [payload.opening.id, payload.opening.wallId]
    elif command.commandType == "moveOpening":
        assert isinstance(payload, MoveOpeningPayload) and command.targetId is not None
        index = next((i for i, item in enumerate(floor.openings) if item.id == command.targetId), None)
        if index is None:
            raise EditOperationError("OPENING_NOT_FOUND", "The requested opening does not exist.", command.targetId)
        opening = floor.openings[index]
        floor.openings[index] = opening.model_copy(update={"offset": payload.offset})
        changed = [opening.id, opening.wallId]
    else:  # removeOpening; commandType has already been exhaustively validated.
        assert command.targetId is not None
        opening = next((item for item in floor.openings if item.id == command.targetId), None)
        if opening is None:
            raise EditOperationError("OPENING_NOT_FOUND", "The requested opening does not exist.", command.targetId)
        floor.openings = [item for item in floor.openings if item.id != opening.id]
        changed = [opening.id, opening.wallId]

    validate_candidate_structure(candidate)
    return candidate, list(dict.fromkeys(changed))

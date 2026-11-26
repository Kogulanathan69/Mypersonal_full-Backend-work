"""Strict command models for isolated M3 candidate transformations."""

from typing import Annotated, Literal, Union
from uuid import UUID

from pydantic import Field, TypeAdapter, model_validator

from .base import StrictModel
from .geometry import (
    DRAFT_SCHEMA_VERSION,
    FiniteNumber,
    NonNegativeFiniteNumber,
    Opening,
    PositiveFiniteNumber,
    Room,
    Wall,
)


class AddRoomPayload(StrictModel):
    room: Room
    walls: list[Wall] = Field(min_length=3, max_length=10_000)
    openings: list[Opening] = Field(default_factory=list, max_length=10_000)


class RemoveRoomPayload(StrictModel):
    removeOrphanOpenings: bool = True


class MoveRoomPayload(StrictModel):
    deltaX: FiniteNumber
    deltaY: FiniteNumber


class ResizeRoomPayload(StrictModel):
    scaleX: PositiveFiniteNumber
    scaleY: PositiveFiniteNumber
    anchorX: FiniteNumber | None = None
    anchorY: FiniteNumber | None = None


class UpdateWallPayload(StrictModel):
    startX: FiniteNumber | None = None
    startY: FiniteNumber | None = None
    endX: FiniteNumber | None = None
    endY: FiniteNumber | None = None
    thickness: PositiveFiniteNumber | None = None

    @model_validator(mode="after")
    def require_a_change(self) -> "UpdateWallPayload":
        if all(value is None for value in (self.startX, self.startY, self.endX, self.endY, self.thickness)):
            raise ValueError("at least one wall property must be supplied")
        return self


class AddOpeningPayload(StrictModel):
    opening: Opening


class MoveOpeningPayload(StrictModel):
    offset: NonNegativeFiniteNumber


class RemoveOpeningPayload(StrictModel):
    pass


CommandPayload = Union[
    AddRoomPayload,
    RemoveRoomPayload,
    MoveRoomPayload,
    ResizeRoomPayload,
    UpdateWallPayload,
    AddOpeningPayload,
    MoveOpeningPayload,
    RemoveOpeningPayload,
]

PAYLOAD_TYPES = {
    "addRoom": AddRoomPayload,
    "removeRoom": RemoveRoomPayload,
    "moveRoom": MoveRoomPayload,
    "resizeRoom": ResizeRoomPayload,
    "updateWall": UpdateWallPayload,
    "addOpening": AddOpeningPayload,
    "moveOpening": MoveOpeningPayload,
    "removeOpening": RemoveOpeningPayload,
}


class PlanEditCommand(StrictModel):
    commandId: UUID
    commandType: Literal[
        "addRoom",
        "removeRoom",
        "moveRoom",
        "resizeRoom",
        "updateWall",
        "addOpening",
        "moveOpening",
        "removeOpening",
    ]
    floorId: UUID
    targetId: UUID | None = None
    payload: dict[str, object]

    def typed_payload(self) -> CommandPayload:
        return TypeAdapter(PAYLOAD_TYPES[self.commandType]).validate_python(self.payload)

    @model_validator(mode="after")
    def validate_payload_and_target(self) -> "PlanEditCommand":
        self.typed_payload()
        if self.commandType not in {"addRoom", "addOpening"} and self.targetId is None:
            raise ValueError(f"targetId is required for {self.commandType}")
        return self


class PlanEditRequest(StrictModel):
    schemaVersion: str
    projectId: UUID
    planId: UUID
    baseRevisionId: UUID
    command: PlanEditCommand

    @model_validator(mode="after")
    def require_supported_schema(self) -> "PlanEditRequest":
        if self.schemaVersion != DRAFT_SCHEMA_VERSION:
            raise ValueError(f"unsupported schema version: {self.schemaVersion}")
        return self

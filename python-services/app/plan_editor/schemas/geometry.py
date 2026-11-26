"""Provisional geometry models matching the M3 C# draft DTOs."""

from typing import Annotated, Literal
from uuid import UUID

from pydantic import Field, model_validator

from .base import StrictModel

DRAFT_SCHEMA_VERSION = "0.1-draft"
FiniteNumber = Annotated[float, Field(allow_inf_nan=False)]
PositiveFiniteNumber = Annotated[float, Field(gt=0, allow_inf_nan=False)]
NonNegativeFiniteNumber = Annotated[float, Field(ge=0, allow_inf_nan=False)]


class CoordinatePoint(StrictModel):
    x: FiniteNumber
    y: FiniteNumber


class CoordinateSystem(StrictModel):
    type: Literal["localCartesian"]
    unit: Literal["m"]
    origin: CoordinatePoint
    xDirection: Literal["right"]
    yDirection: Literal["up"]


class Room(StrictModel):
    id: UUID
    name: str = Field(min_length=1, max_length=200)
    type: str = Field(min_length=1, max_length=100)
    polygon: list[CoordinatePoint] = Field(min_length=4, max_length=10_000)
    wallIds: list[UUID] = Field(min_length=3, max_length=10_000)

    @model_validator(mode="after")
    def require_closed_ring(self) -> "Room":
        if self.polygon[0] != self.polygon[-1]:
            raise ValueError("room polygon must be a closed ring")
        return self


class Wall(StrictModel):
    id: UUID
    start: CoordinatePoint
    end: CoordinatePoint
    thickness: PositiveFiniteNumber


class Opening(StrictModel):
    id: UUID
    type: Literal["door", "window"]
    wallId: UUID
    offset: NonNegativeFiniteNumber
    width: PositiveFiniteNumber


class Floor(StrictModel):
    id: UUID
    level: int
    rooms: list[Room] = Field(default_factory=list, max_length=10_000)
    walls: list[Wall] = Field(default_factory=list, max_length=20_000)
    openings: list[Opening] = Field(default_factory=list, max_length=20_000)


class PlanGeometry(StrictModel):
    schemaVersion: str
    projectId: UUID
    planId: UUID
    landRevisionId: UUID
    requirementsRevisionId: UUID
    coordinateSystem: CoordinateSystem
    floors: list[Floor] = Field(min_length=1, max_length=100)

    @model_validator(mode="after")
    def require_supported_schema(self) -> "PlanGeometry":
        if self.schemaVersion != DRAFT_SCHEMA_VERSION:
            raise ValueError(f"unsupported schema version: {self.schemaVersion}")
        return self

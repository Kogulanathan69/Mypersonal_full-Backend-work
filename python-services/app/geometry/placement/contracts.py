"""Internal, revision-bound D03 inputs for isolated synthetic development."""

from dataclasses import InitVar, dataclass, field
from typing import Annotated, Literal

from pydantic import Field, StrictBool, StrictInt, ValidationError

from app.geometry.footprint.contracts import NonNilUUID, RevisionBinding, ResponseModel
from app.geometry.footprint.feasibility import FeasibilityAssessment
from app.plans.generation.contracts import PositiveMetres, RegulatoryEvidence


@dataclass(frozen=True)
class PlacementAssessment:
    """A trusted D02 result paired with the request evidence used to produce it."""

    assessment: FeasibilityAssessment
    evaluated_evidence: InitVar[RegulatoryEvidence]
    _evidence_json: str = field(init=False, repr=False)

    def __post_init__(self, evaluated_evidence: RegulatoryEvidence) -> None:
        try:
            evidence = RegulatoryEvidence.model_validate(evaluated_evidence)
        except ValidationError:
            raise ValueError("The evaluated regulatory evidence is invalid.") from None
        object.__setattr__(self, "_evidence_json", evidence.model_dump_json())

    def matches_evidence(self, evidence: RegulatoryEvidence) -> bool:
        # Retain D02's exact model equality without aliasing the caller's mutable lists.
        return RegulatoryEvidence.model_validate_json(self._evidence_json) == evidence


class SlotPlacementPolicy(ResponseModel):
    slotId: NonNilUUID
    requiredAreaM2: Annotated[float, Field(gt=0, allow_inf_nan=False)]
    allowRotation: StrictBool


class PlacementRequirements(RevisionBinding):
    """Synthetic counts and one policy per D01 slot, with explicit optional pairs."""

    catalogueVersion: Literal["synthetic-fixture-v1"]
    unit: Literal["m"]
    level: StrictInt = Field(ge=0, le=0)
    roomCounts: dict[str, Annotated[int, Field(ge=1, le=100)]] = Field(min_length=1, max_length=4)
    slots: list[SlotPlacementPolicy] = Field(min_length=1, max_length=100)
    optionalPairs: list[tuple[NonNilUUID, NonNilUUID]] = Field(max_length=4950)


class SearchLimits(ResponseModel):
    candidateChecks: Annotated[int, Field(ge=1, le=20_000)] = 20_000
    minSharedWallM: PositiveMetres = 0.10

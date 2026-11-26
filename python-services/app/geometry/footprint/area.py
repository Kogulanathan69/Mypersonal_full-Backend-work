"""Required-area accounting using explicit revision-bound residential assumptions."""

from math import fsum, isfinite

from pydantic import Field

from app.geometry.footprint.contracts import NonNegativeArea, RevisionBinding, Text
from app.plans.generation.contracts import GenerationInput, RoomProgram


class AreaAssumptions(RevisionBinding):
    """Explicit inputs supplied by the trusted requirements policy or a synthetic fixture."""

    roomProgram: RoomProgram
    additionalSpaceAreasM2: list[NonNegativeArea] = Field(max_length=100)
    assumptionReference: Text


class AreaAssumptionError(ValueError):
    """A fixed diagnostic code, without submitted assumption values."""


def required_area_m2(request: GenerationInput, assumptions: AreaAssumptions) -> float:
    """Sum minimum rectangular room areas and explicitly supplied additional areas."""
    assumptions = AreaAssumptions.model_validate(assumptions)
    expected = (request.projectId, request.landRevisionId, request.requirementsRevisionId)
    if assumptions.revision_key() != expected or assumptions.roomProgram != request.roomProgram:
        raise AreaAssumptionError("INPUT_REVISION_MISMATCH")

    rooms = [
        slot.dimensions.minWidthM * slot.dimensions.minDepthM
        for slot in assumptions.roomProgram.rooms
    ]
    if any(not isfinite(area) or area <= 0 for area in rooms):
        raise AreaAssumptionError("REQUIRED_AREA_POLICY_UNAVAILABLE")
    try:
        total = fsum([*rooms, *assumptions.additionalSpaceAreasM2])
    except OverflowError:
        raise AreaAssumptionError("REQUIRED_AREA_POLICY_UNAVAILABLE") from None
    if not isfinite(total) or total <= 0:
        raise AreaAssumptionError("REQUIRED_AREA_POLICY_UNAVAILABLE")
    return total

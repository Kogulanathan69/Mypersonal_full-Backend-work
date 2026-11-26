from pathlib import Path

import pytest
from pydantic import ValidationError

from app.geometry.footprint.area import AreaAssumptionError, AreaAssumptions, required_area_m2
from app.plans.generation.contracts import GenerationInput
from app.validation.boundary import parse_contract_json

FIXTURE = Path(__file__).parent / "fixtures" / "generation-input.v0.1-draft.json"


def request_fixture():
    return parse_contract_json(GenerationInput, FIXTURE.read_text(encoding="utf-8"))


def assumptions_for(request, *, additional=()):
    return AreaAssumptions(
        projectId=request.projectId,
        landRevisionId=request.landRevisionId,
        requirementsRevisionId=request.requirementsRevisionId,
        roomProgram=request.roomProgram,
        additionalSpaceAreasM2=list(additional),
        assumptionReference="synthetic-area-fixture-v1",
    )


def test_uses_explicit_rectangular_dimensions_and_additional_areas():
    request = request_fixture()
    assert required_area_m2(request, assumptions_for(request, additional=[0, 2.5, 3.5])) == 15
    assert required_area_m2(request, assumptions_for(request)) == 9


def test_additional_area_input_is_required_even_when_empty():
    snapshot = assumptions_for(request_fixture()).model_dump(exclude={"additionalSpaceAreasM2"})
    with pytest.raises(ValidationError):
        AreaAssumptions.model_validate(snapshot)


def test_program_and_requirements_revision_must_match():
    request = request_fixture()
    with pytest.raises(AreaAssumptionError, match="INPUT_REVISION_MISMATCH"):
        required_area_m2(request, assumptions_for(request).model_copy(
            update={"requirementsRevisionId": request.landRevisionId}
        ))
    new_program = request.roomProgram.model_copy(update={"rooms": [
        request.roomProgram.rooms[0].model_copy(update={"roomType": "kitchen"})
    ]})
    with pytest.raises(AreaAssumptionError, match="INPUT_REVISION_MISMATCH"):
        required_area_m2(request, assumptions_for(request).model_copy(update={"roomProgram": new_program}))


@pytest.mark.parametrize("dimension", [1e308, 1e-308])
def test_area_overflow_and_underflow_are_unavailable(dimension):
    request = request_fixture()
    dims = request.roomProgram.rooms[0].dimensions.model_copy(
        update={"minWidthM": dimension, "minDepthM": dimension}
    )
    program = request.roomProgram.model_copy(
        update={"rooms": [request.roomProgram.rooms[0].model_copy(update={"dimensions": dims})]}
    )
    request = request.model_copy(update={"roomProgram": program})
    with pytest.raises(AreaAssumptionError, match="REQUIRED_AREA_POLICY_UNAVAILABLE"):
        required_area_m2(request, assumptions_for(request))


def test_additional_area_sum_cannot_overflow():
    request = request_fixture()
    with pytest.raises(AreaAssumptionError, match="REQUIRED_AREA_POLICY_UNAVAILABLE"):
        required_area_m2(request, assumptions_for(request, additional=[1e308, 1e308]))


@pytest.mark.parametrize("value", [-1, "2", True, float("inf"), float("nan")])
def test_bad_additional_measures_do_not_become_zero(value):
    request = request_fixture()
    with pytest.raises(ValidationError):
        required_area_m2(request, assumptions_for(request).model_copy(
            update={"additionalSpaceAreasM2": [value]}
        ))

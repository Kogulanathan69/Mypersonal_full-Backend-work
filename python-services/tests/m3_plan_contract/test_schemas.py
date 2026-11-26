import json
import math
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
from uuid import UUID, uuid4

import pytest
from pydantic import ValidationError

from app.plan_editor.schemas import EditResult, PlanEditRequest, PlanGeometry, ValidationReference


def command_request(geometry_dict: dict[str, object], command_type: str = "moveRoom") -> dict[str, object]:
    floor = geometry_dict["floors"][0]  # type: ignore[index]
    room = floor["rooms"][0]  # type: ignore[index]
    return {
        "schemaVersion": "0.1-draft",
        "projectId": geometry_dict["projectId"],
        "planId": geometry_dict["planId"],
        "baseRevisionId": str(uuid4()),
        "command": {
            "commandId": str(uuid4()),
            "commandType": command_type,
            "floorId": floor["id"],  # type: ignore[index]
            "targetId": room["id"],  # type: ignore[index]
            "payload": {"deltaX": 1.25, "deltaY": -0.5},
        },
    }


def test_geometry_loads_and_round_trips_without_data_loss(geometry_dict: dict[str, object]) -> None:
    geometry = PlanGeometry.model_validate(geometry_dict)
    serialized = json.loads(geometry.model_dump_json())
    assert serialized == geometry_dict


def test_uuid_fields_are_typed(geometry: PlanGeometry) -> None:
    assert isinstance(geometry.projectId, UUID)
    assert isinstance(geometry.floors[0].rooms[0].id, UUID)


@pytest.mark.parametrize("field", ["projectId", "coordinateSystem", "floors"])
def test_required_geometry_fields_cannot_be_missing(geometry_dict: dict[str, object], field: str) -> None:
    invalid = deepcopy(geometry_dict)
    invalid.pop(field)
    with pytest.raises(ValidationError):
        PlanGeometry.model_validate(invalid)


def test_unknown_geometry_fields_are_not_silently_dropped(geometry_dict: dict[str, object]) -> None:
    invalid = deepcopy(geometry_dict)
    invalid["unexpected"] = "contract drift"
    with pytest.raises(ValidationError):
        PlanGeometry.model_validate(invalid)


def test_unsupported_schema_version_is_rejected(geometry_dict: dict[str, object]) -> None:
    invalid = deepcopy(geometry_dict)
    invalid["schemaVersion"] = "1.0"
    with pytest.raises(ValidationError, match="unsupported schema version"):
        PlanGeometry.model_validate(invalid)


@pytest.mark.parametrize("value", [math.nan, math.inf, -math.inf])
def test_non_finite_coordinates_are_rejected(geometry_dict: dict[str, object], value: float) -> None:
    invalid = deepcopy(geometry_dict)
    invalid["floors"][0]["walls"][0]["start"]["x"] = value  # type: ignore[index]
    with pytest.raises(ValidationError):
        PlanGeometry.model_validate(invalid)


def test_open_ring_is_rejected(geometry_dict: dict[str, object]) -> None:
    invalid = deepcopy(geometry_dict)
    invalid["floors"][0]["rooms"][0]["polygon"][-1] = {"x": 3, "y": 4}  # type: ignore[index]
    with pytest.raises(ValidationError, match="closed ring"):
        PlanGeometry.model_validate(invalid)


def test_valid_command_payload_is_typed(geometry_dict: dict[str, object]) -> None:
    request = PlanEditRequest.model_validate(command_request(geometry_dict))
    payload = request.command.typed_payload()
    assert payload.deltaX == 1.25  # type: ignore[union-attr]


def test_unknown_command_type_is_rejected(geometry_dict: dict[str, object]) -> None:
    request = command_request(geometry_dict, "teleportRoom")
    with pytest.raises(ValidationError):
        PlanEditRequest.model_validate(request)


def test_wrong_command_payload_is_rejected(geometry_dict: dict[str, object]) -> None:
    request = command_request(geometry_dict)
    request["command"]["payload"] = {"offset": 2}  # type: ignore[index]
    with pytest.raises(ValidationError):
        PlanEditRequest.model_validate(request)


def test_null_target_is_rejected_when_command_requires_target(geometry_dict: dict[str, object]) -> None:
    request = command_request(geometry_dict)
    request["command"]["targetId"] = None  # type: ignore[index]
    with pytest.raises(ValidationError, match="targetId is required"):
        PlanEditRequest.model_validate(request)


@pytest.mark.parametrize(
    "status",
    ["accepted", "acceptedWithWarnings", "rejected", "revisionConflict", "validationUnavailable"],
)
def test_result_status_compatibility(geometry: PlanGeometry, status: str) -> None:
    result = EditResult(
        schemaVersion="0.1-draft",
        commandId=uuid4(),
        baseRevisionId=uuid4(),
        status=status,
        candidateGeometry=geometry if status.startswith("accepted") else None,
        diagnostics=[],
        changedEntityIds=[],
    )
    assert result.status == status


def test_validation_reference_preserves_distinct_validation_dimensions() -> None:
    reference = ValidationReference(
        validationId=uuid4(),
        validatorVersion="test-double/1",
        geometryHash="sha256:test-only",
        geometryValid=True,
        requirementsMatch=None,
        regulatoryEvidenceStatus="NOT_VERIFIED",
        hasBlockingIssues=False,
        validatedAtUtc=datetime.now(timezone.utc),
    )
    assert reference.geometryValid is True
    assert reference.requirementsMatch is None
    assert reference.regulatoryEvidenceStatus == "NOT_VERIFIED"


def test_result_rejects_unsupported_schema_and_missing_accepted_candidate() -> None:
    common = {
        "commandId": uuid4(),
        "baseRevisionId": uuid4(),
        "diagnostics": [],
        "changedEntityIds": [],
    }
    with pytest.raises(ValidationError, match="unsupported schema version"):
        EditResult(schemaVersion="1.0", status="rejected", **common)
    with pytest.raises(ValidationError, match="candidateGeometry"):
        EditResult(schemaVersion="0.1-draft", status="accepted", **common)


def test_validation_timestamp_must_include_offset() -> None:
    with pytest.raises(ValidationError, match="UTC offset"):
        ValidationReference(
            validationId=uuid4(), validatorVersion="test/1", geometryHash="hash",
            geometryValid=True, requirementsMatch=True, regulatoryEvidenceStatus="NOT_VERIFIED",
            hasBlockingIssues=False, validatedAtUtc=datetime.now(),
        )


def test_shared_command_samples_match_python_acceptance(geometry_dict: dict[str, object]) -> None:
    path = Path(__file__).parents[3] / "docs" / "proposals" / "m3-plan-editor" / "EDIT-COMMAND-SAMPLES.json"
    samples = json.loads(path.read_text(encoding="utf-8"))
    floor_id = geometry_dict["floors"][0]["id"]  # type: ignore[index]
    for sample in samples:
        common = {
            "commandId": str(uuid4()),
            "commandType": sample["commandType"],
            "floorId": floor_id,
            "targetId": str(uuid4()) if sample["targetRequired"] else None,
        }
        PlanEditRequest.model_validate({
            "schemaVersion": "0.1-draft",
            "projectId": geometry_dict["projectId"],
            "planId": geometry_dict["planId"],
            "baseRevisionId": str(uuid4()),
            "command": {**common, "payload": sample["validPayload"]},
        })
        with pytest.raises(ValidationError):
            PlanEditRequest.model_validate({
                "schemaVersion": "0.1-draft",
                "projectId": geometry_dict["projectId"],
                "planId": geometry_dict["planId"],
                "baseRevisionId": str(uuid4()),
                "command": {**common, "payload": sample["invalidPayload"]},
            })


def test_command_rejects_non_finite_numeric_payload(geometry_dict: dict[str, object]) -> None:
    request = command_request(geometry_dict)
    request["command"]["payload"] = {"deltaX": math.inf, "deltaY": 0}  # type: ignore[index]
    with pytest.raises(ValidationError):
        PlanEditRequest.model_validate(request)

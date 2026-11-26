"""Captain edit-preview route over the existing M3 operations engine (no new edit rules).

The engine applies one structured edit command to the source geometry and checks referential integrity only. The
caller (C# API) then obtains server-issued validation evidence separately, so `validation` is always null here.
"""
import json
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict

from app.plan_editor.operations.engine import EditOperationError, apply_command
from app.plan_editor.schemas.commands import PlanEditCommand
from app.plan_editor.schemas.geometry import DRAFT_SCHEMA_VERSION, PlanGeometry
from app.plan_editor.schemas.results import EditDiagnostic, EditResult


class EditPreviewRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schemaVersion: Literal["captain-plan-edit-preview/1"]
    baseRevisionId: UUID
    sourceGeometry: PlanGeometry
    command: PlanEditCommand


def parse_request(raw: bytes) -> EditPreviewRequest:
    # Strict JSON mode: no type coercion (numbers stay numbers, UUIDs must be canonical strings).
    return EditPreviewRequest.model_validate_json(raw, strict=True)


def preview(request: EditPreviewRequest) -> dict:
    command = request.command
    try:
        candidate, changed = apply_command(request.sourceGeometry, command)
    except EditOperationError as error:
        result = EditResult(
            schemaVersion=DRAFT_SCHEMA_VERSION, commandId=command.commandId, baseRevisionId=request.baseRevisionId,
            status="rejected",
            diagnostics=[EditDiagnostic(code=error.code, message=str(error), severity="error", entityId=error.entity_id)],
        )
        return json.loads(result.model_dump_json())
    result = EditResult(
        schemaVersion=DRAFT_SCHEMA_VERSION, commandId=command.commandId, baseRevisionId=request.baseRevisionId,
        status="accepted", candidateGeometry=candidate, changedEntityIds=changed,
    )
    return json.loads(result.model_dump_json())

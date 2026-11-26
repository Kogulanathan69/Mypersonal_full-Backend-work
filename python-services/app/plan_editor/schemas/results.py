"""Provisional edit-result models; no validation result is fabricated here."""

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import Field, model_validator

from .base import StrictModel
from .geometry import DRAFT_SCHEMA_VERSION, PlanGeometry


class EditDiagnostic(StrictModel):
    code: str = Field(min_length=1, max_length=100)
    message: str = Field(min_length=1, max_length=2_000)
    severity: Literal["error", "warning", "info"]
    entityId: UUID | None = None


class ValidationReference(StrictModel):
    validationId: UUID
    validatorVersion: str = Field(min_length=1, max_length=100)
    geometryHash: str = Field(min_length=1, max_length=512)
    geometryValid: bool
    requirementsMatch: bool | None
    regulatoryEvidenceStatus: str = Field(min_length=1, max_length=100)
    hasBlockingIssues: bool
    validatedAtUtc: datetime

    @model_validator(mode="after")
    def require_offset_aware_timestamp(self) -> "ValidationReference":
        if self.validatedAtUtc.tzinfo is None or self.validatedAtUtc.utcoffset() is None:
            raise ValueError("validatedAtUtc must include a UTC offset")
        return self


class EditResult(StrictModel):
    schemaVersion: str
    commandId: UUID
    baseRevisionId: UUID
    status: Literal[
        "accepted",
        "acceptedWithWarnings",
        "rejected",
        "revisionConflict",
        "validationUnavailable",
    ]
    candidateGeometry: PlanGeometry | None = None
    validation: ValidationReference | None = None
    diagnostics: list[EditDiagnostic] = Field(default_factory=list, max_length=10_000)
    changedEntityIds: list[UUID] = Field(default_factory=list, max_length=30_000)

    @model_validator(mode="after")
    def require_supported_schema_and_candidate(self) -> "EditResult":
        if self.schemaVersion != DRAFT_SCHEMA_VERSION:
            raise ValueError(f"unsupported schema version: {self.schemaVersion}")
        if self.status in {"accepted", "acceptedWithWarnings"} and self.candidateGeometry is None:
            raise ValueError("accepted results require candidateGeometry")
        return self

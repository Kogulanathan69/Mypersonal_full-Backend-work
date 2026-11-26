"""D01-C provisional validation response metadata.

A supplied digest identifies the reported candidate snapshot. Its syntax,
revision references and review metadata do not authenticate the snapshot or
establish spatial validity, evidence provenance or regulatory compliance.
"""

import re
from datetime import datetime, timezone
from typing import Annotated

from pydantic import AwareDatetime, BeforeValidator, Field, StrictBool, model_validator

from app.geometry.footprint.contracts import (
    Diagnostics,
    EvidenceStatus,
    NonNilUUID,
    RevisionBinding,
    Text,
    has_critical_diagnostic,
)


def _timestamp(value: object) -> datetime:
    if isinstance(value, str) and re.fullmatch(
        r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}"
        r"(?:\.[0-9]{1,6})?(?:Z|[+-](?:[01][0-9]|2[0-3]):[0-5][0-9])",
        value,
    ):
        value = datetime.fromisoformat(value)
    if not isinstance(value, datetime) or value.utcoffset() is None:
        raise ValueError("a timezone-aware ISO 8601 timestamp is required")
    try:
        return value.astimezone(timezone.utc)
    except OverflowError as error:
        raise ValueError("timestamp is outside the UTC datetime range") from error


Sha256 = Annotated[str, Field(min_length=64, max_length=64, pattern=r"^[0-9a-f]{64}$")]
UtcTimestamp = Annotated[AwareDatetime, BeforeValidator(_timestamp)]


class ValidationResult(RevisionBinding):
    validationId: NonNilUUID
    candidateId: NonNilUUID
    geometryHash: Sha256
    geometryValid: StrictBool | None
    requirementsMatch: StrictBool | None
    regulatoryEvidenceStatus: EvidenceStatus
    hasBlockingIssues: StrictBool
    validatorVersion: Text
    ruleSetVersion: Text | None
    validatedAtUtc: UtcTimestamp
    diagnostics: Diagnostics

    @model_validator(mode="after")
    def require_consistent_validation(self) -> "ValidationResult":
        """True/false mean evaluated; null means unchecked."""
        if self.regulatoryEvidenceStatus == "REVIEWED" and self.ruleSetVersion is None:
            raise ValueError("reviewed evidence requires a rule set version")
        incomplete_or_failed = (
            self.geometryValid is not True
            or self.requirementsMatch is not True
            or self.regulatoryEvidenceStatus != "REVIEWED"
        )
        critical = has_critical_diagnostic(self.diagnostics)
        if incomplete_or_failed and not critical:
            raise ValueError("unchecked or failed validation requires a critical diagnostic")
        if self.hasBlockingIssues != critical:
            raise ValueError("blocking state must match diagnostic severity")
        return self

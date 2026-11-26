"""Provisional metric-land/0.1-draft contract (D01-B).

Only ring structure is checked. Closure is exact, with no tolerance, repair,
georeference inference, area, intersection, winding or containment checks.
Local resource limits (10,000 points/ring, 100 holes) need M2/M6 approval.
"""

import re
from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, BeforeValidator, ConfigDict, Field, model_validator


class ContractModel(BaseModel):
    model_config = ConfigDict(
        extra="forbid", strict=True, validate_default=True, revalidate_instances="always"
    )


FiniteNumber = Annotated[float, Field(allow_inf_nan=False)]


class CoordinatePoint(ContractModel):
    x: FiniteNumber
    y: FiniteNumber


class CoordinateSystem(ContractModel):
    type: Literal["localCartesian"]
    unit: Literal["m"]
    origin: CoordinatePoint
    xDirection: Literal["right"]
    yDirection: Literal["up"]


Ring = Annotated[list[CoordinatePoint], Field(min_length=4, max_length=10_000)]


class PolygonBoundary(ContractModel):
    exterior: Ring
    holes: list[Ring] = Field(default_factory=list, max_length=100)

    @model_validator(mode="after")
    def require_closed_rings(self) -> "PolygonBoundary":
        for ring in [self.exterior, *self.holes]:
            if not 4 <= len(ring) <= 10_000:
                raise ValueError("polygon rings require 4 to 10,000 points")
            if ring[0] != ring[-1]:
                raise ValueError("polygon rings must be explicitly closed")
        return self


class MetricLand(ContractModel):
    schemaVersion: Literal["metric-land/0.1-draft"]
    coordinateSystem: CoordinateSystem
    boundary: PolygonBoundary


def _uuid(value: object) -> UUID:
    if isinstance(value, str) and re.fullmatch(
        r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", value
    ):
        value = UUID(value)
    if not isinstance(value, UUID) or value.int == 0:
        raise ValueError("a nonnil canonical UUID is required")
    return value


NonNilUUID = Annotated[UUID, BeforeValidator(_uuid)]
Text = Annotated[str, Field(min_length=1, max_length=500, pattern=r"\S")]
NonNegativeArea = Annotated[float, Field(ge=0, allow_inf_nan=False)]
EvidenceStatus = Literal["MISSING", "UNVERIFIED", "CONFLICTING", "REVIEWED"]
DiagnosticCode = Annotated[str, Field(min_length=1, max_length=80, pattern=r"^[A-Z][A-Z0-9_]*$")]
SafeMessage = Annotated[str, Field(min_length=1, max_length=500, pattern=r"^[^\x00-\x1f\x7f]+$")]
JsonPointer = Annotated[str, Field(max_length=500, pattern=r"^(?:/(?:[^~/\x00-\x1f\x7f]|~[01])*)*$")]


class ResponseModel(ContractModel):
    model_config = ConfigDict(hide_input_in_errors=True, revalidate_instances="always")


class RevisionBinding(ResponseModel):
    projectId: NonNilUUID
    landRevisionId: NonNilUUID
    requirementsRevisionId: NonNilUUID

    def revision_key(self) -> tuple[UUID, UUID, UUID]:
        return self.projectId, self.landRevisionId, self.requirementsRevisionId


class Diagnostic(ResponseModel):
    """Public diagnostic. Producers must supply curated, sanitized messages."""

    code: DiagnosticCode
    severity: Literal["CRITICAL", "WARNING"]
    message: SafeMessage
    path: JsonPointer | None = None
    relatedEntityIds: list[NonNilUUID] = Field(default_factory=list, max_length=100)
    sourceReferenceIds: list[Text] = Field(default_factory=list, max_length=100)

    @model_validator(mode="after")
    def require_visible_message(self) -> "Diagnostic":
        if not self.message.strip():
            raise ValueError("diagnostic message must contain visible text")
        return self


Diagnostics = Annotated[list[Diagnostic], Field(max_length=1_000)]


def has_critical_diagnostic(diagnostics: Diagnostics) -> bool:
    return any(item.severity == "CRITICAL" for item in diagnostics)


class FeasibilityResult(RevisionBinding):
    """Provisional response; positive footprints reuse the metric land shape.

    Unchecked measurements are null. An empty calculated footprint has zero
    area and null geometry. Multiple disjoint polygons await the M2/M6 handoff.
    """

    schemaVersion: Literal["m6-feasibility-result/0.1-draft"]
    status: Literal["FEASIBLE", "INFEASIBLE", "NEEDS_REVIEW", "UNSUPPORTED"]
    buildableFootprint: MetricLand | None
    landAreaM2: NonNegativeArea | None
    buildableAreaM2: NonNegativeArea | None
    requiredAreaM2: NonNegativeArea | None
    regulatoryEvidenceStatus: EvidenceStatus
    ruleSetVersion: Text | None
    feasibilityPolicyVersion: Text
    diagnostics: Diagnostics

    @model_validator(mode="after")
    def require_consistent_feasibility(self) -> "FeasibilityResult":
        area = self.buildableAreaM2
        required = self.requiredAreaM2
        critical = has_critical_diagnostic(self.diagnostics)
        if area is not None and (self.landAreaM2 is None or area > self.landAreaM2):
            raise ValueError("buildable area requires land area and cannot exceed it")
        if (self.buildableFootprint is not None) != (area is not None and area > 0):
            raise ValueError("positive buildable area and footprint must be supplied together")
        if self.regulatoryEvidenceStatus == "REVIEWED" and self.ruleSetVersion is None:
            raise ValueError("reviewed evidence requires a rule set version")
        if self.status in ("FEASIBLE", "INFEASIBLE"):
            if area is None or required is None:
                raise ValueError("a feasibility decision requires calculated areas")
            fits = area > 0 and area >= required
            if (self.status == "FEASIBLE") != fits:
                raise ValueError("feasibility status contradicts calculated areas")
        if self.status == "FEASIBLE" and (
            critical or self.regulatoryEvidenceStatus != "REVIEWED"
        ):
            raise ValueError("feasible status requires reviewed evidence and no critical diagnostics")
        if self.status != "FEASIBLE" and not self.diagnostics:
            raise ValueError("non-feasible status requires explicit diagnostics")
        return self

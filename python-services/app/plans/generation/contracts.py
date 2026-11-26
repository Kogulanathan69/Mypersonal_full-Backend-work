"""D01-B local development contracts, pending cross-member approval.

Additional provisional decisions:
* Wire names follow the reviewed camelCase handoff; no alternate aliases.
* IDs accept UUID objects or canonical hyphenated UUID strings, never nil.
* Seed is an integer in [0, 2**31-1], portable to C# Int32; no PRNG promised.
* Catalogue is synthetic-fixture-v1; bedroom/livingRoom/kitchen/bathroom only.
  Dimensions express synthetic user requirements, never legal minimums.
* Adjacency uses fromSlotId/toSlotId/relationship with adjacent or separated;
  both are undirected. A room pair may have only one relationship.
* Evidence statuses: MISSING, UNVERIFIED, CONFLICTING, REVIEWED. REVIEWED
  records supplied review metadata, NOT authenticated or verified compliance.
  VERIFIED/COMPLIANT claims are deliberately unsupported. M7 must approve
  provenance, reviewer authentication and applicability policy before use.
* Only setback constraint metadata is supported. Parcel edges are zero-based
  exterior segment indexes. Source locators are opaque, never fetched.
* Local resource caps: 100 rooms, 4,950 relationships, 1,000 constraints/gaps,
  500 characters per metadata string. These are not approved service limits.

Schemas cannot establish ownership, revision confirmation/freshness, source
authenticity, legal applicability or geometry validity. No I/O or algorithms.
"""

from typing import Annotated, Literal

from pydantic import Field, StrictInt, field_validator, model_validator

from app.geometry.footprint.contracts import (
    ContractModel,
    DiagnosticCode,
    Diagnostics,
    MetricLand,
    NonNegativeArea,
    NonNilUUID,
    ResponseModel,
    RevisionBinding,
    Text,
    _uuid,
    has_critical_diagnostic,
)
from app.plan_editor.schemas.geometry import PlanGeometry
from app.validation.contracts import ValidationResult


PositiveMetres = Annotated[float, Field(gt=0, allow_inf_nan=False)]
Seed = Annotated[int, Field(strict=True, ge=0, le=2**31 - 1)]


class RoomDimensions(ContractModel):
    minWidthM: PositiveMetres
    minDepthM: PositiveMetres
    maxWidthM: PositiveMetres | None = None
    maxDepthM: PositiveMetres | None = None

    @model_validator(mode="after")
    def require_ordered_bounds(self) -> "RoomDimensions":
        for minimum, maximum in (
            (self.minWidthM, self.maxWidthM), (self.minDepthM, self.maxDepthM)
        ):
            if maximum is not None and maximum < minimum:
                raise ValueError("maximum dimension must be at least the minimum")
        return self


class RoomSlot(ContractModel):
    slotId: NonNilUUID
    roomType: Literal["bedroom", "livingRoom", "kitchen", "bathroom"]
    dimensions: RoomDimensions


class RoomProgram(ContractModel):
    level: StrictInt = Field(ge=0, le=0)
    catalogueVersion: Literal["synthetic-fixture-v1"]
    rooms: list[RoomSlot] = Field(min_length=1, max_length=100)

    @model_validator(mode="after")
    def require_unique_slots(self) -> "RoomProgram":
        ids = [room.slotId for room in self.rooms]
        if len(set(ids)) != len(ids):
            raise ValueError("room slot IDs must be unique")
        return self


class AdjacencyPreference(ContractModel):
    fromSlotId: NonNilUUID
    toSlotId: NonNilUUID
    relationship: Literal["adjacent", "separated"]

    @model_validator(mode="after")
    def require_distinct_slots(self) -> "AdjacencyPreference":
        if self.fromSlotId == self.toSlotId:
            raise ValueError("adjacency must reference distinct room slots")
        return self


class EvidenceGap(ContractModel):
    code: Text


class SourceProvenance(ContractModel):
    sourceVersion: Text
    sourceLocator: Text
    jurisdiction: Text


class ConstraintApplicability(ContractModel):
    status: Literal["APPLICABLE", "NOT_APPLICABLE", "UNKNOWN"]
    basis: Text


class ReviewedConstraint(ContractModel):
    ruleId: Text
    type: Literal["setback"]
    source: SourceProvenance
    parcelEdgeIndex: Annotated[int, Field(strict=True, ge=0)]
    distanceM: Annotated[float, Field(ge=0, allow_inf_nan=False)]
    applicability: ConstraintApplicability
    reviewedBy: NonNilUUID
    reviewReference: Text


class RegulatoryEvidence(ContractModel):
    status: Literal["MISSING", "UNVERIFIED", "CONFLICTING", "REVIEWED"]
    ruleSetVersion: Text | None
    constraints: list[ReviewedConstraint] = Field(max_length=1_000)
    gaps: list[EvidenceGap] = Field(max_length=1_000)

    @model_validator(mode="after")
    def require_consistent_evidence(self) -> "RegulatoryEvidence":
        if self.status == "MISSING":
            if self.ruleSetVersion is not None or self.constraints or not self.gaps:
                raise ValueError("missing evidence requires gaps and no rules or constraints")
        elif self.status == "REVIEWED":
            if self.ruleSetVersion is None or not self.constraints or self.gaps:
                raise ValueError("reviewed evidence requires version, constraints and no gaps")
            if any(c.applicability.status == "UNKNOWN" for c in self.constraints):
                raise ValueError("reviewed evidence cannot have unknown applicability")
        elif not self.gaps:
            raise ValueError("unverified or conflicting evidence requires explicit gaps")
        if self.constraints and self.ruleSetVersion is None:
            raise ValueError("constraints require a rule set version")
        identities = [(c.ruleId, c.parcelEdgeIndex) for c in self.constraints]
        if len(set(identities)) != len(identities):
            raise ValueError("duplicate rule/edge evidence must be represented as a gap")
        return self


class GenerationInput(ContractModel):
    schemaVersion: Literal["m6-generation-input/0.1-draft"]
    projectId: NonNilUUID
    landRevisionId: NonNilUUID
    requirementsRevisionId: NonNilUUID
    land: MetricLand
    roomProgram: RoomProgram
    adjacencyPreferences: list[AdjacencyPreference] = Field(max_length=4_950)
    regulatoryEvidence: RegulatoryEvidence
    seed: Seed

    @model_validator(mode="after")
    def require_valid_references(self) -> "GenerationInput":
        slots = {room.slotId for room in self.roomProgram.rooms}
        pairs = set()
        for preference in self.adjacencyPreferences:
            pair = frozenset((preference.fromSlotId, preference.toSlotId))
            if not pair <= slots:
                raise ValueError("adjacency references an unknown room slot")
            if pair in pairs:
                raise ValueError("duplicate or contradictory adjacency for room pair")
            pairs.add(pair)
        edge_count = len(self.land.boundary.exterior) - 1
        if any(c.parcelEdgeIndex >= edge_count for c in self.regulatoryEvidence.constraints):
            raise ValueError("constraint references an unknown exterior parcel edge")
        return self


class AlternativeMetrics(ResponseModel):
    floorAreaM2: NonNegativeArea
    footprintAreaM2: NonNegativeArea
    roomCount: Annotated[int, Field(ge=0, le=100_000)]


class DiversitySignature(ResponseModel):
    version: Text
    value: Annotated[str, Field(min_length=1, max_length=128, pattern=r"^[A-Za-z0-9._-]+$")]


def _geometry_uuids(value: object, depth: int = 0) -> object:
    """Decode only M3 UUID wire fields before strict validation."""
    if not isinstance(value, dict):
        return value

    normalized = value.copy()
    for field in ("projectId", "landRevisionId", "requirementsRevisionId", "planId", "id", "wallId"):
        if field in value:
            normalized[field] = _uuid(value[field])
    wall_ids = value.get("wallIds")
    if isinstance(wall_ids, list) and len(wall_ids) <= 10_000:
        normalized["wallIds"] = [_uuid(item) for item in wall_ids]
    if depth == 0:
        collections = (("floors", 100),)
    elif depth == 1:
        collections = (("rooms", 10_000), ("walls", 20_000), ("openings", 20_000))
    else:
        collections = ()
    for field, limit in collections:
        items = value.get(field)
        if isinstance(items, list) and len(items) <= limit:
            normalized[field] = [_geometry_uuids(item, depth + 1) for item in items]
    return normalized


class GenerationAlternative(RevisionBinding):
    """M3 draft geometry is reused at the boundary.

    Canonical hashing, coordinate tolerances, geometry/metric consistency,
    cross-floor topology and final geometry version negotiation remain open.
    """

    alternativeId: NonNilUUID
    geometry: PlanGeometry
    metrics: AlternativeMetrics
    validation: ValidationResult
    diversitySignature: DiversitySignature
    algorithmVersion: Text

    @field_validator("geometry", mode="before")
    @classmethod
    def require_strict_geometry(cls, value: object) -> PlanGeometry:
        # M3 forbids extra fields but enables coercion by default.
        if isinstance(value, PlanGeometry):
            value = value.model_dump()
        return PlanGeometry.model_validate(_geometry_uuids(value), strict=True)

    @model_validator(mode="after")
    def require_candidate_binding(self) -> "GenerationAlternative":
        geometry = self.geometry
        geometry_ids = (geometry.projectId, geometry.landRevisionId, geometry.requirementsRevisionId)
        if geometry_ids != self.revision_key() or self.validation.revision_key() != self.revision_key():
            raise ValueError("geometry, validation and alternative must bind the same input revisions")
        if self.validation.candidateId != geometry.planId:
            raise ValueError("validation must reference the geometry plan ID")
        ids = {geometry.planId}
        for floor in geometry.floors:
            for entity in (floor, *floor.rooms, *floor.walls, *floor.openings):
                if entity.id in ids:
                    raise ValueError("geometry entity IDs must be unique")
                ids.add(entity.id)
            walls = {wall.id for wall in floor.walls}
            for room in floor.rooms:
                if len(set(room.wallIds)) != len(room.wallIds) or not set(room.wallIds) <= walls:
                    raise ValueError("room must reference distinct walls on its floor")
            if any(opening.wallId not in walls for opening in floor.openings):
                raise ValueError("opening must reference a wall on its floor")
        return self


class GenerationResult(RevisionBinding):
    """Generation envelope for a single input snapshot.

    Supplied hashes, area metrics and diversity signatures are metadata.
    Their calculation and verification belong to later implementation phases.
    """

    schemaVersion: Literal["m6-generation-result/0.1-draft"]
    outcome: Literal["SUCCESS", "UNAVAILABLE", "INFEASIBLE", "NEEDS_REVIEW", "UNSUPPORTED", "FAILED"]
    reasonCode: DiagnosticCode | None
    algorithmVersion: Text | None
    seed: Seed
    alternatives: list[GenerationAlternative] = Field(max_length=3)
    diagnostics: Diagnostics

    @model_validator(mode="after")
    def require_consistent_outcome(self) -> "GenerationResult":
        critical = has_critical_diagnostic(self.diagnostics)
        if self.outcome != "SUCCESS":
            if self.alternatives or self.reasonCode is None or not critical:
                raise ValueError("unsuccessful generation requires no alternatives, a reason and a critical diagnostic")
            if self.outcome == "UNAVAILABLE" and self.reasonCode != "GENERATION_UNAVAILABLE":
                raise ValueError("unavailable generation requires GENERATION_UNAVAILABLE")
            return self

        if len(self.alternatives) != 3 or self.reasonCode is not None or self.algorithmVersion is None or critical:
            raise ValueError("success requires three alternatives, an algorithm version and no reason or critical diagnostics")
        if len({item.alternativeId for item in self.alternatives}) != 3:
            raise ValueError("alternative IDs must be distinct")
        if len({item.geometry.planId for item in self.alternatives}) != 3:
            raise ValueError("candidate plan IDs must be distinct")
        if len({item.validation.validationId for item in self.alternatives}) != 3:
            raise ValueError("validation IDs must be distinct")
        if len({item.diversitySignature.version for item in self.alternatives}) != 1:
            raise ValueError("diversity signatures must share a version")
        if len({item.diversitySignature.value for item in self.alternatives}) != 3:
            raise ValueError("supplied diversity signatures must be distinct")
        for item in self.alternatives:
            if item.revision_key() != self.revision_key() or item.algorithmVersion != self.algorithmVersion:
                raise ValueError("alternative revisions and algorithm must match the result")
            if item.validation.hasBlockingIssues:
                raise ValueError("every successful alternative requires nonblocking validation")
        if len({item.validation.ruleSetVersion for item in self.alternatives}) != 1:
            raise ValueError("alternatives must use the same rule set")
        return self

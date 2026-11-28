"""Captain generation adapter: D02 feasibility -> D04 bounded search -> M6 GenerationResult (draft contract).

No new placement, feasibility or regulatory rules are introduced. This module only:
* builds the trusted D02/D03/D04 inputs from one GenerationInput;
* maps every non-success to a truthful M6 outcome with a fixed reason code (never a partial or invalid layout);
* converts the three D04 candidates (axis-aligned room rectangles) into M3 draft PlanGeometry: one floor at level 0,
  four walls per room, plus generated doors (entrance + internal access tree) and windows (app.internal.openings);
* validates the envelope with the existing GenerationResult model, so an inconsistent result can never leave the
  service.

Limitations (recorded, not hidden):
* Setback evidence is accepted ONLY as an explicit synthetic reviewed snapshot (D02/D04 design). Real reviewed
  constraints are not available (G06), so any other evidence yields NEEDS_REVIEW.
* Adjacency preferences are treated as optional preferences.
* Physical access is validated (every room reachable through doors from an external entrance); door/window sizes are
  design preferences (WARNING), not regulatory minimums. There are no corridors: rooms connect directly.
* Walls are room-boundary segments with a fixed nominal thickness. Shared-wall topology (JC-01/JC-02) is open, so
  adjacent rooms keep coincident walls.
"""
import json
from collections import Counter
from datetime import datetime, timezone
from hashlib import sha256
from typing import Literal
from uuid import UUID, uuid5

from pydantic import StrictBool

from app.geometry.footprint.area import AreaAssumptions
from app.internal.openings import generate_openings
from app.geometry.footprint.contracts import ContractModel, Diagnostic
from app.geometry.footprint.feasibility import ReviewedSetbackSnapshot, assess_feasibility
from app.geometry.footprint.parcel import ConfirmedMetricParcel
from app.geometry.layout_validation import Status, generate_alternatives
from app.geometry.placement.contracts import PlacementAssessment, PlacementRequirements, SlotPlacementPolicy
from app.plans.generation.contracts import GenerationInput, GenerationResult

RESULT_SCHEMA = "m6-generation-result/0.1-draft"
ALGORITHM_VERSION = "captain-generation-adapter/1+d04"
VALIDATOR_VERSION = "captain-d04-validation/1"
DIVERSITY_VERSION = "captain-diversity/1"
GEOMETRY_SCHEMA = "0.1-draft"
NOMINAL_WALL_THICKNESS_M = 0.1
_NAMESPACE = UUID("6f1c9e52-4a8b-5d0e-9c3a-0b7d2e6f8a14")

_ROOM_LABELS = {"bedroom": "Bedroom", "livingRoom": "Living room", "kitchen": "Kitchen", "bathroom": "Bathroom"}

_MESSAGES = {
    "REGULATORY_EVIDENCE_MISSING": "Regulatory setback evidence is missing; generation needs review.",
    "REGULATORY_EVIDENCE_UNVERIFIED": "Regulatory setback evidence is unverified; generation needs review.",
    "REGULATORY_EVIDENCE_CONFLICTING": "Regulatory setback evidence conflicts; generation needs review.",
    "REVIEWED_SETBACK_SNAPSHOT_MISSING": "No reviewed setback snapshot is available for this input.",
    "GENERATION_UNAVAILABLE": "The geometry dependency is unavailable.",
    "GENERATION_FAILED": "Generation could not complete safely.",
    "SYNTHETIC_REGULATORY_EVIDENCE": "Setbacks come from a synthetic local fixture; no regulatory compliance is established.",
    "PHYSICAL_ACCESS_NOT_EVALUATED": "Doors, corridors and physical access were not evaluated.",
    "PHYSICAL_ACCESS_UNSATISFIABLE": "No layout lets every room be reached through doors from an entrance.",
    "OPENING_DIMENSIONS_DESIGN_PREFERENCE": "Door and window sizes are configurable design preferences, not regulatory minimums.",
    "ROOM_WITHOUT_EXTERNAL_WINDOW": "A habitable room has no external wall long enough for a window.",
    "ACCESS_THROUGH_BEDROOM": "Some rooms are reached through a bedroom.",
    "CIRCULATION_IS_RESIDUAL_SPACE": "Circulation is the unoccupied space inside the envelope, closed by generated exterior walls.",
}

_D04_OUTCOME = {
    Status.INCONCLUSIVE: "NEEDS_REVIEW",
    Status.UNSUPPORTED: "UNSUPPORTED",
    Status.INVALID_GEOMETRY: "INFEASIBLE",
    Status.REQUIREMENTS_VIOLATION: "INFEASIBLE",
    Status.REGULATORY_NON_COMPLIANT: "INFEASIBLE",
}


class GenerationRunRequest(ContractModel):
    schemaVersion: Literal["captain-generation-run/1"]
    input: GenerationInput
    # The caller (C# API) asserts that the REVIEWED evidence in `input` is the explicit synthetic local fixture.
    syntheticReviewedSetbacks: StrictBool


def _diagnostic(code: str, severity: str = "CRITICAL", message: str | None = None) -> Diagnostic:
    return Diagnostic(code=code, severity=severity, message=message or _MESSAGES.get(code) or "Generation did not succeed.")


def _envelope(request: GenerationInput, outcome: str, reason: str | None, diagnostics, alternatives=()) -> dict:
    """Build the wire document, then validate it with the unchanged GenerationResult contract in JSON mode.

    JSON mode is how the contract is consumed at the service boundary (UUID strings, ISO timestamps, nested
    objects), so strict validation applies exactly as for any client. An inconsistent envelope raises.
    """
    document = {
        "schemaVersion": RESULT_SCHEMA,
        "projectId": str(request.projectId),
        "landRevisionId": str(request.landRevisionId),
        "requirementsRevisionId": str(request.requirementsRevisionId),
        "outcome": outcome,
        "reasonCode": reason,
        "algorithmVersion": ALGORITHM_VERSION,
        "seed": request.seed,
        "alternatives": list(alternatives),
        "diagnostics": [d.model_dump(mode="json") for d in diagnostics],
    }
    return GenerationResult.model_validate_json(json.dumps(document)).model_dump(mode="json")


def _failure(request: GenerationInput, outcome: str, code: str, diagnostics=()) -> dict:
    if outcome == "UNAVAILABLE":
        code = "GENERATION_UNAVAILABLE"
    critical = [d for d in diagnostics if d.severity == "CRITICAL"]
    return _envelope(request, outcome, code, critical or [_diagnostic(code)])


def _id(*parts: object) -> UUID:
    return uuid5(_NAMESPACE, "|".join(str(part) for part in parts))


def _canonical_hash(value: object) -> str:
    return sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def _geometry(request: GenerationInput, plan_id: UUID, rooms) -> dict:
    floor_id = _id(plan_id, "floor", 0)
    counters: Counter[str] = Counter()
    room_items, walls = [], []
    for room in sorted(rooms, key=lambda item: str(item.slot_id)):
        counters[room.room_type] += 1
        ring = [{"x": float(x), "y": float(y)} for x, y in room.ring]
        wall_ids = []
        for index, (start, end) in enumerate(room.walls):
            wall_id = _id(plan_id, room.slot_id, "wall", index)
            wall_ids.append(str(wall_id))
            walls.append({
                "id": str(wall_id),
                "start": {"x": float(start[0]), "y": float(start[1])},
                "end": {"x": float(end[0]), "y": float(end[1])},
                "thickness": NOMINAL_WALL_THICKNESS_M,
            })
        room_items.append({
            "id": str(_id(plan_id, room.slot_id, "room")),
            "name": f"{_ROOM_LABELS.get(room.room_type, room.room_type)} {counters[room.room_type]}",
            "type": room.room_type,
            "polygon": ring,
            "wallIds": wall_ids,
        })
    return {
        "schemaVersion": GEOMETRY_SCHEMA,
        "projectId": str(request.projectId),
        "planId": str(plan_id),
        "landRevisionId": str(request.landRevisionId),
        "requirementsRevisionId": str(request.requirementsRevisionId),
        "coordinateSystem": request.land.coordinateSystem.model_dump(mode="json"),
        "floors": [{"id": str(floor_id), "level": 0, "rooms": room_items, "walls": walls, "openings": []}],
    }


def _with_openings(geometry: dict, plan_id: UUID) -> list[tuple[str, str]]:
    """Doors (entrance + internal spanning tree) and windows; returns access/opening diagnostics."""
    return generate_openings(geometry["floors"][0], plan_id).diagnostics


def _alternative(request: GenerationInput, ordinal: int, candidate, validated_at: str) -> dict:
    revision = (request.projectId, request.landRevisionId, request.requirementsRevisionId)
    plan_id = _id(*revision, request.seed, ordinal, candidate.validation.candidate_id)
    geometry = _geometry(request, plan_id, candidate.rooms)
    access = _with_openings(geometry, plan_id)
    if any(severity == "CRITICAL" for _, severity in access):
        return None
    geometry_hash = _canonical_hash(geometry)
    area = sum((r.bounds[2] - r.bounds[0]) * (r.bounds[3] - r.bounds[1]) for r in candidate.rooms)
    layout_key = sorted((str(r.slot_id), tuple(float(v) for v in r.bounds)) for r in candidate.rooms)
    warnings = [
        _diagnostic("SYNTHETIC_REGULATORY_EVIDENCE", "WARNING").model_dump(mode="json"),
        _diagnostic("OPENING_DIMENSIONS_DESIGN_PREFERENCE", "WARNING").model_dump(mode="json"),
        *[_diagnostic(code, "WARNING").model_dump(mode="json") for code in dict.fromkeys(c for c, s in access if s == "WARNING")],
    ]
    return {
        "projectId": str(request.projectId),
        "landRevisionId": str(request.landRevisionId),
        "requirementsRevisionId": str(request.requirementsRevisionId),
        "alternativeId": str(_id(plan_id, "alternative")),
        "geometry": geometry,
        "metrics": {"floorAreaM2": float(area), "footprintAreaM2": float(area), "roomCount": len(candidate.rooms)},
        "validation": {
            "projectId": str(request.projectId),
            "landRevisionId": str(request.landRevisionId),
            "requirementsRevisionId": str(request.requirementsRevisionId),
            "validationId": str(_id(plan_id, "validation", geometry_hash)),
            "candidateId": str(plan_id),
            "geometryHash": geometry_hash,
            "geometryValid": True,
            "requirementsMatch": True,
            "regulatoryEvidenceStatus": "REVIEWED",
            "hasBlockingIssues": False,
            "validatorVersion": VALIDATOR_VERSION,
            "ruleSetVersion": request.regulatoryEvidence.ruleSetVersion,
            "validatedAtUtc": validated_at,
            "diagnostics": warnings,
        },
        "diversitySignature": {"version": DIVERSITY_VERSION, "value": _canonical_hash(layout_key)[:40]},
        "algorithmVersion": ALGORITHM_VERSION,
    }


def _vastu_score(rooms, bounds: tuple[float, float, float, float]) -> float:
    """Internal non-blocking compass preference heuristic for ordering distinct candidates. Never blocks or is shown."""
    min_x, min_y, max_x, max_y = bounds
    span_x = max(max_x - min_x, 1e-6)
    span_y = max(max_y - min_y, 1e-6)
    score = 0.0
    for room in rooms:
        x0, y0, x1, y1 = room.bounds
        nx = ((x0 + x1) * 0.5 - min_x) / span_x
        ny = ((y0 + y1) * 0.5 - min_y) / span_y
        if room.room_type == "kitchen":
            score += nx + (1.0 - ny)
        elif room.room_type == "livingRoom":
            score += nx + ny
        elif room.room_type == "bedroom":
            score += (1.0 - nx) + (1.0 - ny)
        elif room.room_type == "bathroom":
            score += (1.0 - nx) * 0.5 + (1.0 - ny) * 0.5
    return score


def run_generation(run: GenerationRunRequest) -> dict:
    request = run.input
    evidence = request.regulatoryEvidence
    if evidence.status != "REVIEWED":
        return _failure(request, "NEEDS_REVIEW", "REGULATORY_EVIDENCE_" + evidence.status)
    if not run.syntheticReviewedSetbacks:
        return _failure(request, "NEEDS_REVIEW", "REVIEWED_SETBACK_SNAPSHOT_MISSING")

    revision = dict(projectId=request.projectId, landRevisionId=request.landRevisionId,
                    requirementsRevisionId=request.requirementsRevisionId)
    confirmed = ConfirmedMetricParcel(projectId=request.projectId, landRevisionId=request.landRevisionId, land=request.land)
    snapshot = ReviewedSetbackSnapshot(**revision, synthetic=True, regulatoryEvidence=evidence)
    assumptions = AreaAssumptions(**revision, roomProgram=request.roomProgram, additionalSpaceAreasM2=[],
                                  assumptionReference="captain-room-minimum-sum-v1")
    feasibility = assess_feasibility(request, confirmed_parcel=confirmed, reviewed_setbacks=snapshot,
                                     area_assumptions=assumptions)
    status = feasibility.result.status
    if status != "FEASIBLE":
        diagnostics = feasibility.result.diagnostics
        codes = [d.code for d in diagnostics if d.severity == "CRITICAL"]
        if "FOOTPRINT_DEPENDENCY_UNAVAILABLE" in codes:
            return _failure(request, "UNAVAILABLE", "GENERATION_UNAVAILABLE")
        outcome = {"INFEASIBLE": "INFEASIBLE", "UNSUPPORTED": "UNSUPPORTED"}.get(status, "NEEDS_REVIEW")
        return _failure(request, outcome, codes[0] if codes else "GENERATION_FAILED", diagnostics)

    requirements = PlacementRequirements(
        **revision, catalogueVersion="synthetic-fixture-v1", unit="m", level=0,
        roomCounts=dict(Counter(slot.roomType for slot in request.roomProgram.rooms)),
        slots=[SlotPlacementPolicy(slotId=slot.slotId,
                                   requiredAreaM2=slot.dimensions.minWidthM * slot.dimensions.minDepthM,
                                   allowRotation=True)
               for slot in request.roomProgram.rooms],
        optionalPairs=[(p.fromSlotId, p.toSlotId) for p in request.adjacencyPreferences],
    )
    generation = generate_alternatives(
        request, PlacementAssessment(feasibility, evidence), requirements,
        confirmed_parcel=confirmed, reviewed_setbacks=snapshot, mandatory_kinds=("setback",),
        physical_access_required=False,
    )
    if generation.status == Status.DEPENDENCY_UNAVAILABLE:
        return _failure(request, "UNAVAILABLE", "GENERATION_UNAVAILABLE")
    if generation.status != Status.VALID or len(generation.candidates) != 3:
        return _failure(request, _D04_OUTCOME.get(generation.status, "NEEDS_REVIEW"), generation.code)

    validated_at = datetime.now(timezone.utc).isoformat(timespec="microseconds").replace("+00:00", "Z")
    xs = [float(p.x) for p in request.land.boundary.exterior]
    ys = [float(p.y) for p in request.land.boundary.exterior]
    bounds = (min(xs), min(ys), max(xs), max(ys)) if xs and ys else (0.0, 0.0, 1.0, 1.0)
    ranked = sorted(
        enumerate(generation.candidates),
        key=lambda item: (-_vastu_score(item[1].rooms, bounds), item[0]),
    )
    alternatives = [_alternative(request, index, candidate, validated_at)
                    for index, (_, candidate) in enumerate(ranked)]
    if any(alternative is None for alternative in alternatives):
        # Truthful failure: never return a layout whose rooms cannot all be reached.
        return _failure(request, "INFEASIBLE", "PHYSICAL_ACCESS_UNSATISFIABLE")
    warning = _diagnostic("SYNTHETIC_REGULATORY_EVIDENCE", "WARNING")
    return _envelope(request, "SUCCESS", None, [warning], alternatives)

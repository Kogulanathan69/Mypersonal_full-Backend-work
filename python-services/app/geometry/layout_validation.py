"""Internal D04 candidate validation and bounded search over D03 placements."""

import json
from collections import Counter
from dataclasses import dataclass
from enum import StrEnum
from fractions import Fraction
from hashlib import sha256
from itertools import combinations, product
from math import isfinite
from typing import TYPE_CHECKING

from pydantic import ValidationError

from app.geometry.footprint.contracts import FeasibilityResult
from app.geometry.footprint.feasibility import POLICY_VERSION, ReviewedSetbackSnapshot
from app.geometry.footprint.parcel import (
    MAX_COORDINATE_M, MAX_EXTERIOR_EDGES, MAX_HOLES, MAX_TOTAL_VERTICES, MIN_CLEARANCE_M,
    ConfirmedMetricParcel, ParcelError,
    ValidatedParcel, validate_confirmed_parcel,
)
from app.geometry.footprint.setbacks import DISTANCE_MARGIN_M, FootprintError, build_footprint
from app.geometry.layout_diversity import Diversity, canonical_rooms, compare_layouts
from app.geometry.placement.contracts import PlacementAssessment, PlacementRequirements, SearchLimits
from app.geometry.placement.engine import (
    CONTACT_TOLERANCE_M, MAX_ROOMS, PlacedRoom, Revision, SharedWall, place_rooms,
)
from app.plans.generation.contracts import GenerationInput
from app.validation.boundary import parse_contract_json

if TYPE_CHECKING:
    from shapely.geometry import Polygon

MAX_VARIANTS = 96


class Status(StrEnum):
    VALID = "VALID"
    INVALID_GEOMETRY = "INVALID_GEOMETRY"
    REQUIREMENTS_VIOLATION = "REQUIREMENTS_VIOLATION"
    REGULATORY_NON_COMPLIANT = "REGULATORY_NON_COMPLIANT"
    UNSUPPORTED = "UNSUPPORTED"
    INCONCLUSIVE = "INCONCLUSIVE"
    DEPENDENCY_UNAVAILABLE = "DEPENDENCY_UNAVAILABLE"


@dataclass(frozen=True)
class Validation:
    status: Status
    code: str
    revision: Revision | None = None
    evidence_json: str | None = None
    walls: tuple[SharedWall, ...] = ()
    candidate_id: str | None = None


@dataclass(frozen=True)
class Candidate:
    rooms: tuple[PlacedRoom, ...]
    validation: Validation


@dataclass(frozen=True)
class Generation:
    status: Status
    code: str
    candidates: tuple[Candidate, ...] = ()
    diversity: tuple[Diversity, ...] = ()
    checked: int = 0
    valid_count: int = 0


class Rejected(Exception):
    def __init__(self, status: Status, code: str):
        self.status = status
        self.code = code


@dataclass(frozen=True)
class Context:
    request: GenerationInput
    requirements: PlacementRequirements
    parcel: ValidatedParcel
    footprint: "Polygon"
    distances: tuple[float, ...]
    revision: Revision
    evidence_json: str
    limits: SearchLimits


def _polygon(land, revision):
    snapshot = ConfirmedMetricParcel(
        projectId=revision[0], landRevisionId=revision[1], land=land,
    )
    return validate_confirmed_parcel(snapshot, *revision[:2])


def _pair(first, second):
    return tuple(sorted((first, second)))


def _is_adjacent(length: float, minimum: float) -> bool:
    return length > CONTACT_TOLERANCE_M and length + CONTACT_TOLERANCE_M >= minimum


def _context(request, assessment, requirements, confirmed_parcel, reviewed_setbacks, mandatory_kinds, limits):
    # D04 is internal. Re-parse model instances to check changes to mutable snapshots.
    if not isinstance(request, GenerationInput):
        raise Rejected(Status.INCONCLUSIVE, "INVALID_INPUT")
    try:
        boundary = request.land.boundary
        if (
            len(boundary.exterior) - 1 > MAX_EXTERIOR_EDGES
            or len(boundary.holes) > MAX_HOLES
            or len(boundary.exterior) - 1 + sum(len(ring) - 1 for ring in boundary.holes)
            > MAX_TOTAL_VERTICES
        ):
            raise Rejected(Status.UNSUPPORTED, "PARCEL_COMPLEXITY_UNSUPPORTED")
        if len(request.roomProgram.rooms) > MAX_ROOMS:
            raise Rejected(Status.UNSUPPORTED, "ROOM_LIMIT")
        request = parse_contract_json(GenerationInput, request.model_dump_json(warnings="error"))
        limits = SearchLimits() if limits is None else SearchLimits.model_validate(limits.model_dump())
    except (ValidationError, TypeError, ValueError, AttributeError):
        raise Rejected(Status.INCONCLUSIVE, "INVALID_INPUT") from None
    revision = request.projectId, request.landRevisionId, request.requirementsRevisionId
    if not isinstance(confirmed_parcel, ConfirmedMetricParcel):
        raise Rejected(Status.INCONCLUSIVE, "CONFIRMED_PARCEL_MISSING")
    if (confirmed_parcel.projectId, confirmed_parcel.landRevisionId) != revision[:2] or (
        confirmed_parcel.land != request.land
    ):
        raise Rejected(Status.INCONCLUSIVE, "CONFIRMED_PARCEL_MISMATCH")
    if mandatory_kinds is None:
        raise Rejected(Status.INCONCLUSIVE, "REGULATORY_SCOPE_MISSING")
    if mandatory_kinds != ("setback",):
        raise Rejected(Status.UNSUPPORTED, "REGULATORY_SCOPE_UNSUPPORTED")

    evidence = request.regulatoryEvidence
    if evidence.status != "REVIEWED":
        unknown = any(rule.applicability.status == "UNKNOWN" for rule in evidence.constraints)
        code = "REGULATORY_APPLICABILITY_UNKNOWN" if unknown else "EVIDENCE_" + evidence.status
        raise Rejected(Status.INCONCLUSIVE, code)
    if reviewed_setbacks is None:
        raise Rejected(Status.INCONCLUSIVE, "REVIEWED_SETBACK_SNAPSHOT_MISSING")
    if (
        not isinstance(reviewed_setbacks, ReviewedSetbackSnapshot)
        or reviewed_setbacks.synthetic is not True
        or reviewed_setbacks.revision_key() != revision
        or reviewed_setbacks.regulatoryEvidence != evidence
    ):
        raise Rejected(Status.INCONCLUSIVE, "REVIEWED_SETBACK_SNAPSHOT_MISMATCH")
    if not isinstance(assessment, PlacementAssessment):
        raise Rejected(Status.INCONCLUSIVE, "FOOTPRINT_ASSESSMENT_MISSING")
    result = FeasibilityResult.model_validate(assessment.assessment.result.model_dump())
    if result.revision_key() != revision or not assessment.matches_evidence(evidence):
        raise Rejected(Status.INCONCLUSIVE, "FOOTPRINT_ASSESSMENT_STALE")
    if result.feasibilityPolicyVersion != POLICY_VERSION:
        raise Rejected(Status.UNSUPPORTED, "FOOTPRINT_POLICY_UNSUPPORTED")
    codes = {diagnostic.code for diagnostic in result.diagnostics}
    if "FOOTPRINT_DEPENDENCY_UNAVAILABLE" in codes:
        raise Rejected(Status.DEPENDENCY_UNAVAILABLE, "GEOMETRY_DEPENDENCY_UNAVAILABLE")
    if "POLYGON_INVALID" in codes:
        raise Rejected(Status.INVALID_GEOMETRY, "POLYGON_INVALID")
    if result.status == "UNSUPPORTED":
        raise Rejected(Status.UNSUPPORTED, "FOOTPRINT_UNSUPPORTED")

    applied = tuple(sorted(
        (rule for rule in evidence.constraints if rule.applicability.status == "APPLICABLE"),
        key=lambda rule: (rule.parcelEdgeIndex, rule.ruleId),
    ))
    distances = {}
    for rule in applied:
        edge = rule.parcelEdgeIndex
        if edge in distances and distances[edge] != rule.distanceM:
            raise Rejected(Status.INCONCLUSIVE, "EVIDENCE_CONFLICTING")
        distances[edge] = rule.distanceM
    if set(distances) != set(range(len(request.land.boundary.exterior) - 1)):
        raise Rejected(Status.INCONCLUSIVE, "SETBACK_EDGE_COVERAGE_MISSING")
    if result.status != "FEASIBLE":
        raise Rejected(Status.INCONCLUSIVE, "FEASIBLE_FOOTPRINT_REQUIRED")
    if (
        result.regulatoryEvidenceStatus != "REVIEWED"
        or result.ruleSetVersion != evidence.ruleSetVersion
        or assessment.assessment.appliedConstraints != applied
    ):
        raise Rejected(Status.INCONCLUSIVE, "APPLIED_EVIDENCE_MISMATCH")

    if requirements is None:
        raise Rejected(Status.INCONCLUSIVE, "ROOM_POLICY_MISSING")
    requirements = PlacementRequirements.model_validate(requirements.model_dump())
    if requirements.revision_key() != revision:
        raise Rejected(Status.INCONCLUSIVE, "ROOM_POLICY_REVISION_MISMATCH")
    slots = request.roomProgram.rooms
    policies = {policy.slotId for policy in requirements.slots}
    if (
        Counter(slot.roomType for slot in slots) != requirements.roomCounts
        or policies != {slot.slotId for slot in slots}
        or len(policies) != len(requirements.slots)
    ):
        raise Rejected(Status.REQUIREMENTS_VIOLATION, "ROOM_POLICY_MISMATCH")
    declared = {_pair(p.fromSlotId, p.toSlotId) for p in request.adjacencyPreferences}
    optional = {_pair(*pair) for pair in requirements.optionalPairs}
    if optional - declared or len(optional) != len(requirements.optionalPairs):
        raise Rejected(Status.REQUIREMENTS_VIOLATION, "RELATIONSHIP_POLICY_MISMATCH")

    parcel = validate_confirmed_parcel(confirmed_parcel, *revision[:2])
    land = result.buildableFootprint
    if land is None or land.coordinateSystem != request.land.coordinateSystem:
        raise Rejected(Status.INVALID_GEOMETRY, "FOOTPRINT_FRAME_INVALID")
    footprint = _polygon(land, revision).polygon
    ordered_distances = tuple(distances[index] for index in range(len(parcel.edges)))
    rebuilt = build_footprint(parcel, ordered_distances).candidate
    if not parcel.polygon.covers(footprint) or not rebuilt.equals(footprint):
        raise Rejected(Status.INVALID_GEOMETRY, "CONSERVATIVE_FOOTPRINT_MISMATCH")
    return Context(
        request, requirements, parcel, footprint, ordered_distances, revision,
        evidence.model_dump_json(), limits,
    )


def _validate(context, rooms, revision, physical_access_required):
    from shapely.geometry import box

    if revision != context.revision:
        raise Rejected(Status.INCONCLUSIVE, "CANDIDATE_REVISION_MISMATCH")
    if not isinstance(rooms, tuple) or len(rooms) > MAX_ROOMS:
        raise Rejected(Status.UNSUPPORTED, "CANDIDATE_REPRESENTATION_UNSUPPORTED")
    slots = {slot.slotId: slot for slot in context.request.roomProgram.rooms}
    policies = {policy.slotId: policy for policy in context.requirements.slots}
    if (
        len(rooms) != len(slots)
        or any(not isinstance(room, PlacedRoom) for room in rooms)
        or {room.slot_id for room in rooms} != slots.keys()
    ):
        raise Rejected(Status.REQUIREMENTS_VIOLATION, "CANDIDATE_ROOM_IDENTITIES_INVALID")
    shapes = {}
    for room in sorted(rooms, key=lambda item: item.slot_id):
        if (
            not isinstance(room.bounds, tuple) or len(room.bounds) != 4
            or any(type(x) not in (float, int) or abs(x) > MAX_COORDINATE_M or not isfinite(x)
                   for x in room.bounds)
        ):
            raise Rejected(Status.INVALID_GEOMETRY, "ROOM_COORDINATES_INVALID")
        left, bottom, right, top = room.bounds
        width, depth = Fraction(right) - Fraction(left), Fraction(top) - Fraction(bottom)
        if width <= 0 or depth <= 0:
            raise Rejected(Status.INVALID_GEOMETRY, "ROOM_GEOMETRY_INVALID")
        if min(width, depth) < MIN_CLEARANCE_M:
            raise Rejected(Status.UNSUPPORTED, "ROOM_PRECISION_UNSUPPORTED")
        shape = box(left, bottom, right, top)
        if not shape.is_valid or shape.area <= 0 or not context.parcel.polygon.covers(shape):
            raise Rejected(Status.INVALID_GEOMETRY, "ROOM_OUTSIDE_PARCEL_OR_HOLE")
        slot, policy = slots[room.slot_id], policies[room.slot_id]
        if room.room_type != slot.roomType:
            raise Rejected(Status.REQUIREMENTS_VIOLATION, "CANDIDATE_ROOM_TYPE_INVALID")
        if type(room.rotated) is not bool or (room.rotated and not policy.allowRotation):
            raise Rejected(Status.REQUIREMENTS_VIOLATION, "ROTATION_POLICY_VIOLATION")
        if room.rotated:
            width, depth = depth, width
        size = slot.dimensions
        if (
            width < size.minWidthM or depth < size.minDepthM
            or (size.maxWidthM is not None and width > size.maxWidthM)
            or (size.maxDepthM is not None and depth > size.maxDepthM)
        ):
            raise Rejected(Status.REQUIREMENTS_VIOLATION, "ROOM_DIMENSIONS_VIOLATION")
        if width * depth < policy.requiredAreaM2:
            raise Rejected(Status.REQUIREMENTS_VIOLATION, "ROOM_AREA_VIOLATION")
        shapes[room.slot_id] = shape

    walls = {}
    for pair in combinations(sorted(shapes), 2):
        first, second = (shapes[slot] for slot in pair)
        if not first.relate_pattern(second, "F********"):
            raise Rejected(Status.INVALID_GEOMETRY, "ROOM_INTERIORS_OVERLAP")
        contact = first.boundary.intersection(second.boundary)
        if contact.length > 0:
            left, bottom, right, top = contact.bounds
            walls[pair] = SharedWall(pair, ((left, bottom), (right, top)), contact.length)

    optional = {_pair(*pair) for pair in context.requirements.optionalPairs}
    for rule in sorted(context.request.adjacencyPreferences, key=lambda item: _pair(item.fromSlotId, item.toSlotId)):
        pair = _pair(rule.fromSlotId, rule.toSlotId)
        if pair in optional:
            continue
        length = walls[pair].length_m if pair in walls else 0.0
        if rule.relationship == "adjacent":
            if not _is_adjacent(length, context.limits.minSharedWallM):
                raise Rejected(Status.REQUIREMENTS_VIOLATION, "MANDATORY_ADJACENCY_VIOLATION")
        elif length > CONTACT_TOLERANCE_M:
            raise Rejected(Status.REQUIREMENTS_VIOLATION, "MANDATORY_SEPARATION_VIOLATION")

    if any(
            distance and shape.distance(edge) < distance - DISTANCE_MARGIN_M
            for shape in shapes.values()
            for edge, distance in zip(context.parcel.edges, context.distances)
    ):
        raise Rejected(Status.REGULATORY_NON_COMPLIANT, "VERIFIED_SETBACK_VIOLATION")
    if any(not context.footprint.covers(shape) for shape in shapes.values()):
        raise Rejected(Status.INVALID_GEOMETRY, "ROOM_OUTSIDE_CONSERVATIVE_FOOTPRINT")

    # The existing generation contracts carry adjacency, but no approved
    # physical-access policy or candidate-bound opening evidence.
    if physical_access_required is None:
        raise Rejected(Status.INCONCLUSIVE, "PHYSICAL_ACCESS_SCOPE_UNKNOWN")
    if type(physical_access_required) is not bool:
        raise Rejected(Status.UNSUPPORTED, "PHYSICAL_ACCESS_SCOPE_UNSUPPORTED")
    if physical_access_required:
        raise Rejected(Status.INCONCLUSIVE, "PHYSICAL_ACCESS_EVIDENCE_UNAVAILABLE")

    rooms = canonical_rooms(rooms)
    identity = json.dumps([
        [str(value) for value in revision],
        [[str(room.slot_id), room.room_type, room.bounds] for room in rooms],
    ], separators=(",", ":"), allow_nan=False)
    return Validation(
        Status.VALID, "SUPPORTED_SYNTHETIC_CHECKS_PASSED", revision,
        context.evidence_json, tuple(walls.values()), sha256(identity.encode()).hexdigest(),
    )


def _checked(operation):
    """Translate internal faults using fixed codes only."""
    try:
        from shapely.errors import ShapelyError
    except ImportError:
        return Status.DEPENDENCY_UNAVAILABLE, "GEOMETRY_DEPENDENCY_UNAVAILABLE"
    try:
        return operation()
    except Rejected as error:
        return error.status, error.code
    except (ParcelError, FootprintError) as error:
        code = str(error)
        if code.endswith("_UNSUPPORTED") or code == "FOOTPRINT_DISCONNECTED":
            return Status.UNSUPPORTED, "GEOMETRY_UNSUPPORTED"
        if code == "POLYGON_INVALID":
            return Status.INVALID_GEOMETRY, "POLYGON_INVALID"
        return Status.INCONCLUSIVE, "GEOMETRY_CHECK_INCONCLUSIVE"
    except ImportError:
        return Status.DEPENDENCY_UNAVAILABLE, "GEOMETRY_DEPENDENCY_UNAVAILABLE"
    except (ShapelyError, ValidationError, ArithmeticError, ValueError, TypeError, AttributeError):
        return Status.INCONCLUSIVE, "VALIDATION_CHECK_INCONCLUSIVE"


def validate_candidate(
    request: GenerationInput,
    assessment: PlacementAssessment,
    requirements: PlacementRequirements | None,
    rooms: tuple[PlacedRoom, ...],
    revision: Revision,
    *,
    confirmed_parcel: ConfirmedMetricParcel,
    reviewed_setbacks: ReviewedSetbackSnapshot | None,
    mandatory_kinds: tuple[str, ...] | None,
    physical_access_required: bool | None = None,
    limits: SearchLimits | None = None,
) -> Validation:
    """Validate the synthetic scope. False explicitly excludes physical access."""
    prepared = _checked(lambda: _context(
        request, assessment, requirements, confirmed_parcel, reviewed_setbacks, mandatory_kinds, limits,
    ))
    if isinstance(prepared, tuple):
        return Validation(*prepared)
    result = _checked(lambda: _validate(prepared, rooms, revision, physical_access_required))
    if isinstance(result, tuple):
        return Validation(*result, prepared.revision, prepared.evidence_json)
    return result


def _variants(rooms, bounds):
    # At most 4 base/reflected layouts, 16 translations and 15 moves per room.
    yield rooms
    left, bottom, right, top = bounds
    for reflect_x, reflect_y in ((True, False), (False, True), (True, True)):
        yield tuple(
            PlacedRoom(room.slot_id, room.room_type, (
                left + (right - x1) if reflect_x else x0,
                bottom + (top - y1) if reflect_y else y0,
                left + (right - x0) if reflect_x else x1,
                bottom + (top - y0) if reflect_y else y1,
            ), room.rotated)
            for room in rooms for x0, y0, x1, y1 in (room.bounds,)
        )

    def shifts(low, high, start, end):
        return (0.0, low - start, (high - end + low - start) / 2, high - end)

    def shifted(room, dx, dy):
        x0, y0, x1, y1 = room.bounds
        return PlacedRoom(room.slot_id, room.room_type, (x0 + dx, y0 + dy, x1 + dx, y1 + dy), room.rotated)

    for dx, dy in product(
        shifts(left, right, min(r.bounds[0] for r in rooms), max(r.bounds[2] for r in rooms)),
        shifts(bottom, top, min(r.bounds[1] for r in rooms), max(r.bounds[3] for r in rooms)),
    ):
        yield tuple(shifted(room, dx, dy) for room in rooms)
    # Vary one slot at a time in round-robin order, using its own available extents.
    for ix, iy in product(range(4), repeat=2):
        if ix == iy == 0:
            continue
        for index, room in enumerate(rooms):
            x0, y0, x1, y1 = room.bounds
            moved = shifted(room, shifts(left, right, x0, x1)[ix], shifts(bottom, top, y0, y1)[iy])
            yield rooms[:index] + (moved,) + rooms[index + 1:]


def _search(context, assessment, variant_limit, physical_access_required):
    seed = place_rooms(context.request, assessment, context.requirements, limits=context.limits)
    if seed.outcome != "SUCCESS":
        if any(d.code == "PLACEMENT_DEPENDENCY_UNAVAILABLE" for d in seed.diagnostics):
            return Generation(Status.DEPENDENCY_UNAVAILABLE, "GEOMETRY_DEPENDENCY_UNAVAILABLE")
        return Generation(Status.INCONCLUSIVE, "D03_PLACEMENT_NOT_FOUND")
    seed_validation = _validate(context, seed.rooms, seed.revision, physical_access_required)
    seed_rooms = canonical_rooms(seed.rooms)

    nodes = []
    scores = {}
    seen = set()
    code = "INSUFFICIENT_DIVERSITY"
    for rooms in _variants(seed_rooms, context.footprint.bounds):
        if rooms in seen:
            continue
        if len(seen) == variant_limit:
            code = "VARIANT_LIMIT_REACHED"
            break
        seen.add(rooms)
        try:
            validation = (
                seed_validation if rooms == seed_rooms
                else _validate(context, rooms, context.revision, physical_access_required)
            )
        except Rejected as error:
            if error.status in (Status.INVALID_GEOMETRY, Status.REQUIREMENTS_VIOLATION,
                                Status.REGULATORY_NON_COMPLIANT, Status.UNSUPPORTED):
                continue
            raise
        candidate = Candidate(canonical_rooms(rooms), validation)
        topology = frozenset(wall.slots for wall in validation.walls
                             if _is_adjacent(wall.length_m, context.limits.minSharedWallM))
        index = len(nodes)
        neighbors = []
        for prior, (other, other_topology) in enumerate(nodes):
            metric = compare_layouts(
                candidate.rooms, other.rooms, topology, other_topology, context.footprint.bounds,
            )
            if metric.distinct:
                scores[prior, index] = metric
                neighbors.append(prior)
        for first, second in combinations(neighbors, 2):
            if (first, second) in scores:
                return Generation(
                    Status.VALID, "THREE_DISTINCT_CANDIDATES",
                    (nodes[first][0], nodes[second][0], candidate),
                    (scores[first, second], scores[first, index], scores[second, index]),
                    len(seen), index + 1,
                )
        nodes.append((candidate, topology))
    return Generation(Status.INCONCLUSIVE, code, checked=len(seen), valid_count=len(nodes))


def generate_alternatives(
    request: GenerationInput,
    assessment: PlacementAssessment,
    requirements: PlacementRequirements | None,
    *,
    confirmed_parcel: ConfirmedMetricParcel,
    reviewed_setbacks: ReviewedSetbackSnapshot | None,
    mandatory_kinds: tuple[str, ...] | None,
    physical_access_required: bool | None = None,
    limits: SearchLimits | None = None,
    variant_limit: int = MAX_VARIANTS,
) -> Generation:
    """Return the first pairwise-distinct triple in deterministic enumeration order."""
    if type(variant_limit) is not int or not 1 <= variant_limit <= MAX_VARIANTS:
        return Generation(Status.UNSUPPORTED, "VARIANT_LIMIT_INVALID")

    def run():
        context = _context(request, assessment, requirements, confirmed_parcel, reviewed_setbacks, mandatory_kinds, limits)
        return _search(context, assessment, variant_limit, physical_access_required)

    result = _checked(run)
    return Generation(*result) if isinstance(result, tuple) else result

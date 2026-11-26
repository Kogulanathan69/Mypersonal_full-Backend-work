"""Bounded rectangular room placement over a D02 conservative footprint."""

from collections import Counter, defaultdict
from dataclasses import dataclass
from fractions import Fraction
from itertools import combinations, product
from math import nextafter, sqrt
from typing import TYPE_CHECKING, Literal
from uuid import UUID

from pydantic import ValidationError

from app.geometry.footprint.contracts import CoordinatePoint, Diagnostic, FeasibilityResult, MetricLand
from app.geometry.footprint.feasibility import POLICY_VERSION, FeasibilityAssessment
from app.geometry.footprint.parcel import (
    MAX_COORDINATE_M,
    ConfirmedMetricParcel,
    ParcelError,
    validate_confirmed_parcel,
)
from app.geometry.placement.contracts import PlacementAssessment, PlacementRequirements, SearchLimits
from app.plans.generation.contracts import GenerationInput, RoomSlot

if TYPE_CHECKING:
    from shapely.geometry import Polygon

MAX_ROOMS = 16
CONTACT_TOLERANCE_M = 1e-9
Pair = tuple[UUID, UUID]
Bounds = tuple[float, float, float, float]
Point = tuple[float, float]
Segment = tuple[Point, Point]
Outcome = Literal["SUCCESS", "FAILED", "NEEDS_REVIEW", "UNSUPPORTED", "INFEASIBLE"]
Revision = tuple[UUID, UUID, UUID]

_MESSAGES = {
    "PLACEMENT_INPUT_INVALID": "The placement inputs violate the supported contracts.",
    "INPUT_REVISION_MISMATCH": "The placement snapshots do not bind to the exact input revisions.",
    "PLACEMENT_REQUIREMENTS_MISSING": "An explicit placement policy is required for every room slot.",
    "PLACEMENT_COMPLEXITY_UNSUPPORTED": "The room program exceeds the placement complexity limit.",
    "PLACEMENT_FOOTPRINT_UNSUPPORTED": "The footprint exceeds the supported geometry or precision limits.",
    "PLACEMENT_FOOTPRINT_INVALID": "The supplied footprint failed conservative geometry validation.",
    "PLACEMENT_DEPENDENCY_UNAVAILABLE": "The geometry dependency is unavailable.",
    "PLACEMENT_CALCULATION_FAILED": "Geometry calculation or final validation failed.",
    "PLACEMENT_AREA_INSUFFICIENT": "The conservative footprint has less area than the room minima require.",
    "PLACEMENT_SEARCH_EXHAUSTED": "The bounded placement search found no complete candidate.",
    "MANDATORY_ADJACENCY_UNSATISFIED": "The search found room geometry but no candidate satisfying all mandatory relationships.",
}


@dataclass(frozen=True)
class PlacedRoom:
    slot_id: UUID
    room_type: str
    bounds: Bounds
    rotated: bool = False

    @property
    def ring(self) -> tuple[Point, ...]:
        x0, y0, x1, y1 = self.bounds
        return ((x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0))

    @property
    def walls(self) -> tuple[Segment, ...]:
        ring = self.ring
        return tuple(zip(ring, ring[1:]))


@dataclass(frozen=True)
class SharedWall:
    slots: Pair
    segment: Segment
    length_m: float


@dataclass(frozen=True)
class RoomPlacementResult:
    outcome: Outcome
    revision: Revision | None = None
    rooms: tuple[PlacedRoom, ...] = ()
    diagnostics: tuple[Diagnostic, ...] = ()
    candidate_checks: int = 0
    optional_matches: int = 0
    shared_walls: tuple[SharedWall, ...] = ()


@dataclass(frozen=True)
class Room:
    slot: RoomSlot
    area: float
    rotate: bool


@dataclass(frozen=True)
class Rule:
    pair: Pair
    adjacent: bool
    mandatory: bool


def _pair(a: UUID, b: UUID) -> Pair:
    return (a, b) if a < b else (b, a)


def _shared_wall(a: Bounds, b: Bounds) -> float:
    """Exact collinearity. A positive gap is never a shared boundary."""
    ax, ay, ar, at = a
    bx, by, br, bt = b
    if ar == bx or br == ax:
        return max(0.0, min(at, bt) - max(ay, by))
    if at == by or bt == ay:
        return max(0.0, min(ar, br) - max(ax, bx))
    return 0.0


def _matches(rule: Rule, shared: float, minimum: float) -> bool:
    if rule.adjacent:
        return shared > CONTACT_TOLERANCE_M and shared + CONTACT_TOLERANCE_M >= minimum
    return shared <= CONTACT_TOLERANCE_M


def _dimensions_match(room: Room, bounds: Bounds, rotated: bool) -> bool:
    x0, y0, x1, y1 = bounds
    # Validate the actual emitted coordinates without rounding away undersizing.
    width = Fraction(x1) - Fraction(x0)
    depth = Fraction(y1) - Fraction(y0)
    if rotated:
        if not room.rotate:
            return False
        width, depth = depth, width
    d = room.slot.dimensions
    return (
        width >= d.minWidthM and depth >= d.minDepthM
        and (d.maxWidthM is None or width <= d.maxWidthM)
        and (d.maxDepthM is None or depth <= d.maxDepthM)
        and width * depth >= room.area
    )


def _sizes(room: Room) -> list[tuple[float, float, bool]]:
    d = room.slot.dimensions
    w, h = d.minWidthM, d.minDepthM
    square = sqrt(room.area)
    proportional_width = max(w, square * sqrt(w) / sqrt(h))
    widths = {w, max(w, room.area / h), max(w, square), proportional_width}
    if d.maxWidthM is not None:
        widths.add(d.maxWidthM)
    if d.maxDepthM is not None:
        widths.add(max(w, room.area / d.maxDepthM))
    sizes = set()
    for width in widths:
        depth = max(h, room.area / width)
        if not (0 < width <= 2 * MAX_COORDINATE_M and 0 < depth <= 2 * MAX_COORDINATE_M):
            continue
        if Fraction(width) * Fraction(depth) < room.area:
            depth = nextafter(depth, float("inf"))
        if _dimensions_match(room, (0.0, 0.0, width, depth), False):
            sizes.add((width, depth, False))
            if room.rotate and width != depth:
                sizes.add((depth, width, True))
    return sorted(sizes, key=lambda size: (size[0] * size[1], size))


def _exact_area(land: MetricLand) -> Fraction:
    def ring_area(ring: list[CoordinatePoint]) -> Fraction:
        points = [(Fraction(p.x), Fraction(p.y)) for p in ring]
        return abs(sum(x * yy - xx * y for (x, y), (xx, yy) in zip(points, points[1:]))) / 2

    return ring_area(land.boundary.exterior) - sum(ring_area(ring) for ring in land.boundary.holes)


def _validate_candidate(
    footprint: "Polygon", specs: dict[UUID, Room], rooms: tuple[PlacedRoom, ...],
    rules: tuple[Rule, ...], minimum: float,
) -> tuple[bool, bool, int, tuple[SharedWall, ...]]:
    """Rebuild polygon geometry and measure shared boundaries independently of search."""
    from shapely.geometry import Polygon

    if len(rooms) != len(specs) or {room.slot_id for room in rooms} != specs.keys():
        return False, False, 0, ()
    polygons = {}
    for room in rooms:
        spec = specs[room.slot_id]
        if room.room_type != spec.slot.roomType or not _dimensions_match(spec, room.bounds, room.rotated):
            return False, False, 0, ()
        shape = Polygon(room.ring)
        if not shape.is_valid or shape.area <= 0 or not footprint.covers(shape):
            return False, False, 0, ()
        polygons[room.slot_id] = shape
    shared: dict[Pair, SharedWall] = {}
    for a, b in combinations(sorted(polygons), 2):
        first, second = polygons[a], polygons[b]
        if not first.relate_pattern(second, "F********"):
            return False, False, 0, ()
        contact = first.boundary.intersection(second.boundary)
        length = contact.length
        if length > 0:
            x0, y0, x1, y1 = contact.bounds
            shared[a, b] = SharedWall((a, b), ((x0, y0), (x1, y1)), length)
    mandatory_ok = True
    score = 0
    for rule in rules:
        wall = shared.get(rule.pair)
        fits = _matches(rule, wall.length_m if wall is not None else 0.0, minimum)
        if rule.mandatory:
            mandatory_ok &= fits
        else:
            score += fits
    return True, mandatory_ok, score, tuple(shared.values())


@dataclass
class Search:
    footprint: "Polygon"
    specs: dict[UUID, Room]
    rules: tuple[Rule, ...]
    limits: SearchLimits
    checks: int = 0
    adjacency_rejected: bool = False

    def run(self) -> tuple[PlacedRoom, ...]:
        from shapely.geometry import box
        from shapely.prepared import prep

        footprint = prep(self.footprint)
        x_min, y_min, x_max, y_max = self.footprint.bounds
        points = [
            point
            for ring in (self.footprint.exterior, *self.footprint.interiors)
            for point in ring.coords
        ]
        axes = [set(values) for values in zip(*points)]
        sizes = {slot: _sizes(room) for slot, room in self.specs.items()}
        related = defaultdict(list)
        for rule in self.rules:
            for slot, other in (rule.pair, rule.pair[::-1]):
                related[slot].append((other, rule))
        order = sorted(self.specs, key=lambda slot: (
            -sum(rule.mandatory for _, rule in related[slot]),
            -self.specs[slot].area, slot,
        ))
        placed: dict[UUID, PlacedRoom] = {}

        def candidates(slot: UUID) -> list[tuple[int, int, Bounds, bool]]:
            x_anchors, y_anchors = (axis.copy() for axis in axes)
            for room in placed.values():
                left, bottom, right, top = room.bounds
                x_anchors.update((left, right))
                y_anchors.update((bottom, top))
            stop = min(self.checks + 1024, self.limits.candidateChecks)
            ranked = []
            seen = set()
            for width, depth, rotated in sizes[slot]:
                xs = sorted({(x, x + width) for x in x_anchors} | {(x - width, x) for x in x_anchors})
                ys = sorted({(y, y + depth) for y in y_anchors} | {(y - depth, y) for y in y_anchors})
                for (left, right), (bottom, top) in product(xs, ys):
                    if self.checks >= stop:
                        return sorted(ranked)[:32]
                    self.checks += 1
                    bounds = left, bottom, right, top
                    if left < x_min or bottom < y_min or right > x_max or top > y_max:
                        continue
                    key = bounds, rotated
                    if key in seen or not _dimensions_match(self.specs[slot], bounds, rotated):
                        continue
                    seen.add(key)
                    if any(
                        max(left, x) < min(right, r) and max(bottom, y) < min(top, t)
                        for x, y, r, t in (room.bounds for room in placed.values())
                    ) or not footprint.covers(box(*bounds)):
                        continue
                    mandatory = optional = 0
                    for other, rule in related[slot]:
                        if other in placed and _matches(
                            rule, _shared_wall(bounds, placed[other].bounds), self.limits.minSharedWallM
                        ):
                            mandatory += rule.mandatory
                            optional += not rule.mandatory
                    ranked.append((-mandatory, -optional, bounds, rotated))
            return sorted(ranked)[:32]

        # Spend at most 1024 probes per depth visit, leaving budget for deeper rooms.
        # Candidate ranking is local and the branch cap is deliberately non-exhaustive.
        def visit(index: int) -> tuple[PlacedRoom, ...]:
            if index == len(order):
                result = tuple(placed[slot] for slot in sorted(placed))
                if all(
                    _matches(rule, _shared_wall(*(placed[slot].bounds for slot in rule.pair)),
                             self.limits.minSharedWallM)
                    for rule in self.rules if rule.mandatory
                ):
                    return result
                self.adjacency_rejected = True
                return ()
            slot = order[index]
            for _, _, bounds, rotated in candidates(slot):
                placed[slot] = PlacedRoom(slot, self.specs[slot].slot.roomType, bounds, rotated)
                result = visit(index + 1)
                if result:
                    return result
                del placed[slot]
            return ()

        return visit(0)


def place_rooms(
    request: GenerationInput,
    footprint: PlacementAssessment,
    requirements: PlacementRequirements | None,
    *,
    limits: SearchLimits | None = None,
) -> RoomPlacementResult:
    """Local-only entry point for a trusted D02 assessment and its original evidence."""
    revision = None
    search = None

    def failure(outcome: Outcome, code: str) -> RoomPlacementResult:
        return RoomPlacementResult(
            outcome, revision,
            diagnostics=(Diagnostic(code=code, severity="CRITICAL", message=_MESSAGES[code]),),
            candidate_checks=search.checks if search else 0,
        )

    try:
        request = GenerationInput.model_validate(request)
        revision = request.projectId, request.landRevisionId, request.requirementsRevisionId
        limits = SearchLimits.model_validate(limits if limits is not None else SearchLimits())
        if not isinstance(footprint, PlacementAssessment) or not isinstance(
            footprint.assessment, FeasibilityAssessment
        ):
            return failure("FAILED", "PLACEMENT_INPUT_INVALID")
        assessment = FeasibilityResult.model_validate(footprint.assessment.result)
        if assessment.revision_key() != revision:
            return failure("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
        if assessment.feasibilityPolicyVersion != POLICY_VERSION:
            return failure("UNSUPPORTED", "PLACEMENT_FOOTPRINT_UNSUPPORTED")
        evidence = request.regulatoryEvidence
        if not footprint.matches_evidence(evidence):
            return failure("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
        if assessment.ruleSetVersion is not None and (
            evidence.status != "REVIEWED" or evidence.ruleSetVersion != assessment.ruleSetVersion
        ):
            return failure("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
        applied = footprint.assessment.appliedConstraints
        if applied or assessment.status in ("FEASIBLE", "INFEASIBLE"):
            applicable = tuple(sorted(
                (item for item in evidence.constraints if item.applicability.status == "APPLICABLE"),
                key=lambda item: (item.parcelEdgeIndex, item.ruleId),
            ))
            if not applied or applied != applicable or assessment.regulatoryEvidenceStatus != "REVIEWED":
                return failure("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
        if assessment.status != "FEASIBLE":
            return RoomPlacementResult(
                assessment.status, revision, diagnostics=tuple(assessment.diagnostics),
            )
        if requirements is None:
            return failure("NEEDS_REVIEW", "PLACEMENT_REQUIREMENTS_MISSING")
        requirements = PlacementRequirements.model_validate(requirements)
        if requirements.revision_key() != revision:
            return failure("NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
        slots = {slot.slotId: slot for slot in request.roomProgram.rooms}
        if requirements.roomCounts != Counter(slot.roomType for slot in slots.values()):
            return failure("FAILED", "PLACEMENT_INPUT_INVALID")
        policies = {policy.slotId: policy for policy in requirements.slots}
        if len(policies) != len(requirements.slots) or policies.keys() - slots.keys():
            return failure("FAILED", "PLACEMENT_INPUT_INVALID")
        if slots.keys() - policies.keys():
            return failure("NEEDS_REVIEW", "PLACEMENT_REQUIREMENTS_MISSING")
        if len(slots) > MAX_ROOMS:
            return failure("UNSUPPORTED", "PLACEMENT_COMPLEXITY_UNSUPPORTED")
        optional = {_pair(*pair) for pair in requirements.optionalPairs}
        if len(optional) != len(requirements.optionalPairs):
            return failure("FAILED", "PLACEMENT_INPUT_INVALID")
        relationships = {
            _pair(item.fromSlotId, item.toSlotId): item.relationship
            for item in request.adjacencyPreferences
        }
        if optional - relationships.keys():
            return failure("FAILED", "PLACEMENT_INPUT_INVALID")
        rules = tuple(
            Rule(pair, relationships[pair] == "adjacent", pair not in optional)
            for pair in sorted(relationships)
        )
    except ValidationError:
        return failure("FAILED", "PLACEMENT_INPUT_INVALID")

    specs = {
        slot_id: Room(slot, policies[slot_id].requiredAreaM2, policies[slot_id].allowRotation)
        for slot_id, slot in slots.items()
    }
    land = assessment.buildableFootprint
    if land is None or land.coordinateSystem != request.land.coordinateSystem:
        return failure("FAILED", "PLACEMENT_FOOTPRINT_INVALID")
    try:
        from shapely.errors import ShapelyError
    except ImportError:
        return failure("NEEDS_REVIEW", "PLACEMENT_DEPENDENCY_UNAVAILABLE")
    try:
        # D02 and D03 share the same supported topology/precision validation.
        def polygon(metric_land: MetricLand) -> "Polygon":
            snapshot = ConfirmedMetricParcel(projectId=revision[0], landRevisionId=revision[1], land=metric_land)
            return validate_confirmed_parcel(snapshot, revision[0], revision[1]).polygon

        allowed = polygon(land)
        if not polygon(request.land).covers(allowed):
            return failure("FAILED", "PLACEMENT_FOOTPRINT_INVALID")
        required = sum(max(
            Fraction(room.area),
            Fraction(room.slot.dimensions.minWidthM) * Fraction(room.slot.dimensions.minDepthM),
        ) for room in specs.values())
        if required > _exact_area(land):
            return failure("INFEASIBLE", "PLACEMENT_AREA_INSUFFICIENT")
        search = Search(allowed, specs, rules, limits)
        result = search.run()
        if not result:
            code = ("MANDATORY_ADJACENCY_UNSATISFIED" if search.adjacency_rejected
                    else "PLACEMENT_SEARCH_EXHAUSTED")
            return failure("NEEDS_REVIEW", code)
        valid, adjacent, score, walls = _validate_candidate(allowed, specs, result, rules, limits.minSharedWallM)
        if not valid or not adjacent:
            return failure("FAILED", "PLACEMENT_CALCULATION_FAILED")
        return RoomPlacementResult(
            "SUCCESS", revision, result, candidate_checks=search.checks,
            optional_matches=score, shared_walls=walls,
        )
    except ParcelError as error:
        if str(error).endswith("_UNSUPPORTED"):
            return failure("UNSUPPORTED", "PLACEMENT_FOOTPRINT_UNSUPPORTED")
        return failure("FAILED", "PLACEMENT_FOOTPRINT_INVALID")
    except ImportError:
        return failure("NEEDS_REVIEW", "PLACEMENT_DEPENDENCY_UNAVAILABLE")
    except (ShapelyError, ValidationError, ArithmeticError, ValueError):
        return failure("FAILED", "PLACEMENT_CALCULATION_FAILED")

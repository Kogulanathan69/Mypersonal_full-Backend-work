"""Synthetic D03 acceptance and fault-injection coverage."""

import builtins
from dataclasses import replace
from itertools import combinations
from math import cos, sin, tau
from uuid import UUID

import pytest
from shapely.errors import GEOSException
from shapely.geometry import Polygon, box

from app.geometry.placement import engine
from app.geometry.placement.contracts import (
    PlacementAssessment,
    PlacementRequirements,
    SearchLimits,
    SlotPlacementPolicy,
)
from app.geometry.placement.engine import PlacedRoom, Room, Rule, place_rooms
from app.plans.generation.contracts import AdjacencyPreference, RegulatoryEvidence, RoomSlot
from app.validation.boundary import parse_contract_json
from test_d02_area import request_fixture
from test_d02_feasibility import assess, setback, with_evidence
from test_d02_parcel import metric_land


def rectangle(w, h):
    return [(0, 0), (w, 0), (w, h), (0, h), (0, 0)]


def assess_for_placement(request, *, additional=()):
    return PlacementAssessment(assess(request, additional=additional), request.regulatoryEvidence)


def setup(ext=None, *, holes=(), sizes=((2, 2),), areas=None, rotate=False,
          relationship=None, optional=False, setbacks=None):
    land = metric_land(ext or rectangle(10, 8), holes=holes)
    rooms = [
        RoomSlot.model_validate({
            "slotId": UUID(int=100 + i),
            "roomType": "bedroom",
            "dimensions": {"minWidthM": w, "minDepthM": h},
        })
        for i, (w, h) in enumerate(sizes)
    ]
    request = request_fixture()
    pair = tuple(slot.slotId for slot in rooms[:2])
    preferences = [] if relationship is None else [
        AdjacencyPreference(fromSlotId=pair[0], toSlotId=pair[1], relationship=relationship)
    ]
    request = with_evidence(request.model_copy(update={
        "land": land,
        "roomProgram": request.roomProgram.model_copy(update={"rooms": rooms}),
        "adjacencyPreferences": preferences,
    }), setbacks if setbacks is not None else [0] * (len(land.boundary.exterior) - 1))
    # Round-trip through the D01 ingress boundary before applying D02.
    request = parse_contract_json(type(request), request.model_dump_json())
    footprint = assess_for_placement(request)
    requirements = PlacementRequirements(
        projectId=request.projectId,
        landRevisionId=request.landRevisionId,
        requirementsRevisionId=request.requirementsRevisionId,
        catalogueVersion="synthetic-fixture-v1", unit="m", level=0,
        roomCounts={"bedroom": len(rooms)},
        slots=[
            SlotPlacementPolicy(slotId=room.slotId, requiredAreaM2=area, allowRotation=rotate)
            for room, area in zip(rooms, areas or [w * h for w, h in sizes])
        ],
        optionalPairs=[pair] if optional else [],
    )
    return request, footprint, requirements


def assert_success(args, **kwargs):
    result = place_rooms(*args, **kwargs)
    assert result.outcome == "SUCCESS", result.diagnostics
    request, assessment, requirements = args
    assert result.revision == requirements.revision_key()
    assert {room.slot_id for room in result.rooms} == {slot.slotId for slot in request.roomProgram.rooms}
    boundary = assessment.assessment.result.buildableFootprint.boundary
    footprint = Polygon([(p.x, p.y) for p in boundary.exterior],
                        [[(p.x, p.y) for p in ring] for ring in boundary.holes])
    polygons = [Polygon(room.ring) for room in result.rooms]
    source = {room.slotId: room for room in request.roomProgram.rooms}
    area = {room.slotId: room.requiredAreaM2 for room in requirements.slots}
    for room, polygon in zip(result.rooms, polygons):
        slot = source[room.slot_id]
        assert room.room_type == slot.roomType
        assert polygon.is_valid and polygon.area >= area[room.slot_id]
        assert footprint.covers(polygon)
        x0, y0, x1, y1 = polygon.bounds
        width, depth = (y1-y0, x1-x0) if room.rotated else (x1-x0, y1-y0)
        assert width >= slot.dimensions.minWidthM and depth >= slot.dimensions.minDepthM
    for a, b in combinations(polygons, 2):
        assert a.intersection(b).area == 0
    return result, polygons


def failure(args, kind, code, **kwargs):
    result = place_rooms(*args, **kwargs)
    assert result.outcome == kind
    assert not result.rooms
    assert result.diagnostics[0].code == code
    assert result.diagnostics[0].severity == "CRITICAL"
    return result


def test_single_room_and_rectangular_footprint():
    result, polygons = assert_success(setup())
    assert result.rooms[0].bounds == (0, 0, 2, 2)
    assert polygons[0].area == 4


def test_uses_conservative_setback_boundaries():
    args = setup(setbacks=(1, 2, 1, 2))
    result, polygons = assert_success(args)
    assert box(2, 1, 8, 7).covers(polygons[0])
    assert result.rooms[0].bounds[0] >= 2


def test_multiple_rooms_keep_requested_type_and_quantities():
    result, polygons = assert_success(setup(sizes=((2, 2), (3, 2), (1, 3))))
    assert len({room.slot_id for room in result.rooms}) == 3
    assert {room.room_type for room in result.rooms} == {"bedroom"}
    assert [shape.area for shape in polygons] == [4, 6, 3]


def test_area_above_dimension_product():
    _, polygons = assert_success(setup(sizes=((2, 2),), areas=[6]))
    assert polygons[0].area == 6


def test_grows_width_when_depth_is_constrained():
    _, polygons = assert_success(setup(rectangle(3, 2), sizes=((2, 2),), areas=[6]))
    assert polygons[0].bounds == (0, 0, 3, 2)


def test_backtracks_when_first_room_blocks_the_only_tall_region():
    ext = [(0, 0), (5, 0), (5, 2), (2, 2), (2, 4), (0, 4), (0, 0)]
    result, _ = assert_success(setup(ext, sizes=((3, 2), (2, 3))))
    assert result.rooms[0].bounds == (2, 0, 5, 2)
    assert result.rooms[1].bounds == (0, 0, 2, 3)


@pytest.mark.parametrize("rotate", [True, False])
def test_rotation_permission(rotate):
    args = setup(rectangle(2, 4), sizes=((3, 2),), rotate=rotate)
    if rotate:
        result, polygons = assert_success(args)
        assert result.rooms[0].rotated
        assert polygons[0].bounds == (0, 0, 2, 3)
    else:
        failure(args, "NEEDS_REVIEW", "PLACEMENT_SEARCH_EXHAUSTED")


@pytest.mark.parametrize("ext,holes", [
    ([(0, 0), (7, 0), (5, 6), (1, 5), (0, 0)], ()),
    ([(0, 0), (7, 0), (7, 2), (3, 2), (3, 6), (0, 6), (0, 0)], ()),
    (rectangle(8, 8), ([(1, 1), (5, 1), (5, 5), (1, 5), (1, 1)],)),
])
def test_polygon_topologies(ext, holes):
    assert_success(setup(ext, holes=holes, sizes=((2, 2), (2, 2))))


@pytest.mark.parametrize("relationship,expected", [("adjacent", 2), ("separated", 0)])
def test_mandatory_relationships(relationship, expected):
    result, polygons = assert_success(setup(sizes=((2, 2), (2, 2)), relationship=relationship))
    assert polygons[0].boundary.intersection(polygons[1].boundary).length == expected
    assert len(result.rooms[0].walls) == 4
    if expected:
        wall, = result.shared_walls
        assert wall.slots == tuple(room.slot_id for room in result.rooms)
        assert wall.segment == ((0, 2), (2, 2))
        assert wall.length_m == expected
    else:
        assert result.shared_walls == ()


@pytest.mark.parametrize("relationship,minimum", [("separated", 0.1), ("adjacent", 2.1)])
def test_found_room_geometry_with_unsatisfied_mandatory_rules(relationship, minimum):
    args = setup(rectangle(4, 2), sizes=((2, 2), (2, 2)), relationship=relationship)
    failure(args, "NEEDS_REVIEW", "MANDATORY_ADJACENCY_UNSATISFIED",
            limits=SearchLimits(minSharedWallM=minimum))


@pytest.mark.parametrize("expected,minimum", [(1, 2), (0, 3)])
def test_optional_rule_ranks_candidates_without_blocking(expected, minimum):
    args = setup(sizes=((2, 2), (2, 2)), relationship="adjacent", optional=True)
    result, polygons = assert_success(args, limits=SearchLimits(minSharedWallM=minimum))
    assert result.optional_matches == expected
    if expected:
        assert polygons[0].boundary.intersection(polygons[1].boundary).length == 2


@pytest.mark.parametrize("bounds,valid,adjacent", [
    ((1, 1, 3, 3), False, False),   # overlap
    ((-2, 0, 0, 2), False, False),  # outside
    ((5, 5, 7, 7), False, False),   # hole
    ((2, 2, 4, 4), True, False),    # corner only
    ((2, 0, 4, 2), True, True),     # wall
    ((2 + 2**-32, 0, 4 + 2**-32, 2), True, False),  # tiny positive gap
    ((3, 0, 4, 2), False, False),   # inadequate width
])
def test_independent_final_validation(bounds, valid, adjacent):
    request, _, _ = setup(sizes=((2, 2), (2, 2)))
    first, second = request.roomProgram.rooms
    pair = first.slotId, second.slotId
    specs = {slot.slotId: Room(slot, 4, False) for slot in (first, second)}
    rooms = (PlacedRoom(pair[0], "bedroom", (0, 0, 2, 2)), PlacedRoom(pair[1], "bedroom", bounds))
    hole = [(5, 5), (7, 5), (7, 7), (5, 7), (5, 5)]
    footprint = Polygon(rectangle(10, 8), [hole])
    assert engine._validate_candidate(footprint, specs, rooms, (Rule(pair, True, True),), 0.1)[:3] == (
        valid, adjacent, 0,
    )


@pytest.mark.parametrize("offset,success", [(0, True), (0.5e-9, True), (2e-9, False)])
def test_contact_length_threshold(offset, success):
    args = setup(rectangle(4, 2), sizes=((2, 2), (2, 2)), relationship="adjacent")
    limits = SearchLimits(minSharedWallM=2 + offset)
    if success:
        assert_success(args, limits=limits)
    else:
        failure(args, "NEEDS_REVIEW", "MANDATORY_ADJACENCY_UNSATISFIED", limits=limits)


def test_proven_area_shortfall_uses_required_room_area():
    failure(setup(rectangle(5, 2), sizes=((2, 2), (2, 2)), areas=[6, 5]),
            "INFEASIBLE", "PLACEMENT_AREA_INSUFFICIENT")


def test_sufficient_area_does_not_prove_placement_or_impossibility():
    failure(setup(rectangle(3, 3), sizes=((2, 2), (2, 2))),
            "NEEDS_REVIEW", "PLACEMENT_SEARCH_EXHAUSTED")


def test_search_budget_is_a_hard_cap():
    result = failure(setup(sizes=((2, 2), (3, 2))), "NEEDS_REVIEW",
                     "PLACEMENT_SEARCH_EXHAUSTED", limits=SearchLimits(candidateChecks=1))
    assert result.candidate_checks == 1


def test_repeatability_and_order_independence():
    args = setup(sizes=((2, 2), (3, 2)), relationship="adjacent")
    request, footprint, requirements = args
    shuffled = request.model_copy(update={"roomProgram": request.roomProgram.model_copy(
        update={"rooms": request.roomProgram.rooms[::-1]},
    )})
    assert place_rooms(*args) == place_rooms(shuffled, footprint, requirements)
    assert place_rooms(*args) == place_rooms(*args)


@pytest.mark.parametrize("field", ["projectId", "landRevisionId", "requirementsRevisionId"])
def test_stale_requirement_revisions(field):
    request, footprint, requirements = setup()
    stale = requirements.model_copy(update={field: UUID(int=77)})
    failure((request, footprint, stale), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")


def test_stale_footprint_revision():
    request, footprint, requirements = setup()
    assessment = footprint.assessment
    stale = PlacementAssessment(
        replace(assessment, result=assessment.result.model_copy(update={"landRevisionId": UUID(int=22)})),
        request.regulatoryEvidence,
    )
    failure((request, stale, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")


def test_changed_setback_evidence_requires_fresh_assessment():
    request, footprint, requirements = setup()
    assert_success((request, footprint, requirements))
    changed = with_evidence(request, (1, 1, 1, 1))
    failure((changed, footprint, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    assert_success((changed, assess_for_placement(changed), requirements))


def test_obsolete_empty_buildable_region_requires_fresh_assessment():
    request, footprint, requirements = setup(setbacks=(5, 5, 5, 5))
    failure((request, footprint, requirements), "INFEASIBLE", "EMPTY_BUILDABLE_REGION")
    changed = with_evidence(request, (0, 0, 0, 0))
    assert requirements.revision_key() == (
        changed.projectId, changed.landRevisionId, changed.requirementsRevisionId,
    )
    failure((changed, footprint, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    assert_success((changed, assess_for_placement(changed), requirements))


def test_proven_d02_shortfall_requires_current_evidence():
    request, _, requirements = setup()
    original = assess_for_placement(request, additional=[90])
    assert original.assessment.result.buildableAreaM2 > 0
    failure((request, original, requirements), "INFEASIBLE", "BUILDABLE_AREA_INSUFFICIENT")
    changed = with_evidence(request, (1, 1, 1, 1))
    failure((changed, original, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    fresh = assess_for_placement(changed, additional=[90])
    failure((changed, fresh, requirements), "INFEASIBLE", "BUILDABLE_AREA_INSUFFICIENT")


def test_review_outcome_with_applied_rules_requires_current_evidence():
    request, _, requirements = setup()
    original = assess_for_placement(request, additional=None)
    assert original.assessment.appliedConstraints
    failure((request, original, requirements), "NEEDS_REVIEW", "REQUIRED_AREA_POLICY_UNAVAILABLE")
    changed = with_evidence(request, (1, 1, 1, 1))
    failure((changed, original, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    fresh = assess_for_placement(changed, additional=None)
    failure((changed, fresh, requirements), "NEEDS_REVIEW", "REQUIRED_AREA_POLICY_UNAVAILABLE")


@pytest.mark.parametrize("status,code", [
    ("MISSING", "SETBACK_EVIDENCE_MISSING"),
    ("UNVERIFIED", "SETBACK_EVIDENCE_UNVERIFIED"),
    ("CONFLICTING", "REGULATORY_EVIDENCE_CONFLICT"),
])
def test_review_outcome_without_applied_rules_requires_current_evidence(status, code):
    request, _, requirements = setup()
    incomplete = request.model_copy(update={"regulatoryEvidence": RegulatoryEvidence(
        status=status, ruleSetVersion=None, constraints=[], gaps=[{"code": "synthetic-gap"}],
    )})
    original = assess_for_placement(incomplete)
    assert not original.assessment.appliedConstraints
    failure((incomplete, original, requirements), "NEEDS_REVIEW", code)
    failure((request, original, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    failure((incomplete, assess_for_placement(request), requirements),
            "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")


def test_changed_gap_requires_fresh_review_outcome_even_when_status_is_unchanged():
    request, _, requirements = setup()
    data = request.model_dump()
    data["regulatoryEvidence"] = {
        "status": "MISSING", "ruleSetVersion": None, "constraints": [], "gaps": [{"code": "first-gap"}],
    }
    original = type(request).model_validate(data)
    footprint = assess_for_placement(original)
    data["regulatoryEvidence"]["gaps"][0]["code"] = "replacement-gap"
    changed = type(request).model_validate(data)
    failure((changed, footprint, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    failure((changed, assess_for_placement(changed), requirements),
            "NEEDS_REVIEW", "SETBACK_EVIDENCE_MISSING")


@pytest.mark.parametrize("setbacks", [(0, 0, 0, 0), (5, 5, 5, 5)])
def test_decisive_d02_result_must_agree_with_applied_evidence(setbacks):
    request, footprint, requirements = setup(setbacks=setbacks)
    inconsistent = replace(footprint.assessment, appliedConstraints=())
    pair = PlacementAssessment(inconsistent, request.regulatoryEvidence)
    failure((request, pair, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")


@pytest.mark.parametrize("field,value", [
    ("ruleId", "different-synthetic-rule"),
    ("reviewReference", "different-synthetic-review"),
    ("reviewedBy", UUID(int=77)),
])
def test_evidence_identity_and_review_provenance_require_fresh_assessment(field, value):
    request, footprint, requirements = setup()
    data = request.model_dump()
    data["regulatoryEvidence"]["constraints"][0][field] = value
    changed = type(request).model_validate(data)
    failure((changed, footprint, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")


def test_non_applied_provenance_change_cannot_reuse_same_applied_constraints():
    request, _, requirements = setup()
    rules = [setback(i, 0) for i in range(4)]
    rules.append(setback(0, 1, rule="not-applicable", applicability="NOT_APPLICABLE"))
    request = with_evidence(request, rules=rules)
    original = assess_for_placement(request)
    data = request.model_dump()
    data["regulatoryEvidence"]["constraints"][-1]["source"]["sourceVersion"] = "replacement"
    changed = type(request).model_validate(data)
    fresh = assess_for_placement(changed)
    assert original.assessment.appliedConstraints == fresh.assessment.appliedConstraints
    failure((changed, original, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    assert_success((changed, fresh, requirements))


def test_captured_evidence_does_not_alias_mutated_request():
    request, footprint, requirements = setup()
    request.regulatoryEvidence.ruleSetVersion = "changed-version"
    failure((request, footprint, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")


@pytest.mark.parametrize("evidence", [None, {"status": "REVIEWED"}])
def test_invalid_request_evidence_is_rejected_safely(evidence):
    request, footprint, requirements = setup()
    payload = request.model_dump()
    payload["regulatoryEvidence"] = evidence
    failure((payload, footprint, requirements), "FAILED", "PLACEMENT_INPUT_INVALID")
    with pytest.raises(ValueError, match="^The evaluated regulatory evidence is invalid\\.$"):
        PlacementAssessment(footprint.assessment, evidence)


def test_unbound_d02_result_is_rejected():
    request, footprint, requirements = setup()
    failure((request, footprint.assessment, requirements), "FAILED", "PLACEMENT_INPUT_INVALID")


@pytest.mark.parametrize("change", ["duplicate", "contradiction", "missing_dimension", "nonfinite",
                                    "additional_type", "unrecognized_relationship"])
def test_invalid_generation_input(change):
    request, footprint, requirements = setup(sizes=((2, 2), (2, 2)), relationship="adjacent")
    payload = request.model_dump()
    slots = payload["roomProgram"]["rooms"]
    if change == "duplicate":
        slots[1]["slotId"] = slots[0]["slotId"]
    elif change == "contradiction":
        old = payload["adjacencyPreferences"][0]
        payload["adjacencyPreferences"].append({**old, "relationship": "separated"})
    elif change == "missing_dimension":
        del slots[0]["dimensions"]["minWidthM"]
    elif change == "nonfinite":
        payload["land"]["boundary"]["exterior"][1]["x"] = float("inf")
    elif change == "additional_type":
        slots[0]["roomType"] = "unsupported-room"
    else:
        payload["adjacencyPreferences"][0]["relationship"] = "near"
    failure((payload, footprint, requirements), "FAILED", "PLACEMENT_INPUT_INVALID")


@pytest.mark.parametrize("patch", [
    {"unit": "ft"}, {"level": True}, {"level": 1},
    {"roomCounts": {"bedroom": 2}}, {"roomCounts": {"kitchen": 1}}, {"roomCounts": {"bedroom": True}},
])
def test_rejects_invalid_scope_or_counts(patch):
    request, footprint, requirements = setup()
    failure((request, footprint, requirements.model_copy(update=patch)), "FAILED", "PLACEMENT_INPUT_INVALID")


def test_missing_room_policy():
    request, footprint, requirements = setup(sizes=((2, 2), (2, 2)))
    requirements.slots.pop()
    failure((request, footprint, requirements), "NEEDS_REVIEW", "PLACEMENT_REQUIREMENTS_MISSING")
    failure((request, footprint, None), "NEEDS_REVIEW", "PLACEMENT_REQUIREMENTS_MISSING")


def test_geometry_complexity_cap():
    request, footprint, requirements = setup()
    points = [(3 + 2*cos(i*tau/129), 3 + 2*sin(i*tau/129)) for i in range(129)]
    complex_land = metric_land([*points, points[0]])
    complex_result = footprint.assessment.result.model_copy(update={
        "buildableFootprint": complex_land, "buildableAreaM2": 12.5,
    })
    supplied = PlacementAssessment(replace(footprint.assessment, result=complex_result), request.regulatoryEvidence)
    failure((request, supplied, requirements),
            "UNSUPPORTED", "PLACEMENT_FOOTPRINT_UNSUPPORTED")


def test_d02_multipart_failure_preserved():
    ext = [(0, 0), (4, 0), (4, 2), (8, 2), (8, 0), (12, 0), (12, 5),
           (8, 5), (8, 3), (4, 3), (4, 5), (0, 5), (0, 0)]
    request, assessment, requirements = setup(ext, setbacks=[0.6] * 12)
    assert not assessment.assessment.appliedConstraints
    failure((request, assessment, requirements), "UNSUPPORTED", "FOOTPRINT_DISCONNECTED")
    changed = with_evidence(request, [0] * 12)
    failure((changed, assessment, requirements), "NEEDS_REVIEW", "INPUT_REVISION_MISMATCH")
    assert_success((changed, assess_for_placement(changed), requirements))


def test_room_complexity_cap():
    failure(setup(sizes=((1, 1),)*17), "UNSUPPORTED", "PLACEMENT_COMPLEXITY_UNSUPPORTED")


def test_dependency_unavailable(monkeypatch):
    args = setup()
    original_import = builtins.__import__

    def unavailable(name, *args, **kwargs):
        if name == "shapely.errors":
            raise ImportError("private installation path")
        return original_import(name, *args, **kwargs)

    monkeypatch.setattr(builtins, "__import__", unavailable)
    result = failure(args, "NEEDS_REVIEW", "PLACEMENT_DEPENDENCY_UNAVAILABLE")
    assert "private" not in result.diagnostics[0].message


def test_geometry_failure_is_safe(monkeypatch):
    args = setup()

    def error(*_):
        raise GEOSException("private coordinates")

    monkeypatch.setattr(engine, "_validate_candidate", error)
    result = failure(args, "FAILED", "PLACEMENT_CALCULATION_FAILED")
    assert "private" not in result.diagnostics[0].message

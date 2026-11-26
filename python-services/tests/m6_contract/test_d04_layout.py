"""Synthetic D04 contract, geometry and bounded-search evidence."""

import builtins
from dataclasses import replace
from itertools import permutations
from math import cos, sin, tau
from uuid import UUID

import pytest
from shapely.errors import GEOSException

from app.geometry import layout_validation as d04
from app.geometry.layout_diversity import compare_layouts, movement_threshold
from app.geometry.placement.contracts import PlacementAssessment, SearchLimits
from app.geometry.placement.engine import place_rooms
from app.plans.generation.contracts import RegulatoryEvidence
from test_d02_feasibility import reviewed_snapshot, setback, with_evidence
from test_d02_parcel import confirmed, metric_land
from test_d03_placement import assess_for_placement, rectangle, setup


def options(args, **overrides):
    return {
        "confirmed_parcel": confirmed(args[0].land),
        "physical_access_required": False,
        "reviewed_setbacks": reviewed_snapshot(args[0]), "mandatory_kinds": ("setback",),
        **overrides,
    }


def generate(args, **overrides):
    return d04.generate_alternatives(*args, **options(args, **overrides))


def check(args, rooms, revision=None, **overrides):
    return d04.validate_candidate(
        *args, rooms, args[2].revision_key() if revision is None else revision,
        **options(args, **overrides),
    )


def test_three_alternatives_with_required_adjacency():
    args = setup(sizes=((2, 2), (2, 2)), relationship="adjacent")
    result = generate(args)
    assert result.status == d04.Status.VALID
    assert len(result.candidates) == len(result.diversity) == 3
    assert all(metric.distinct for metric in result.diversity)
    assert len({item.validation.candidate_id for item in result.candidates}) == 3
    assert [
        tuple(room.bounds for room in item.rooms) for item in result.candidates
    ] == [
        ((0, 0, 2, 2), (0, 2, 2, 4)),
        ((8, 0, 10, 2), (8, 2, 10, 4)),
        ((0, 6, 2, 8), (0, 4, 2, 6)),
    ]
    assert [metric.score for metric in result.diversity] == [100, 125, 150]
    for item in result.candidates:
        assert item.validation == check(args, item.rooms)
        evidence = RegulatoryEvidence.model_validate_json(item.validation.evidence_json)
        assert evidence == args[0].regulatoryEvidence
        assert item.validation.walls[0].length_m == 2


def test_adjacency_and_diversity_use_the_same_shared_wall_tolerance(monkeypatch):
    args = setup(sizes=((2, 2), (2, 2)), relationship="adjacent")
    limits = SearchLimits(minSharedWallM=2.0000000001)
    compared_topologies = []

    def record_comparison(first, second, first_walls, second_walls, bounds):
        compared_topologies.extend((first_walls, second_walls))
        return compare_layouts(first, second, first_walls, second_walls, bounds)

    monkeypatch.setattr(d04, "compare_layouts", record_comparison)
    result = generate(args, limits=limits)
    assert result.status == d04.Status.VALID
    (wall,) = result.candidates[0].validation.walls
    assert wall.length_m == 2.0 < limits.minSharedWallM
    assert compared_topologies
    assert all(topology == frozenset({wall.slots}) for topology in compared_topologies)


@pytest.mark.parametrize("width,height", [(w, h) for w in range(5, 10) for h in range(4, 8)])
def test_generated_fixtures_reproduce(width, height):
    args = setup(rectangle(width, height), sizes=((2, 2), (2, 2)), relationship="adjacent")
    result = generate(args)
    assert result == generate(args)
    assert result.status == d04.Status.VALID
    assert all(check(args, item.rooms).status == d04.Status.VALID for item in result.candidates)


def test_room_and_serialization_order_and_candidate_ids_cannot_create_diversity():
    args = setup(sizes=((2, 2), (2, 2), (2, 2)))
    candidate = generate(args).candidates[0]
    for rooms in permutations(candidate.rooms):
        assert check(args, rooms) == candidate.validation
    request, assessment, policy = args
    payload = dict(reversed(list(request.model_dump().items())))
    reordered = type(request).model_validate(payload)
    reordered.roomProgram.rooms.reverse()
    assert generate((reordered, assessment, policy)) == generate(args)
    other = replace(candidate, validation=replace(candidate.validation, candidate_id="forged"))
    metric = compare_layouts(candidate.rooms, other.rooms, frozenset(), frozenset(), (0, 0, 10, 8))
    assert metric.score == 0 and not metric.distinct


def test_repeated_room_types_retain_required_slot_identities_and_topology():
    args = setup(sizes=((2, 2), (2, 2)))
    first = place_rooms(*args).rooms
    second = (first[0], replace(first[1], bounds=(4, 0, 6, 2)))
    first_walls = frozenset(wall.slots for wall in check(args, first).walls)
    second_walls = frozenset(wall.slots for wall in check(args, second).walls)
    bounds = (0, 0, 10, 8)
    metric = compare_layouts(first, second, first_walls, second_walls, bounds)
    assert metric.distinct and metric.topology_difference == 1 and metric.relative_displacement_m > 0
    swapped = (replace(first[0], bounds=first[1].bounds), replace(first[1], bounds=first[0].bounds))
    assert compare_layouts(first, swapped, first_walls, first_walls, bounds).distinct
    assert check(args, first).candidate_id != check(args, swapped).candidate_id


@pytest.mark.parametrize("movement,distinct", [(1e-9, False), (.04999, False), (.05, True), (.05001, True)])
def test_diversity_tolerance(movement, distinct):
    room = d04.PlacedRoom(UUID(int=1), "bedroom", (0, 0, 1, 1))
    shifted = replace(room, bounds=(movement, 0, 1 + movement, 1))
    edge = frozenset({(UUID(int=1), UUID(int=2))})
    metric = compare_layouts((room,), (shifted,), edge, frozenset(), (0, 0, 4, 4))
    assert metric.distinct == distinct
    assert metric == compare_layouts((shifted,), (room,), frozenset(), edge, (0, 0, 4, 4))
    assert movement_threshold((0, 0, 200, 200)) == .5


@pytest.mark.parametrize("width,limit,code,valid_count", [
    (2, 96, "INSUFFICIENT_DIVERSITY", 1),
    (4, 2, "VARIANT_LIMIT_REACHED", 2),
    (4, 1, "VARIANT_LIMIT_REACHED", 1),
])
def test_incomplete_search_is_inconclusive(width, limit, code, valid_count):
    result = generate(setup(rectangle(width, 2)), variant_limit=limit)
    assert (result.status, result.code, result.valid_count) == (d04.Status.INCONCLUSIVE, code, valid_count)
    assert not result.candidates and not result.diversity


def test_duplicate_variants_do_not_spend_unique_candidate_budget():
    args = setup(rectangle(2, 2))
    result = generate(args, variant_limit=3)
    assert (result.status, result.code, result.checked, result.valid_count) == (
        d04.Status.INCONCLUSIVE, "INSUFFICIENT_DIVERSITY", 1, 1,
    )
    assert not result.candidates and not result.diversity
    result = generate(setup(rectangle(6, 2)), variant_limit=3)
    assert (result.status, result.checked, result.valid_count) == (d04.Status.VALID, 3, 3)
    assert len(result.candidates) == 3


def test_d03_probe_limit_has_no_candidates():
    result = generate(setup(), limits=SearchLimits(candidateChecks=1))
    assert (result.status, result.code, result.candidates) == (
        d04.Status.INCONCLUSIVE, "D03_PLACEMENT_NOT_FOUND", (),
    )


@pytest.mark.parametrize("limit", [0, 97, True, float("nan")])
def test_invalid_variant_limits(limit):
    assert generate(setup(), variant_limit=limit).status == d04.Status.UNSUPPORTED


@pytest.mark.parametrize("bounds,code", [
    ((0, 0, 0, 2), "ROOM_GEOMETRY_INVALID"),
    ((2, 0, 0, 2), "ROOM_GEOMETRY_INVALID"),
    ((0, 0, float("nan"), 2), "ROOM_COORDINATES_INVALID"),
    ((0, 0, float("inf"), 2), "ROOM_COORDINATES_INVALID"),
    ((0, 0, 10**1000, 2), "ROOM_COORDINATES_INVALID"),
    ((0, 0, 2), "ROOM_COORDINATES_INVALID"),
    ((False, 0, 2, 2), "ROOM_COORDINATES_INVALID"),
    ((-1, 0, 2, 2), "ROOM_OUTSIDE_PARCEL_OR_HOLE"),
    ((0, 0, 1, 4), "ROOM_DIMENSIONS_VIOLATION"),
    ((0, 0, 2, 2), "ROOM_AREA_VIOLATION"),
])
def test_room_geometry_and_minima(bounds, code):
    args = setup(areas=[5])
    (room,) = place_rooms(*args).rooms
    assert check(args, (replace(room, bounds=bounds),)).code == code


def test_count_and_id_checks():
    args = setup(sizes=((2, 2), (2, 2)))
    first, second = place_rooms(*args).rooms
    for rooms in ((first,), (first, second, replace(second, slot_id=UUID(int=900))),
                  (first, first), (first, replace(second, slot_id=UUID(int=900)))):
        assert check(args, rooms).code == "CANDIDATE_ROOM_IDENTITIES_INVALID"
    assert check(args, (first, replace(second, room_type="kitchen"))).code == "CANDIDATE_ROOM_TYPE_INVALID"
    wrong = args[2].model_copy(update={"roomCounts": {"bedroom": 1}})
    assert check((*args[:2], wrong), (first, second)).code == "ROOM_POLICY_MISMATCH"


@pytest.mark.parametrize("relationship,bounds,expected", [
    (None, (1, 1, 3, 3), "ROOM_INTERIORS_OVERLAP"),
    ("adjacent", (2, 2, 4, 4), "MANDATORY_ADJACENCY_VIOLATION"),
    ("adjacent", (2.0000001, 0, 4.0000001, 2), "MANDATORY_ADJACENCY_VIOLATION"),
    ("separated", (2, 0, 4, 2), "MANDATORY_SEPARATION_VIOLATION"),
    ("separated", (2, 2, 4, 4), "SUPPORTED_SYNTHETIC_CHECKS_PASSED"),
    ("adjacent", (2, 0, 4, 2), "SUPPORTED_SYNTHETIC_CHECKS_PASSED"),
])
def test_collision_and_relationships(relationship, bounds, expected):
    args = setup(sizes=((2, 2), (2, 2)), relationship=relationship)
    first, second = sorted(place_rooms(*args).rooms, key=lambda r: r.slot_id)
    rooms = replace(first, bounds=(0, 0, 2, 2)), replace(second, bounds=bounds)
    assert check(args, rooms).code == expected


def test_hole_and_invalid_and_unsupported_footprint():
    hole = [(3, 3), (3, 6), (6, 6), (6, 3), (3, 3)]
    args = setup(holes=[hole])
    (room,) = place_rooms(*args).rooms
    assert check(args, (replace(room, bounds=(3, 3, 5, 5)),)).status == d04.Status.INVALID_GEOMETRY
    for ring, status in [
        ([(0, 0), (4, 4), (4, 0), (0, 4), (0, 0)], d04.Status.INVALID_GEOMETRY),
        ([(5 + 4*cos(i*tau/129), 4 + 4*sin(i*tau/129)) for i in range(129)], d04.Status.UNSUPPORTED),
        (rectangle(8, 8), d04.Status.INVALID_GEOMETRY),
    ]:
        if ring[0] != ring[-1]:
            ring.append(ring[0])
        assessment = args[1].assessment
        forged = replace(assessment, result=assessment.result.model_copy(
            update={"buildableFootprint": metric_land(ring)},
        ))
        paired = PlacementAssessment(forged, args[0].regulatoryEvidence)
        assert check((args[0], paired, args[2]), (room,)).status == status


def test_verified_setback_violation_and_conservative_margin_are_distinct():
    args = setup(setbacks=(1, 1, 1, 1))
    (room,) = place_rooms(*args).rooms
    assert check(args, (room,)).status == d04.Status.VALID
    inside_setback = replace(room, bounds=(0, 0, 2, 2))
    assert check(args, (inside_setback,)).status == d04.Status.REGULATORY_NON_COMPLIANT
    on_nominal_boundary = replace(room, bounds=(1, 1, 3, 3))
    assert check(args, (on_nominal_boundary,)).code == "ROOM_OUTSIDE_CONSERVATIVE_FOOTPRINT"


def test_revision_and_evidence_freshness():
    args = setup()
    assert check(args, place_rooms(*args).rooms, revision=(UUID(int=19),) * 3).code == "CANDIDATE_REVISION_MISMATCH"
    wrong = args[2].model_copy(update={"requirementsRevisionId": UUID(int=19)})
    assert generate((*args[:2], wrong)).code == "ROOM_POLICY_REVISION_MISMATCH"
    changed = with_evidence(args[0], (1, 0, 0, 0))
    assert generate((changed, *args[1:])).code == "FOOTPRINT_ASSESSMENT_STALE"
    assert generate(args, reviewed_setbacks=reviewed_snapshot(changed)).code == "REVIEWED_SETBACK_SNAPSHOT_MISMATCH"
    assert generate(args, reviewed_setbacks=None).code == "REVIEWED_SETBACK_SNAPSHOT_MISSING"
    assert generate(args, confirmed_parcel=confirmed(metric_land(rectangle(12, 8)))).code == "CONFIRMED_PARCEL_MISMATCH"


def test_stale_negative_d02_result_is_never_reused():
    args = setup(setbacks=(8, 8, 8, 8))
    assert args[1].assessment.result.status == "INFEASIBLE"
    changed = with_evidence(args[0], (0, 0, 0, 0))
    assert generate((changed, *args[1:])).code == "FOOTPRINT_ASSESSMENT_STALE"
    assert generate((changed, assess_for_placement(changed), args[2])).status == d04.Status.VALID


def test_forged_d03_wall_metadata_is_rebuilt(monkeypatch):
    args = setup(sizes=((2, 2), (2, 2)), relationship="adjacent")
    seed = place_rooms(*args)
    monkeypatch.setattr(d04, "place_rooms", lambda *a, **kw: replace(seed, shared_walls=()))
    result = generate(args)
    assert result.status == d04.Status.VALID
    assert all(item.validation.walls[0].length_m == 2 for item in result.candidates)


@pytest.mark.parametrize("status,unknown,code", [
    ("MISSING", False, "EVIDENCE_MISSING"),
    ("UNVERIFIED", False, "EVIDENCE_UNVERIFIED"),
    ("CONFLICTING", False, "EVIDENCE_CONFLICTING"),
    ("UNVERIFIED", True, "REGULATORY_APPLICABILITY_UNKNOWN"),
])
def test_unverified_evidence_does_not_claim_compliance(status, unknown, code):
    args = setup()
    evidence = RegulatoryEvidence.model_validate({
        "status": status, "ruleSetVersion": "synthetic-v1" if unknown else None,
        "constraints": [setback(0, 0, applicability="UNKNOWN")] if unknown else [],
        "gaps": [{"code": "fixture-gap"}],
    })
    result = generate((args[0].model_copy(update={"regulatoryEvidence": evidence}), *args[1:]))
    assert (result.status, result.code, result.candidates) == (d04.Status.INCONCLUSIVE, code, ())


def test_missing_or_conflicting_edge_rules_and_unsupported_kinds():
    args = setup()
    assert generate(args, mandatory_kinds=None).code == "REGULATORY_SCOPE_MISSING"
    result = generate(args, mandatory_kinds=("setback", "height"))
    assert result.status == d04.Status.UNSUPPORTED and not result.candidates
    for rules, code in [
        ([setback(0, 0)], "SETBACK_EDGE_COVERAGE_MISSING"),
        ([setback(0, 0), setback(0, 1, rule="conflict")], "EVIDENCE_CONFLICTING"),
    ]:
        request = with_evidence(args[0], rules=rules)
        assert generate((request, assess_for_placement(request), args[2])).code == code


def test_missing_dependency_and_safe_geos_failure(monkeypatch):
    args = setup()
    original = builtins.__import__

    def unavailable(name, *args, **kwargs):
        if name == "shapely.errors":
            raise ImportError("private location")
        return original(name, *args, **kwargs)

    with monkeypatch.context() as patch:
        patch.setattr(builtins, "__import__", unavailable)
        result = generate(args)
        assert result.status == d04.Status.DEPENDENCY_UNAVAILABLE
        assert "private" not in repr(result)

    def broken(*args):
        raise GEOSException("private location")

    monkeypatch.setattr(d04, "build_footprint", broken)
    result = generate(args)
    assert result.status == d04.Status.INCONCLUSIVE
    assert "private" not in repr(result)


def test_malformed_input_and_false_d03_success(monkeypatch):
    args = setup()
    assert d04.generate_alternatives(None, *args[1:], **options(args)).code == "INVALID_INPUT"
    assert generate((args[0].model_copy(update={"seed": float("nan")}), *args[1:])).code == "INVALID_INPUT"
    seed = place_rooms(*args)
    monkeypatch.setattr(d04, "place_rooms", lambda *a, **kw: replace(seed, rooms=()))
    result = generate(args)
    assert result.status == d04.Status.REQUIREMENTS_VIOLATION and not result.candidates


@pytest.mark.parametrize("second_bounds", [(2, 0, 4, 2), (2, 2, 4, 4), (6, 6, 8, 8)])
def test_physical_access_requires_evidence_even_if_rooms_touch(second_bounds):
    args = setup(sizes=((2, 2), (2, 2)))
    first, second = place_rooms(*args).rooms
    rooms = replace(first, bounds=(0, 0, 2, 2)), replace(second, bounds=second_bounds)
    result = check(args, rooms, physical_access_required=True)
    assert (result.status, result.code) == (
        d04.Status.INCONCLUSIVE, "PHYSICAL_ACCESS_EVIDENCE_UNAVAILABLE",
    )
    assert not result.candidate_id
    assert result == check(args, rooms[::-1], physical_access_required=True)


@pytest.mark.parametrize("scope,status,code", [
    (None, d04.Status.INCONCLUSIVE, "PHYSICAL_ACCESS_SCOPE_UNKNOWN"),
    (True, d04.Status.INCONCLUSIVE, "PHYSICAL_ACCESS_EVIDENCE_UNAVAILABLE"),
    (1, d04.Status.UNSUPPORTED, "PHYSICAL_ACCESS_SCOPE_UNSUPPORTED"),
    ({"opening": {"type": "door"}}, d04.Status.UNSUPPORTED, "PHYSICAL_ACCESS_SCOPE_UNSUPPORTED"),
])
def test_unresolved_access_scope_cannot_emit_alternatives(scope, status, code):
    args = setup(sizes=((2, 2), (2, 2)), relationship="adjacent")
    validation = check(args, place_rooms(*args).rooms, physical_access_required=scope)
    result = generate(args, physical_access_required=scope)
    assert (validation.status, validation.code) == (result.status, result.code) == (status, code)
    assert not result.candidates and not result.diversity
    assert result == generate(args, physical_access_required=scope)


def test_omitting_access_scope_fails_closed_at_both_entry_points():
    args = setup()
    kwargs = options(args)
    del kwargs["physical_access_required"]
    seed = place_rooms(*args)
    validation = d04.validate_candidate(*args, seed.rooms, seed.revision, **kwargs)
    result = d04.generate_alternatives(*args, **kwargs)
    assert validation.code == result.code == "PHYSICAL_ACCESS_SCOPE_UNKNOWN"
    assert not result.candidates


def test_access_gate_does_not_override_existing_mandatory_failures():
    args = setup(sizes=((2, 2), (2, 2)), relationship="adjacent")
    first, second = place_rooms(*args).rooms
    assert check(args, (first, replace(second, bounds=first.bounds)),
                 physical_access_required=True).code == "ROOM_INTERIORS_OVERLAP"
    assert check(args, (first, replace(second, bounds=(6, 6, 8, 8))),
                 physical_access_required=True).code == "MANDATORY_ADJACENCY_VIOLATION"

    args = setup(setbacks=(1, 1, 1, 1))
    (room,) = place_rooms(*args).rooms
    result = check(args, (replace(room, bounds=(0, 0, 2, 2)),), physical_access_required=True)
    assert result.status == d04.Status.REGULATORY_NON_COMPLIANT
    changed = with_evidence(args[0], (0, 0, 0, 0))
    assert generate((changed, *args[1:]), physical_access_required=True).code == "FOOTPRINT_ASSESSMENT_STALE"


def test_contradictory_adjacency_is_rejected_before_access_scope():
    request, assessment, policy = setup(sizes=((2, 2), (2, 2)), relationship="adjacent")
    original, = request.adjacencyPreferences
    opposite = original.model_copy(update={"relationship": "separated"})
    request = request.model_copy(update={"adjacencyPreferences": [original, opposite]})
    result = generate((request, assessment, policy), physical_access_required=True)
    assert (result.status, result.code) == (d04.Status.INCONCLUSIVE, "INVALID_INPUT")
    assert not result.candidates

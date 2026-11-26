import builtins
from uuid import UUID

import pytest

pytest.importorskip("shapely", minversion="2.1.2")

from shapely.errors import GEOSException
from shapely.geometry import Point, Polygon, box

from app.geometry.footprint import feasibility
from app.geometry.footprint.contracts import FeasibilityResult
from app.geometry.footprint.feasibility import (
    FeasibilityInputError,
    ReviewedSetbackSnapshot,
    assess_feasibility,
)
from app.plans.generation.contracts import RegulatoryEvidence
from app.validation.boundary import format_public_validation_errors, parse_contract_json
from test_d02_area import assumptions_for, request_fixture
from test_d02_parcel import confirmed, metric_land

REVIEWER = UUID("20000000-0000-4000-8000-000000000001")


def setback(edge, distance, *, rule=None, applicability="APPLICABLE"):
    return {
        "ruleId": rule or f"synthetic-edge-{edge}",
        "type": "setback",
        "source": {
            "sourceVersion": "synthetic-source-v1",
            "sourceLocator": f"fixture/edge/{edge}",
            "jurisdiction": "Synthetic fixture only",
        },
        "parcelEdgeIndex": edge,
        "distanceM": distance,
        "applicability": {"status": applicability, "basis": "Synthetic scenario"},
        "reviewedBy": REVIEWER,
        "reviewReference": "synthetic-review-v1",
    }


def with_evidence(request, distances=(1, 1, 1, 1), *, rules=None):
    evidence = RegulatoryEvidence.model_validate({
        "status": "REVIEWED",
        "ruleSetVersion": "synthetic-d02-v1",
        "constraints": list(rules) if rules is not None else [setback(i, d) for i, d in enumerate(distances)],
        "gaps": [],
    })
    return request.model_copy(update={"regulatoryEvidence": evidence})


def reviewed_snapshot(request):
    return ReviewedSetbackSnapshot(
        projectId=request.projectId,
        landRevisionId=request.landRevisionId,
        requirementsRevisionId=request.requirementsRevisionId,
        synthetic=True,
        regulatoryEvidence=request.regulatoryEvidence,
    )


def assess(request, *, additional=None):
    return assess_feasibility(
        request,
        confirmed_parcel=confirmed(request.land),
        reviewed_setbacks=reviewed_snapshot(request),
        area_assumptions=None if additional is None else assumptions_for(request, additional=additional),
    )


def codes(assessment):
    return [item.code for item in assessment.result.diagnostics]


def test_supported_area_is_preliminary_and_retains_exact_revisions_and_provenance():
    request = with_evidence(request_fixture(), (1, 2, 3, 0.5))
    result = assess(request, additional=[2, 4])
    wire = parse_contract_json(FeasibilityResult, result.result.model_dump_json())
    assert wire.revision_key() == (request.projectId, request.landRevisionId, request.requirementsRevisionId)
    assert wire.status == "FEASIBLE"
    assert wire.landAreaM2 == 80
    assert wire.buildableAreaM2 == pytest.approx(30, abs=0.03)
    assert wire.requiredAreaM2 == 15
    assert result.removedAreaM2 == 80 - wire.buildableAreaM2
    assert result.areaShortfallM2 == 0
    assert wire.regulatoryEvidenceStatus == "REVIEWED"
    assert wire.ruleSetVersion == "synthetic-d02-v1"
    assert all(rule.source.sourceVersion == "synthetic-source-v1" for rule in result.appliedConstraints)
    assert [rule.parcelEdgeIndex for rule in result.appliedConstraints] == [0, 1, 2, 3]
    assert codes(result) == ["PRELIMINARY_AREA_ONLY"]
    assert wire.diagnostics[0].severity == "WARNING"
    assert "Room placement" in wire.diagnostics[0].message
    points = [(point.x, point.y) for point in wire.buildableFootprint.boundary.exterior]
    polygon = Polygon(points)
    assert box(0.5, 1, 8, 5).covers(polygon)
    assert polygon.covers(Point(1, 2))


def test_insufficient_area_is_established_against_optimistic_bound():
    result = assess(with_evidence(request_fixture()), additional=[51])
    assert result.result.status == "INFEASIBLE"
    assert result.result.requiredAreaM2 == 60
    assert result.result.buildableAreaM2 == pytest.approx(48, abs=0.02)
    assert result.optimisticBuildableAreaM2 == pytest.approx(48, abs=1e-6)
    assert result.areaShortfallM2 == pytest.approx(12, abs=1e-6)
    assert codes(result) == ["BUILDABLE_AREA_INSUFFICIENT"]


def test_area_near_approximation_band_is_inconclusive():
    result = assess(with_evidence(request_fixture()), additional=[39])
    assert result.result.requiredAreaM2 == 48
    assert result.result.buildableAreaM2 < 48
    assert result.result.status == "NEEDS_REVIEW"
    assert result.optimisticBuildableAreaM2 >= 48
    assert result.areaShortfallM2 is None
    assert codes(result) == ["FOOTPRINT_PRECISION_INCONCLUSIVE"]


@pytest.mark.parametrize("additional, status", [([], "INFEASIBLE"), (None, "NEEDS_REVIEW")])
def test_empty_footprint_is_zero_and_missing_area_remains_unknown(additional, status):
    result = assess(with_evidence(request_fixture(), (5, 0, 5, 0)), additional=additional)
    assert result.result.status == status
    assert result.result.buildableAreaM2 == 0
    assert result.result.buildableFootprint is None
    assert result.removedAreaM2 == 80
    assert result.areaShortfallM2 == (9 if additional is not None else None)
    assert "EMPTY_BUILDABLE_REGION" in codes(result)


def test_explicit_zero_reuses_the_entire_validated_parcel():
    request = with_evidence(request_fixture(), (0, 0, 0, 0))
    result = assess(request, additional=[])
    assert result.result.status == "FEASIBLE"
    assert result.result.buildableFootprint == request.land
    assert result.result.buildableAreaM2 == 80
    assert result.removedAreaM2 == 0


def test_missing_area_keeps_computed_footprint_without_asserting_feasibility():
    result = assess(with_evidence(request_fixture()))
    assert result.result.status == "NEEDS_REVIEW"
    assert result.result.buildableFootprint is not None
    assert result.result.requiredAreaM2 is None
    assert codes(result) == ["REQUIRED_AREA_POLICY_UNAVAILABLE"]


@pytest.mark.parametrize("status, code", [
    ("MISSING", "SETBACK_EVIDENCE_MISSING"),
    ("CONFLICTING", "REGULATORY_EVIDENCE_CONFLICT"),
    ("UNVERIFIED", "SETBACK_EVIDENCE_UNVERIFIED"),
])
def test_uncertain_evidence_does_not_become_zero_or_computed_footprint(status, code):
    request = request_fixture()
    request = request.model_copy(update={"regulatoryEvidence": request.regulatoryEvidence.model_copy(
        update={"status": status}
    )})
    result = assess(request, additional=[])
    assert result.result.status == "NEEDS_REVIEW"
    assert result.result.regulatoryEvidenceStatus == status
    assert result.result.buildableAreaM2 is None
    assert result.removedAreaM2 is None
    assert result.appliedConstraints == ()
    assert codes(result) == [code]


def test_reviewed_label_without_trusted_snapshot_is_not_enough():
    request = with_evidence(request_fixture())
    result = assess_feasibility(request, confirmed_parcel=confirmed(request.land))
    assert result.result.regulatoryEvidenceStatus == "UNVERIFIED"
    assert result.result.buildableAreaM2 is None
    assert codes(result) == ["SETBACK_EVIDENCE_UNVERIFIED"]


@pytest.mark.parametrize("synthetic", [False, 1, "true"])
def test_snapshot_requires_an_explicit_synthetic_true_boolean(synthetic):
    request = with_evidence(request_fixture())
    result = assess_feasibility(
        request, confirmed_parcel=confirmed(request.land),
        reviewed_setbacks=reviewed_snapshot(request).model_copy(update={"synthetic": synthetic}),
    )
    assert result.result.status == "NEEDS_REVIEW"
    assert codes(result) == ["SETBACK_INVALID"]


@pytest.mark.parametrize("field", ["projectId", "landRevisionId", "requirementsRevisionId"])
def test_evidence_binding_mismatch_is_rejected(field):
    request = with_evidence(request_fixture())
    snapshot = reviewed_snapshot(request).model_copy(update={field: REVIEWER})
    result = assess_feasibility(request, confirmed_parcel=confirmed(request.land), reviewed_setbacks=snapshot)
    assert result.result.buildableAreaM2 is None
    assert codes(result) == ["INPUT_REVISION_MISMATCH"]


def test_same_revision_with_different_geometry_or_evidence_fails_closed():
    request = with_evidence(request_fixture())
    other_land = metric_land([(0, 0), (10, 0), (10, 7), (0, 7), (0, 0)])
    broken_land = assess_feasibility(request, confirmed_parcel=confirmed(other_land))
    assert broken_land.result.landAreaM2 is None
    assert codes(broken_land) == ["INPUT_REVISION_MISMATCH"]

    other_pack = reviewed_snapshot(with_evidence(request, (2, 1, 1, 1)))
    broken_pack = assess_feasibility(request, confirmed_parcel=confirmed(request.land), reviewed_setbacks=other_pack)
    assert broken_pack.result.buildableAreaM2 is None
    assert codes(broken_pack) == ["INPUT_REVISION_MISMATCH"]


def test_required_area_bound_to_a_different_revision_never_succeeds():
    request = with_evidence(request_fixture())
    result = assess_feasibility(
        request, confirmed_parcel=confirmed(request.land), reviewed_setbacks=reviewed_snapshot(request),
        area_assumptions=assumptions_for(request).model_copy(update={"requirementsRevisionId": REVIEWER}),
    )
    assert result.result.requiredAreaM2 is result.result.buildableAreaM2 is None
    assert codes(result) == ["INPUT_REVISION_MISMATCH"]


def test_missing_or_non_applicable_edge_does_not_supply_zero():
    rules = [setback(0, 0), setback(1, 0), setback(2, 0), setback(3, 0, applicability="NOT_APPLICABLE")]
    result = assess(with_evidence(request_fixture(), rules=rules), additional=[])
    assert result.result.status == "NEEDS_REVIEW"
    assert result.result.buildableAreaM2 is None
    assert codes(result) == ["SETBACK_EVIDENCE_MISSING"]


def test_distinct_values_for_same_edge_are_conflicting():
    rules = [setback(i, 1) for i in range(4)] + [setback(0, 2, rule="synthetic-conflict")]
    result = assess(with_evidence(request_fixture(), rules=rules), additional=[])
    assert result.result.regulatoryEvidenceStatus == "CONFLICTING"
    assert result.result.buildableFootprint is None
    assert codes(result) == ["REGULATORY_EVIDENCE_CONFLICT"]


def test_repeatable_output_and_provenance_with_reordered_rules():
    rules = [setback(i, i / 2) for i in range(4)] + [setback(0, 0, rule="synthetic-independent-source")]
    request = request_fixture()
    first = assess(with_evidence(request, rules=rules), additional=[])
    second = assess(with_evidence(request, rules=reversed(rules)), additional=[])
    assert first.result.status == "FEASIBLE"
    assert first.result.model_dump_json() == second.result.model_dump_json()
    assert first.appliedConstraints == second.appliedConstraints
    assert len(first.appliedConstraints) == 5


@pytest.mark.parametrize("field, bad", [("distanceM", -1), ("parcelEdgeIndex", 4)])
def test_invalid_input_never_leaks_values_or_fabricates_identity(field, bad):
    request = with_evidence(request_fixture())
    constraints = list(request.regulatoryEvidence.constraints)
    constraints[0] = constraints[0].model_copy(update={field: bad})
    request = request.model_copy(update={"regulatoryEvidence": request.regulatoryEvidence.model_copy(
        update={"constraints": constraints}
    )})
    with pytest.raises(FeasibilityInputError) as error:
        assess_feasibility(request, confirmed_parcel=confirmed())
    assert error.value.diagnostic.code == "FEASIBILITY_INPUT_INVALID"
    assert str(error.value) == "The feasibility input violates the generation contract."


@pytest.mark.parametrize("vertices, status, expected_code", [
    ([(0, 0), (10, 8), (0, 8), (10, 0), (0, 0)], "NEEDS_REVIEW", "POLYGON_INVALID"),
    ([(0, 0), (2_000_000, 0), (3, 6), (0, 0)], "UNSUPPORTED", "PARCEL_PRECISION_UNSUPPORTED"),
    ([(0, 0), (4, 0), (4, 2), (8, 2), (8, 0), (12, 0), (12, 5),
      (8, 5), (8, 3), (4, 3), (4, 5), (0, 5), (0, 0)], "UNSUPPORTED", "FOOTPRINT_DISCONNECTED"),
])
def test_invalid_or_unsupported_geometry_never_reports_insufficiency(vertices, status, expected_code):
    request = request_fixture().model_copy(update={"land": metric_land(vertices)})
    result = assess(with_evidence(request, (0.75,) * (len(vertices) - 1)), additional=[])
    assert result.result.status == status
    assert result.result.buildableAreaM2 is None
    assert result.appliedConstraints == ()
    assert codes(result) == [expected_code]


def test_geos_error_is_safely_classified(monkeypatch):
    def fail(*args):
        raise GEOSException("SECRET_TOKEN from a local geometry operation")

    monkeypatch.setattr(feasibility, "build_footprint", fail)
    result = assess(with_evidence(request_fixture()), additional=[])
    assert result.result.status == "NEEDS_REVIEW"
    assert result.result.buildableAreaM2 is None
    assert "SECRET_TOKEN" not in result.result.model_dump_json()
    assert codes(result) == ["FOOTPRINT_CALCULATION_FAILED"]


def test_missing_optional_geometry_dependency_never_reports_infeasible(monkeypatch):
    original_import = builtins.__import__

    def guarded_import(name, *args, **kwargs):
        if name == "shapely.errors":
            raise ImportError("SECRET_TOKEN")
        return original_import(name, *args, **kwargs)

    monkeypatch.setattr(builtins, "__import__", guarded_import)
    result = assess(with_evidence(request_fixture()), additional=[])
    assert result.result.status == "NEEDS_REVIEW"
    assert codes(result) == ["FOOTPRINT_DEPENDENCY_UNAVAILABLE"]
    assert result.result.buildableAreaM2 is None


def test_duplicate_or_malformed_json_and_untrusted_value_have_safe_boundary_errors():
    from app.plans.generation.contracts import GenerationInput

    for payload in ('{"secret":"SECRET_TOKEN",', '{"secret":"SECRET_TOKEN","secret":0}'):
        with pytest.raises(ValueError) as error:
            parse_contract_json(GenerationInput, payload)
        assert "SECRET_TOKEN" not in str(format_public_validation_errors(error.value))

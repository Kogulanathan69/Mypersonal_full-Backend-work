"""Human-reviewed regulatory constraint provider (constraints contract v0; synthetic fixtures only)."""
import copy
import json
from pathlib import Path

import pytest
from pydantic import ValidationError

from app import canonical_json
from app.auth import InternalAuth
from app.constraints import ConstraintProvider, ConstraintsRequest, seal, table_digest
from app.main import create_app
from fastapi.testclient import TestClient
from support import TEST_TOKEN
from synthetic_corpus import NORTH_JURISDICTION, SOUTH_JURISDICTION, SyntheticEngine

ROOT = Path(__file__).resolve().parents[1]
TABLE = json.loads((ROOT / "tests" / "fixtures" / "synthetic_constraints.v0.json").read_text(encoding="utf-8"))
REVIEWERS = json.loads((ROOT / "tests" / "fixtures" / "synthetic_reviewers.v0.json").read_text(encoding="utf-8"))
EXAMPLE = ROOT / "contract" / "constraints_response.v0.example.json"
SCENARIOS = json.loads((ROOT / "contract" / "constraints_scenarios.v0.json").read_text(encoding="utf-8"))
ENDPOINT = "/internal/v1/regulations/constraints"
BINDING = {
    "project_id": "11111111-1111-4111-8111-111111111111",
    "project_revision": 3,
    "land_revision_id": "22222222-2222-4222-8222-222222222222",
    "requirement_revision_id": "33333333-3333-4333-8333-333333333333",
}
ENGINE = SyntheticEngine()


def provider(table=None, reviewers=None, **options):
    options.setdefault("allow_synthetic", True)
    return ConstraintProvider(copy.deepcopy(table or TABLE), ENGINE.page_text, copy.deepcopy(reviewers or REVIEWERS), **options)


def request(**overrides):
    body = {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING} | overrides
    return ConstraintsRequest.model_validate(body)


def by_kind(pack):
    return {k["kind"]: k for k in pack["kinds"]}


def client(constraints):
    from support import AUTH_HEADER
    return TestClient(create_app(ENGINE, InternalAuth.from_tokens([TEST_TOKEN]), constraints), headers={AUTH_HEADER: TEST_TOKEN})


def constraint(constraint_id):
    return copy.deepcopy(next(c for c in TABLE["constraints"] if c["constraint_id"] == constraint_id))


def with_constraints(*extra, replace=None):
    """The base table with constraints added/replaced, RESEALED so only the rule under test can refuse it."""
    table = copy.deepcopy(TABLE)
    if replace:
        table["constraints"] = [replace.get(c["constraint_id"], c) for c in table["constraints"]]
    table["constraints"] += list(extra)
    return seal(table)


# ---- states -------------------------------------------------------------------------------------------------------

def test_reviewed_constraint_is_verified_with_value_unit_source_and_applicability():
    pack = provider().lookup(request())
    rear = by_kind(pack)["REAR_SPACE_MIN"]

    assert rear["status"] == "VERIFIED" and pack["overall_status"] == "COMPLETE"
    c = rear["constraints"][0]
    assert (c["comparator"], c["value"], c["unit"], c["applies_to"], c["review_status"]) == (">=", 3.0, "m", "residential", "HUMAN_REVIEWED")
    assert (c["reviewer_id"], c["review_reference"]) == ("synthetic:reviewer-north", "SYN-2026-001")
    assert ENGINE.page_text(c["source"]["document_id"], c["source"]["page"])[c["source"]["start_char"]:c["source"]["end_char"]] == c["source"]["excerpt"]
    assert pack["legal_status_verified"] is False


def test_unreviewed_constraint_is_returned_only_as_unverified():
    side = by_kind(provider().lookup(request(kinds=["SIDE_SPACE_MIN"])))["SIDE_SPACE_MIN"]

    assert side["status"] == "UNVERIFIED"
    assert side["constraints"][0]["review_status"] == "UNREVIEWED"
    assert side["constraints"][0]["reviewer_id"] is None


def test_missing_kind_is_missing_never_unlimited_and_pack_is_incomplete():
    pack = provider().lookup(request(kinds=["REAR_SPACE_MIN", "FLOOR_AREA_RATIO_MAX"]))

    assert by_kind(pack)["FLOOR_AREA_RATIO_MAX"] == {"kind": "FLOOR_AREA_RATIO_MAX", "status": "MISSING", "constraints": []}
    assert pack["overall_status"] == "INCOMPLETE"


def test_rejected_constraints_are_never_returned():
    parking = by_kind(provider().lookup(request(jurisdiction=SOUTH_JURISDICTION, kinds=["PARKING_SPACES_MIN"])))["PARKING_SPACES_MIN"]

    assert parking == {"kind": "PARKING_SPACES_MIN", "status": "MISSING", "constraints": []}


def test_contradicting_reviewed_values_in_the_same_scope_are_conflicting():
    clone = constraint("syn.north.height") | {"constraint_id": "syn.north.height-2", "value": 12, "review_reference": "SYN-2026-090"}

    height = by_kind(provider(with_constraints(clone)).lookup(request(kinds=["BUILDING_HEIGHT_MAX"])))["BUILDING_HEIGHT_MAX"]

    assert height["status"] == "CONFLICTING" and len(height["constraints"]) == 2


def test_a_general_rule_and_an_overlapping_conditional_rule_conflict_without_assumed_precedence():
    """A conditional rule never silently overrides a general one: specificity/precedence is not assumed (M6)."""
    wide_road = constraint("syn.north.height") | {
        "constraint_id": "syn.north.height-wide-road", "value": 20, "review_reference": "SYN-2026-091",
        "conditions": [{"field": "road_width_m", "comparator": ">=", "value": 9, "unit": "m"}]}

    height = by_kind(provider(with_constraints(wide_road)).lookup(request(kinds=["BUILDING_HEIGHT_MAX"])))["BUILDING_HEIGHT_MAX"]

    assert height["status"] == "CONFLICTING" and len(height["constraints"]) == 2


@pytest.mark.parametrize(("low", "high", "expected"), [
    ({"comparator": "<", "value": 9}, {"comparator": ">=", "value": 9}, "VERIFIED"),     # disjoint: [.., 9) and [9, ..)
    ({"comparator": "<=", "value": 9}, {"comparator": ">=", "value": 9}, "CONFLICTING"),  # both apply at exactly 9 m
    ({"comparator": "<", "value": 9}, {"comparator": ">", "value": 12}, "VERIFIED"),
    ({"comparator": "==", "value": 9}, {"comparator": ">", "value": 9}, "VERIFIED"),
    ({"comparator": "==", "value": 9}, {"comparator": "<=", "value": 9}, "CONFLICTING"),
])
def test_conditional_rules_conflict_exactly_when_their_applicability_overlaps(low, high, expected):
    road = {"field": "road_width_m", "unit": "m"}
    narrow = constraint("syn.north.height") | {"conditions": [road | low]}
    wide = constraint("syn.north.height") | {"constraint_id": "syn.north.height-wide", "value": 20,
                                             "review_reference": "SYN-2026-092", "conditions": [road | high]}

    pack = provider(with_constraints(wide, replace={"syn.north.height": narrow})).lookup(request(kinds=["BUILDING_HEIGHT_MAX"]))

    assert by_kind(pack)["BUILDING_HEIGHT_MAX"]["status"] == expected


def test_conditions_on_different_fields_overlap():
    narrow = constraint("syn.north.height") | {"conditions": [{"field": "road_width_m", "comparator": "<", "value": 9, "unit": "m"}]}
    large = constraint("syn.north.height") | {"constraint_id": "syn.north.height-large", "value": 20, "review_reference": "SYN-2026-093",
                                              "conditions": [{"field": "plot_area_m2", "comparator": ">=", "value": 500, "unit": "m2"}]}

    pack = provider(with_constraints(large, replace={"syn.north.height": narrow})).lookup(request(kinds=["BUILDING_HEIGHT_MAX"]))

    assert by_kind(pack)["BUILDING_HEIGHT_MAX"]["status"] == "CONFLICTING"  # a narrow road on a large plot satisfies both


def test_jurisdiction_is_exact_and_unknown_jurisdictions_are_reported():
    pack = provider().lookup(request(jurisdiction="Synthetic North Council"))  # not the exact label

    assert by_kind(pack)["REAR_SPACE_MIN"]["status"] == "MISSING"
    assert "NO_CONSTRAINTS_FOR_JURISDICTION" in pack["warnings"]


def test_synthetic_tables_are_always_flagged():
    pack = provider().lookup(request())

    assert pack["synthetic"] is True and any(w.startswith("SYNTHETIC_TABLE") for w in pack["warnings"])


# ---- binding / evidence pack ----------------------------------------------------------------------------------------

def test_evidence_pack_is_bound_to_exact_revisions_and_deterministic():
    first = provider().lookup(request())
    again = provider().lookup(request())
    other_revision = provider().lookup(request(binding=BINDING | {"project_revision": 4}))
    other_land = provider().lookup(request(binding=BINDING | {"land_revision_id": "44444444-4444-4444-8444-444444444444"}))

    assert first["binding"] == BINDING and first["table_version"] == "synthetic-0.2"
    assert first["evidence_pack_id"] == again["evidence_pack_id"]
    assert len({first["evidence_pack_id"], other_revision["evidence_pack_id"], other_land["evidence_pack_id"]}) == 3


def test_evidence_pack_id_is_the_canonical_digest_of_the_rest_of_the_pack():
    pack = provider().lookup(request())
    body = {k: v for k, v in pack.items() if k != "evidence_pack_id"}

    assert pack["digest_algorithm"] == "SHA-256/BM-JCS-1"
    assert pack["evidence_pack_id"] == canonical_json.digest(body)
    assert pack["table_sha256"] == TABLE["integrity"]["sha256"]
    assert pack["reviewer_registry_sha256"] == REVIEWERS["integrity"]["sha256"]


def test_shared_contract_example_is_the_real_provider_output():
    actual = provider().lookup(request(kinds=["REAR_SPACE_MIN", "SIDE_SPACE_MIN", "FLOOR_AREA_RATIO_MAX"]))

    assert json.loads(EXAMPLE.read_text(encoding="utf-8")) == actual


def test_every_shared_scenario_response_is_reproducible_and_self_consistent():
    for scenario in SCENARIOS["scenarios"]:
        response = scenario["response"]
        body = {k: v for k, v in response.items() if k != "evidence_pack_id"}
        assert response["evidence_pack_id"] == canonical_json.digest(body), scenario["name"]
        assert {k["kind"]: k["status"] for k in response["kinds"]} == scenario["expected"]["kind_statuses"]
        assert scenario["expected"]["feasible_permitted"] == (scenario["expected"]["m6_regulatory_evidence_status"] == "REVIEWED")
        assert scenario["expected"]["feasible_permitted"] == (response["overall_status"] == "COMPLETE")


def test_committed_fixtures_match_the_builder():
    import subprocess
    import sys

    result = subprocess.run([sys.executable, "-B", str(ROOT / "tools" / "build_contract_fixtures.py"), "--check"],
                            capture_output=True, text=True, timeout=120)

    assert result.returncode == 0, result.stdout + result.stderr


# ---- table loading: refuse anything unreviewable --------------------------------------------------------------------

def _mutated(constraint_id, **changes):
    table = copy.deepcopy(TABLE)
    for c in table["constraints"]:
        if c["constraint_id"] == constraint_id:
            c.update(changes)
    return seal(table)


@pytest.mark.parametrize("table", [
    _mutated("syn.north.rear-space", unit="percent"),                       # unit/kind mismatch
    _mutated("syn.north.rear-space", comparator="<="),                      # comparator/kind mismatch
    _mutated("syn.north.rear-space", value=-1),                             # out of range
    _mutated("syn.north.rear-space", value=101),                            # out of range
    _mutated("syn.north.rear-space", reviewer_role=None),                   # reviewed without reviewer
    _mutated("syn.north.rear-space", reviewer_id=None),                     # reviewed without reviewer id
    _mutated("syn.north.rear-space", review_reference=None),                # reviewed without review reference
    _mutated("syn.north.rear-space", review_reference="review 1"),          # malformed reference
    _mutated("syn.north.rear-space", reviewer_id="Jane Perera"),            # a personal name is never an id
    _mutated("syn.north.rear-space", reviewed_at="2999-01-01"),             # review in the future
    _mutated("syn.north.side-space", reviewer_id="synthetic:reviewer-north"),  # unreviewed with review details
    _mutated("syn.north.rear-space", unexpected="x"),                       # unknown field
    _mutated("syn.north.rear-space", kind="SETBACK_ANYTHING"),              # unknown kind
    _mutated("syn.north.rear-space", review_status="AUTO_VERIFIED"),        # no automatic verification state exists
    _mutated("syn.north.rear-space", conditions=[{"field": "floors", "comparator": ">=", "value": 2, "unit": "m"}]),       # condition unit
    _mutated("syn.north.rear-space", conditions=[{"field": "floors", "comparator": ">=", "value": 500, "unit": "count"}]),  # condition range
    _mutated("syn.north.rear-space", conditions=[{"field": "floors", "comparator": "<", "value": 2, "unit": "count"},
                                                 {"field": "floors", "comparator": ">", "value": 3, "unit": "count"}]),     # unsatisfiable
    seal(dict(copy.deepcopy(TABLE), contract_version="constraints-1")),     # unknown contract version
])
def test_invalid_tables_are_refused(table):
    with pytest.raises((ValueError, ValidationError)):
        provider(table)


def test_non_finite_values_cannot_even_be_sealed_or_loaded():
    table = copy.deepcopy(TABLE)
    table["constraints"][0]["value"] = float("nan")

    with pytest.raises(canonical_json.CanonicalizationError):
        seal(table)
    with pytest.raises((ValueError, ValidationError)):
        provider(table)


def test_duplicate_constraint_ids_are_refused():
    table = copy.deepcopy(TABLE)
    table["constraints"].append(copy.deepcopy(table["constraints"][0]))

    with pytest.raises(ValueError, match="duplicate"):
        provider(seal(table))


@pytest.mark.parametrize("change", [
    {"excerpt": "A minimum rear space of FIVE metres shall be provided"},  # text not in the source
    {"start_char": 0},                                                      # offsets do not match the excerpt
    {"page": 9},                                                            # page does not exist
    {"document_id": "c" * 64},                                              # document not in the corpus
])
def test_constraints_whose_citation_does_not_reconstruct_are_refused(change):
    table = copy.deepcopy(TABLE)
    table["constraints"][0]["source"] = table["constraints"][0]["source"] | change

    with pytest.raises(ValueError, match="does not reconstruct"):
        provider(seal(table))


# ---- integrity and version verification -----------------------------------------------------------------------------

def test_tampered_tables_are_refused_by_their_integrity_seal():
    table = copy.deepcopy(TABLE)
    table["constraints"][0]["value"] = 2  # edited after review, not resealed

    with pytest.raises(ValueError, match="integrity digest does not match"):
        provider(table)


def test_missing_integrity_block_is_refused():
    table = {k: v for k, v in TABLE.items() if k != "integrity"}

    with pytest.raises(ValidationError):
        provider(table)


def test_pinned_version_and_digest_must_match():
    provider(expected_version="synthetic-0.2", expected_sha256=TABLE["integrity"]["sha256"])  # accepted

    with pytest.raises(ValueError, match="pinned version"):
        provider(expected_version="synthetic-0.1")
    with pytest.raises(ValueError, match="pinned digest"):
        provider(expected_sha256="0" * 64)
    with pytest.raises(ValueError, match="pinned digest"):
        provider(expected_reviewers_sha256="0" * 64)


def test_synthetic_tables_load_only_with_the_explicit_isolated_testing_opt_in():
    with pytest.raises(ValueError, match="only in isolated development/testing"):
        provider(allow_synthetic=False)


def test_synthetic_tables_must_use_a_synthetic_version():
    with pytest.raises(ValueError, match="synthetic- version"):
        provider(seal(dict(copy.deepcopy(TABLE), table_version="council-2026.1")))


def _real(table):
    """The synthetic table relabelled as real (still SYNTHETIC test data; used only to prove refusals)."""
    return seal(dict(copy.deepcopy(table), synthetic=False, table_version="council-2026.1"))


def test_real_tables_require_version_table_and_registry_pins():
    with pytest.raises(ValueError, match="requires version, table digest and reviewer registry digest pins"):
        provider(_real(TABLE), allow_synthetic=False)


def test_real_tables_cannot_be_authorized_by_a_synthetic_registry_or_use_synthetic_identities():
    real = _real(TABLE)
    pins = {"expected_version": "council-2026.1", "expected_sha256": real["integrity"]["sha256"],
            "expected_reviewers_sha256": REVIEWERS["integrity"]["sha256"]}

    with pytest.raises(ValueError, match="cannot be authorized by a synthetic reviewer registry"):
        provider(real, allow_synthetic=False, **pins)


def test_synthetic_tables_require_synthetic_reviewer_ids_and_references():
    table = _mutated("syn.north.rear-space", review_reference="BMR-2026-001")

    with pytest.raises(ValueError, match="synthetic reviewer ids and references"):
        provider(table)


# ---- reviewer authorization (not just identifier format) -----------------------------------------------------------

def _registry(**changes_for_north):
    registry = copy.deepcopy(REVIEWERS)
    for r in registry["reviewers"]:
        if r["reviewer_id"] == "synthetic:reviewer-north":
            r.update(changes_for_north)
    return seal(registry)


@pytest.mark.parametrize(("table", "reviewers", "reason"), [
    (_mutated("syn.north.rear-space", reviewer_id="synthetic:reviewer-unknown"), None, "not in the reviewer registry"),
    (_mutated("syn.north.rear-space", reviewer_id="synthetic:reviewer-revoked"), None, "revoked"),
    (None, _registry(status="REVOKED"), "revoked"),
    (_mutated("syn.north.rear-space", reviewer_role="synthetic_other_role"), None, "role does not match"),
    (_mutated("syn.north.rear-space", reviewer_id="synthetic:reviewer-south"), None, "not authorized for the jurisdiction"),
    (None, _registry(kinds=["FRONT_SETBACK_MIN", "SIDE_SPACE_MIN", "BUILDING_HEIGHT_MAX"]), "not authorized for the constraint kind"),
    (None, _registry(authorized_from="2026-11-20"), "outside the reviewer's authorization period"),
    (None, _registry(authorized_until="2026-11-18"), "outside the reviewer's authorization period"),
    (_mutated("syn.south.parking", reviewer_id="synthetic:reviewer-north"), None, "not authorized for the jurisdiction"),  # rejections too
])
def test_sign_offs_by_unauthorized_reviewers_are_refused(table, reviewers, reason):
    with pytest.raises(ValueError, match=reason):
        provider(table, reviewers)


def test_tampered_reviewer_registries_are_refused():
    registry = copy.deepcopy(REVIEWERS)
    registry["reviewers"][0]["jurisdictions"].append(SOUTH_JURISDICTION)  # scope widened without resealing

    with pytest.raises(ValueError, match="reviewer registry integrity digest"):
        provider(reviewers=registry)


@pytest.mark.parametrize("registry", [
    seal(dict(copy.deepcopy(REVIEWERS), reviewers=REVIEWERS["reviewers"] + [REVIEWERS["reviewers"][0]])),  # duplicate id
    seal(dict(copy.deepcopy(REVIEWERS), synthetic=False)),                                                  # synthetic ids in a real registry
    _registry(authorized_from="2026-12-01", authorized_until="2026-11-01"),                                 # inverted period
    _registry(full_name="x"),                                                                               # no personal data fields
])
def test_invalid_reviewer_registries_are_refused(registry):
    with pytest.raises((ValueError, ValidationError)):
        provider(reviewers=registry)


def test_registry_digest_uses_the_shared_canonical_form():
    assert REVIEWERS["integrity"]["sha256"] == table_digest(REVIEWERS)
    assert TABLE["integrity"]["sha256"] == table_digest(TABLE)


# ---- HTTP endpoint ------------------------------------------------------------------------------------------------

def test_endpoint_returns_the_bound_evidence_pack():
    response = client(provider()).post(ENDPOINT, json={"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING})

    assert response.status_code == 200
    assert response.json()["kinds"][0]["status"] == "VERIFIED"


def test_endpoint_without_a_table_is_truthfully_unavailable():
    response = client(None).post(ENDPOINT, json={"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING})

    assert response.status_code == 503 and response.json() == {"code": "CONSTRAINTS_UNAVAILABLE"}


@pytest.mark.parametrize("body", [
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"]},                                   # binding missing
    {"jurisdiction": NORTH_JURISDICTION, "kinds": [], "binding": BINDING},                               # no kinds
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN", "REAR_SPACE_MIN"], "binding": BINDING},
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["UNKNOWN_KIND"], "binding": BINDING},
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING | {"project_revision": 0}},
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING | {"project_revision": "3"}},
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING | {"land_revision_id": "not-a-uuid"}},
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING, "verified": True},
    {"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"], "binding": BINDING, "building_use": "industrial"},
])
def test_endpoint_rejects_invalid_contract_structures(body):
    response = client(provider()).post(ENDPOINT, json=body)

    assert response.status_code == 422 and response.json()["code"] == "VALIDATION_FAILED"


@pytest.mark.parametrize(("revision", "accepted"), [
    (1, True), (2_147_483_648, True), (999_999_999_999_999, True),   # Int64 revisions beyond Int32
    (1_000_000_000_000_000, False), (2**63 - 1, False), (0, False),  # outside the exact BM-JCS-1 integer domain
])
def test_project_revisions_are_int64_within_the_canonical_integer_domain(revision, accepted):
    response = client(provider()).post(ENDPOINT, json={"jurisdiction": NORTH_JURISDICTION, "kinds": ["REAR_SPACE_MIN"],
                                                       "binding": BINDING | {"project_revision": revision}})

    assert response.status_code == (200 if accepted else 422)
    if accepted:
        pack = response.json()
        assert pack["binding"]["project_revision"] == revision
        assert pack["evidence_pack_id"] == canonical_json.digest({k: v for k, v in pack.items() if k != "evidence_pack_id"})


def test_retrieval_never_creates_constraints():
    """A search for a phrase that exists in the corpus does not change the constraint pack."""
    c = client(provider())
    before = c.post(ENDPOINT, json={"jurisdiction": NORTH_JURISDICTION, "kinds": ["SIDE_SPACE_MIN", "FRONT_SETBACK_MIN"], "binding": BINDING}).json()
    c.post("/internal/v1/regulations/search", json={"query": "front setback side space", "jurisdiction": NORTH_JURISDICTION})
    after = c.post(ENDPOINT, json={"jurisdiction": NORTH_JURISDICTION, "kinds": ["SIDE_SPACE_MIN", "FRONT_SETBACK_MIN"], "binding": BINDING}).json()

    assert before == after
    assert by_kind(after)["SIDE_SPACE_MIN"]["status"] == "UNVERIFIED" and by_kind(after)["FRONT_SETBACK_MIN"]["status"] == "MISSING"


# ---- Python/C# receipt parity (M6 D02 C11) --------------------------------------------------------------------------

def _real_shaped():
    """In-memory REAL-SHAPED table and registry built from synthetic test data (never committed, never real values)."""
    registry = copy.deepcopy(REVIEWERS)
    registry["synthetic"] = False
    registry["registry_version"] = "reviewers-2026.1"
    for r in registry["reviewers"]:
        r["reviewer_id"] = r["reviewer_id"].removeprefix("synthetic:")
    registry = seal(registry)
    table = copy.deepcopy(TABLE)
    table["synthetic"] = False
    table["table_version"] = "council-2026.1"
    for n, c in enumerate(table["constraints"], 1):
        if c["review_status"] != "UNREVIEWED":
            c["reviewer_id"] = c["reviewer_id"].removeprefix("synthetic:")
            c["review_reference"] = f"BMR-2026-{n:03d}"
    table = seal(table)
    pins = {"expected_version": "council-2026.1", "expected_sha256": table["integrity"]["sha256"],
            "expected_reviewers_sha256": registry["integrity"]["sha256"]}
    return table, registry, pins


@pytest.mark.parametrize(("version", "synthetic"), [
    ("reviewers-0.1", True),             # M6 D02 finding: synthetic registry without the synthetic- version prefix
    ("synthetic-reviewers-2026.1", False),
])
def test_registry_version_prefix_must_match_the_synthetic_flag(version, synthetic):
    if synthetic:
        registry = seal(dict(copy.deepcopy(REVIEWERS), registry_version=version))
        table, options = TABLE, {}
    else:
        table, registry, options = _real_shaped()
        registry = seal(dict(registry, registry_version=version))
        options = options | {"expected_reviewers_sha256": registry["integrity"]["sha256"], "allow_synthetic": False}

    with pytest.raises(ValueError, match="registry version must start with synthetic- exactly when"):
        provider(table, registry, **options)


def _assert_csharp_receipt_prefix_rules(pack):
    synthetic = pack["synthetic"]
    assert pack["table_version"].startswith("synthetic-") == synthetic
    assert pack["reviewer_registry_version"].startswith("synthetic-") == synthetic
    for kind in pack["kinds"]:
        for c in kind["constraints"]:
            if c["review_status"] == "HUMAN_REVIEWED":
                assert c["reviewer_id"].startswith("synthetic:") == synthetic
                assert c["review_reference"].startswith("SYN-") == synthetic


def test_every_pack_python_can_serve_satisfies_the_csharp_receipt_prefix_rules():
    import sys
    sys.path.insert(0, str(ROOT / "tools"))
    from build_contract_fixtures import reviewers as fixture_reviewers, scenario_cases, scenario_request
    from synthetic_corpus import page_text

    for _name, _description, table, overrides, _status in scenario_cases():
        pack = ConstraintProvider(table, page_text, fixture_reviewers(), allow_synthetic=True).lookup(
            ConstraintsRequest.model_validate(scenario_request(**overrides)))
        _assert_csharp_receipt_prefix_rules(pack)

    table, registry, pins = _real_shaped()
    real = provider(table, registry, allow_synthetic=False, **pins).lookup(request(kinds=["REAR_SPACE_MIN", "BUILDING_HEIGHT_MAX"]))
    assert real["synthetic"] is False and by_kind(real)["REAR_SPACE_MIN"]["status"] == "VERIFIED"
    _assert_csharp_receipt_prefix_rules(real)

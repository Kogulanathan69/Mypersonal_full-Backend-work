"""Builds every SYNTHETIC shared contract fixture deterministically (M7, constraints contract v0).

    python tools/build_contract_fixtures.py          # rewrite the fixtures
    python tools/build_contract_fixtures.py --check  # exit 1 if any committed fixture differs (also a pytest test)

Outputs (all synthetic test data, NOT real regulations or real reviewers):
- tests/fixtures/synthetic_constraints.v0.json       sealed synthetic constraint table
- tests/fixtures/synthetic_reviewers.v0.json         sealed synthetic reviewer registry (pseudonymous ids)
- contract/constraints_response.v0.example.json      provider output for the documented example request
- contract/constraints_scenarios.v0.json             shared M6/M7 setback integration scenarios
- contract/canonical_json_vectors.v1.json            BM-JCS-1 digest vectors shared by Python and C#
"""
import copy
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT), str(ROOT / "tests")]

from app import canonical_json  # noqa: E402
from app.constraints import ConstraintProvider, ConstraintsRequest, seal  # noqa: E402
from synthetic_corpus import NORTH, NORTH_JURISDICTION, SOUTH, SOUTH_JURISDICTION, page_text  # noqa: E402

REVIEW_DATE = "2026-11-19"
ROLE = "synthetic_test_reviewer"
BINDING = {
    "project_id": "11111111-1111-4111-8111-111111111111",
    "project_revision": 3,
    "land_revision_id": "22222222-2222-4222-8222-222222222222",
    "requirement_revision_id": "33333333-3333-4333-8333-333333333333",
}
SETBACKS = ["FRONT_SETBACK_MIN", "REAR_SPACE_MIN", "SIDE_SPACE_MIN"]


def source(document_id, page, excerpt):
    text = page_text(document_id, page)
    start = text.find(excerpt)
    assert start >= 0 and text.find(excerpt, start + 1) < 0, excerpt
    return {"document_id": document_id, "page": page, "start_char": start, "end_char": start + len(excerpt), "excerpt": excerpt}


def constraint(constraint_id, jurisdiction, kind, comparator, value, unit, applies_to, src, review=None, conditions=()):
    c = {
        "constraint_id": constraint_id, "jurisdiction": jurisdiction, "kind": kind, "comparator": comparator,
        "value": value, "unit": unit, "applies_to": applies_to, "conditions": list(conditions),
    }
    if review is None:
        c["review_status"] = "UNREVIEWED"
    else:
        status, reviewer, reference = review
        c |= {"review_status": status, "reviewer_role": ROLE, "reviewer_id": reviewer,
              "review_reference": reference, "reviewed_at": REVIEW_DATE}
    return c | {"source": src}


NORTH_REVIEWER = "synthetic:reviewer-north"
SOUTH_REVIEWER = "synthetic:reviewer-south"
REAR = source(NORTH, 1, "A minimum rear space of three metres shall be provided")
SIDE = source(NORTH, 1, "A side space of one metre shall be kept free of permanent structures")
FRONT = source(NORTH, 1, "behind every residential building constructed on a plot")  # synthetic citation only


def reviewers():
    return seal({
        "registry_version": "synthetic-reviewers-0.1",
        "synthetic": True,
        "reviewers": [
            {"reviewer_id": NORTH_REVIEWER, "role": ROLE, "status": "ACTIVE", "authorized_from": "2026-01-01",
             "authorized_until": "2026-12-31", "jurisdictions": [NORTH_JURISDICTION],
             "kinds": ["FRONT_SETBACK_MIN", "REAR_SPACE_MIN", "SIDE_SPACE_MIN", "BUILDING_HEIGHT_MAX"]},
            {"reviewer_id": SOUTH_REVIEWER, "role": ROLE, "status": "ACTIVE", "authorized_from": "2026-01-01",
             "authorized_until": "2026-12-31", "jurisdictions": [SOUTH_JURISDICTION],
             "kinds": ["PLOT_COVERAGE_MAX", "PARKING_SPACES_MIN"]},
            {"reviewer_id": "synthetic:reviewer-revoked", "role": ROLE, "status": "REVOKED", "authorized_from": "2026-01-01",
             "authorized_until": None, "jurisdictions": [NORTH_JURISDICTION, SOUTH_JURISDICTION], "kinds": SETBACKS},
        ],
    })


def table(constraints=None):
    base = [
        constraint("syn.north.rear-space", NORTH_JURISDICTION, "REAR_SPACE_MIN", ">=", 3, "m", "residential", REAR,
                   ("HUMAN_REVIEWED", NORTH_REVIEWER, "SYN-2026-001")),
        constraint("syn.north.side-space", NORTH_JURISDICTION, "SIDE_SPACE_MIN", ">=", 1, "m", "any", SIDE),
        constraint("syn.north.height", NORTH_JURISDICTION, "BUILDING_HEIGHT_MAX", "<=", 15, "m", "residential",
                   source(NORTH, 2, "shall not exceed fifteen metres"), ("HUMAN_REVIEWED", NORTH_REVIEWER, "SYN-2026-002")),
        constraint("syn.south.coverage", SOUTH_JURISDICTION, "PLOT_COVERAGE_MAX", "<=", 65, "percent", "residential",
                   source(SOUTH, 1, "shall not exceed sixty five percent of the plot area"), ("HUMAN_REVIEWED", SOUTH_REVIEWER, "SYN-2026-003")),
        constraint("syn.south.parking", SOUTH_JURISDICTION, "PARKING_SPACES_MIN", ">=", 1, "count", "residential",
                   source(SOUTH, 1, "One parking space shall be provided for each dwelling unit"), ("REJECTED", SOUTH_REVIEWER, "SYN-2026-004")),
    ]
    return seal({"contract_version": "constraints-0", "table_version": "synthetic-0.2", "synthetic": True,
                 "constraints": base if constraints is None else constraints})


def variant(add=(), replace=None):
    """The base table with constraints replaced (by id) and/or added, resealed."""
    constraints = copy.deepcopy(table()["constraints"])
    if replace:
        constraints = [replace.get(c["constraint_id"], c) for c in constraints]
    return table(constraints + list(add))


def height(comparator, value):
    return {"field": "building_height_m", "comparator": comparator, "value": value, "unit": "m"}


def pack(tbl, **request):
    body = scenario_request(**request)
    provider = ConstraintProvider(tbl, page_text, reviewers(), allow_synthetic=True)
    return body, provider.lookup(ConstraintsRequest.model_validate(body))


def scenario_cases():
    """(name, description, table, request overrides, expected M6 status) for every shared setback scenario.

    Also used by the test-only acceptance service (tests/acceptance_app.py) so live and file fixtures cannot diverge.
    """
    reviewed_front = constraint("syn.north.front-setback", NORTH_JURISDICTION, "FRONT_SETBACK_MIN", ">=", 2, "m", "residential", FRONT,
                                ("HUMAN_REVIEWED", NORTH_REVIEWER, "SYN-2026-005"))
    reviewed_side = constraint("syn.north.side-space", NORTH_JURISDICTION, "SIDE_SPACE_MIN", ">=", 1, "m", "any", SIDE,
                               ("HUMAN_REVIEWED", NORTH_REVIEWER, "SYN-2026-006"))
    tall_rear = constraint("syn.north.rear-space-tall", NORTH_JURISDICTION, "REAR_SPACE_MIN", ">=", 4, "m", "residential", REAR,
                           ("HUMAN_REVIEWED", NORTH_REVIEWER, "SYN-2026-007"), [height(">=", 10)])
    low_rear = constraint("syn.north.rear-space", NORTH_JURISDICTION, "REAR_SPACE_MIN", ">=", 3, "m", "residential", REAR,
                          ("HUMAN_REVIEWED", NORTH_REVIEWER, "SYN-2026-001"), [height("<", 10)])
    complete = variant(add=[reviewed_front], replace={"syn.north.side-space": reviewed_side})
    cases = [
        ("north-baseline", "Rear reviewed, side unreviewed, front absent: never FEASIBLE.",
         table(), {}, "MISSING"),
        ("all-setbacks-verified", "All three setbacks human-reviewed by an authorized reviewer: evidence may support FEASIBLE.",
         complete, {}, "REVIEWED"),
        ("rear-conflict-overlapping-conditions",
         "General rear 3 m and rear 4 m for buildings >= 10 m overlap with different values: CONFLICTING (no precedence assumed).",
         variant(add=[reviewed_front, tall_rear], replace={"syn.north.side-space": reviewed_side}), {}, "CONFLICTING"),
        ("rear-disjoint-conditions",
         "Rear 3 m below 10 m and 4 m from 10 m never overlap: VERIFIED with two conditional constraints (M6 evaluates the conditions).",
         variant(add=[reviewed_front, tall_rear], replace={"syn.north.side-space": reviewed_side, "syn.north.rear-space": low_rear}), {}, "REVIEWED"),
        ("side-unverified-only", "Front and rear reviewed, side only unreviewed: UNVERIFIED, never FEASIBLE.",
         variant(add=[reviewed_front]), {}, "UNVERIFIED"),
        ("unknown-jurisdiction", "No constraints for the jurisdiction: every setback MISSING.",
         complete, {"jurisdiction": "Synthetic East Council planning area"}, "MISSING"),
    ]
    return cases


def scenario_request(**overrides):
    return {"jurisdiction": NORTH_JURISDICTION, "kinds": SETBACKS, "building_use": "residential", "binding": BINDING} | overrides


def scenarios():
    out = []
    for name, description, tbl, overrides, m6_status in scenario_cases():
        request, response = pack(tbl, **overrides)
        out.append({
            "name": name,
            "description": description,
            "mandatory_kinds": SETBACKS,
            "request": request,
            "response": response,
            "expected": {
                "kind_statuses": {k["kind"]: k["status"] for k in response["kinds"]},
                "overall_status": response["overall_status"],
                "m6_regulatory_evidence_status": m6_status,
                "feasible_permitted": m6_status == "REVIEWED",
            },
        })
    return {
        "contract_version": "constraints-0",
        "synthetic": True,
        "note": "SYNTHETIC integration scenarios for M6 setback evaluation. Not real regulations or reviewers. "
                "m6_regulatory_evidence_status precedence: CONFLICTING > MISSING > UNVERIFIED > REVIEWED over the mandatory kinds; "
                "FEASIBLE is permitted only for REVIEWED (M6 deterministic validation remains authoritative).",
        "scenarios": out,
    }


def vectors():
    valid = [
        ("key-order-utf16", '{"b":1,"a":2,"\\u00e9":3,"A":4}'),
        ("utf16-not-code-point-order", '{"\\ufb01":1,"\\ud83d\\ude00":2}'),
        ("nested-and-arrays", '{"z":[3,{"y":null,"x":true}],"a":{"c":false,"b":"s"}}'),
        ("whitespace-removed", '{ "a" : [ 1 , 2 ] ,\n "b" : { } }'),
        ("string-escapes", '{"s":"q\\" b\\\\ \\/ \\b\\f\\n\\r\\t \\u0001 \\u001f \\u007f"}'),
        ("non-ascii-raw", '{"s":"Kandy \\u0dc1\\u0dca\\u200d\\u0dbb\\u0dd3 \\u20ac"}'),
        ("integers", '{"a":3,"b":3.0,"c":-0,"d":1e2,"e":999999999999999,"f":-42}'),
        ("decimals", '{"a":0.5,"b":12.25,"c":0.0001,"d":-2.75,"e":123456789012.5,"f":0.1}'),
        ("constraint-shape", '{"comparator":">=","value":3,"unit":"m","conditions":[{"field":"building_height_m","comparator":"<","value":9.5,"unit":"m"}]}'),
    ]
    invalid = [
        ("duplicate-key", '{"a":1,"a":2}'),
        ("number-too-large", '{"a":1e15}'),
        ("integer-too-large", '{"a":1000000000000000}'),
        ("fraction-too-small", '{"a":0.00001}'),
        ("lone-surrogate", '{"a":"\\ud800"}'),
    ]

    def parse(text):
        def pairs(items):
            keys = [k for k, _ in items]
            if len(set(keys)) != len(keys):
                raise canonical_json.CanonicalizationError("duplicate key")
            return dict(items)
        return json.loads(text, object_pairs_hook=pairs)

    out_valid = []
    for name, text in valid:
        canonical = canonical_json.canonicalize(parse(text))
        out_valid.append({"name": name, "json": text, "canonical": canonical,
                          "sha256": canonical_json.digest(parse(text))})
    for name, text in invalid:
        try:
            canonical_json.canonicalize(parse(text))
        except canonical_json.CanonicalizationError:
            continue
        raise AssertionError(f"invalid vector accepted: {name}")
    return {"algorithm": canonical_json.ALGORITHM, "valid": out_valid,
            "invalid": [{"name": n, "json": t} for n, t in invalid]}


def outputs():
    request, example = pack(table(), kinds=["REAR_SPACE_MIN", "SIDE_SPACE_MIN", "FLOOR_AREA_RATIO_MAX"], building_use=None)
    return {
        ROOT / "tests" / "fixtures" / "synthetic_constraints.v0.json": table(),
        ROOT / "tests" / "fixtures" / "synthetic_reviewers.v0.json": reviewers(),
        ROOT / "contract" / "constraints_response.v0.example.json": example,
        ROOT / "contract" / "constraints_scenarios.v0.json": scenarios(),
        ROOT / "contract" / "canonical_json_vectors.v1.json": vectors(),
    }


def render(value):
    return json.dumps(value, indent=2, ensure_ascii=False) + "\n"


def main(check):
    stale = []
    for path, value in outputs().items():
        text = render(value)
        if check:
            if not path.exists() or path.read_text(encoding="utf-8") != text:
                stale.append(path.relative_to(ROOT).as_posix())
        else:
            path.write_text(text, encoding="utf-8", newline="\n")
            print("wrote", path.relative_to(ROOT).as_posix())
    if stale:
        print("stale fixtures:", ", ".join(stale))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main("--check" in sys.argv))

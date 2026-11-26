"""Exercise the same synthetic contract corpus used by the pending C# scenarios."""

import json
from copy import deepcopy
from pathlib import Path

import pytest
from pydantic import ValidationError

from app.geometry.footprint.contracts import FeasibilityResult, MetricLand
from app.plans.generation.contracts import GenerationInput, GenerationResult, RegulatoryEvidence
from app.plans.generation.job_contracts import GenerationJob
from app.validation.boundary import parse_contract_json
from app.validation.contracts import ValidationResult

CORPUS = json.loads(
    (Path(__file__).parent / "fixtures/parity/shared-contract-cases.json").read_text(encoding="utf-8")
)
MODELS = {
    model.__name__: model
    for model in (
        FeasibilityResult, MetricLand, GenerationInput, GenerationResult, RegulatoryEvidence,
        GenerationJob, ValidationResult,
    )
}
POSITIVE = {case["id"]: case for case in CORPUS["positive"]}
NUMBER_MARKER = "__m6_raw_number__"


def variant_json(base, change):
    payload = deepcopy(base)
    *parents, leaf = change["path"]
    target = payload
    for part in parents:
        target = target[part]
    operation = change["op"]
    if operation == "remove":
        del target[leaf]
    elif operation == "rename":
        target[change["value"]] = target.pop(leaf)
    elif operation == "set":
        target[leaf] = change["value"]
    elif operation == "number":
        assert change["value"] == "1e999"
        target[leaf] = NUMBER_MARKER
    else:
        raise AssertionError(f"Unknown fixture operation: {operation}")
    text = json.dumps(payload, allow_nan=False)
    if operation == "number":
        assert text.count(json.dumps(NUMBER_MARKER)) == 1
        text = text.replace(json.dumps(NUMBER_MARKER), change["value"])
    return text


@pytest.mark.parametrize("case", CORPUS["positive"], ids=lambda case: case["id"])
def test_positive_contract_fixture(case):
    model = MODELS[case["model"]]
    payload = case["payload"]
    result = parse_contract_json(model, json.dumps(payload, allow_nan=False))
    assert result.model_dump(mode="json") == payload
    assert parse_contract_json(model, result.model_dump_json()) == result


@pytest.mark.parametrize("case", CORPUS["variants"], ids=lambda case: case["id"])
def test_contract_fixture_variant(case):
    base = POSITIVE[case["base"]]
    model = MODELS[base["model"]]
    wire = variant_json(base["payload"], case["change"])

    if case["python"] == "accept":
        assert case["csharp"] == "known-difference"
        result = parse_contract_json(model, wire)
        # Python defaults the omitted M3 collection. The shared wire remains unchanged.
        floor = result.alternatives[0].geometry.floors[0]
        assert getattr(floor, case["change"]["path"][-1]) == []
    else:
        assert case["python"] == "reject"
        with pytest.raises(ValidationError) as error:
            parse_contract_json(model, wire)
        expected = case["pythonError"]
        expected_types = {expected} if isinstance(expected, str) else set(expected)
        assert expected_types <= {issue["type"] for issue in error.value.errors()}

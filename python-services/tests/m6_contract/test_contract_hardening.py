"""Regression probes for the D01-G audit findings."""

import json
from copy import deepcopy
from pathlib import Path

import pytest
from pydantic import ValidationError

from app.geometry.footprint.contracts import Diagnostic, FeasibilityResult, MetricLand
from app.plans.generation.contracts import GenerationInput
from app.plans.generation.job_contracts import (
    DIAGNOSTIC_REGISTRY,
    GenerationJob,
    make_public_job_diagnostic,
    serialize_job_snapshot,
    validate_job_update,
)
from app.validation.boundary import ContractJsonError, format_public_validation_errors, parse_contract_json


CORPUS_PATH = Path(__file__).parent / "fixtures/parity/shared-contract-cases.json"
PAYLOADS = {
    case["id"]: case["payload"]
    for case in json.loads(CORPUS_PATH.read_text(encoding="utf-8"))["positive"]
}
SENSITIVE = "SYNTHETIC_DO_NOT_DISCLOSE"


@pytest.mark.parametrize("exterior", [[], [{"x": 0, "y": 0}] * 3])
def test_fresh_json_rejects_short_exterior(exterior):
    payload = deepcopy(PAYLOADS["metric-land"])
    payload["boundary"]["exterior"] = exterior
    with pytest.raises(ValidationError, match="at least 4"):
        parse_contract_json(MetricLand, json.dumps(payload))


@pytest.mark.parametrize("fixture,model,field", [
    ("generation-input", GenerationInput, "land"),
    ("feasibility-needs-review", FeasibilityResult, "buildableFootprint"),
])
def test_nested_mutated_land_rejected(fixture, model, field):
    land = MetricLand.model_validate(PAYLOADS["metric-land"])
    land.boundary.exterior.pop()
    payload = {**PAYLOADS[fixture], field: land}
    with pytest.raises(ValidationError, match="explicitly closed"):
        model.model_validate(payload)


@pytest.mark.parametrize("create", ["copy", "construct"])
def test_prebuilt_boundary_empty_ring_rejected_cleanly(create):
    payload = PAYLOADS["metric-land"]
    land = MetricLand.model_validate(payload)
    boundary = land.boundary
    invalid = (
        boundary.model_copy(update={"exterior": []})
        if create == "copy" else type(boundary).model_construct(exterior=[], holes=[])
    )
    with pytest.raises(ValidationError, match="at least 4"):
        MetricLand.model_validate({**payload, "boundary": invalid})


def test_nested_point_mutation_is_revalidated():
    land = MetricLand.model_validate(PAYLOADS["metric-land"])
    land.boundary.exterior[1].x = float("inf")
    with pytest.raises(ValidationError, match="finite"):
        MetricLand.model_validate(land)


@pytest.mark.parametrize("before,after", [
    ('"seed": 42', '"seed": -1, "seed": 42'),
    ('"seed": 42', '"seed": 42, "seed": 42'),
    ('"seed": 42', r'"seed": -1, "\u0073eed": 42'),
    ('"origin": {"x": 0, "y": 0}', '"origin": {"x": 2, "x": 0, "y": 0}'),
    ('"maxWidthM": null', '"maxWidthM": null, "maxWidthM": null'),
])
def test_duplicate_members_rejected_before_contract_validation(before, after):
    source = json.dumps(PAYLOADS["generation-input"])
    assert before in source
    raw = source.replace(before, after, 1)
    with pytest.raises(ContractJsonError, match="Duplicate JSON property"):
        parse_contract_json(GenerationInput, raw)


@pytest.mark.parametrize("wire_type", [str, bytes, bytearray])
def test_strict_parser_preserves_valid_json(wire_type):
    text = json.dumps(PAYLOADS["generation-input"])
    payload = text if wire_type is str else wire_type(text, "utf-8")
    assert parse_contract_json(GenerationInput, payload) == GenerationInput.model_validate_json(payload)


def test_public_errors_omit_input_path_context_and_untrusted_exception_message():
    bad = {"code": "TEST", "severity": "WARNING", "message": f"{SENSITIVE}\ninvalid", SENSITIVE: True}
    with pytest.raises(ValidationError) as caught:
        Diagnostic.model_validate(bad)
    public = format_public_validation_errors(caught.value)
    assert {item["code"] for item in public} == {"CONTRACT_VALUE_INVALID", "PROPERTY_NOT_ALLOWED"}
    for error in (caught.value, RuntimeError(SENSITIVE), ContractJsonError(SENSITIVE)):
        body = format_public_validation_errors(error)
        assert all(set(item) == {"code", "message"} for item in body)
        assert SENSITIVE not in json.dumps(body)


def test_public_errors_are_bounded_and_suppress_custom_validator_context():
    with pytest.raises(ValidationError) as caught:
        Diagnostic.model_validate({
            "code": "TEST", "severity": "WARNING", "message": " ",
            "relatedEntityIds": [SENSITIVE] * 30,
        })
    body = format_public_validation_errors(caught.value)
    assert len(body) == 20
    assert SENSITIVE not in json.dumps(body)


def test_all_registered_codes_have_curated_diagnostics():
    diagnostics = [make_public_job_diagnostic(code) for code in DIAGNOSTIC_REGISTRY]
    assert len(diagnostics) == 25
    assert all(diagnostic.severity == "CRITICAL" and diagnostic.message for diagnostic in diagnostics)
    assert len({diagnostic.message for diagnostic in diagnostics}) == 25
    with pytest.raises(ValueError, match="Unknown job diagnostic code") as caught:
        make_public_job_diagnostic(SENSITIVE)
    assert SENSITIVE not in str(caught.value)


@pytest.mark.parametrize("mode", ["mutation", "copy", "construct"])
def test_invalid_job_cannot_be_published(mode):
    raw = PAYLOADS["job-queued"]
    queued = GenerationJob.model_validate(raw)
    if mode == "mutation":
        queued.state = "SUCCEEDED"
        invalid = queued
    elif mode == "copy":
        invalid = queued.model_copy(update={"state": "SUCCEEDED"})
    else:
        invalid = GenerationJob.model_construct(**{**raw, "state": "SUCCEEDED"})
    with pytest.raises(ValidationError):
        serialize_job_snapshot(invalid)


def test_allowed_job_updates_and_publication_remain_valid():
    queued = GenerationJob.model_validate(PAYLOADS["job-queued"])
    running = GenerationJob.model_validate(PAYLOADS["job-running"])
    terminal = GenerationJob.model_validate(PAYLOADS["job-succeeded"])
    assert validate_job_update(queued, running) == running
    assert parse_contract_json(GenerationJob, serialize_job_snapshot(terminal, previous=running)) == terminal


@pytest.mark.parametrize("previous,proposed", [
    ("job-queued", "job-succeeded"), ("job-running", "job-queued"),
    ("job-succeeded", "job-running"), ("job-succeeded", "job-succeeded"),
])
def test_prohibited_updates_rejected(previous, proposed):
    with pytest.raises(ValueError, match="unsupported generation job state transition"):
        serialize_job_snapshot(
            GenerationJob.model_validate(PAYLOADS[proposed]),
            previous=GenerationJob.model_validate(PAYLOADS[previous]),
        )


@pytest.mark.parametrize("changes", [
    {"jobId": "10000000-0000-4000-8000-000000000099"},
    {"landRevisionId": "10000000-0000-4000-8000-000000000099"},
    {"requirementsRevisionId": "10000000-0000-4000-8000-000000000099"},
    {"correlationId": "10000000-0000-4000-8000-000000000099"},
    {"createdAtUtc": "2026-11-19T06:30:00Z"},
])
def test_job_update_preserves_identity_and_history(changes):
    before = GenerationJob.model_validate(PAYLOADS["job-queued"])
    after = GenerationJob.model_validate({**PAYLOADS["job-running"], **changes})
    with pytest.raises(ValueError, match="identity, input snapshot and recorded history"):
        validate_job_update(before, after)


def test_job_publication_redacts_registered_and_unregistered_diagnostics():
    failed = deepcopy(PAYLOADS["job-failed"])
    failed["failure"]["diagnostics"][0].update(message=SENSITIVE, sourceReferenceIds=[SENSITIVE])
    failed_wire = serialize_job_snapshot(GenerationJob.model_validate(failed))
    assert SENSITIVE not in failed_wire
    assert json.loads(failed_wire)["failure"]["diagnostics"][0]["message"] == (
        make_public_job_diagnostic("SEARCH_EXHAUSTED").message
    )

    success = deepcopy(PAYLOADS["job-succeeded"])
    warning = {"code": SENSITIVE, "severity": "WARNING", "message": SENSITIVE, "path": f"/{SENSITIVE}"}
    success["result"]["diagnostics"] = [warning]
    success["result"]["alternatives"][0]["validation"]["diagnostics"] = [warning]
    success_wire = serialize_job_snapshot(GenerationJob.model_validate(success))
    assert SENSITIVE not in success_wire
    result = parse_contract_json(GenerationJob, success_wire).result
    assert result.diagnostics[0].code == "UNCLASSIFIED_DIAGNOSTIC"
    assert result.alternatives[0].validation.diagnostics[0].severity == "WARNING"


@pytest.mark.parametrize("field,value", [
    ("projectId", "10000000-0000-4000-8000-000000000099"),
    ("inputFingerprint", "c" * 64), ("seed", 19),
])
def test_update_rejects_coordinated_snapshot_changes(field, value):
    before = GenerationJob.model_validate(PAYLOADS["job-queued"])
    proposed = deepcopy(PAYLOADS["job-running"])
    proposed[field] = proposed["idempotency"][field] = value
    with pytest.raises(ValueError, match="identity, input snapshot and recorded history"):
        validate_job_update(before, GenerationJob.model_validate(proposed))


def test_terminal_update_cannot_rewrite_start_time():
    before = GenerationJob.model_validate(PAYLOADS["job-running"])
    proposed = {**PAYLOADS["job-failed"], "startedAtUtc": "2026-11-19T06:31:01Z"}
    with pytest.raises(ValueError, match="identity, input snapshot and recorded history"):
        validate_job_update(before, GenerationJob.model_validate(proposed))


def test_job_publication_revalidates_nested_result_and_metrics():
    job = GenerationJob.model_validate(PAYLOADS["job-succeeded"])
    job.result.alternatives[0].metrics.floorAreaM2 = float("inf")
    with pytest.raises(ValidationError, match="finite"):
        serialize_job_snapshot(job)

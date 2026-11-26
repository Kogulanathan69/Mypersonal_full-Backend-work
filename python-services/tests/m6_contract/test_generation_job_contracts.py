from itertools import product
from typing import get_args

import pytest
from pydantic import ValidationError

from app.plans.generation.job_contracts import GenerationJob, JobState, validate_job_transition
from test_generation_response_contracts import synthetic_id, synthetic_success

REVISIONS = {
    "projectId": synthetic_id(1),
    "landRevisionId": synthetic_id(2),
    "requirementsRevisionId": synthetic_id(3),
}
CREATED = "2026-11-19T12:00:00+05:30"
STARTED = "2026-11-19T06:31:00Z"
COMPLETED = "2026-11-19T06:32:00Z"
AREA_SHORTFALL = "BUILDABLE_AREA_INSUFFICIENT"
TRANSITIONS = {
    ("QUEUED", "RUNNING"),
    ("RUNNING", "SUCCEEDED"),
    ("RUNNING", "INFEASIBLE"),
    ("RUNNING", "FAILED"),
}


def failure_details(code):
    return {
        "reasonCode": code,
        "diagnostics": [{"code": code, "severity": "CRITICAL", "message": "Synthetic job diagnostic."}],
    }


@pytest.fixture
def queued():
    return {
        **REVISIONS,
        "schemaVersion": "m6-generation-job/0.1-draft",
        "jobId": synthetic_id(4),
        "correlationId": synthetic_id(5),
        "seed": 42,
        "state": "QUEUED",
        "idempotency": {
            "key": "synthetic-request-v1",
            "projectId": REVISIONS["projectId"],
            "operation": "GENERATE_PLAN",
            "inputFingerprint": "a" * 64,
            "seed": 42,
        },
        "inputFingerprint": "a" * 64,
        "createdAtUtc": CREATED,
    }


@pytest.fixture
def succeeded(queued, synthetic_success):
    return {
        **queued,
        **{field: synthetic_success[field] for field in REVISIONS},
        "state": "SUCCEEDED",
        "idempotency": {**queued["idempotency"], "projectId": synthetic_success["projectId"]},
        "startedAtUtc": STARTED,
        "completedAtUtc": COMPLETED,
        "result": synthetic_success,
    }


@pytest.mark.parametrize("state", get_args(JobState))
def test_each_state_round_trips(state, queued, succeeded):
    data = {**queued, "state": state}
    if state != "QUEUED":
        data["startedAtUtc"] = STARTED
    if state == "SUCCEEDED":
        data = succeeded
    elif state in ("FAILED", "INFEASIBLE"):
        code = AREA_SHORTFALL if state == "INFEASIBLE" else "SEARCH_EXHAUSTED"
        data.update(completedAtUtc=COMPLETED, failure=failure_details(code))

    job = GenerationJob.model_validate(data)
    assert job.state == state
    assert job.model_dump(mode="json")["createdAtUtc"] == "2026-11-19T06:30:00Z"
    assert GenerationJob.model_validate_json(job.model_dump_json()) == job
    assert GenerationJob.model_validate(job.model_dump(mode="json")) == job


@pytest.mark.parametrize("current,target", list(product(get_args(JobState), repeat=2)))
def test_transition_matrix(current, target):
    if (current, target) in TRANSITIONS:
        assert validate_job_transition(current, target) is None
    else:
        with pytest.raises(ValueError, match="unsupported generation job state transition"):
            validate_job_transition(current, target)


@pytest.mark.parametrize("current,target", [
    ("UNKNOWN", "RUNNING"), ("QUEUED", "SUCCESS"), ("RUNNING", None), ([], "RUNNING"),
])
def test_unknown_transition_states(current, target):
    with pytest.raises(ValueError, match="unsupported generation job state transition"):
        validate_job_transition(current, target)


@pytest.mark.parametrize("field", [
    "schemaVersion", "jobId", "projectId", "requirementsRevisionId", "correlationId",
    "idempotency", "inputFingerprint", "createdAtUtc", "seed", "state",
])
def test_required_job_fields(queued, field):
    del queued[field]
    with pytest.raises(ValidationError, match="Field required"):
        GenerationJob.model_validate(queued)


@pytest.mark.parametrize("changes", [
    {"schemaVersion": "0.1-draft"}, {"state": "SUCCESS"}, {"state": "queued"},
    {"state": "RUNNING"}, {"jobId": "bad"}, {"jobId": synthetic_id(0)},
    {"projectId": synthetic_id(0)}, {"landRevisionId": synthetic_id(2).replace("-", "")},
    {"requirementsRevisionId": 3}, {"correlationId": synthetic_id(0)},
    {"inputFingerprint": "A" * 64}, {"inputFingerprint": "a" * 63},
    {"seed": True}, {"seed": "42"}, {"seed": -1}, {"seed": 2**31}, {"extraField": True},
    {"createdAtUtc": 0}, {"createdAtUtc": "2026-11-19T06:30:00"},
    {"createdAtUtc": "2026-11-19T06:30:00+00:60"}, {"startedAtUtc": STARTED},
    {"completedAtUtc": COMPLETED}, {"failure": failure_details("GENERATION_FAILED")},
    {"state": "RUNNING", "startedAtUtc": "2026-11-19T12:29:00+06:00"},
    {"state": "RUNNING", "startedAtUtc": STARTED, "completedAtUtc": COMPLETED},
    {"state": "RUNNING", "startedAtUtc": STARTED, "failure": failure_details("GENERATION_FAILED")},
])
def test_invalid_active_job(queued, changes):
    with pytest.raises(ValidationError):
        GenerationJob.model_validate({**queued, **changes})


@pytest.mark.parametrize("changes", [
    {"key": ""}, {"key": " "}, {"key": "key\n"}, {"key": "k" * 129}, {"key": 123},
    {"projectId": synthetic_id(0)}, {"projectId": synthetic_id(6)},
    {"operation": "VALIDATE_PLAN"}, {"inputFingerprint": "b" * 64},
    {"inputFingerprint": "g" * 64}, {"seed": 43}, {"seed": False}, {"unrecognized": True},
])
def test_invalid_idempotency_metadata(queued, changes):
    queued["idempotency"].update(changes)
    with pytest.raises(ValidationError):
        GenerationJob.model_validate(queued)


def test_key_seed_timestamp_bounds(queued):
    queued["idempotency"].update(key="k" * 128, seed=2**31 - 1)
    queued.update(
        seed=2**31 - 1, state="FAILED", startedAtUtc=CREATED, completedAtUtc=CREATED,
        failure=failure_details("GENERATION_TIMEOUT"),
    )
    result = GenerationJob.model_validate(queued)
    assert result.createdAtUtc == result.startedAtUtc == result.completedAtUtc


@pytest.mark.parametrize("count", [0, 1, 2, 4])
def test_success_requires_three_alternatives(succeeded, count):
    result = succeeded["result"]
    result["alternatives"] = (result["alternatives"] * 2)[:count]
    with pytest.raises(ValidationError):
        GenerationJob.model_validate(succeeded)


@pytest.mark.parametrize("changes", [
    {"startedAtUtc": None}, {"completedAtUtc": None}, {"completedAtUtc": CREATED},
    {"completedAtUtc": "2026-11-19T06:32:00"}, {"result": None},
    {"landRevisionId": synthetic_id(77)}, {"requirementsRevisionId": synthetic_id(77)},
    {"failure": failure_details("GENERATION_FAILED")},
])
def test_invalid_success(succeeded, changes):
    with pytest.raises(ValidationError):
        GenerationJob.model_validate({**succeeded, **changes})


def test_result_project_binding(succeeded):
    succeeded["projectId"] = succeeded["idempotency"]["projectId"] = synthetic_id(999)
    with pytest.raises(ValidationError, match="must match job revisions and seed"):
        GenerationJob.model_validate(succeeded)


def test_result_seed_binding(succeeded):
    succeeded["result"]["seed"] = 0
    with pytest.raises(ValidationError, match="must match job revisions and seed"):
        GenerationJob.model_validate(succeeded)


def test_success_forbids_blocking_job_result(succeeded):
    succeeded["result"]["diagnostics"] = failure_details("VALIDATION_UNAVAILABLE")["diagnostics"]
    with pytest.raises(ValidationError):
        GenerationJob.model_validate(succeeded)


@pytest.mark.parametrize("state", ["QUEUED", "RUNNING", "FAILED", "INFEASIBLE"])
def test_other_states_forbid_success_result(succeeded, state):
    succeeded["state"] = state
    if state in ("QUEUED", "RUNNING"):
        succeeded["completedAtUtc"] = None
        if state == "QUEUED":
            succeeded["startedAtUtc"] = None
    else:
        succeeded["failure"] = failure_details(AREA_SHORTFALL if state == "INFEASIBLE" else "GENERATION_FAILED")
    with pytest.raises(ValidationError):
        GenerationJob.model_validate(succeeded)


def test_success_cannot_wrap_an_unavailable_compute_result(succeeded):
    succeeded["result"].update(
        outcome="UNAVAILABLE", reasonCode="GENERATION_UNAVAILABLE", alternatives=[],
        diagnostics=failure_details("GENERATION_UNAVAILABLE")["diagnostics"],
    )
    with pytest.raises(ValidationError, match="succeeded job requires a successful result"):
        GenerationJob.model_validate(succeeded)


@pytest.mark.parametrize("code", [
    "SEARCH_EXHAUSTED", "GENERATION_TIMEOUT", "INSUFFICIENT_DISTINCT_ALTERNATIVES",
    "SETBACK_EVIDENCE_MISSING", "REGULATORY_EVIDENCE_CONFLICT", "REQUIRED_AREA_POLICY_UNAVAILABLE",
])
@pytest.mark.parametrize("also_has_area_diagnostic", [False, True])
def test_infeasible_rejects_inconclusive_results(queued, code, also_has_area_diagnostic):
    failure = failure_details(code)
    if also_has_area_diagnostic:
        failure["reasonCode"] = AREA_SHORTFALL
        failure["diagnostics"] += failure_details(AREA_SHORTFALL)["diagnostics"]
    queued.update(state="INFEASIBLE", startedAtUtc=STARTED, completedAtUtc=COMPLETED, failure=failure)
    with pytest.raises(ValidationError, match="requires an established buildable area shortfall"):
        GenerationJob.model_validate(queued)


@pytest.mark.parametrize("changes", [
    {"failure": None}, {"failure": {"diagnostics": failure_details("GENERATION_FAILED")["diagnostics"]}},
    {"failure": failure_details(AREA_SHORTFALL)}, {"startedAtUtc": None}, {"completedAtUtc": None},
])
def test_invalid_failed_job(queued, changes):
    queued.update(
        state="FAILED", startedAtUtc=STARTED, completedAtUtc=COMPLETED,
        failure=failure_details("GENERATION_FAILED"),
    )
    with pytest.raises(ValidationError):
        GenerationJob.model_validate({**queued, **changes})

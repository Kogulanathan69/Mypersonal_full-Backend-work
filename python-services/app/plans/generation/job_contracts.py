"""D01-D provisional job snapshots and transition rules.

Job states describe the lifecycle of an operation. Internal generation
outcomes describe a compute attempt; callers need an explicit classification
before recording a terminal job state, especially INFEASIBLE.
"""

from types import MappingProxyType
from typing import Annotated, Literal

from pydantic import Field, model_validator

from app.geometry.footprint.contracts import (
    Diagnostic,
    DiagnosticCode,
    NonNilUUID,
    ResponseModel,
    RevisionBinding,
)
from app.plans.generation.contracts import GenerationResult, Seed
from app.validation.contracts import Sha256, UtcTimestamp

JobState = Literal["QUEUED", "RUNNING", "SUCCEEDED", "INFEASIBLE", "FAILED"]
_ALLOWED_TRANSITIONS = frozenset({
    ("QUEUED", "RUNNING"),
    ("RUNNING", "SUCCEEDED"),
    ("RUNNING", "INFEASIBLE"),
    ("RUNNING", "FAILED"),
})

# Each registered code blocks the affected operation when reported as a job
# failure. D01-C diagnostics retain their existing open code and severity model.
# Job-specific additions distinguish an area shortfall, a timeout and generic
# execution failure. Public HTTP mappings remain open.
_PUBLIC_JOB_MESSAGES = {
    "INVALID_IDENTIFIER": "An identifier is invalid.",
    "SCHEMA_VERSION_UNSUPPORTED": "The contract schema version is unsupported.",
    "METRIC_UNITS_REQUIRED": "Metric units are required.",
    "COORDINATE_SYSTEM_UNSUPPORTED": "The coordinate system is unsupported.",
    "INPUT_REVISION_MISMATCH": "The input revisions do not match.",
    "POLYGON_INVALID": "A polygon failed structural validation.",
    "ROOM_OVERLAP": "Rooms overlap.",
    "OUTSIDE_BUILDABLE_FOOTPRINT": "Geometry extends beyond the buildable footprint.",
    "WALL_TOPOLOGY_INVALID": "Wall topology is invalid.",
    "OPENING_WALL_REFERENCE_INVALID": "An opening references an invalid wall.",
    "OPENING_OUTSIDE_WALL": "An opening extends beyond its wall.",
    "OPENING_COLLISION": "Openings collide.",
    "ROOM_INACCESSIBLE": "A room has no supported access.",
    "ROOM_REQUIREMENT_UNMET": "A room requirement is unmet.",
    "SETBACK_EVIDENCE_MISSING": "Setback evidence is missing.",
    "REGULATORY_EVIDENCE_CONFLICT": "Regulatory evidence conflicts.",
    "REQUIRED_AREA_POLICY_UNAVAILABLE": "The required-area policy is unavailable.",
    "VALIDATION_EVIDENCE_STALE": "Validation evidence is stale.",
    "VALIDATION_UNAVAILABLE": "Validation is unavailable.",
    "GENERATION_UNAVAILABLE": "Plan generation is unavailable.",
    "SEARCH_EXHAUSTED": "The generation search ended without a result.",
    "INSUFFICIENT_DISTINCT_ALTERNATIVES": "The search produced too few distinct alternatives.",
    "BUILDABLE_AREA_INSUFFICIENT": "The available buildable area is insufficient.",
    "GENERATION_TIMEOUT": "Plan generation timed out.",
    "GENERATION_FAILED": "Plan generation failed."
}
DIAGNOSTIC_REGISTRY = MappingProxyType(dict.fromkeys(_PUBLIC_JOB_MESSAGES, "CRITICAL"))

# This is the sole supported proof category for the provisional job contract.
# The upstream feasibility computation must establish the area shortfall.
_INFEASIBILITY_REASON = "BUILDABLE_AREA_INSUFFICIENT"

IdempotencyKey = Annotated[str, Field(min_length=1, max_length=128, pattern=r"^[A-Za-z0-9._~-]+$")]


def validate_job_transition(current: JobState, target: JobState) -> None:
    """Validate one state change. Replays read the existing job snapshot."""
    if not isinstance(current, str) or not isinstance(target, str):
        raise ValueError("unsupported generation job state transition")
    if (current, target) not in _ALLOWED_TRANSITIONS:
        raise ValueError("unsupported generation job state transition")


class RegisteredDiagnostic(Diagnostic):
    """Opt-in registry validation for diagnostics attached to job failures."""

    @model_validator(mode="after")
    def require_registered_classification(self) -> "RegisteredDiagnostic":
        expected = DIAGNOSTIC_REGISTRY.get(self.code)
        if expected is None or self.severity != expected:
            raise ValueError("job diagnostic code and severity must match the registry")
        return self


class JobFailure(ResponseModel):
    reasonCode: DiagnosticCode
    diagnostics: list[RegisteredDiagnostic] = Field(min_length=1, max_length=1_000)

    @model_validator(mode="after")
    def require_reason_diagnostic(self) -> "JobFailure":
        if not any(item.code == self.reasonCode for item in self.diagnostics):
            raise ValueError("job failure reason requires a matching registered diagnostic")
        return self


class JobIdempotency(ResponseModel):
    """Replay metadata scoped to a project and operation.

    The future job service returns the existing job for the same key and
    effective request within that scope, and reports a conflict for a changed
    request. Different projects have independent key spaces. Fingerprinting,
    authorization, atomic replay and concurrency are D05 responsibilities.
    """

    key: IdempotencyKey
    projectId: NonNilUUID
    operation: Literal["GENERATE_PLAN"]
    inputFingerprint: Sha256
    seed: Seed


class GenerationJob(RevisionBinding):
    """Local job snapshot with an inline success result and separate failure."""

    schemaVersion: Literal["m6-generation-job/0.1-draft"]
    jobId: NonNilUUID
    correlationId: NonNilUUID
    seed: Seed
    state: JobState
    idempotency: JobIdempotency
    inputFingerprint: Sha256
    createdAtUtc: UtcTimestamp
    startedAtUtc: UtcTimestamp | None = None
    completedAtUtc: UtcTimestamp | None = None
    result: GenerationResult | None = None
    failure: JobFailure | None = None

    @model_validator(mode="after")
    def require_consistent_job(self) -> "GenerationJob":
        metadata = self.idempotency
        if (metadata.projectId, metadata.inputFingerprint, metadata.seed) != (
            self.projectId, self.inputFingerprint, self.seed
        ):
            raise ValueError("job idempotency metadata must match project, fingerprint and seed")

        started = self.startedAtUtc
        completed = self.completedAtUtc
        if self.state == "QUEUED":
            if started is not None:
                raise ValueError("queued job cannot have a start timestamp")
        elif started is None or started < self.createdAtUtc:
            raise ValueError("started job requires a start timestamp at or after creation")

        if self.state in ("QUEUED", "RUNNING"):
            if completed is not None or self.result is not None or self.failure is not None:
                raise ValueError("active job cannot contain terminal data")
            return self

        if completed is None or started is None or completed < started:
            raise ValueError("terminal job requires completion at or after start")

        if self.state == "SUCCEEDED":
            result = self.result
            if result is None or result.outcome != "SUCCESS" or self.failure is not None:
                raise ValueError("succeeded job requires a successful result and no failure")
            if result.revision_key() != self.revision_key() or result.seed != self.seed:
                raise ValueError("successful result must match job revisions and seed")
            return self

        failure = self.failure
        if self.result is not None or failure is None:
            raise ValueError("unsuccessful terminal job requires failure details and no result")
        if self.state == "INFEASIBLE":
            if any(item.code != _INFEASIBILITY_REASON for item in failure.diagnostics):
                raise ValueError("infeasible job requires an established buildable area shortfall")
        elif failure.reasonCode == _INFEASIBILITY_REASON:
            raise ValueError("a buildable area shortfall belongs to the infeasible state")
        return self


def make_public_job_diagnostic(code: str) -> RegisteredDiagnostic:
    """Build a job-failure diagnostic from the curated registry."""
    if code not in DIAGNOSTIC_REGISTRY:
        raise ValueError("Unknown job diagnostic code.")
    return RegisteredDiagnostic(
        code=code, severity="CRITICAL", message=_PUBLIC_JOB_MESSAGES[code],
    )


def validate_job_update(previous: GenerationJob, proposed: GenerationJob) -> GenerationJob:
    """Validate a proposed replacement against a caller-supplied trusted snapshot."""
    before = GenerationJob.model_validate(previous)
    after = GenerationJob.model_validate(proposed)
    validate_job_transition(before.state, after.state)
    if (
        before.jobId != after.jobId
        or before.revision_key() != after.revision_key()
        or before.correlationId != after.correlationId
        or before.seed != after.seed
        or before.inputFingerprint != after.inputFingerprint
        or before.idempotency != after.idempotency
        or before.createdAtUtc != after.createdAtUtc
        or (before.startedAtUtc is not None and before.startedAtUtc != after.startedAtUtc)
    ):
        raise ValueError("Job identity, input snapshot and recorded history must be preserved.")
    return after


def _public_diagnostic(diagnostic: Diagnostic) -> Diagnostic:
    if diagnostic.code in DIAGNOSTIC_REGISTRY:
        return Diagnostic(
            code=diagnostic.code, severity=diagnostic.severity,
            message=_PUBLIC_JOB_MESSAGES[diagnostic.code],
        )
    return Diagnostic(
        code="UNCLASSIFIED_DIAGNOSTIC", severity=diagnostic.severity,
        message="A contract check reported an issue.",
    )


def serialize_job_snapshot(
    snapshot: GenerationJob, *, previous: GenerationJob | None = None
) -> str:
    """Publish a validated snapshot with curated diagnostics.

    Supply the trusted previous snapshot when publishing an update. A single
    snapshot can establish structural consistency only. Future persistence
    must obtain the previous snapshot and apply the update atomically.
    """
    job = (
        GenerationJob.model_validate(snapshot)
        if previous is None else validate_job_update(previous, snapshot)
    )
    payload = job.model_dump()
    if job.failure is not None:
        payload["failure"]["diagnostics"] = [
            make_public_job_diagnostic(item.code).model_dump() for item in job.failure.diagnostics
        ]
    if job.result is not None:
        result = payload["result"]
        result["diagnostics"] = [
            _public_diagnostic(item).model_dump() for item in job.result.diagnostics
        ]
        for index, alternative in enumerate(job.result.alternatives):
            result["alternatives"][index]["validation"]["diagnostics"] = [
                _public_diagnostic(item).model_dump() for item in alternative.validation.diagnostics
            ]
    return GenerationJob.model_validate(payload).model_dump_json()

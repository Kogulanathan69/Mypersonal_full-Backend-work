"""Versioned, human-reviewed regulatory constraint provider (M7; constraints contract v0, conditionally approved by M6
on 2026-11-19: seven kinds, units, revision binding, C# orchestration direction).

Principles (doc 29):
- Constraints come ONLY from a versioned, human-reviewed, integrity-sealed table loaded at start-up. Retrieval results
  are never converted into constraints: this module has no dependency on the search path.
- Only HUMAN_REVIEWED constraints can yield VERIFIED, and every reviewed (or rejected) constraint must be signed off
  by a reviewer who is AUTHORIZED in a separate, sealed reviewer registry: present, not revoked, holding the stated
  role, scoped to the constraint's jurisdiction and kind, and within the authorization period on the review date.
  UNREVIEWED ones yield UNVERIFIED; REJECTED ones are never returned; absence yields MISSING (never "no limit").
- CONFLICTING: two reviewed constraints whose applicability OVERLAPS (building use and every condition interval
  intersect) state different requirements. No precedence or specificity rule is assumed: a conditional rule never
  silently overrides a general one, and jurisdictions are never merged.
- Every citation must reconstruct exactly from the hash-verified corpus, otherwise the table is refused.
- Synthetic tables load only when explicitly allowed (isolated development/testing); real tables require version and
  digest pins and may not use the synthetic reviewer/reference namespaces.
- Evidence packs are bound to exact project, land and requirement revisions and identified by
  SHA-256 over BuildMind canonical JSON v1 (app/canonical_json.py), recomputed independently by the C# client.
"""
import math
from datetime import date
from typing import Annotated, Any, Callable, Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, StrictInt, StringConstraints, model_validator

from . import canonical_json

CONSTRAINTS_CONTRACT_VERSION = "constraints-0"
DIGEST_ALGORITHM = canonical_json.ALGORITHM

Kind = Literal[
    "REAR_SPACE_MIN", "SIDE_SPACE_MIN", "FRONT_SETBACK_MIN", "BUILDING_HEIGHT_MAX",
    "PLOT_COVERAGE_MAX", "FLOOR_AREA_RATIO_MAX", "PARKING_SPACES_MIN",
]
Unit = Literal["m", "percent", "ratio", "count", "m2"]
# kind -> (required comparator, required unit, (minimum, maximum) plausible value range). Shared with the C# client.
KIND_RULES: dict[str, tuple[str, str, tuple[float, float]]] = {
    "REAR_SPACE_MIN": (">=", "m", (0, 100)),
    "SIDE_SPACE_MIN": (">=", "m", (0, 100)),
    "FRONT_SETBACK_MIN": (">=", "m", (0, 100)),
    "BUILDING_HEIGHT_MAX": ("<=", "m", (0, 1000)),
    "PLOT_COVERAGE_MAX": ("<=", "percent", (0, 100)),
    "FLOOR_AREA_RATIO_MAX": ("<=", "ratio", (0, 100)),
    "PARKING_SPACES_MIN": (">=", "count", (0, 1000)),
}
# condition field -> (unit, (minimum, maximum)). Shared with the C# client.
CONDITION_RULES: dict[str, tuple[str, tuple[float, float]]] = {
    "building_height_m": ("m", (0, 1000)),
    "plot_area_m2": ("m2", (0, 1_000_000)),
    "floors": ("count", (0, 200)),
    "road_width_m": ("m", (0, 200)),
}
MAX_PROJECT_REVISION = 999_999_999_999_999
SYNTHETIC_REVIEWER_PREFIX = "synthetic:"
SYNTHETIC_VERSION_PREFIX = "synthetic-"
SYNTHETIC_REFERENCE_PREFIX = "SYN-"

DocumentId = Annotated[str, StringConstraints(pattern=r"^[a-f0-9]{64}$")]
ConstraintId = Annotated[str, StringConstraints(pattern=r"^[a-z0-9][a-z0-9._-]{2,63}$")]
ReviewerId = Annotated[str, StringConstraints(pattern=r"^(synthetic:)?[a-z0-9][a-z0-9._-]{2,63}$")]  # pseudonymous, never a name
ReviewReference = Annotated[str, StringConstraints(pattern=r"^[A-Z][A-Z0-9]{1,9}-[0-9]{4}-[0-9]{3,6}$")]
Finite = Annotated[float, Field(allow_inf_nan=False)]


class _Strict(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)


class Condition(_Strict):
    field: Literal["building_height_m", "plot_area_m2", "floors", "road_width_m"]
    comparator: Literal["<", "<=", ">", ">=", "=="]
    value: Finite
    unit: Unit

    @model_validator(mode="after")
    def _unit_and_range(self):
        unit, (low, high) = CONDITION_RULES[self.field]
        if self.unit != unit:
            raise ValueError("condition unit does not match its field")
        if not low <= self.value <= high:
            raise ValueError("condition value outside the plausible range")
        return self


class Source(_Strict):
    document_id: DocumentId
    page: StrictInt = Field(ge=1)
    start_char: StrictInt = Field(ge=0)
    end_char: StrictInt = Field(ge=1)
    excerpt: str = Field(min_length=1, max_length=4000)

    @model_validator(mode="after")
    def _ordered(self):
        if self.end_char <= self.start_char:
            raise ValueError("end_char must follow start_char")
        return self


class Constraint(_Strict):
    constraint_id: ConstraintId
    jurisdiction: str = Field(min_length=1, max_length=200)
    kind: Kind
    comparator: Literal[">=", "<="]
    value: Finite
    unit: Unit
    applies_to: Literal["residential", "any"]
    conditions: list[Condition] = Field(default_factory=list, max_length=5)
    review_status: Literal["HUMAN_REVIEWED", "UNREVIEWED", "REJECTED"]
    reviewer_role: str | None = Field(default=None, min_length=1, max_length=100)
    reviewer_id: ReviewerId | None = None
    review_reference: ReviewReference | None = None
    reviewed_at: date | None = None
    source: Source

    @model_validator(mode="after")
    def _consistent(self):
        comparator, unit, (low, high) = KIND_RULES[self.kind]
        if self.comparator != comparator or self.unit != unit:
            raise ValueError("comparator or unit does not match the constraint kind")
        if not low <= self.value <= high:
            raise ValueError("value outside the plausible range for the kind")
        review = (self.reviewer_role, self.reviewer_id, self.review_reference, self.reviewed_at)
        if self.review_status == "UNREVIEWED":
            if any(v is not None for v in review):
                raise ValueError("an unreviewed constraint must not carry review details")
        elif any(v is None for v in review):
            raise ValueError("a reviewed or rejected constraint needs reviewer_role, reviewer_id, review_reference and reviewed_at")
        elif self.reviewed_at > date.today():
            raise ValueError("reviewed_at is in the future")
        if _region(self.conditions) is None:
            raise ValueError("conditions are unsatisfiable")
        return self


class Integrity(_Strict):
    algorithm: Literal["SHA-256/BM-JCS-1"]
    sha256: Annotated[str, StringConstraints(pattern=r"^[a-f0-9]{64}$")]


class ConstraintTable(_Strict):
    contract_version: Literal["constraints-0"]
    table_version: str = Field(pattern=r"^[a-z0-9][a-z0-9._-]{0,63}$")
    synthetic: bool
    integrity: Integrity
    constraints: list[Constraint] = Field(max_length=10000)


class Reviewer(_Strict):
    """An authorized reviewer (pseudonymous id only; never a personal name or contact detail)."""

    reviewer_id: ReviewerId
    role: str = Field(min_length=1, max_length=100)
    status: Literal["ACTIVE", "REVOKED"]
    authorized_from: date
    authorized_until: date | None = None
    jurisdictions: list[Annotated[str, StringConstraints(min_length=1, max_length=200)]] = Field(min_length=1, max_length=200)
    kinds: list[Kind] = Field(min_length=1, max_length=len(KIND_RULES))

    @model_validator(mode="after")
    def _period(self):
        if self.authorized_until is not None and self.authorized_until < self.authorized_from:
            raise ValueError("authorized_until precedes authorized_from")
        return self


class ReviewerRegistry(_Strict):
    registry_version: str = Field(pattern=r"^[a-z0-9][a-z0-9._-]{0,63}$")
    synthetic: bool
    integrity: Integrity
    reviewers: list[Reviewer] = Field(min_length=1, max_length=1000)


def authorization_failure(reviewer: "Reviewer | None", c: "Constraint") -> str | None:
    """Why the reviewer was not authorized to sign off this constraint, or None when authorized.

    A REVOKED reviewer invalidates all of their sign-offs (conservative: affected constraints must be re-reviewed).
    """
    if reviewer is None:
        return "reviewer is not in the reviewer registry"
    if reviewer.status != "ACTIVE":
        return "reviewer authorization is revoked"
    if reviewer.role != c.reviewer_role:
        return "reviewer_role does not match the registered role"
    if c.jurisdiction not in reviewer.jurisdictions:
        return "reviewer is not authorized for the jurisdiction"
    if c.kind not in reviewer.kinds:
        return "reviewer is not authorized for the constraint kind"
    if c.reviewed_at < reviewer.authorized_from or (reviewer.authorized_until is not None and c.reviewed_at > reviewer.authorized_until):
        return "review date is outside the reviewer's authorization period"
    return None


class Binding(_Strict):
    """Exact revisions the evidence pack is bound to (supplied by the C# orchestration, never inferred)."""

    project_id: UUID
    # Int64 like Project.Revision, bounded below 1e15 so BM-JCS-1 represents it exactly in Python and C#.
    project_revision: StrictInt = Field(ge=1, le=MAX_PROJECT_REVISION)
    land_revision_id: UUID
    requirement_revision_id: UUID


class ConstraintsRequest(_Strict):
    jurisdiction: str = Field(min_length=1, max_length=200)
    kinds: list[Kind] = Field(min_length=1, max_length=len(KIND_RULES))
    building_use: Literal["residential"] | None = None
    binding: Binding

    @model_validator(mode="after")
    def _unique_kinds(self):
        if len(set(self.kinds)) != len(self.kinds):
            raise ValueError("kinds must be unique")
        return self


# ---- applicability intervals -------------------------------------------------------------------------------------
# An interval is (low, low_inclusive, high, high_inclusive). A region maps condition field -> interval; an absent
# field is unbounded. None means the conditions can never hold together.

def _interval(c: Condition) -> tuple[float, bool, float, bool]:
    v = c.value
    return {
        ">=": (v, True, math.inf, False), ">": (v, False, math.inf, False),
        "<=": (-math.inf, False, v, True), "<": (-math.inf, False, v, False),
        "==": (v, True, v, True),
    }[c.comparator]


def _intersect(a, b):
    low, low_in = max((a[0], a[1]), (b[0], b[1]), key=lambda t: (t[0], not t[1]))
    high, high_in = min((a[2], a[3]), (b[2], b[3]), key=lambda t: (t[0], t[1]))
    if low > high or (low == high and not (low_in and high_in)):
        return None
    return (low, low_in, high, high_in)


def _region(conditions) -> dict[str, tuple] | None:
    region: dict[str, tuple] = {}
    for c in conditions:
        current = region.get(c.field, (-math.inf, False, math.inf, False))
        merged = _intersect(current, _interval(c))
        if merged is None:
            return None
        region[c.field] = merged
    return region


def _overlap(a: "Constraint", b: "Constraint") -> bool:
    """True when some building could be subject to both constraints (no precedence or specificity is assumed)."""
    if "residential" not in (a.applies_to, b.applies_to) and a.applies_to != b.applies_to:
        return False  # only reachable if new uses are added; residential and any always overlap
    ra, rb = _region(a.conditions), _region(b.conditions)
    unbounded = (-math.inf, False, math.inf, False)
    return all(_intersect(ra.get(f, unbounded), rb.get(f, unbounded)) is not None for f in set(ra) | set(rb))


def _signature(c: Constraint) -> tuple:
    return (c.comparator, c.value, c.unit)


def table_digest(table: dict[str, Any]) -> str:
    """Canonical digest of a table (or registry) document WITHOUT its integrity block (what is sealed)."""
    return canonical_json.digest({k: v for k, v in table.items() if k != "integrity"})


def seal(document: dict[str, Any]) -> dict[str, Any]:
    """Returns the document with its integrity block set (tooling for reviewed tables and test fixtures)."""
    body = {k: v for k, v in document.items() if k != "integrity"}
    return body | {"integrity": {"algorithm": DIGEST_ALGORITHM, "sha256": canonical_json.digest(body)}}


class ConstraintProvider:
    def __init__(
        self,
        table: dict[str, Any],
        page_text: Callable[[str, int], str | None],
        reviewers: dict[str, Any],
        *,
        allow_synthetic: bool = False,
        expected_version: str | None = None,
        expected_sha256: str | None = None,
        expected_reviewers_sha256: str | None = None,
    ):
        self.registry = ReviewerRegistry.model_validate(reviewers)
        registry_sealed = table_digest(reviewers)
        if registry_sealed != self.registry.integrity.sha256:
            raise ValueError("reviewer registry integrity digest does not match its content")
        if expected_reviewers_sha256 is not None and registry_sealed != expected_reviewers_sha256:
            raise ValueError("reviewer registry digest does not match the pinned digest")
        registered = [r.reviewer_id for r in self.registry.reviewers]
        if len(set(registered)) != len(registered):
            raise ValueError("duplicate reviewer_id in reviewer registry")
        if any(r.startswith(SYNTHETIC_REVIEWER_PREFIX) != self.registry.synthetic for r in registered):
            raise ValueError("reviewer ids must be synthetic exactly when the registry is synthetic")
        if self.registry.registry_version.startswith(SYNTHETIC_VERSION_PREFIX) != self.registry.synthetic:
            # Parity with the C# receipt policy (M6 D02 C11): synthetic registries use synthetic- versions, real never.
            raise ValueError("reviewer registry version must start with synthetic- exactly when the registry is synthetic")
        self.table = ConstraintTable.model_validate(table)
        sealed = table_digest(table)
        if sealed != self.table.integrity.sha256:
            raise ValueError("constraint table integrity digest does not match its content")
        t = self.table
        if t.synthetic:
            if not allow_synthetic:
                raise ValueError("synthetic constraint tables are allowed only in isolated development/testing")
            if not t.table_version.startswith(SYNTHETIC_VERSION_PREFIX):
                raise ValueError("synthetic tables must use a synthetic- version")
        else:
            if expected_version is None or expected_sha256 is None or expected_reviewers_sha256 is None:
                raise ValueError("a real constraint table requires version, table digest and reviewer registry digest pins")
            if self.registry.synthetic:
                raise ValueError("a real constraint table cannot be authorized by a synthetic reviewer registry")
            if t.table_version.startswith(SYNTHETIC_VERSION_PREFIX):
                raise ValueError("a real table must not use a synthetic version")
        if t.synthetic and not self.registry.synthetic:
            raise ValueError("a synthetic table must not be signed against the real reviewer registry")
        if expected_version is not None and t.table_version != expected_version:
            raise ValueError("constraint table version does not match the pinned version")
        if expected_sha256 is not None and sealed != expected_sha256:
            raise ValueError("constraint table digest does not match the pinned digest")

        ids = [c.constraint_id for c in t.constraints]
        if len(set(ids)) != len(ids):
            raise ValueError("duplicate constraint_id in table")
        for c in t.constraints:
            reviewed = c.review_status != "UNREVIEWED"
            synthetic_identity = reviewed and (c.reviewer_id.startswith(SYNTHETIC_REVIEWER_PREFIX) or c.review_reference.startswith(SYNTHETIC_REFERENCE_PREFIX))
            if reviewed and t.synthetic and not (c.reviewer_id.startswith(SYNTHETIC_REVIEWER_PREFIX) and c.review_reference.startswith(SYNTHETIC_REFERENCE_PREFIX)):
                raise ValueError(f"constraint {c.constraint_id}: synthetic tables must use synthetic reviewer ids and references")
            if not t.synthetic and synthetic_identity:
                raise ValueError(f"constraint {c.constraint_id}: a real table must not use synthetic reviewer ids or references")
            if reviewed:
                failure = authorization_failure(self._reviewer(c.reviewer_id), c)
                if failure:
                    raise ValueError(f"constraint {c.constraint_id}: {failure}")
            text = page_text(c.source.document_id, c.source.page)
            if text is None or c.source.end_char > len(text) or text[c.source.start_char:c.source.end_char] != c.source.excerpt:
                raise ValueError(f"constraint {c.constraint_id}: source excerpt does not reconstruct from the corpus")
        self.table_sha256 = sealed
        self.registry_sha256 = registry_sealed

    def _reviewer(self, reviewer_id: str) -> "Reviewer | None":
        return next((r for r in self.registry.reviewers if r.reviewer_id == reviewer_id), None)

    def lookup(self, request: ConstraintsRequest) -> dict[str, Any]:
        known_jurisdiction = any(c.jurisdiction == request.jurisdiction for c in self.table.constraints)
        results = []
        for kind in request.kinds:
            candidates = [
                c for c in self.table.constraints
                if c.jurisdiction == request.jurisdiction and c.kind == kind and c.review_status != "REJECTED"
                and (request.building_use is None or c.applies_to in (request.building_use, "any"))
            ]
            reviewed = [c for c in candidates if c.review_status == "HUMAN_REVIEWED"]
            if reviewed:
                conflict = any(
                    _signature(a) != _signature(b) and _overlap(a, b)
                    for i, a in enumerate(reviewed) for b in reviewed[i + 1:]
                )
                status = "CONFLICTING" if conflict else "VERIFIED"
            elif candidates:
                status = "UNVERIFIED"
            else:
                status = "MISSING"
            results.append({
                "kind": kind,
                "status": status,
                "constraints": [self._public(c) for c in sorted(candidates, key=lambda c: c.constraint_id)],
            })

        warnings = ["Legal currency and amendments are not verified.",
                    "Only HUMAN_REVIEWED constraints may block; UNVERIFIED, CONFLICTING and MISSING kinds require review.",
                    "No precedence is applied: overlapping reviewed constraints with different requirements are CONFLICTING."]
        if self.table.synthetic:
            warnings.append("SYNTHETIC_TABLE: test values only, not real regulations.")
        if not known_jurisdiction:
            warnings.append("NO_CONSTRAINTS_FOR_JURISDICTION")
        pack = {
            "contract_version": CONSTRAINTS_CONTRACT_VERSION,
            "digest_algorithm": DIGEST_ALGORITHM,
            "table_version": self.table.table_version,
            "table_sha256": self.table_sha256,
            "reviewer_registry_version": self.registry.registry_version,
            "reviewer_registry_sha256": self.registry_sha256,
            "synthetic": self.table.synthetic,
            "jurisdiction": request.jurisdiction,
            "building_use": request.building_use,
            "binding": {
                "project_id": str(request.binding.project_id),
                "project_revision": request.binding.project_revision,
                "land_revision_id": str(request.binding.land_revision_id),
                "requirement_revision_id": str(request.binding.requirement_revision_id),
            },
            "kinds": results,
            "overall_status": "COMPLETE" if all(r["status"] == "VERIFIED" for r in results) else "INCOMPLETE",
            "legal_status_verified": False,
            "warnings": warnings,
        }
        return pack | {"evidence_pack_id": canonical_json.digest(pack)}

    @staticmethod
    def _public(c: Constraint) -> dict[str, Any]:
        data = c.model_dump(mode="json")
        data.pop("jurisdiction")
        return data

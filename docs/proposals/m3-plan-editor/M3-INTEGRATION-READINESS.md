# BuildMind M3 Integration Readiness

Date: 2026-11-17 (Asia/Colombo)
Branch: `feature/m3-plan-editor`
Base HEAD: `763a9a7b46fd0d4a25f0ce38ff7f03baa353100b`

This is a provisional integration handoff. It does not approve the draft
geometry contract, prove live integrations, or claim production readiness.

## Completed independent functionality

- Strict Python geometry, command, result and validation-reference models for
  schema `0.1-draft`.
- Strict C# geometry validation and command-specific payload validation for all
  eight operations, including unknown/duplicate property rejection, required
  fields, null behavior, nested objects, identifiers and finite numeric bounds.
- A shared `EDIT-COMMAND-SAMPLES.json` fixture consumed by Python and C# tests.
- Deep-copy candidate transformations for add/remove/move/resize room,
  thickness-only referenced-wall edits, and add/move/remove opening.
- Fail-safe rejection for shared-wall move/resize, referenced wall endpoint
  changes, malformed input, numeric overflow and broken references.
- Immutable revision application models, hash/evidence binding, restore as a
  new revision, optimistic concurrency and duplicate-command status.
- Exact/current-revision confirmation preconditions and provisional ports for
  ownership, lifecycle, land, requirements, persistence, compute, validation,
  reporting and AI proposal integrations.
- In-memory security/workflow tests. These are explicitly not database or live
  service integration tests.

## Remaining independent defects

- Python accepts an all-zero UUID where C# rejects it; contract approval is
  required before tightening the Python draft.
- C# strict unknown-member behavior depends on use of
  `PlanEditorJson.StrictOptions` at every boundary.
- No preview-candidate store/apply token exists. Candidate replay protection is
  represented only by atomic expected-revision and duplicate edit-command
  requirements; the final candidate binding contract remains pending M4/M6.
- Report publication after confirmation needs an M4 transactional outbox or
  durable retry mechanism.
- The custom C# executable harness lacks standard test discovery/CI reporting.
- Canonical JSON hashing, tolerance, precision and diagnostic catalogue remain
  unapproved.

## Geometry capability matrix

| Capability | Current state | Safe behavior | Owner/dependency |
| --- | --- | --- | --- |
| Rectangular room resize | Partial | Affine X/Y scaling about an anchor when all walls are exclusive | M6 validates result |
| General polygon transform | Partial | Translation and affine scaling preserve vertex order and closure | No self-intersection/area proof |
| Shared walls | Unsupported for move/resize | Reject `UNSUPPORTED_TOPOLOGY`; removal preserves still-referenced walls | M6 topology contract |
| Connected walls | Partial | Exclusive walls move/scale with room | Shared junction adjustment unsupported |
| Neighboring rooms | Unsupported | Never changes neighbors silently | M2/M6 policy required |
| Room overlap | Not detected | Must be rejected by authoritative validator before save | M6/Shapely candidate |
| Boundary containment | Not detected | Must be validated against confirmed land revision | M2 + M6 |
| Wall intersection | Not detected | Must be rejected by authoritative validator | M6/Shapely candidate |
| Opening intersection | Not detected | Only parent, offset, width and wall fit are checked | M6 policy/library |
| Degenerate walls | Detected | Candidate rejected | M3 structural check |
| Degenerate/self-intersecting polygons | Partially detected | Closure/size checked; zero area and self-intersection not detected | M6/Shapely candidate |
| Invalid references | Detected | Candidate rejected without source mutation | M3 structural check |
| Referenced wall endpoint edits | Unsupported | Reject to avoid polygon desynchronization | Topology policy required |
| Wall thickness edit | Supported structurally | Positive finite thickness only | M6 validates architectural result |

No overlap, containment, intersection, polygon-validity or regulatory result
from M3 is authoritative.

## Integration interface register

| Dependency | Provisional required interface | Request | Response | Failure behavior | Owner | Approval | Local coverage / acceptance |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Authentication/ownership | `IProjectOwnershipGate` | project ID + authenticated actor from claims | owned/not owned | fail `Unauthorized`; never accept body user ID | M1/M4 | Pending | denial double tested; accept when real cross-project tests pass |
| Project lifecycle | `IProjectLifecycleEligibilityPort`; lifecycle transition inside `IFinalConfirmationTransaction` | project + actor / exact confirmation | exists, eligible, blocking code / atomic result | block ineligible; rollback confirmation if transition fails | M1/M4 | Pending | interface only; accept with real eligible/ineligible transaction tests |
| Land revision/geometry | `IConfirmedLandRevisionPort` | project + revision ID | confirmed metric boundary + schema version | missing/stale/unreadable blocks validation/apply/confirm | M2 | Pending | interface only; accept with CRS/unit and containment fixtures |
| Persistence/transactions | `IPlanRevisionStore`, `IFinalConfirmationTransaction` | scoped IDs, expected base, immutable snapshots | created/conflict/duplicate/rollback statuses | atomic rollback; no partial pointer/record/lifecycle write | M4 | Pending/owner blocked | in-memory semantics only; accept with MySQL concurrency/rollback tests |
| Internal Python HTTP | `IPlanEditorComputeClient` | source geometry, validated command, correlation ID, cancellation | structured `PlanEditResultDto` | timeout/unavailable => no persistence; structured diagnostic | M4/M6 | Pending | interface only; accept with auth, timeout and contract fixture tests |
| Requirement revision | `IRequirementRevisionPort` and current input source | project + revision ID | exact revision reference/schema | stale/missing blocks confirmation | M5 | Pending | mismatch double tested; accept with real current/stale data |
| Report snapshot | `IConfirmedPlanSnapshotSink` | immutable confirmation + exact revision snapshot | durable acknowledgement | retry/outbox; never regenerate geometry | M5/M4 | Pending | success/failure double tested; accept with durable delivery/idempotency |
| Authoritative validation | `IPlanValidationEvidenceVerifier` | project, plan, exact hash, evidence reference | verified/rejected/unavailable | rejected/unavailable creates no revision | M6 | Pending | fail-closed double tested; accept with authenticated live validator |
| AI edit proposal | `IAiEditProposalPort` | scoped IDs, text, correlation ID | structured commands + diagnostics/status | unavailable/rejected; never execute raw output | M7 | Pending | interface only; accept with malformed/adversarial output tests |

All interfaces in `Integration/ProvisionalIntegrationPorts.cs` are provisional
consumer ports, not implementations or route registrations.

## Security and persistence readiness

| Scenario | Current evidence | Remaining requirement |
| --- | --- | --- |
| Unauthorized/cross-project | Ownership denial tested | Real claims mapping and repository scoping |
| Duplicate command/replay | Duplicate command ID rejected by atomic store double | DB unique constraint/idempotency key |
| Candidate replay | Stale base and duplicate command mitigate accepted-edit replay | Approved candidate ID/hash/expiry/one-time consume contract |
| Stale base revision | One winner/one conflict tested | MySQL concurrency token/transaction test |
| Hash mismatch | Revision and confirmation checks tested | Canonical hash contract |
| Untrusted evidence | Verifier unavailable/rejected fails closed | Authenticated M6 lookup, not field echo |
| Expired validation | Freshness policy rejection tested | Approved age/input-change policy |
| Concurrent editing | Two-task in-memory test gives one winner | Multi-connection DB test |
| Duplicate confirmation | `AlreadyConfirmed` propagation tested | Unique DB constraint and idempotent response |
| Transaction failure | Failure prevents report publication in double | Real rollback plus outbox behavior |
| Atomic rollback | No revision on validation unavailable/replay in double | Failure injection in M4 repository |

## Shapely dependency approval request

**Requested owners:** M6 (geometry/validation), M4 (dependency/runtime review),
with M3 as consumer reviewer.

**Purpose:** reliable polygon validity and predicates for self-intersection,
zero-area/degenerate rings, room overlap, boundary containment, wall/room
intersection, shared-boundary discovery and tolerance-aware geometry tests.

**Proposed scope:** approve a Python 3.13-compatible Shapely 2.x range in the
shared Python dependency policy. M6 remains authoritative validator owner. M3
would use only an approved structural/candidate precheck adapter and would not
relabel it as regulatory validation.

**Compatibility checks required:** Windows 11/Python 3.13 wheel availability,
GEOS wheel behavior, FastAPI/Pydantic compatibility, CI platform wheels,
installation size/startup impact and license review. PyProj is unnecessary for
local Cartesian predicates unless M2 requires CRS transformation.

**Risks:** native wheel/platform failures, precision/tolerance differences,
invalid-geometry exceptions, performance/denial-of-service from very large
inputs, and duplicated M3/M6 validation semantics.

**Proposed tests:** valid/invalid polygons, bow-ties, zero area, touching versus
overlapping rooms, holes/ring orientation as approved, boundary containment,
wall crossings, shared edges, epsilon cases, large-input limits, deterministic
results, timeout/cancellation and parity with M6 fixtures.

No package was installed and no requirements file was changed.

## Actionable team requests

### M1 — authentication and lifecycle

Approve the authenticated actor source and `IProjectOwnershipGate` semantics;
define eligible confirmation states and lifecycle transition failure codes;
provide ownership and lifecycle test fixtures including cross-project denial.

### M2 — land geometry

Approve confirmed land revision identity, metric boundary representation,
origin/CRS/axis/precision, and provide valid, outside-boundary and boundary-touch
fixtures. Confirm whether local Cartesian geometry ever requires PyProj.

### M4 — infrastructure

Implement/approve strict API JSON handling, claims-to-actor mapping, DI,
internal HTTP auth/timeouts/cancellation, immutable revision repository,
duplicate-command constraint, optimistic concurrency, atomic confirmation and
lifecycle transaction, migrations, and durable report outbox. Supply MySQL
rollback/concurrency/idempotency evidence.

### M5 — requirements and reports

Approve requirement revision reference/currentness rules and implement the
exact confirmed-snapshot consumer. Define durable acknowledgement/idempotency
for report delivery; reports must not regenerate plan geometry.

### M6 — geometry and validation

Publish/approve canonical schema, topology, tolerance, precision, hash and
validation evidence contracts; implement authenticated evidence lookup and
authoritative validation; decide the Shapely request and provide shared
positive/negative geometry fixtures.

### M7 — AI and QA

Approve `IAiEditProposalPort`, structured-command response and unavailable/
rejected behavior. Provide adversarial tests proving raw model output cannot be
executed and coordinate final contract/acceptance review.

## Automated test evidence

Final local verification:

- M3 Python suite: `41 passed`, exit `0`.
- Existing Python health regression: `1 passed`, exit `0`, with one third-party
  Starlette deprecation warning.
- Full Python suite: `42 passed`, exit `0`, with the same warning.
- Dependency-free C# contract/workflow harness: `22 passed, 0 failed`, exit `0`.
- Application build: succeeded with `0 warnings, 0 errors`, exit `0`.
- API build: succeeded with `0 warnings, 0 errors`, exit `0`.
- Tracked `git diff --check`: exit `0`.
- Untracked whitespace/final-LF inspection: passed after correcting this report.
- Secret scan: no matches (`rg` exit `1` means no pattern match).
- Generated-artifact scan: no untracked virtual environment, cache, `bin`,
  `obj`, `pyc` or `__pycache__` paths.
- Working tree after readiness work: 33 untracked files, 0 tracked changes and
  0 staged files.

Test doubles are named as such and are not live integration evidence.

## Exact outstanding blockers

1. Revised M3 backend ownership is not reconciled with the legacy register.
2. Canonical geometry/hash/topology/tolerance contracts are unapproved.
3. M6 validator and validation-evidence lookup are unavailable.
4. M4 persistence, transactions, concurrency, DI, HTTP client and migrations
   are unavailable.
5. M1 ownership/lifecycle, M2 boundary, M5 requirements/report and M7 AI ports
   have no real implementations.
6. Candidate preview binding/expiry/one-time application is unapproved.
7. Shapely decision is pending.
8. No database, HTTP, integration or end-to-end test has passed.

## Git delivery preparation

Nothing is staged or committed. Proposed groups after owner approval:

1. **Draft contracts and shared fixtures**
   - `docs/proposals/m3-plan-editor/GEOMETRY-CONTRACT-PROPOSAL.md`
   - `docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json`
   - `docs/proposals/m3-plan-editor/EDIT-COMMAND-SAMPLES.json`
   - `src/BuildMind.Application/Features/PlanEditor/Contracts/**`
   - `python-services/app/plan_editor/schemas/**`
2. **Candidate geometry operations and tests**
   - `python-services/app/plan_editor/operations/**`
   - `python-services/tests/m3_plan_contract/**`
3. **Versioning, confirmation and provisional integration ports**
   - `src/BuildMind.Application/Features/PlanEditor/Versioning/**`
   - `src/BuildMind.Application/Features/PlanEditor/Confirmation/**`
   - `src/BuildMind.Application/Features/PlanEditor/Integration/**`
4. **C# verification harness**
   - `tests/BuildMind.Application.ContractTests/**`
5. **Handoff and evidence documentation**
   - `docs/proposals/m3-plan-editor/TEAM-HANDOFF.md`
   - `docs/proposals/m3-plan-editor/M3-DEVELOPMENT-PROGRESS.md`
   - `docs/proposals/m3-plan-editor/M3-QUALITY-AUDIT.md`
   - `docs/proposals/m3-plan-editor/M3-INTEGRATION-READINESS.md`
   - `python-services/app/plan_editor/README.md`
   - `src/BuildMind.Application/Features/PlanEditor/README.md`

**Delivery readiness:** ready for owner review as a provisional draft; not
ready to merge or deploy. Wait for explicit user approval before any staging,
commit, push, PR or merge action.

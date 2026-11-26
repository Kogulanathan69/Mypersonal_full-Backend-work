# M3 Phase 02 integration progress

Date: 2026-11-17

Task: `BE-M3-INT-01` — Consumer Contract Verification Foundation

Branch: `feature/m3-plan-integration`

Baseline: `b03430ee49d5cfbb42a8e3394255cc1e43190b5f`

## Approval and scope

The approved slice permits changes only to the M3 C# contract-test harness, new test-only integration doubles, and this independent progress report. It does not authorize production adapters, API registration, DI, shared infrastructure, database work, dependencies, commits, pushes, merges, or rebases.

All results below use configurable in-process test doubles. They are consumer contract verification, not live M1/M2/M4/M5/M6/M7 integration evidence.

## Implementation

### Fixture discovery defect

The C# harness previously assumed a fixed number of parent directories beneath `AppContext.BaseDirectory`. A normal root invocation resolved the fixture beneath `tests/docs` and failed two checks. The shared command-fixture check also required an explicit geometry argument even though the geometry check attempted a default.

The new test-only `FixtureLocator`:

- preserves a single explicit fixture argument;
- searches upward from both the working directory and application base directory;
- uses only repository-relative paths;
- verifies the adjacent command fixture;
- reports every searched path for default discovery failure;
- returns a concise error and process exit code `2` for fixture-discovery/configuration failure.

Normal invocation now passes from the repository root and the contract-test project directory.

### Explicit consumer test doubles

The test project now contains explicitly named implementations for:

- M1 `IProjectOwnershipGate`;
- M1 `IProjectLifecycleEligibilityPort`;
- M2 `IConfirmedLandRevisionPort`;
- M4 `IPlanRevisionStore`;
- M4 `IFinalConfirmationTransaction`;
- M4 `IPlanEditorComputeClient`;
- M5 `IRequirementRevisionPort`;
- M5 `IConfirmedPlanSnapshotSink`;
- M6 `IPlanValidationEvidenceVerifier`;
- M7 `IAiEditProposalPort`.

Supporting test-only doubles cover applicable input revisions and validation freshness. No production implementation, registration, or transport behavior was added.

## Files

Modified:

- `tests/BuildMind.Application.ContractTests/Program.cs`

Created:

- `tests/BuildMind.Application.ContractTests/Integration/FixtureLocator.cs`
- `tests/BuildMind.Application.ContractTests/Integration/ConsumerContractTestDoubles.cs`
- `docs/proposals/m3-plan-editor/M3-PHASE-02-INTEGRATION-PROGRESS.md`

## Coverage matrix

| Dependency or behavior | Verified with test double | Not verified / blocked |
|---|---|---|
| M1 ownership | Owner success and cross-project denial | Real authentication, authorization store and policy |
| M1 lifecycle | Eligible and blocked response shapes | Approved lifecycle states and transition adapter |
| M2 land revision | Success, missing revision and deliberately invalid cross-project response fixture | Runtime adapter rejection, canonical boundary and CRS validation |
| M4 revision persistence | Atomic append behavior inherited from existing tests; duplicate command, concurrency conflict and injected failure leaving history unchanged | MySQL, EF Core, isolation level and real rollback |
| M4 confirmation transaction | Success, duplicate confirmation and injected transaction failure without report publication | Database transaction and M1 lifecycle atomicity |
| M4 compute client | Successful response validation, correlation propagation, unavailability and cancellation/timeout representation | HTTP transport, service identity, timeout policy and error mapping |
| M5 requirements | Success and missing revision | Runtime cross-project response rejection and approved schema |
| M5 snapshot delivery | Success inherited from existing confirmation checks; injected publication failure remains visible | Outbox, retry, delivery idempotency and PDF integration |
| M6 validation | Verified/unavailable behavior inherited; explicit mismatched evidence rejection | Authoritative validator, canonical hash approval and evidence lookup |
| M7 proposals | Unavailable state and malformed proposed command rejection by existing M3 validator | AI/RAG service, provenance, prompt safety and approved envelope |
| Confirmation | Unauthorized actor, cross-project denial, stale validation, stale/current revision checks, hash mismatch, duplicate confirmation and transaction failure | Live member adapters and durable transaction |

The current provisional interfaces do not themselves enforce M2/M5 response identity. The deliberately invalid response fixtures document what future orchestration or adapters must reject; the tests do not claim the missing rejection exists.

Cancellation is used as the current transport-neutral representation of a dependency timeout because no approved M4 timeout/error contract exists. No official shared error codes were introduced.

## Regression evidence

| Verification | Previous result | Current result | Exit code |
|---|---:|---:|---:|
| Full Python suite | 42 passed | 42 passed, 2 warnings | 0 |
| M3-specific Python suite | 41 passed | 41 passed | 0 |
| C# harness, normal root invocation | 20 passed, 2 failed due fixture lookup | 32 passed, 0 failed | 0 |
| C# harness, normal project-directory invocation | Not previously recorded | 32 passed, 0 failed | 0 |
| C# harness, explicit fixture | 22 passed, 0 failed | 32 passed, 0 failed | 0 |
| Missing explicit fixture diagnostic | Not previously recorded | Clear diagnostic | 2 (expected configuration failure) |
| Application build, `--no-restore` | Passed | 0 warnings, 0 errors | 0 |
| API build, `--no-restore` | Passed | 0 warnings, 0 errors | 0 |
| Tracked diff whitespace | Not applicable | Clean | 0 |
| New-file trailing whitespace | Not applicable | None found | 0 |
| Targeted secret-pattern scan | Not applicable | No matching files | 0 |
| Generated-artifact review | Not applicable | No generated artifacts in changed/untracked set | 0 |

The C# harness increased from 22 to 32 checks. These are console-harness checks rather than tests executed through a unit-test framework.

## Pytest environment warning assessment

The repository-local virtual environment is usable and all tests pass. The full suite reports:

1. An existing Starlette/FastAPI TestClient deprecation warning recommending a future `httpx2` migration. Dependencies were not changed in this task.
2. A pytest cache warning for `python-services/.pytest_cache`.

The cache directory exists, but the current process cannot enumerate it or read its ACL (`Access denied` / unauthorized operation). This is a local filesystem/environment condition, not an M3 product failure. No Windows permission, ownership, ACL, or security setting was changed. Running the M3 suite with pytest's cache provider disabled produces 41 passes without the cache warning. The inaccessible directory was not deleted or modified.

The deleted remote `main` branch and dangling remote HEAD were not modified.

## Fixed defects

- Corrected repository fixture discovery for normal C# harness invocation.
- Removed the inconsistent requirement to pass the geometry fixture solely to load command samples.
- Added clear missing-fixture diagnostics and a distinct configuration exit code.
- Added explicit test-only consumer doubles for every approved dependency boundary.
- Added contract scenarios for missing/unavailable dependencies, cancellation, identity isolation, persistence failure, failed report publication, mismatched evidence and malformed AI commands.

## Remaining limitations and blockers

- No live dependent member implementation is present.
- M1 lifecycle values and ownership adapter are unapproved/unavailable.
- M2 canonical land geometry, CRS and confirmed-revision adapter are unavailable.
- M4 MySQL, transaction, HTTP, retry, outbox and DI implementations are unavailable.
- M5 requirements schema, snapshot delivery contract and PDF service are unavailable.
- M6 canonical geometry, deterministic hash and authoritative validator are unavailable.
- M7 proposal envelope, provenance and AI/RAG adapter are unavailable.
- Timeout/error translation remains provisional pending the M4 shared error contract.
- Report publication currently occurs after confirmation and has no approved durable outbox. The test proves failure visibility only; it does not prove rollback of an already confirmed transaction.
- Invalid M2/M5 dependency responses require a future consumer orchestration/adapter validation boundary.
- No Shapely dependency was installed and no authoritative topology behavior was added.

## Proposed next bounded task

`BE-M3-INT-02 — Provisional Candidate Preview Binding`

Proposed scope: feature-local candidate identity and binding, expiry through injected time, explicit Apply/Reject, one-time use, duplicate/replay protection, stale input and tamper rejection, plus test-only in-memory storage. It must remain provisional and must not add production persistence, DI, or API endpoints without separate approval.

Status after this slice: implementation and test-double verification complete; live integration, team approval, merge readiness and production readiness remain pending.

---

## BE-M3-INT-02 — Provisional Candidate Preview Binding

Date: 2026-11-17

Task identifier status: provisional, not added to the official task register.

### Approved scope

This slice adds a feature-local candidate workflow under `Application/Features/PlanEditor/Editing`, integration test doubles/scenarios, contract-harness registration, and this evidence update. It does not add API endpoints, DI registration, shared infrastructure, database code, migrations, dependencies, or canonical geometry changes.

### Candidate lifecycle

The feature-local provisional states are:

- `Pending`: immutable preview exists and is eligible for Apply or Reject while its bindings remain current.
- `Applied`: terminal; the candidate was atomically consumed with one immutable plan revision.
- `Rejected`: terminal; no plan revision was created.
- `Expired`: terminal; the configured expiry boundary was reached.

Terminal states cannot return to `Pending`. Repeated Apply returns `AlreadyApplied`; repeated Reject returns `AlreadyRejected`. Reject after Apply and Apply after Reject observe the existing terminal state. These are feature-local names, not approved public API values.

Candidate metadata binds the server-derived owner, project, plan, base revision, land revision, requirement revision, command, serialized geometry snapshot, geometry hash, schema version, validation evidence, creation time and expiry time. Geometry is stored as serialized immutable snapshot data rather than a mutable object reference.

Expiry uses `TimeProvider` and a constructor-supplied lifetime. No production expiry duration is hardcoded. A zero or negative lifetime fails configuration validation.

### Preview workflow

The provisional service:

1. Checks the authenticated actor through `IProjectOwnershipGate`.
2. Loads the current plan revision and rejects a stale requested base.
3. Loads current applicable land and requirement revision IDs.
4. Confirms both exact dependency revisions and their project identities.
5. Validates the edit command with the existing M3 contract validator.
6. Calls `IPlanEditorComputeClient`; it does not duplicate the Python edit engine.
7. Validates compute response identity, status, geometry identity and input revision bindings.
8. Serializes the candidate and computes its provisional exact-snapshot hash.
9. Obtains server-side validation evidence and verifies it through the existing `IPlanValidationEvidenceVerifier`.
10. Persists only a pending candidate reference; the saved plan remains unchanged.

The current compute response does not contain an authoritative validation reference. A provisional feature-local `ICandidateValidationEvidenceSource` was therefore introduced so evidence is acquired server-side rather than accepted from the customer. M6 must approve or replace this boundary.

### Apply workflow

Apply verifies ownership, project and plan binding, pending state, expiry, current base revision, current land and requirements revisions, serialized geometry validity, recomputed hash, evidence hash, authoritative evidence status, blocking issues and warning acknowledgement.

It creates an immutable `PlanRevisionSnapshot` compatible with existing versioning models. `ICandidatePreviewTransaction.TryApplyAtomicallyAsync` is the mandatory persistence boundary: M4 must recheck candidate state and current base revision, consume the candidate, and append the revision in one transaction. Two unrelated writes are not permitted.

The test-only transaction performs these operations under one lock. This verifies consumer expectations only; it is not evidence of MySQL concurrency safety.

### Reject workflow

Reject performs the same trusted ownership and candidate identity checks. Only a pending, unexpired candidate can transition to `Rejected`. It creates no plan revision. Repeated rejection reports `AlreadyRejected`; it does not silently return a new success.

Expiry is persisted through the atomic transaction boundary when an action observes the exact expiry boundary or a later time.

### Security behavior

- Owner identity is accepted only as trusted application context, never from the candidate creation command.
- Candidate lookup is followed by owner, project and plan verification.
- Customer-supplied geometry hashes are not accepted.
- Apply reparses and validates the stored geometry and recomputes its hash.
- Stale base/input revisions fail closed.
- Critical validation cannot be acknowledged away.
- Warning acknowledgement applies only to nonblocking warning conditions.
- Preview never mutates or appends a saved plan revision.
- No AI command is automatically persisted.
- Geometry contents are not logged.

### INT-02 files

Created:

- `src/BuildMind.Application/Features/PlanEditor/Editing/CandidatePreviewModels.cs`
- `src/BuildMind.Application/Features/PlanEditor/Editing/CandidatePreviewContracts.cs`
- `src/BuildMind.Application/Features/PlanEditor/Editing/CandidatePreviewService.cs`
- `tests/BuildMind.Application.ContractTests/Integration/CandidatePreviewContractScenarios.cs`

Modified:

- `tests/BuildMind.Application.ContractTests/Program.cs`
- `docs/proposals/m3-plan-editor/M3-PHASE-02-INTEGRATION-PROGRESS.md`

INT-01 files and fixture-discovery behavior remain preserved.

### INT-02 automated coverage

Twenty-five candidate scenarios cover:

- Valid creation and immutable binding.
- Preview preserving the original revision.
- Valid Apply and exactly one appended revision.
- Valid Reject with no revision.
- Unauthorized and cross-project access.
- Cross-plan access.
- Missing candidate.
- Exact expiry boundary and persisted expiry.
- Applied/rejected replay.
- Stale base revision.
- Changed land and requirement revisions.
- Geometry hash and serialized geometry tampering.
- Validation unavailability, mismatched evidence and blocking evidence.
- Required warning acknowledgement.
- Concurrent Apply with exactly one winner.
- Apply/Reject race with exactly one terminal winner.
- Transaction failure rollback.
- Stale preview base.
- Missing land or requirement dependency.
- Invalid edit command rejection before compute.

### INT-02 regression evidence

| Verification | Result | Exit code |
|---|---:|---:|
| Full Python suite | 42 passed, 2 pre-existing environment warnings | 0 |
| M3-specific Python suite, cache disabled | 41 passed | 0 |
| C# harness, default fixture discovery | 57 passed, 0 failed | 0 |
| C# harness, explicit fixture | 57 passed, 0 failed | 0 |
| Application build, `--no-restore` | 0 warnings, 0 errors | 0 |
| API build, `--no-restore` | 0 warnings, 0 errors | 0 |

The C# total increased from 32 after INT-01 to 57 after INT-02. Candidate concurrency results come exclusively from the explicit in-memory test double.

### Provisional decisions and blockers

- Candidate state names, result statuses and expiry policy remain provisional.
- The snapshot hash uses the existing M3 provisional serializer/hash implementation; canonical M6 hashing remains pending.
- M4 must implement durable candidate storage, atomic candidate/revision consumption, concurrency constraints, replay protection and rollback behavior.
- M6 must provide/approve server-side validation evidence acquisition and authoritative revalidation semantics.
- M1 must supply the real ownership adapter.
- M2 and M5 must supply exact/current revision adapters.
- Error/status mapping to public HTTP Problem Details remains owned by M4 and is not implemented here.
- No API, DI, MySQL, migration or live service integration was added.

INT-02 status: bounded implementation and test-double verification complete. Live integration, external contract approval, database concurrency proof, merge readiness and production readiness remain pending.

---

## BE-M3-INT-03 — Provisional diagnostics and dependency failures

Date: 2026-11-17

Task identifier status: provisional, not added to the official task register.

### Approved scope

This slice adds a feature-local diagnostic catalog, structured internal diagnostic model, failure mapper, contract scenarios, harness registration and this evidence update. It does not replace the shared HTTP error contract or modify endpoints, middleware, logging, API composition, infrastructure, geometry rules, dependencies or databases.

### Existing-code reconciliation

The Python editor already emits these compatible identifiers, which are preserved verbatim:

- `FLOOR_NOT_FOUND`
- `ROOM_NOT_FOUND`
- `WALL_NOT_FOUND`
- `OPENING_NOT_FOUND`
- `INVALID_EDIT_PAYLOAD`
- `INVALID_GEOMETRY_REFERENCE`
- `INVALID_OPENING_POSITION`
- `UNSUPPORTED_TOPOLOGY`

The C# `PlanEditDiagnosticDto` remains the existing serialization DTO. The new `PlanEditorDiagnostic` is an Application-level proposed failure representation containing safe message, severity, blocking state, trusted correlation ID, optional entity ID and retry classification. It does not contain exception details.

### Proposed diagnostic catalog

Input/geometry: `INVALID_EDIT_COMMAND`, `INVALID_EDIT_PAYLOAD`, `UNSUPPORTED_EDIT_OPERATION`, `UNSUPPORTED_SCHEMA_VERSION`, `INVALID_GEOMETRY_REFERENCE`, `DUPLICATE_ENTITY_ID`, `GEOMETRY_OVERLAP`, `OUTSIDE_LAND_BOUNDARY`, `INVALID_OPENING_POSITION`, `UNSUPPORTED_TOPOLOGY`.

Access/resources: `UNAUTHORIZED_PROJECT_ACCESS`, `PROJECT_NOT_FOUND`, `PLAN_NOT_FOUND`, `FLOOR_NOT_FOUND`, `ROOM_NOT_FOUND`, `WALL_NOT_FOUND`, `OPENING_NOT_FOUND`.

Candidates: `CANDIDATE_NOT_FOUND`, `CANDIDATE_EXPIRED`, `CANDIDATE_ALREADY_APPLIED`, `CANDIDATE_ALREADY_REJECTED`, `CANDIDATE_BINDING_MISMATCH`, `CANDIDATE_TAMPERED`.

Revisions: `REVISION_CONFLICT`, `REVISION_NOT_FOUND`, `DUPLICATE_COMMAND`, `LAND_REVISION_CHANGED`, `REQUIREMENT_REVISION_CHANGED`, `GEOMETRY_HASH_MISMATCH`.

Validation: `VALIDATION_UNAVAILABLE`, `VALIDATION_EVIDENCE_INVALID`, `VALIDATION_EVIDENCE_EXPIRED`, `BLOCKING_VALIDATION_ISSUES`, `WARNING_ACKNOWLEDGEMENT_REQUIRED`.

Dependencies/persistence: `DEPENDENCY_UNAVAILABLE`, `DEPENDENCY_TIMEOUT`, `INVALID_DEPENDENCY_RESPONSE`, `PERSISTENCE_FAILURE`, `TRANSACTION_FAILURE`, `AI_PROPOSAL_INVALID`, `REPORT_PUBLICATION_FAILURE`, `UNEXPECTED_INTERNAL_FAILURE`.

Confirmation: `CONFIRMATION_BLOCKED`, `CONFIRMATION_ALREADY_EXISTS`.

`UNEXPECTED_INTERNAL_FAILURE` safely represents an unexpected failure without exposing exception data. Geometry and regulatory identifiers may describe upstream results; M3 does not generate authoritative M6 conclusions.

### Severity and blocking rules

Provisional severities are `Information`, `Warning`, `Error` and `Critical`.

- Authorization, integrity/tamper, blocking validation, invalid evidence, persistence and transaction failures are blocking.
- Warning acknowledgement is a noncritical warning that blocks progression until explicit acknowledgement.
- Acknowledgement never converts a critical diagnostic into an allowed result.
- Successful workflow statuses cannot be passed through the failure mapper.
- Informational diagnostics are supported but are not fabricated for current workflows.

### Dependency failure and retry mapping

| Failure | Proposed diagnostic | Retry |
|---|---|---|
| HTTP/service unavailable | `DEPENDENCY_UNAVAILABLE` | Safe for an idempotent/read retry |
| Dependency timeout | `DEPENDENCY_TIMEOUT` | Safe for an idempotent/read retry |
| Caller cancellation | Propagate cancellation | No diagnostic conversion |
| Invalid JSON/data response | `INVALID_DEPENDENCY_RESPONSE` | Never blindly retry |
| Persistence provider failure | `PERSISTENCE_FAILURE` | Conditional on idempotency/current state |
| Atomic transaction failure | `TRANSACTION_FAILURE` | Conditional on outcome/idempotency |
| Report publication after committed confirmation | `REPORT_PUBLICATION_FAILURE` | Conditional; durable outbox required |
| Invalid AI proposal | `AI_PROPOSAL_INVALID` | Never automatically apply/retry |
| Unexpected internal failure | `UNEXPECTED_INTERNAL_FAILURE` | Never blindly retry |

Raw exception messages, SQL/provider errors, stack traces, credentials and geometry are not copied into safe diagnostics.

### Correlation IDs

The mapper accepts an optional correlation ID assigned by trusted infrastructure and preserves it unchanged. It does not fabricate one when absent. M4 must propagate the trusted request trace/correlation identifier into Application calls and serialize it through the approved shared error envelope. M3 does not change middleware or logging.

### Proposed HTTP mapping

| Category | Proposed status |
|---|---:|
| Malformed or unsupported input | 400 |
| Authenticated non-owner | 403 |
| Missing resource | 404 |
| Revision/replay/idempotency conflict | 409 |
| Semantic validation rejection | 422 |
| Unexpected/persistence/report failure | 500 |
| Dependency or validation unavailable | 503 |
| Dependency timeout | 504 |

These mappings are proposals only and are not registered in API endpoints or M4-owned middleware.

### INT-03 files

Created:

- `src/BuildMind.Application/Features/PlanEditor/Diagnostics/PlanEditorErrorCodes.cs`
- `src/BuildMind.Application/Features/PlanEditor/Diagnostics/PlanEditorDiagnosticModels.cs`
- `src/BuildMind.Application/Features/PlanEditor/Diagnostics/PlanEditorFailureMapper.cs`
- `tests/BuildMind.Application.ContractTests/Integration/DiagnosticContractScenarios.cs`

Modified:

- `tests/BuildMind.Application.ContractTests/Program.cs`
- `docs/proposals/m3-plan-editor/M3-PHASE-02-INTEGRATION-PROGRESS.md`

### INT-03 automated coverage

Nineteen scenarios verify stable identifiers, existing Python code compatibility, severity/blocking behavior, invalid command and authorization mapping, candidate expiry/replay/binding, hash/tamper behavior, stale revisions, validation failures, timeout versus unavailability, caller cancellation, invalid responses, sensitive-detail suppression, retry safety, warning/critical separation, trusted correlation propagation, report publication after confirmation, AI proposal failure and rejection of successful statuses passed to failure mapping.

### INT-03 regression evidence

| Verification | Result | Exit code |
|---|---:|---:|
| Full Python suite | 42 passed, 2 pre-existing environment warnings | 0 |
| M3 Python suite, cache disabled | 41 passed | 0 |
| C# harness, default fixture | 76 passed, 0 failed | 0 |
| C# harness, explicit fixture | 76 passed, 0 failed | 0 |
| Application build, `--no-restore` | 0 warnings, 0 errors | 0 |
| API build, `--no-restore` | 0 warnings, 0 errors | 0 |

The C# harness increased from 57 after INT-02 to 76 after INT-03. These are feature-local console contract checks, not live cross-member tests.

### Shared approvals and remaining blockers

- M4 must approve the shared Problem Details envelope, middleware translation, trace source and final HTTP/retry rules.
- M1 must approve authorization/not-found disclosure and lifecycle business codes.
- M2 and M5 must approve missing/stale/invalid revision diagnostics.
- M6 must approve validation, geometry and regulatory diagnostic ownership and meanings.
- M7 must approve AI proposal and evidence failure semantics.
- Durable report outbox and retry behavior remain an M4/M5 dependency.
- No live adapters, persistence, endpoints or middleware were verified.

INT-03 status: feature-local implementation and test verification complete. Official contract approval, live integration, merge readiness and production readiness remain pending.

---

## BE-M3-INT-04 — Persistence mapping and transaction design

Date: 2026-11-17

Task identifier status: provisional, not added to the official task register.

### Approved scope and audit

This slice is limited to M3-owned persistence requirements, proposal documents and test-only conformance scenarios. Production feature models/interfaces, EF Core, DbContext, provider configuration, migrations, MySQL, DI and shared infrastructure were not changed.

Existing persistence boundaries were reused:

- `IPlanRevisionStore` for immutable revision lookup and compare-and-append.
- `ICandidatePreviewStore` for candidate storage.
- `ICandidatePreviewTransaction` for atomic Apply/Reject/Expire behavior.
- `IFinalConfirmationTransaction` for confirmation and M1 lifecycle atomicity.
- `IConfirmedPlanSnapshotSink` as the current post-confirmation publication boundary.

The audit found two material integration requirements:

1. `PlanConfirmationSnapshot` does not contain land/requirement IDs, but the transaction receives the exact `PlanRevisionSnapshot` and can persist them after owner approval.
2. `IFinalConfirmationTransaction` does not explicitly state that an outbox message is inserted. M4 must make confirmation, M1 lifecycle transition and outbox insertion atomic where the agreed database architecture permits it.

### Deliverables

- `M3-PERSISTENCE-MAPPING-PROPOSAL.md`: FloorPlans, PlanRevisions, EditCandidates, PlanConfirmations and OutboxMessages logical mappings; MySQL type trade-offs; relationships, indexes, constraints, retention and open decisions.
- `M3-TRANSACTION-DESIGN.md`: Apply, Reject, Expire, append, restore and confirmation atomic workflows; rollback points; concurrency/idempotency; uncertain-outcome reconciliation.
- `M3-OUTBOX-CONTRACT-PROPOSAL.md`: exact confirmed-plan event, at-least-once delivery, consumer idempotency, retries, leases, dead letter and recovery.

All three documents are marked `PROVISIONAL — OWNER APPROVAL REQUIRED`.

### Logical persistence summary

- FloorPlans own the current immutable revision pointer and an application-managed concurrency token.
- PlanRevisions are append-only with unique `(PlanId, VersionNumber)` and scoped command idempotency.
- EditCandidates retain immutable geometry/hash/evidence bindings and a one-way terminal status with optional AppliedRevisionId.
- PlanConfirmations bind exact revision, hash, validation and input revisions; confirmation uniqueness depends on M1 lifecycle approval.
- OutboxMessages are a shared M4/M5 proposal, inserted in the confirmation transaction and delivered after commit.
- No cascade deletion may destroy immutable plan history, confirmations or delivery audit records.

Candidate Apply must atomically insert one revision, conditionally move the plan pointer and conditionally consume the pending candidate. Confirmation must atomically insert confirmation, apply the approved lifecycle transition and insert the outbox event when all state shares one database boundary.

### INT-04 files

Created:

- `docs/proposals/m3-plan-editor/M3-PERSISTENCE-MAPPING-PROPOSAL.md`
- `docs/proposals/m3-plan-editor/M3-TRANSACTION-DESIGN.md`
- `docs/proposals/m3-plan-editor/M3-OUTBOX-CONTRACT-PROPOSAL.md`
- `tests/BuildMind.Application.ContractTests/Integration/PersistenceContractScenarios.cs`

Modified:

- `tests/BuildMind.Application.ContractTests/Program.cs`
- `docs/proposals/m3-plan-editor/M3-PHASE-02-INTEGRATION-PROGRESS.md`

### Persistence conformance scenarios

Fourteen test-only scenarios cover:

- Successful revision append and immutable history preservation.
- Rollback before/after staged revision insertion.
- Rollback before pointer update and candidate consumption.
- Duplicate and concurrent candidate Apply.
- Apply versus Reject race.
- Duplicate confirmation.
- Lifecycle transition and outbox insertion failures.
- Post-commit report failure preserving confirmation/outbox state.
- Duplicate outbox delivery consumption.
- Uncertain committed Apply outcome reconciled by candidate identity.

The persistence double stages changes and publishes them only at its simulated commit boundary. This verifies the application contract; it is not evidence of EF Core/MySQL isolation, SQL generation, locking or rollback behavior.

Some physical guarantees cannot be proven through current interfaces: cross-project composite foreign keys, provider-specific affected-row behavior, database time, deadlock handling, worker leases, real isolation levels and distributed M1 lifecycle atomicity. These remain documented M4/M1 contract requirements.

### INT-04 regression evidence

| Verification | Result | Exit code |
|---|---:|---:|
| Full Python suite | 42 passed, 2 pre-existing environment warnings | 0 |
| M3 Python suite, cache disabled | 41 passed | 0 |
| C# harness, default fixture | 90 passed, 0 failed | 0 |
| C# harness, explicit fixture | 90 passed, 0 failed | 0 |
| Application build, `--no-restore` | 0 warnings, 0 errors | 0 |
| API build, `--no-restore` | 0 warnings, 0 errors | 0 |

The C# harness increased from 76 after INT-03 to 90 after INT-04. Total currently executed automated checks are 132 when counting the 42-test full Python run and the 90-check C# harness; the 41 M3 Python tests are a subset of the full Python suite.

### Required approvals and blockers

- M4: provider/EF version, physical names/types, UUID conversion, immutable JSON storage, indexes/FKs, concurrency SQL, isolation, migrations, outbox worker and recovery tests.
- M1: project lifecycle eligibility/transition, confirmation uniqueness, project deletion/retention and whether lifecycle state shares the transaction database.
- M5: requirement revision FK contract, confirmed-event schema, inbox/report idempotency, payload/reference choice and report retry/dead-letter behavior.
- M6: canonical serialization/hash representation, validation evidence persistence/retention and revalidation/freshness semantics.
- Cross-owner: retention/privacy rules and uncertain-commit reconciliation API behavior.

No live database integration, migration, table, provider, outbox worker or real concurrency proof exists.

### Recommended next bounded task

`BE-M3-INT-05 — Advanced Geometry Fixtures and Unsupported Topology Preparation`.

Recommended scope: synthetic shared-wall, neighboring-room, irregular polygon, intersection, land-containment and invalid-topology fixtures plus explicit unsupported-operation diagnostics. It must not install Shapely, define canonical M6 rules or implement authoritative validation without separate approval.

INT-04 status: proposal and test-only conformance work complete. Owner approval, physical design, live integration, merge readiness and production readiness remain pending.

---

## BE-M3-INT-05 — Advanced geometry preparation

Date: 2026-11-17

Task identifier status: provisional, not added to the official task register.

### Approved scope

This slice adds synthetic fixtures, test-only Python/C# structural regression scenarios and proposal documentation. Production Python geometry engines, C# geometry contracts/workflows, dependencies, requirements, canonical geometry, infrastructure and databases were not modified.

### Capability audit

Current executable M3 behavior is limited to deep-copy transformations and bounded structural checks. It supports the existing eight commands, stable IDs, unique identities, closed rings, finite/positive numeric fields, valid references, non-degenerate walls and parent-wall opening fit.

It safely rejects shared-wall room move/resize and referenced-wall endpoint changes using `UNSUPPORTED_TOPOLOGY`. It does not prove polygon validity, area, overlap, containment, wall intersection, opening overlap, adjacency, junction correctness or regulatory validity.

### Fixture inventory

Created `advanced_geometry_cases.json`:

- One executable base plan with two adjacent rooms, one stable shared wall and a door on that wall.
- Eight executable structural mutations: accepted base, duplicate ID, missing wall, zero-length wall, opening beyond wall, open ring, negative opening offset and non-finite coordinate.
- Eighteen explicitly pending/basic/shared-wall/invalid/opening conformance records, including concavity, multiple floors, three rooms, junctions, reversed orientation, neighbor resize, overlap, self-intersection, zero area, repeated vertices and advanced opening relationships.

Created `land_boundary_cases.json` with seven pending cases: rectangular and irregular parcels, inside/crossing/outside/touching footprints and self-intersecting parcel.

The 25 pending records are not executable acceptance tests and are not included in passed-test counts.

### Shared-wall and neighbor design

The existing shared-wall representation is inferred when more than one room references the same Wall ID. It lacks ordered room-edge mapping, side/orientation, explicit adjacency, stable junction/vertex IDs, non-manifold rules and unambiguous opening parameterization.

The proposal compares one shared wall record, half-edge topology and per-room segments with a shared physical-wall group. No option is selected. Dynamic neighboring resize remains unsupported until M6 approves topology, precision and opening policies.

The proposed safe workflow operates on a complete candidate copy, resolves every affected room/wall/opening, rejects ambiguous topology, requests whole-plan M6 validation, returns affected IDs and awaits explicit Apply/Reject.

### Numeric and coordinate decisions

Current draft checks use local Cartesian metres, origin `(x,y)`, X right, Y up, finite doubles and exact closure. M2/M6 must approve CRS derivation, origin provenance, winding, precision, rounding, tolerance, minimum segment/area, touching semantics and canonical hash ordering. No new tolerance or regulatory dimension was introduced.

### Shapely assessment

Shapely/GEOS or another approved spatial library is anticipated for polygon validity, intersection/overlap, containment/touching, shared boundaries and spatial relationship analysis. M3 transformation ownership remains separate from M6 authoritative acceptance. No dependency was installed and no requirements file changed.

### INT-05 files

Created:

- `python-services/tests/m3_plan_contract/fixtures/advanced_geometry_cases.json`
- `python-services/tests/m3_plan_contract/fixtures/land_boundary_cases.json`
- `python-services/tests/m3_plan_contract/test_advanced_geometry.py`
- `tests/BuildMind.Application.ContractTests/Integration/AdvancedGeometryContractScenarios.cs`
- `docs/proposals/m3-plan-editor/M3-ADVANCED-GEOMETRY-PROPOSAL.md`
- `docs/proposals/m3-plan-editor/M3-GEOMETRY-CAPABILITY-MATRIX.md`
- `docs/proposals/m3-plan-editor/M3-SHARED-WALL-TOPOLOGY-PROPOSAL.md`

Modified:

- `tests/BuildMind.Application.ContractTests/Integration/FixtureLocator.cs`
- `tests/BuildMind.Application.ContractTests/Program.cs`
- `docs/proposals/m3-plan-editor/M3-PHASE-02-INTEGRATION-PROGRESS.md`

### Executed test coverage

Twelve new Python checks execute current behavior:

- Eight fixture-driven accepted/rejected structural cases.
- Shared-wall move and resize fail safely without source mutation.
- Valid opening movement preserves all stable IDs and the source snapshot.
- Opening movement beyond its wall is rejected without source mutation.

Five new C# checks verify cross-language structural acceptance of the shared-wall fixture, duplicate-ID rejection, missing-reference rejection, invalid-opening rejection and stable `UNSUPPORTED_TOPOLOGY` catalog presence.

Existing candidate tests continue to verify rejected/tampered/unvalidated candidates create no revision.

### INT-05 regression evidence

| Verification | Passed | Failed | Skipped | Warnings | Exit code |
|---|---:|---:|---:|---:|---:|
| Full Python suite | 54 | 0 | 0 | 2 pre-existing environment warnings | 0 |
| M3 Python subset, cache disabled | 53 | 0 | 0 | 0 | 0 |
| C# harness, default fixture | 95 | 0 | 0 | 0 | 0 |
| C# harness, explicit fixture | 95 | 0 | 0 | 0 | 0 |
| Application build, `--no-restore` | build passed | 0 | 0 | 0 compiler warnings | 0 |
| API build, `--no-restore` | build passed | 0 | 0 | 0 compiler warnings | 0 |

Non-duplicated automated count: 149 using the 54-test full Python suite plus 95-check C# harness. The 53 M3 tests are a subset and are not added again. Pending spatial fixtures are not tests and are not counted.

### Approval requests and blockers

- M2: approve confirmed land fixture, normalization, revision binding, CRS/origin/axis and boundary semantics.
- M6: approve canonical geometry, shared-wall/adjacency model, precision/tolerance, hash, validator/evidence contract, spatial diagnostics and Shapely decision.
- M4: approve dependency onboarding, compute payload/entity/vertex limits, timeout/cancellation and response-size policy.
- Frontend/M6 review is required before selecting wall orientation/opening offset behavior.

Unsupported: neighbor adjustment, arbitrary polygon validation, overlap, containment, wall/opening intersection, T-junction transforms and tolerance-sensitive predicates.

### Recommended INT-06 audit scope

`BE-M3-INT-06 — Phase 02 Final Integration Readiness and Change Review`.

Recommended read-only-first scope: audit all accumulated INT-01–05 diffs, contract consistency, test evidence, file inventory, documentation cross-references, uncommitted artifact safety, commit grouping and remaining owner blockers. Any corrective writes should require explicit bounded approval.

INT-05 status: executable structural preparation and provisional fixture design complete. Advanced spatial support, owner approval, live integration, merge readiness and production readiness remain pending.

---

## BE-M3-INT-06 — Final integration readiness audit

Date: 2026-11-17

The independent audit verified the repository root, branch, expected HEAD, effective Git identity, current `origin/develop`, zero staged files, one modified tracked file and exactly 22 Phase 02 new files before creation of the final audit document.

New remote branches were discovered without merging:

- M1 `feature/m1-auth-projects` adds draft owner-scoped project repository methods and project status/revision fields but no M3 ownership/lifecycle adapters.
- M4 `feature/m4-infrastructure` adds DI/composition scaffolding and tests but explicitly registers no persistence, HTTP, transaction or outbox adapters yet.

One confirmed feature-local diagnostic defect was corrected: dependency-originated plain cancellation without caller cancellation now maps to `DEPENDENCY_TIMEOUT`; actual caller cancellation continues to propagate. One regression check was added.

Final evidence:

- Full Python: 54 passed, 0 failed, 0 skipped, 2 environmental warnings, exit 0.
- M3 Python subset: 53 passed, 0 failed, 0 skipped, exit 0.
- C# harness from root: 96 passed, exit 0.
- C# harness from test project directory: 96 passed, exit 0.
- C# harness with explicit fixture: 96 passed, exit 0.
- Missing fixture negative test: clear diagnostic, expected exit 2.
- Application/API builds: 0 warnings and 0 errors.
- Non-duplicated automated total: 150.

The complete inventory, integration matrix, security findings, commit grouping and readiness decision are recorded in `M3-PHASE-02-FINAL-AUDIT.md`.

INT-06 status: complete. Phase 02 is ready for controlled feature-branch Git delivery after explicit authorization; live integration, merge readiness and production readiness remain pending.

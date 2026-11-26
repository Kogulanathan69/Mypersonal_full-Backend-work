# BuildMind M3 Backend Quality Audit

Audit date: 2026-11-17 (Asia/Colombo)
Branch: `feature/m3-plan-editor`
Audited HEAD: `763a9a7b46fd0d4a25f0ce38ff7f03baa353100b`
Assessment: independent local source and test audit; not team approval or production certification.

## Executive summary

The pre-audit claims were reproducible: the branch and HEAD matched, the original
working tree contained exactly 27 untracked M3 files and no tracked/staged
changes, the original full Python run passed 32 tests, the C# harness passed 7
checks, and both Application and API built with zero warnings/errors. No commit
or push exists after HEAD and no shared bootstrap file was modified.

The implementation is a useful provisional foundation, not a complete backend.
The Python engine safely creates detached candidates for bounded affine edits
and opening operations, but does not perform spatial polygon validity,
intersection, overlap, containment, land-boundary, adjacency, or regulatory
validation. C# versioning and confirmation are application abstractions tested
with in-memory doubles; no MySQL, HTTP, M1 lifecycle, M5 report, or M6 live
validator integration exists.

The audit found and fixed contract strictness, topology, actor-identity, result
validation, timestamp, numeric-overflow, and validation-provenance defects. The
post-fix suite passes 39 M3 Python tests, 40 Python tests repository-wide, and 11
C# harness checks. Production readiness remains **NO**.

## Verified implementation inventory

Relative to HEAD, every item below is untracked; no tracked file is changed.
The original 27 files were inspected. The audit added `PlanEditorJson.cs` and
this report, producing 29 untracked files after the audit.

### Documentation

- `docs/proposals/m3-plan-editor/GEOMETRY-CONTRACT-PROPOSAL.md`
- `docs/proposals/m3-plan-editor/M3-DEVELOPMENT-PROGRESS.md`
- `docs/proposals/m3-plan-editor/M3-QUALITY-AUDIT.md` (audit-created)
- `docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json`
- `docs/proposals/m3-plan-editor/TEAM-HANDOFF.md`

### Python implementation and tests

- `python-services/app/plan_editor/README.md`
- `python-services/app/plan_editor/__init__.py`
- `python-services/app/plan_editor/operations/__init__.py`
- `python-services/app/plan_editor/operations/engine.py`
- `python-services/app/plan_editor/schemas/__init__.py`
- `python-services/app/plan_editor/schemas/base.py`
- `python-services/app/plan_editor/schemas/commands.py`
- `python-services/app/plan_editor/schemas/geometry.py`
- `python-services/app/plan_editor/schemas/results.py`
- `python-services/tests/m3_plan_contract/__init__.py`
- `python-services/tests/m3_plan_contract/conftest.py`
- `python-services/tests/m3_plan_contract/test_operations.py`
- `python-services/tests/m3_plan_contract/test_schemas.py`

### C# implementation and tests

- `src/BuildMind.Application/Features/PlanEditor/README.md`
- `src/BuildMind.Application/Features/PlanEditor/Contracts/PlanGeometryDtos.cs`
- `src/BuildMind.Application/Features/PlanEditor/Contracts/PlanEditCommandDtos.cs`
- `src/BuildMind.Application/Features/PlanEditor/Contracts/PlanEditResultDtos.cs`
- `src/BuildMind.Application/Features/PlanEditor/Contracts/PlanEditorContractValidator.cs`
- `src/BuildMind.Application/Features/PlanEditor/Contracts/PlanEditorJson.cs` (audit-created)
- `src/BuildMind.Application/Features/PlanEditor/Versioning/PlanRevisionModels.cs`
- `src/BuildMind.Application/Features/PlanEditor/Versioning/PlanRevisionService.cs`
- `src/BuildMind.Application/Features/PlanEditor/Confirmation/FinalPlanConfirmation.cs`
- `tests/BuildMind.Application.ContractTests/BuildMind.Application.ContractTests.csproj`
- `tests/BuildMind.Application.ContractTests/Program.cs`

Generated `bin`, `obj`, `__pycache__`, `.pyc`, and `.venv` content is ignored and
not included. The secret-pattern scan found no credential or private-key
material; matches for `CancellationToken` were false positives.

## Python editor findings

| Command | Actual supported behavior | Important exclusions |
| --- | --- | --- |
| `addRoom` | Adds a new room, entirely new referenced walls, and optional openings; rejects duplicate IDs and unsupplied walls. | Cannot reuse/share an existing wall; does not prove polygon-wall correspondence, overlap freedom, or boundary containment. |
| `removeRoom` | Removes the room, deletes exclusively owned walls, preserves walls referenced by another room, and removes/rejects orphan openings per payload policy. | Does not repair neighboring polygons or prove a remaining room is topologically valid. |
| `moveRoom` | Translates the polygon and exclusively referenced walls; wall-relative openings remain stable; source is unchanged. | Rejects any shared-wall room; does not detect collision, out-of-bound movement, or polygon self-intersection. |
| `resizeRoom` | Scales polygon and exclusive walls about explicit/default anchor; proportionally moves opening offsets while preserving widths. | Rejects shared walls; no neighbor adjustment; not semantic width/length editing and not authoritative arbitrary-polygon resizing. |
| `updateWall` | Thickness changes are supported. Endpoint changes are allowed only for unreferenced walls. | Referenced endpoint changes now reject as `UNSUPPORTED_TOPOLOGY`; no connected-room adjustment. |
| `addOpening` | Adds door/window with unique ID, valid parent wall, positive width, nonnegative offset, and wall-fit check. | No architectural clearance, spacing, orientation, swing, height, or regulation checks. |
| `moveOpening` | Changes wall-relative offset and rechecks fit. | Cannot change parent wall; no clearance/regulation checks. |
| `removeOpening` | Removes the exact opening from the candidate. | No higher-level requirement validation. |

Candidate isolation uses `model_copy(deep=True)`. All mutations occur on the
candidate, so exceptions preserve the source. Stable IDs are retained except
for explicitly added entities. Finite input is enforced by Pydantic; arithmetic
overflow is now translated to `INVALID_EDIT_PAYLOAD`. Structural validation
checks ID uniqueness, references, non-degenerate walls, and opening fit only.

## C# and Python contract comparison

JSON names, UUID string representation, principal numeric types, status strings,
optional `targetId`, nullable validation fields, and the shared fixture round
trip align. Both sides now reject unsupported schema versions for geometry,
requests, and results, and accepted results require candidate geometry.

Remaining differences:

- Python validates each command-specific payload. C# stores payload as
  `JsonElement` and validates only that it is an object; command-specific C#
  payload validation is not implemented.
- Python rejects unknown members intrinsically. C# rejects them only when
  callers use `PlanEditorJson.StrictOptions`; no public API registration exists
  to enforce those options globally.
- Python list/string maxima are model constraints. C# mirrors the geometry
  limits in its explicit validator, but deserialization alone does not enforce
  them.
- Python currently accepts the all-zero UUID unless another rule rejects it;
  the C# validator rejects empty principal/entity/command/floor UUIDs.
- Python opening-fit comparison uses a `1e-9` tolerance; C# uses a strict
  comparison. The canonical tolerance is unapproved.
- C# SHA-256 is over its own serialized JSON bytes. Cross-language semantic
  equivalence is not a canonical hash guarantee.

The `0.1-draft` schema remains provisional and must not be published as the
M6-owned canonical schema.

## Versioning audit

Verified locally:

- Geometry is stored as a JSON string snapshot and historical records are not
  updated by the application service.
- Parent revision and incremented version are created.
- Restore reads a historical snapshot and appends a new revision.
- The store boundary performs compare-and-append against the expected current
  revision; the in-memory double demonstrates a stale conflict.
- Validation hash must equal the exact C# serialized snapshot hash.
- Audit fix: an `IPlanValidationEvidenceVerifier` must verify evidence with the
  authoritative adapter. `Unavailable` and `Rejected` fail before append.
- Audit fix: creator identity is a separate authenticated method argument, not
  a client-shaped command field.

Not verified or implemented:

- No MySQL tables, EF configuration, repository, transaction, row version, or
  migration exists.
- The hash is deterministic for the tested C# serializer/object, but is not an
  approved canonical JSON representation across languages or serializer versions.
- No real parallel database concurrency test exists; the in-memory store is
  single-threaded and contains no locking.
- Cross-project protection depends on the future repository honoring all
  project/plan predicates. No real authorization adapter guards revision APIs.
- Atomicity is an interface promise, not observed database behavior.

## Final confirmation audit

Verified in application logic:

- Exact requested revision lookup and current-revision comparison.
- Expected geometry hash and stored validation hash matching.
- Authenticated ownership gate; audit fix removed actor identity from the
  client-shaped command.
- Current land and requirements revision matching.
- Injected validation-freshness policy.
- Geometry invalidity and blocking issues stop confirmation.
- Unverified evidence or non-true requirement match requires acknowledgement.
- Confirmation records bind exact revision, geometry hash and validation ID.
- An atomic transaction interface explicitly delegates immutable confirmation,
  idempotency, and lifecycle transition to M4/M1 integration.

Not verified or implemented:

- No real ownership, project existence, lifecycle eligibility, persistence,
  duplicate-confirmation, or transaction implementation exists.
- The report sink runs after transaction success. A sink failure can produce an
  error after confirmation is already committed; a transactional outbox or
  retryable delivery design is required.
- Confirmation trusts stored revision evidence; safe provenance depends on all
  revisions entering through the verified revision path and repository constraints.

## Security assessment

### Critical defects

No unresolved executable critical defect is claimed because no production route
or persistence adapter exists. The following are critical integration gates:

1. Authoritative M6 validation provenance and service authentication must be
   real; the new verifier boundary must not be implemented as a field echo.
2. M4 persistence must enforce project scoping, optimistic concurrency,
   idempotent confirmation, and atomic revision/current-pointer/lifecycle writes.
3. API code must source actor IDs only from authenticated identity and use the
   separate actor parameters added by this audit.

### High-priority defects / blockers

- No canonical geometry/hash contract; cross-language hashes are not reliable.
- Spatial validity, overlap, land containment, neighbor topology, and arbitrary
  polygon safety are absent.
- C# command payloads lack command-specific validation.
- Report publication is not outbox-backed or atomic with confirmation.
- Production input/body size, request timeout, cancellation propagation, and
  internal service authentication are not configured because no route/client exists.

### Medium-priority findings

- Empty UUID handling differs between Python and C#.
- Numeric/tolerance/precision policy is unapproved.
- Diagnostics are free-form strings; no approved shared error-code catalogue is consumed.
- No tests cover actual concurrent threads/processes, DB rollback, unauthorized
  repository access, duplicate confirmation, report retry, or cancellation.
- The C# harness is a custom executable, not a standard test-runner project, so
  test discovery, per-test reporting, and CI integration are limited.

### Low-priority findings

- The existing health test emits a third-party Starlette deprecation warning.
- The sample JSON contains a UTF-8 BOM; the Python fixture intentionally uses
  `utf-8-sig`.
- `.pytest_cache` could not be inspected due to local permissions, but it is
  generated/ignored and not an untracked delivery file.

No unsafe `eval`, `exec`, shell execution, SQL, credentials, connection strings,
AI execution, direct database writes, or sensitive-data logging was found in M3 files.

## Fixed defects and added regression coverage

- Rejected result schema drift and accepted results without candidates.
- Required offset-aware validation timestamps.
- Rejected referenced-wall endpoint edits that desynchronized room polygons.
- Converted numeric overflow into structured edit rejection.
- Added strict C# JSON options and unknown-member regression coverage.
- Mirrored C# floor, string, polygon, wall-reference, axis and identifier checks.
- Removed client-shaped creator/confirmer actor IDs from commands.
- Added fail-closed authoritative validation evidence boundary.
- Replaced an overbroad `pytest.raises(Exception)` assertion.
- Added shared-resize, rollback, missing-wall opening, deterministic transform,
  authentication, and geometry-hash negative tests.

## Test evidence

### Pre-fix claim verification

- Existing regression: `1 passed`, one Starlette deprecation warning.
- M3 Python: `31 passed`.
- Full Python: `32 passed`, one warning.
- C# harness: `7 passed, 0 failed`.
- Application and API: build succeeded, `0 warnings`, `0 errors`.

### Post-fix final verification

- M3 Python: `39 passed`.
- Full Python: `40 passed`, one third-party warning.
- C# harness: `11 passed, 0 failed`.
- Application and API: build succeeded, `0 warnings`, `0 errors`.
- Tracked `git diff --check`: clean.
- Independent whitespace scan over untracked `.cs`, `.csproj`, `.py`, `.md`,
  and `.json`: clean, including final LF checks.

Mocks/test doubles are explicitly local. None of these results proves MySQL,
HTTP, M1, M5, M6, or M7 integration.

## Shapely dependency assessment

Shapely is absent and not declared in current requirements. Existing code works
without it for schema parsing, affine point transforms, wall-length arithmetic,
reference management, and candidate isolation.

A robust spatial library is needed for polygon validity/self-intersection,
room overlap/intersection, containment within land/floor boundaries, shared
boundary discovery, geometric predicates with tolerances, and reliable neighbor
topology. Those checks overlap the M6 authoritative validation boundary. M3
should request an M6/M4 dependency and contract decision rather than adding
Shapely unilaterally. No dependency was installed or requirements file modified.

## Cross-member blockers

- **M1:** authenticated ownership and eligible lifecycle transition.
- **M2:** approved land revision, boundary, CRS/origin and precision.
- **M4:** strict API boundary, DI, authenticated actor mapping, repository,
  transaction, concurrency, idempotency, migrations and outbox.
- **M5:** current requirement revision and confirmed report snapshot consumer.
- **M6:** canonical geometry, hash/tolerance conventions, validation provenance,
  authoritative spatial validation and Shapely decision.
- **M7:** AI command adapter and independent acceptance/contract review.

## Readiness assessment

**Production readiness: NOT READY.** The work is provisional application and
compute logic with local tests. Required contracts and all production adapters
are missing.

**Git delivery readiness: NOT READY FOR MERGE; suitable for review as a draft
feature patch.** The working tree is intentionally uncommitted and untracked.
Before staging, review all 29 files explicitly, obtain the legacy/revised
ownership reconciliation, ratify the canonical contract, and split commits by
contracts, Python operations/tests, C# orchestration/tests, and documentation.
No commit, push, merge, credential change, dependency installation, migration,
or shared bootstrap modification was performed during this audit.

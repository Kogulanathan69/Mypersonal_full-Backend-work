# Development progress — Captain full-backend completion

- Main workspace: `C:\Users\kogul\Documents\BuildMind-Platform` on `feature/bm-final-backend-integration` at `fec3254`. It is dirty with work of unverified origin, all preserved untouched:
  - `BuildMind.sln`;
  - `TestResults/`;
  - the M6 handoff doc;
  - `Generation/Orchestration/`;
  - `GenerationWorkflowTests.cs`;
  - `BuildMind.Generation.IsolatedTests/`.
- Captain worktree: `C:\Users\kogul\Documents\BuildMind-Worktrees\captain-full-backend-completion`. Branch `feature/captain-full-backend-completion` was created from `fec3254`; the branch and directory did not exist before.
- Starting SHA: `fec32547e7b2508f9d7af71206dc651cfebe5794`.
- Build/test host: WSL Ubuntu-24.04 (.NET SDK 10.0.115, Docker 29.1.3), with a one-way copy at `~/bm-captain/src` (`~/bm-captain/sync.sh`) to respect G17. It is separate from `~/bm-final` and `~/projects`.

## Phase 0 — audit (complete)

Baseline, `dotnet build BuildMind.sln -c Release`: 0 warnings, 0 errors.

| Suite | Result |
|---|---|
| Application.UnitTests | 233 pass |
| Infrastructure.Tests | 1140 pass |
| Reports.Tests | 163 pass, 1 skipped (`IndependentParserVerifiesExactSyntheticReport`, needs `BUILDMIND_PDF_QA_PYTHON`) |
| Requirements.Tests | 456 pass |
| Workflow.Tests | 197 pass |
| Application.ContractTests | Console runner, exit 0 |
| Persistence.IntegrationTests | NOT RUN: outside the solution by design; needs a disposable MySQL (Phase 2) |
| python-services pytest | 954 pass |
| rag-service pytest | 238 pass |

The worktree has no venvs, so the Python suites ran with the existing `python-services/.venv` (Python 3.13) against the worktree sources.

Gap map: see BACKEND-IMPLEMENTATION-MATRIX.

## Phase 1 — composition and endpoint registration

Implemented the **Captain local-integration profile** (`CaptainIntegration:Enabled`). It is Development only and requires M1 authentication. Its start-up guards fail closed. Production activation is not approved.

| File | Change |
|---|---|
| `src/BuildMind.API/Composition/CaptainLocalIntegration.cs` | Profile guard. Registers Requirements (existing M5 services and modules) and the revision-history GET routes. |
| `src/BuildMind.API/Program.cs` | One call to `AddCaptainLocalIntegration(m1AuthEnabled)` |
| `src/BuildMind.Infrastructure/CaptainIntegration/CaptainIntegrationInfrastructure.cs` | Registers adapters: owner authorization port, MySQL history query, protected requirement repository, catalogue provider |
| `.../OwnerProtectedRequirementRevisionRepository.cs` | Adapter for the unchanged port. Fresh owner read, then `TrySaveAuthorizedAtomicallyAsync` (project row lock + owner + version in the save transaction) |
| `.../ConfiguredRequirementCatalogueProvider.cs` | Policy loaded from the operator file `CaptainIntegration:RequirementCatalogueFile`. No compiled-in values; an invalid file means unavailable (fail closed). |
| `docs/captain-development/samples/requirement-catalogue.synthetic.json` | Labeled synthetic policy `synthetic-fixture-v1`, matching the M6 generation vocabulary |
| `tests/.../CaptainIntegration/CaptainLocalIntegrationTests.cs` | 18 tests |

Existing gates are unchanged:
- `AddIsolatedRequirementsFeature` stays Testing-only;
- the M1 Development-only guard;
- the M7 retrieval guard.

Tests: the new focused suite passed 18/18. Full Infrastructure.Tests passed 1158/1158 (baseline 1140 + 18). Release build: 0 warnings.

## Phase 2 — local MySQL, Survey persistence, live verification

- **Database:** container `bm-captain-mysql` (MySQL 8.4.13, loopback 3417, verified TLS, least-privilege accounts). All 4 existing migrations applied with the Option B ranged script. See DATABASE-SETUP.
- **Survey persistence (new):**
  - `SurveyRecord`, `SurveyRecordConfiguration`;
  - `SurveyRepository`:
    - protected insert: fresh owner read, then project row `FOR UPDATE` + owner + version in the insert transaction;
    - project-scoped reads;
    - CAS state updates;
  - migration `20261124095515_AddSurveys`.
- **Composition:** Survey is registered under the Captain profile:
  - existing `SurveyUploadService`, `SurveyStatusService` and `SurveyEndpointModule`;
  - existing `PrivateSurveyFileStorage`, rooted at `CaptainIntegration:SurveyStorageDirectory`.
- **Defect fixed (M1/M4 code):** `RefreshTokenStore.RotateAsync` intermittently deadlocked under concurrent presentation of the same token. It now takes a locking read on the presented token row first. Evidence: S03 failed in 1 of 3 full runs before the fix; after it, 10/10 consecutive runs of `SessionSecurityLiveTests` passed. Before the fix the failure was already fail-closed (deadlock → 503 by the approved D06 mapping) but it broke single-winner semantics.
- **Migration pin tests updated:** `BuildMindMigrationTests` now has `M4Migrations`/`M4Tables` plus a `CaptainMigrations` registry, and `RequirementRevisionRepositoryTests.R01` was updated. The M4 assertions are unchanged; Captain tables are also verified to roll back to the M4 baseline.
- **New live tests:**
  - `SurveyRepositoryLiveTests` (4): owner insert/scoped read, non-owner denied, authorization change between read and write denied, CAS;
  - `ProtectedRequirementLiveTests` (3): owner chain + stale expected revision, non-owner denied, stale authorization version denied inside the lock.
- **Live API:** `tools/captain/run-api.sh` + `smoke_phase2.py`: 19/19.

**Not done in Phase 2** (moved to their feature phases): generation jobs, plan candidates/revision writes, reports, architect requests.

Post-Phase-2 commit `279ff2f`: LF endings for `tools/captain` scripts.

## Phase 3 — AI generation workflow

The interrupted `generation.py` `_envelope()` edit had **not** been applied (verified at HEAD `279ff2f`). It is now implemented: JSON-mode validation through the unchanged `GenerationResult` contract, with no duplicate return paths.

### Python (python-services, commit `0ebb7d8`)

| File | Change |
|---|---|
| `app/internal/auth.py` | `X-BuildMind-Internal-Token`, adopting the rag-service G11 pattern. Fail closed: no token configured gives 503; a missing, wrong or repeated header gives 401. Disabling requires `BUILDMIND_PY_ENVIRONMENT=development`. |
| `app/internal/land.py` | `POST /internal/v1/land/validate` over the existing `validate_confirmed_parcel` |
| `app/internal/generation.py` | `POST /internal/v1/generation/run`: D02 `assess_feasibility`, then D04 `generate_alternatives`, then M3 draft geometry. Truthful outcomes: `NEEDS_REVIEW` / `INFEASIBLE` / `UNSUPPORTED` / `UNAVAILABLE` with no alternatives. Synthetic setbacks only through the explicit flag. Every alternative carries the WARNING diagnostics `SYNTHETIC_REGULATORY_EVIDENCE` and `PHYSICAL_ACCESS_NOT_EVALUATED`. |
| `app/internal/plan_validation.py` | `POST /internal/v1/plans/validate`: strict schema, single floor, rooms valid, inside the parcel and buildable area, no overlaps, openings fit, room counts |
| `app/main.py` | `create_app(auth)`; docs/OpenAPI disabled; fixed-code errors |
| `tests/captain/test_internal_api.py` | 17 tests (auth cases, land, success and failure envelopes, determinism, plan validation, no coercion) |

### C#

- **Application** (`Features/Planning/`):
  - `PlanningAccess`: fresh owner authorization and version;
  - `LandService`;
  - `GenerationService`: idempotency, stale-input binding, deterministic room program from requirements plus the policy, evidence modes, job lifecycle `Running` → `Succeeded`/`Failed`, dependency failure recorded;
  - `PlanSaveService`: server re-validation, then server-issued evidence, then plan revision 1.
- **Infrastructure:**
  - `PlanningComputeClient`: every generation response is parsed by the existing strict `GenerationContractBoundary`, which is the live parity check;
  - `FileGenerationPolicyProvider`;
  - MySQL stores `MySqlLandRevisionStore`, `MySqlGenerationJobStore` and `MySqlPlanCreationStore`. All use `ProtectedPlanningWriter` (project row `FOR UPDATE` + owner + version), so idempotency-key and head checks are race-free.
- **Migration** `20261124104612_AddCaptainPlanningWorkflow` (additive): `land_revisions`, `generation_jobs` (unique project+actor+key), `generation_candidates`, `plan_validation_evidence` (server-issued evidence ledger).
- **API:** `PlanningEndpointModule` under the Captain profile. Generation uses the expensive-operation limiter.
- **Synthetic labelling:**
  - policy file `docs/captain-development/samples/generation-policy.synthetic.json` ("DEVELOPMENT ONLY");
  - saved plans record `regulatoryEvidenceStatus = SYNTHETIC_FIXTURE`, never `REVIEWED`;
  - every response carries a preliminary notice ("not construction-ready").

### WSL Python environment

`python3-venv`, pip and passwordless sudo are absent. A pinned **uv 0.9.5** binary was downloaded and its SHA-256 verified against the published checksum (`2cf10bab…4f66`). It created the Python 3.13.9 venvs `~/bm-captain/venv-{python-services,rag-service}` from the pinned requirements (`tools/captain/python-setup.sh`).

### Results

| Suite | Result |
|---|---|
| python-services, Windows venv | 971/971 |
| python-services, WSL venv | 971/971 |
| Application.UnitTests | 246/246 (+13) |
| Infrastructure.Tests | 1160/1160 |
| Live MySQL | 92/92 (+4 planning store tests) |
| Live journey phases 2–3 (`smoke_journey.py`) | 32/32 |

### Review corrections (after the Phase 3 commit `96975a5`)

- **Refresh-token lock order.**
  - Before the fix:
    - logout-all, disable and password reset lock the `users` row first, then tokens;
    - rotation took token rows first, then `users` (FK check on insert, token_version bump on replay);
    - logout took family rows without `users`.
    - Rotation therefore ran opposite to logout-all and reset, and deadlocks were possible.
  - Fix: every session writer now takes the `users` row lock FIRST (`SessionSql.LockUserAsync`). Rotation then keeps the token-row lock (the Phase 2 fix is preserved), and logout also locks `users` first. Password reset (reset row → users → tokens) has no cycle with these.
  - New live tests:
    - **S08**: same family, consumed T1 + current T2, two rotations, a replay, logout and logout-all concurrently, 8 rounds;
    - **S09**: rotation vs logout-all vs password reset, 8 rounds.
  - Both assert no exception and that every token ends up revoked. Revocation semantics are unchanged.
  - Result: `SessionSecurityLiveTests` passed 5/5 consecutive runs (9 tests each). No deadlock was observed in 80 mixed rounds. This is evidence, not proof.
- **Database TLS: VerifyFull** (see DATABASE-SETUP §TLS). Two provider requirements were handled without weakening TLS: a client certificate, and a PKCS#1 key. Live MySQL 94/94 and live journey 32/32 under VerifyFull; CLI negative identity check passed.
- **Internal Python auth:** fail closed. Covered by `tests/captain/test_internal_api.py`: missing → 401, invalid → 401, repeated header → 401, unconfigured → 503, valid → passes. Disabling is possible only with `BUILDMIND_PY_ENVIRONMENT=development`; production/missing environment raises at start-up. The live API used the real token.
- **Readiness smoke (Phase 3 review):** see the end of the Phase 3 section.

## Phase 4 — plan editing, revisions, confirmation

The existing M3 services are reused unchanged:
- `CandidatePreviewService`;
- `EditValidationApplicationFacade` and `TrustedCandidateActionFacade`;
- `FinalPlanConfirmationService`;
- the full `PlanEditorEndpointModule` (history + candidates create/apply/reject).

The Captain work supplies their adapters:

| Adapter | Behaviour |
|---|---|
| `MySqlPlanRevisionStore` | `IPlanRevisionStore`: immutable revisions, protected head CAS via `PlanHead.AppendAsync` |
| `CaptainApplicableInputs`, `CaptainConfirmedLandPort`, `CaptainAuthorizedRequirementPort` | Current land and requirement revisions; owner-authorized requirement access with the authorization snapshot |
| `PlanEditorComputeClient` | Python `POST /internal/v1/plans/edit-preview` over the existing `apply_command` engine. Echoes the verified head as the base. Maps dependency failures to Timeout/HttpRequest exceptions so the existing M3 503/504 mapping applies. |
| `EditGeometryHandoff` | Request-scoped. The M3 evidence port receives only the hash, so the accepted geometry is passed under that exact hash. |
| `LedgerValidationEvidenceSource` | Server-side Python plan validation (parcel, buildable area, overlaps, openings, counts), then server-issued evidence recorded in `plan_validation_evidence` under the protected write |
| `LedgerValidationEvidenceVerifier` | Verified only when the ledger row matches project, plan, hash and every field exactly |
| `MySqlCandidatePreviewStore` (store + transaction) | Unique (project, plan, commandId). Apply / reject / expire are protected transactions with candidate state CAS, head CAS and immutable append. |
| `ConfiguredValidationFreshnessPolicy` | `CaptainIntegration:ValidationFreshnessHours`, default 24 h. This is a provisional product value. |
| `MySqlFinalConfirmationTransaction` | Protected write. The revision must be the head; one confirmation per plan. **No project lifecycle transition is applied**, because no approved M1 lifecycle writer exists. |
| `DurableConfirmationSink` | The confirmation row is the durable hand-off to reports |
| `MySqlPlanCatalog` | Plan list with head and confirmation |
| `MicrosecondTimeProvider` | M3 compares timestamps exactly and MySQL keeps microseconds |
| `PlanLifecycleEndpointModule` | `GET /projects/{id}/plans`, `GET /projects/{id}/plans/{planId}`, `POST /projects/{id}/plans/{planId}/confirmation` |

- **Migration** `20261124112814_AddCaptainPlanLifecycle` (additive): `plan_edit_candidates`, `plan_confirmations`.
- **Python change:** `ROOM_COUNTS_MISMATCH` is now severity WARNING, reported via `requirementsMatch=false`. Under M3 semantics it requires explicit acknowledgement; geometry failures stay CRITICAL (blocking).
- **Honesty gates (existing M3 behaviour, verified live):** apply and confirmation require `acknowledgedWarnings=true` unless the evidence is `VERIFIED`. Synthetic evidence is never `VERIFIED`.

### Results

| Suite | Result |
|---|---|
| Captain Python tests | 20/20 (+3 edit preview) |
| Application.UnitTests | 246/246 |
| Infrastructure.Tests | 1160/1160 (run without live connection-string variables) |
| Live MySQL | 94/94 |
| Live journey phases 2–4 | 55/55 |

Phase 4 journey coverage:
- out-of-parcel edit → 422 `VALIDATION_EVIDENCE_INVALID`;
- stale base revision → 409/404;
- cross-user list/read/edit/apply/confirm → 404;
- apply without acknowledgement → 422; apply → v2; duplicate apply → 409;
- history [2, 1]; edit on the superseded revision → 409; explicit reject → 200;
- confirmation: wrong hash → 409, superseded revision → 409, no acknowledgement → 422, success → 201 with a non-approval notice, duplicate → 409.

**Observations:**
1. Back-to-back journey runs hit HTTP 429 on register. The auth-strict limiter (10/min) is working; runs are spaced by more than 60 s.
2. A harness defect: sourcing `env.sh` before the unit suites leaked `ConnectionStrings__BuildMind` and failed one startup test. This is not a product defect; it is fixed in the harness, and the rerun gave 1160/1160.

**Not covered by dedicated live store tests:** the Phase 4 adapters are covered by the live API journey against real MySQL and Python, plus the unchanged M3 unit suites.

- **Readiness smoke:** the `smoke_phase2.py` readiness check now asserts the exact `mysql` entry (`Healthy`/`reachable`); its requirements read-back now compares `revisionId` (it previously compared `None == None`). Rerun: 19/19.

## Phase 5 — Regulation RAG integration (fail closed)

**STEP 8B:** the repository never defines its scope; it is mentioned only as "M7 STEP 8B: strictly on hold". It is treated conservatively: **no change** to `rag-service`, `RagServiceClient`, the M7 retrieval module or its registration guard. Phase 5 only *consumes* the existing approved contracts (evidence v1, constraints v0) in the Development-only profile.

**Implemented:**
- **`RagRegulatoryEvidenceProvider`**, generation evidence mode `rag-constraints`. It queries the existing `IRegulatoryConstraintSource` for Front/Rear/Side setback kinds, bound to project revision + land revision + requirements revision, and assesses the result with the existing `ConstraintEvidenceGate` (`allowSynthetic: false`).
  - Synthetic packs never count as reviewed.
  - Fully verified kinds are still passed to generation as `UNVERIFIED` with the gap `PARCEL_EDGE_ROLE_MAPPING_UNAPPROVED`. Mapping front/rear/side rules to parcel edges is an open M6 decision (doc 29 §11), so no setback value is placed on an edge and generation answers `NEEDS_REVIEW`.
  - A RAG dependency failure propagates: 503/504, and no job is created.
  - A missing jurisdiction gives `MISSING`, and the service is never queried.
- **Traceability:** the evidence pack id is stored on the job (`generation_jobs.evidence_pack_id`, migration `20261124114516_AddGenerationEvidencePackId`, additive nullable column) and returned in the job view.
- **Policy:** `docs/captain-development/samples/generation-policy.rag.json` (labelled development only; operator-configured jurisdiction).

**Live environment:**
- rag-service runs in WSL from a pinned Python 3.13 venv with the internal token required.
- **Real corpus: BLOCKED in this environment.** The approved STEP 4 manifest at `C:\BuildMind-PDF-Processor` pins source identities with Windows path separators. Under Linux the files do not resolve, and a separator-normalised copy is correctly refused by the loader ("Approved identity/scope mismatch"). That integrity control was **not bypassed**; the helper was deleted.
- The API cannot run on Windows (G17), and WSL is in NAT mode, so Windows loopback is unreachable.
- rag-service therefore runs **without a corpus**. Search, sources and constraints answer 503, which live-verifies the fail-closed paths.
- Real-corpus retrieval evidence from M7 (EV-20261119-11, Windows) is reference only and is not re-claimed.

**Results:**

| Suite | Result |
|---|---|
| Application.UnitTests | 252/252 (+6 RAG evidence tests) |
| rag-service pytest, WSL | 238/238 |
| Infrastructure.Tests | 1160/1160 |
| Live MySQL | 94/94 |
| Live journey phases 2–5 | 63/63 |

Phase 5 journey checks:
- readiness `rag = Degraded/unavailable`;
- retrieval anonymous → 401;
- retrieval authenticated → 503 with no passages;
- rag-mode generation → 503 `DEPENDENCY_UNAVAILABLE` with no job published (repeat → 503, not a replay).

## Phase 6 — preliminary PDF reports and architect workflow

The existing M5 services are reused unchanged:
- `ReportGenerationCandidate`, with `ReportDataBuilder` and the existing `PreliminaryPdfRenderer`;
- `ReportReadCandidate`, with `ReportPrivatePdfReadVerifier` and `ReportArtifactIntegrity`;
- `ArchitectWorkflowCandidate`.

The Captain work supplies their boundaries:

| Component | Behaviour |
|---|---|
| `CaptainReportBoundary` (generation + read) | **Accept:** fresh owner authorization. The persisted confirmation must match plan, revision and geometry hash. The snapshot is built from immutable sources (requirement revision, plan revision, confirmation, evidence) plus a fixed disclaimer, then inserted as Pending under the protected write; an existing report id gives `Existing`. **Publish:** write the artifact privately, read it back through the bounded verifier, then CAS Pending→Ready under the protected write. If the CAS fails, the unreferenced file is deleted. **Fail:** CAS to Failed. **Read:** owner check, bounded verified read, and an owner **re-check after the read**. Missing and unauthorized are both reported as Hidden. |
| `PrivateReportArtifactStore` | Operator-provisioned directory. File names are the opaque server GUID only, so there is no user input in paths. Create-new + atomic rename. |
| `CaptainArchitectBoundary` | Recipient must be in the operator-supplied architect directory (synthetic sample). The acknowledged policy must equal `CaptainIntegration:SharingPolicyVersion`. Only a Ready, intact report bound to the plan's persisted confirmation is shareable; other document types are NotEligible (scope undefined, G12). Consent and request are stored together under the protected write. Withdrawal is an atomic Pending→Withdrawn CAS. **Nothing is dispatched externally.** |
| `DeliverablesEndpointModule` | `POST/GET /projects/{id}/reports[/{reportId}[/download]]`; `POST/GET /projects/{id}/architect-requests[/{requestId}[/withdraw]]`. Report generation uses the expensive limiter. Downloads are `application/pdf` attachments with `no-store`. |

**Report honesty:**
- The disclaimer `captain-preliminary-disclaimer/1` states: preliminary 2D; synthetic dimensions and setbacks; no doors or circulation; not a construction drawing, design or approval; the evidence status is not a compliance determination.
- The existing `ReportDataBuilder` refuses non-`VERIFIED` evidence unless the confirmation acknowledged warnings.

**Migration** `20261124120204_AddCaptainDeliverables` (additive): `reports` (FK to the confirmation, unique artifact id) and `architect_requests` (unique consent id, FK to the report).

### Results

| Suite | Result |
|---|---|
| Application.UnitTests | 252/252 |
| Infrastructure.Tests | 1160/1160 |
| Reports.Tests | 163 passed + 1 skipped (`BUILDMIND_PDF_QA_PYTHON` unset; run in Phase 7) |
| Requirements.Tests | 456/456 |
| Workflow.Tests | 197/197 |
| ContractTests | exit 0 |
| Live MySQL | 94/94 |
| Live journey phases 2–6 | 82/82 |

Phase 6 journey coverage:
- report: hash mismatch → 404; cross-user generate/status/download → 404; generate → 201 Ready; idempotent repeat → 200; PDF attachment with `no-store`; unknown report → 404;
- architect: missing consent → 422; unknown recipient → 422; stale policy → 409; cross-user → 404; create → 201 Pending (not dispatched); list; withdraw → 200; second withdraw → 409.

**Not covered by dedicated live store tests:** the Phase 6 boundaries are exercised by the live API journey against real MySQL and file storage, plus the unchanged M5 unit suites.

## Phase 7 — full verification (final)

- **Resume note:** the session resumed after an interruption. The stack was down and Phase 6 was uncommitted. Its suites were re-run (all green) and it was committed as `7213b42`. Nothing else was repeated.
- **Tooling** (`d2d74bc`): phase 7 security journey, PDF content check, `unit-suites.sh`, `python-and-pdf-checks.sh`.
- **Final results:**
  - live journey phases 2–7: 107/107;
  - .NET: Application 252, Infrastructure 1160, Reports 164 (PDF-QA executed), Requirements 456, Workflow 197, ContractTests exit 0;
  - live MySQL: 94/94;
  - python-services 974; rag-service 238;
  - independent PDF text check: pass.
- **Final state:** see FINAL-BACKEND-HANDOVER (A: substantially complete with recorded gaps; B: locally verified; C: partial; D: production NO-GO).

## Master execution order (2026-11-24): full backend completion

**Phase 0** (`f4880f6`): requirements audit and master matrix.
- The V3 register gives 46 product routes. The "180+" claim is unsupported.
- At start: 9 routes missing, R-13 partial, R-34 blocked, admin out of scope.

**Phases 3–13** (`86b9593`): implementation and verification.

**Survey extraction:**
- **Python:** internal `/internal/v1/survey/extract`. Readable PDFs go through the text pipeline; otherwise local Tesseract OCR, always NEEDS_REVIEW.
- **C#:** integrity-checked blob reader; DB lease queue with CAS claims, stale reclaim, bounded attempts and lease-token publication in a protected transaction; hosted worker with bounded concurrency; result endpoint.
- **Schema:** migration `AddSurveyExtraction`.

**V3 routes:**
- `/users/me` (Captain module only, after the M1 pin caught the first placement);
- land validate/confirm;
- alternatives/select;
- `POST /plans`;
- edits/validate;
- `POST /versions` (server re-validation + evidence + head CAS);
- confirm alias;
- AI proposals (truthful 503);
- reports list.

**Verification:**

| Suite | Result |
|---|---|
| .NET | Application 252, Infrastructure 1160, Reports 164, Requirements 456, Workflow 197, ContractTests exit 0 |
| Live MySQL | 103/103 (0 leftovers) |
| python-services | 981 |
| Local OCR | 3 |
| rag-service | 238 |
| PDF check | pass |
| Live journey (phases 2–8) | **136/136** |
| OpenAPI | 57 operations |

**Documents:** FRONTEND-INTEGRATION-GUIDE, SECURITY-REVIEW, TECHNICAL-DECISION-REGISTER, DATABASE-MIGRATION-REGISTER, FEATURE-COMPLETION-MATRIX and INTEGRATION-COMPLETION-MATRIX are new. Matrix, inventory, blockers, test evidence, runbook and handover are updated.

**Final state:**
- A: complete for the approved register (43/46 implemented; 2 partial; 1 blocked but truthful);
- B: verified locally;
- C: partial;
- D: production NO-GO;
- E: admin/AI/email/dispatch/cost-3D out of scope or blocked by decision.

**Next task** (requires owner decisions; see the handover): contract ratification by M2/M3/M6 and M4 review of the migrations. No push or merge without explicit authorization.

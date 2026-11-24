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

**Review corrections:** the `smoke_phase2.py` readiness check now asserts the exact `mysql` entry (`Healthy`/`reachable`); its requirements read-back now compares `revisionId` (it previously compared `None == None`). Rerun: 19/19.

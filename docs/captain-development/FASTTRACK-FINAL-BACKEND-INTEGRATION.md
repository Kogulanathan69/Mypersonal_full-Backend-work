# BuildMind Fast-Track Final Backend Integration Report

Date: 2026-11-28
Canonical Working Repository: `C:\Users\kogul\Documents\BuildMind-Worktrees\captain-full-backend-completion\Mypersonal_full-Backend-work`
Branch: `feature/fasttrack-final-backend`
Personal Baseline: `688756613a553e14a58533cee625e263811fadc9`
Team P0 PR #119 Isolated Worktree: `C:\Users\kogul\Documents\BuildMind-Worktrees\m4-pr119-land-migration` (`feature/m2-survey-land-d04`, clean at `734729d7f258671a7969377ba11d21ed7f7660c0`)

---

## 1. Executive Summary & Readiness Verdict

- **Development / Local Team Testing / Swagger / Portable Docker Readiness:** **READY**
  - Portable Docker stack (`tools/portable/start.sh`) builds cleanly (`0 Warning(s), 0 Error(s)`), applies all **18** EF Core migrations over `VerifyFull` TLS (`20261127153155_AddLandMetadataAndConstraints` inclusive), registers **148** Swagger operations across **126** paths on `http://localhost:5182/swagger/index.html` (Mailpit at `http://localhost:8027`), and passes end-to-end authenticated HTTP workflows against real MySQL 8.4 + mTLS `python-services`.
- **STEP 8B (Approved Sri Lanka Regulation Corpus Activation):** **ON HOLD** (`rag-service` stays fail-closed / reports `Degraded:unavailable` in readiness until an approved corpus manifest and index are supplied).
- **Production Deployment / Live Release:** **NO-GO** (pending G19 independent code/security review, external SMTP/LLM/geocoding provider selection and credentials, STEP 8B corpus/constraint ratification, RH09 `MySql.Data` provider resolution / G03 licence decision, and CI push approval).

---

## 2. Repository Reconciliation & Preserved Baseline

All integration work preserved the canonical personal baseline (`688756613a553e14a58533cee625e263811fadc9`) and avoided duplicating already-implemented M3/M5/M6 tables or services from divergent team PRs:

1. **Portability PR #1 (`origin/main` `a6ec895` -> merge commit `65360ad`)**:
   - Merged cleanly into `feature/fasttrack-final-backend` (`.github/workflows/backend-ci.yml`, `docs/portable/INSTALLATION.md`, `tools/portable/*`, `deploy/portable/*`).
   - Two latent portable-deployment issues found and fixed during live Docker testing:
     - `tools/portable/compose.sh`: sent `docker compose version` and `SAMPLE_FOUND:` messages to stderr (`>&2`) so SQL captured by `tools/portable/migrate.sh` does not fail MySQL syntax checks (`ERROR 1064`) when new forward migrations are applied.
     - `deploy/portable/compose.yml`: added `BUILDMIND_PY_INTERNAL_TOKENS_FILE: /run/bm/secrets/python-internal.token` to `python-services` so authenticated `/internal/*` mTLS+token calls succeed instead of returning `503 AUTH_NOT_CONFIGURED`.
2. **Team P0 PR #119 (`feature/m2-survey-land-d04`, `734729d7f258671a7969377ba11d21ed7f7660c0`)**:
   - Verified cleanly in isolated worktree `m4-pr119-land-migration` against ephemeral MySQL 8.4: `BuildMind.sln` Release build (`0 Warning(s), 0 Error(s)`), `has-pending-model-changes` clean, targeted `BuildMind.Infrastructure.Tests` **70/70 passed**, and targeted `BuildMind.Persistence.IntegrationTests` **30/30 passed**.
   - Avoided touching `OneDrive\Desktop\BuildMind_backend` or cross-contaminating branches.
3. **Personal Baseline Features Preserved Intact**:
   - All 17 prior Captain/M1–M7 migrations (`20261118133137_InitialFeatureSchema` through `20261126173737_AddRequirementMutationReceipts`), G12 requirement mutation receipts, M3 plan lifecycle & edit candidates, M5 reports/deliverables/architect portal, M6 exact validation digests & PROJ land frames, M2 survey extraction lease queue, and M7 fail-closed RAG regulation evidence provider (`PARCEL_EDGE_ROLE_MAPPING_UNAPPROVED` retained).
   - Scope exclusions enforced: **no** cost estimation, **no** BOQ, **no** customer 3D generation, and **no** payment processing.

---

## 3. Integrated Functionality by Module (Checkpoints 1–3 + Final)

| Module | Integrated Capabilities | Key Files |
|---|---|---|
| **M1 Session & Mutation Security (PR #137 + End-to-End Wiring)** | Added `M1MutationSessionGuard` (`users` row `FOR UPDATE` checking `is_active` and `token_version` before `projects` row lock in fixed global hierarchy); added `ICurrentUser.TokenVersion` (`"tv"` claim extraction in `HttpContextCurrentUser`); wired `currentUser?.TokenVersion` across `ProjectMutationAuthorization.LockAndCheckAsync` and every protected repository (`OwnerProtectedRequirementRevisionRepository`, `RequirementRevisionRepository`, `RequirementMutationStore`, `SurveyRepository`, `ProtectedPlanningWriter`, `PlanLifecycleAdapters`, `DeliverableAdapters`, `LandEdgeRoles`, `SurveyStorageReconciliation`). | `M1MutationSessionGuard.cs`, `AuthenticationServiceCollectionExtensions.cs`, `ICurrentUser.cs`, `HttpContextCurrentUser.cs`, `ProjectMutationAuthorization.cs`, `PlanningStores.cs`, `PlanLifecycleAdapters.cs`, `DeliverableAdapters.cs`, `LandEdgeRoles.cs`, `RequirementMutationStore.cs`, `RequirementRevisionRepository.cs`, `OwnerProtectedRequirementRevisionRepository.cs`, `SurveyRepository.cs`, `SurveyStorageReconciliation.cs`, `CaptainIntegrationInfrastructure.cs` |
| **M2 Survey, Land, CRS & Metadata** | Checkpoint 1 (`ee503f4`): Fixed anchor-shift edge lineage continuity in `python-services/app/internal/land_frame.py` + `PlanningModels.cs` / `PlanningServices.cs` / `PlanningComputeClient.cs` and CRS alias normalization in `PythonLandGeometryValidator.cs`. Checkpoint 2 (`d3aa25b`): Added `LandLocationInput`, `ReportedLandAreaInput`, fail-closed verification/review checks in `LandService`, 16 nullable metadata columns & 5 MySQL check constraints (`ck_land_revisions_*`), and extended `PutLandHttpRequest` / `LandRevisionSummaryDto` / `CaptainReadModels` with `perimeterM`, `location`, and `reportedArea`. | `land_frame.py`, `PythonLandGeometryValidator.cs`, `PlanningServices.cs`, `PlanningStores.cs`, `PlanningRecordConfigurations.cs`, `PlanningEndpointModule.cs`, `CaptainReadModels.cs`, `20261127153155_AddLandMetadataAndConstraints.cs` |
| **M3 / V3 Manual Floor-Plan Creation** | Added `CreateManualPlanCommand` + `PlanSaveService.CreateManualPlanAsync` and mapped `POST /api/v1/projects/{projectId}/plans/manual` (`CreateManualPlan`, `CreateManualPlanHttpRequest`), executing authoritative Python validation (`ValidateAsync`) and persisting revision 1 via `IPlanCreationStore`. | `PlanningServices.cs`, `V3RouteCompletionEndpointModule.cs`, `PlanningServiceTests.cs` |
| **M4 Migrations & Concurrency** | Added additive 18th EF Core migration `20261127153155_AddLandMetadataAndConstraints` (`16` columns + `5` check constraints on `land_revisions`, symmetric `Down`); updated `BuildMindMigrationTests` (`M06` + new `M07_land_metadata_migration_upgrades_rolls_back_and_reapplies`) and `RequirementMutationMigrationLiveTests`; updated `RequirementMutationLiveTests.G12_L07_L08_Concurrent_mutations_are_serialized` to reflect `users -> projects` lock acquisition while retaining `projects FOR UPDATE NOWAIT` contention verification. | `20261127153155_AddLandMetadataAndConstraints.cs`, `BuildMindDbContextModelSnapshot.cs`, `PersistenceRegistrationTests.cs`, `BuildMindMigrationTests.cs`, `RequirementMutationMigrationLiveTests.cs`, `RequirementMutationLiveTests.cs` |
| **M5 Authoritative Requirements & Deliverables** | Existing M5 catalogue, owner-protected requirement mutations, G12 idempotent receipts, PDF deliverable generation/signing/downloads, and architect requests wired cleanly with M1 transaction-time session guard (`TokenVersion`). | `OwnerProtectedRequirementRevisionRepository.cs`, `RequirementMutationStore.cs`, `DeliverableAdapters.cs` |
| **M6 Planning Generation & Vastu Tie-Breaker** | Added internal non-blocking `_vastu_score` tie-breaker in `python-services/app/internal/generation.py` (never fails generation, never alters feasibility gates, and never surfaces Vastu fields/diagnostics in API responses); verified 3 distinct feasible alternatives returned without Vastu disclosure in both pytest and live API smoke tests. | `python-services/app/internal/generation.py`, `python-services/tests/captain/test_internal_api.py` |
| **M7 RAG & Portable Deployment** | Retained fail-closed M7 RAG posture (STEP 8B on hold; zero fabricated regulations); fixed `tools/portable/compose.sh` and `deploy/portable/compose.yml` for reliable clean migration #18 upgrades and authenticated C#-to-Python mTLS inside portable containers. | `tools/portable/compose.sh`, `deploy/portable/compose.yml` |

---

## 4. Verification Results & Test Evidence

All verification runs executed inside Ubuntu 24.04 WSL / Docker against real MySQL 8.4.13 (`SslMode=Required` / `VerifyFull`):

### 4.1 `.NET` Solution Build & Suites (`task-823`)
- `dotnet build BuildMind.sln --configuration Release --no-restore`: **0 Warning(s), 0 Error(s)**.
- `dotnet-ef migrations has-pending-model-changes --context BuildMindDbContext`: **No changes have been made to the model since the last migration.** (Also re-verified inside Docker build in `task-1265`: **0 Warning(s), 0 Error(s)**, no pending model changes.)
- `BuildMind.Application.UnitTests`: **270 passed, 0 failed, 0 skipped**.
- `BuildMind.Infrastructure.Tests`: **1271 passed, 0 failed, 0 skipped**.
- `BuildMind.Requirements.Tests`: **499 passed, 0 failed, 0 skipped**.
- `BuildMind.Workflow.Tests`: **197 passed, 0 failed, 0 skipped**.
- `BuildMind.Reports.Tests`: **163 passed, 0 failed, 1 skipped** (`IndependentParserVerifiesExactSyntheticReport` skipped because `BUILDMIND_PDF_QA_PYTHON` / `BUILDMIND_SYNTHETIC_PDF_EVIDENCE` PDF QA was not configured in that run).
- `BuildMind.Application.ContractTests`: **480 passed, 0 failed** (exit code `0`).

### 4.2 Python Services (`task-797`, unchanged Python source since run)
- `python-services` pytest: **1215 passed, 0 failed, 0 skipped** (`123.18s`).
- `rag-service` pytest: **238 passed, 0 failed, 0 skipped** (`9.22s`).

### 4.3 Live MySQL 8.4 & Cross-Service Persistence Integration (`task-1131` + `task-1165`)
- Environment: fresh ephemeral `mysql:8.4.13` container (`bm-fasttrack-mysql`, torn down cleanly after verification with `0` leftover `buildmind_test_*` databases), compiled `CiGeneratedSpike` migration (`T21`), plus live loopback FastAPI instances (`python-services` on `127.0.0.1:8099`, `rag-service` on `127.0.0.1:8098`, and synthetic constraint acceptance app on `127.0.0.1:8094`).
- Full `BuildMind.Persistence.IntegrationTests` run (`task-1131`): **184 passed, 1 failed** out of **185**:
  - Passing highlights: `BuildMindMigrationTests` (`M01`–`M07` including legacy `land_revisions` upgrade 17->18, metadata round-trip, check-constraint enforcement, rollback 18->17, reapply), `RequirementMutationMigrationLiveTests.M07_g12_rollback_preserves_requirement_history_and_head`, `M1MutationSessionGuardLiveTests` (all 3), `RequirementMutationLiveTests` (including both `G12_L07_L08` concurrency serialization cases), `ProviderCompatibilityTests` (`T01`–`T25`, `LP`, `SQL`), `RagConstraintAcceptanceTests` (12/12), `ExactPlanValidationLiveTests` (3/3), `PythonLiveSmokeTests` (7/7), `RagLiveSmokeTests` (5/5), `AdministrationLiveTests` (9/9), `SessionSecurityLiveTests`, `ProjectAuthorizationLiveTests`, `PlanRevisionHistoryMySqlTests`, `SurveyExtractionLiveTests`, `SurveyRepositoryLiveTests`.
  - Single failure in `task-1131`: `ReadinessLiveTests.RD02_repeated_checks_do_not_leak_connections` hit an initial cold-startup probe timeout (`status=Unhealthy; reason=timeout`) immediately after container pull/startup.
- Isolated `ReadinessLiveTests` rerun on warmed MySQL 8.4 (`task-1165`): **2 passed, 0 failed** (`RD01` in 103 ms, `RD02` in 4 s; total 6.88 s).

### 4.4 Portable Docker Stack & End-to-End Live HTTP Verification (`task-1265` & `task-1471`)
- `tools/portable/start.sh` applied migration #18 (`20261127153155_AddLandMetadataAndConstraints`) onto existing `bm-portable_mysql-data` over `VERIFY_IDENTITY` TLS (`PORTABLE_DATABASE_MIGRATION_COMPLETE`), started all 5 containers cleanly, and passed `tools/portable/verify-api-routes.py` (`REGISTERED_ROUTES: 148`, `BUILDMIND_DEVELOPMENT_ROUTES_REGISTERED`).
- Live Swagger (`/swagger/v1/swagger.json`) verified **126 paths / 148 operations**, including `POST /api/v1/projects/{projectId}/plans/manual` (`CreateManualPlanHttpRequest`) and `PutLandHttpRequest` (`exterior`, `georeference`, `location`, `reportedArea`, `surveyId`, `expectedCurrentLandRevisionId`).
- Live health/readiness (`http://127.0.0.1:5182`): `/health` returned `200 {"service":"buildmind-api","status":"ok"}`; `/health/ready` returned `200` (`mysql`: `Healthy:reachable`, `python`: `Healthy:reachable`, `rag`: `Degraded:unavailable` expected without corpus).
- End-to-end authenticated workflow (`task-1471`): `POST /auth/register` (`201`) -> `POST /auth/login` (`200`) -> `POST /projects` (`201`) -> `PUT /land` with `UserSelected` Colombo location (`201`, `areaM2=300`, `perimeterM=70`) -> `PUT /requirements` (`200`) -> `POST /generation-jobs` (`201`, **3 candidates**, `VASTU_LEAKED: False`) -> `POST /plans/manual` (`201`, `versionNumber=1`).

---

## 5. Remaining External Dependencies & Deferred Blockers

| Item | Classification | Status / Action Owner |
|---|---|---|
| Real geocoding API key/provider (verified location coordinates) | External dependency (`I-1`) | Provision external geocoding provider credentials & server adapter before enabling `Verified` geocoding status. |
| External SMTP credentials & approved sender domain | External dependency (`I-2`) | Configure `CaptainIntegration:Email:*` + `AllowExternalDelivery` for production; Mailpit captures dev email locally on port `8027`. |
| External LLM provider (G20) | External dependency (`I-3`) | Implement external `IAiEditProposalPort` provider with privacy/cost guardrails when authorized. |
| Approved Sri Lanka regulation corpus & constraints (STEP 8B) | External / ON HOLD (`I-4` / `G06`) | Keep fail-closed (`Degraded:unavailable`) until M7 delivers signed approved corpus manifest & index. |
| PDF QA optional test (`IndependentParserVerifiesExactSyntheticReport`) | Optional env config | Configure `BUILDMIND_PDF_QA_PYTHON` with `pypdf` and `BUILDMIND_SYNTHETIC_PDF_EVIDENCE` when running PDF QA. |
| `MySql.Data` provider cancellation hang (RH09) & G03 licence | Provider defect (`B-1`, `B-7`) | Bounded containment in place (`MySqlConnectionOpenGate`, server timeouts); M4 / G03 review required for production sign-off. |
| Independent code & security review | Gate (`G19`) | Production remains **NO-GO** until independent review and target CI pipeline pass. |

---

## 6. How Team Members Run & Test Locally

From Windows PowerShell at the repository root:

```powershell
wsl -d Ubuntu-24.04 --exec bash tools/portable/start.sh
```

- **Swagger UI:** `http://localhost:5182/swagger/index.html` (`148` registered operations)
- **Local Email Capture (Mailpit):** `http://localhost:8027`
- **Route Check:** `wsl -d Ubuntu-24.04 --exec python3 tools/portable/verify-api-routes.py`
- **Stop Stack (preserving DB & file volumes):** `wsl -d Ubuntu-24.04 --exec bash tools/portable/stop.sh`

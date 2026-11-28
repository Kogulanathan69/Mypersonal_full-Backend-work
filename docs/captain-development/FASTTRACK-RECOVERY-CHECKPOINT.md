# BuildMind Fast-Track Backend Recovery Checkpoint

Last updated: 2026-11-28T10:36+05:30

## 1. Repository State

- **Canonical personal backend repo**: `C:\Users\kogul\Documents\BuildMind-Worktrees\captain-full-backend-completion\Mypersonal_full-Backend-work` (WSL: `/mnt/c/Users/kogul/Documents/BuildMind-Worktrees/captain-full-backend-completion/Mypersonal_full-Backend-work`)
- **Current branch**: `feature/fasttrack-final-backend`
- **Original baseline**: `688756613a553e14a58533cee625e263811fadc9`
- **Checkpoint 1 (M2 land coordinate-frame anchor shift + CRS normalization)**: `ee503f461bd21a7863004da960c41fc0babfae44`
- **Merged portability PR #1 (`origin/main`)**: `a6ec89542cc10c5700ed2b56ae9b89caf0cfefc8` (merge commit `65360ad`)
- **Checkpoint 2 (M2 canonical land metadata, validation, constraints & API DTOs)**: `d3aa25bf1d0e41909f7250246beef1c3ab9ab2c0`
- **PR #119 isolated worktree**: `C:\Users\kogul\Documents\BuildMind-Worktrees\m4-pr119-land-migration` (`feature/m2-survey-land-d04`, clean at remote HEAD `734729d7f258671a7969377ba11d21ed7f7660c0`, verified)

## 2. Final Integration Work Completed (Above `d3aa25b`)

- **M1 Transaction-Time Session Guard (PR #137 + End-to-End Wiring)**:
  - Ported `M1MutationSessionGuard.cs`, `M1MutationSessionGuardTests.cs`, `M1MutationSessionGuardRegistrationTests.cs`, `M1MutationSessionGuardLiveTests.cs`, and `tests/BuildMind.Persistence.IntegrationTests/xunit.runner.json`.
  - Registered `M1MutationSessionGuard` in `AuthenticationServiceCollectionExtensions.cs`.
  - Added `long? TokenVersion => null;` on `ICurrentUser` and `"tv"` claim resolution in `HttpContextCurrentUser.cs`.
  - Wired `M1MutationSessionGuard` / `SessionSql.LockAccountAsync` (`users` `FOR UPDATE` -> `projects` `FOR UPDATE`) in `ProjectMutationAuthorization.LockAndCheckAsync` and threaded `currentUser?.TokenVersion` through `OwnerProtectedRequirementRevisionRepository`, `RequirementRevisionRepository`, `RequirementMutationStore`, `SurveyRepository`, `ProtectedPlanningWriter` (`PlanningStores`, `PlanLifecycleAdapters`, `DeliverableAdapters`, `LandEdgeRoles`, `SurveyStorageReconciliation`, `CaptainIntegrationInfrastructure`).
- **EF Migration #18 (`20261127153155_AddLandMetadataAndConstraints`)**:
  - Generated, verified (`has-pending-model-changes` clean), updated `PersistenceRegistrationTests.cs`, `BuildMindMigrationTests.cs` (`M06` + `M07_land_metadata_migration_upgrades_rolls_back_and_reapplies`), and `RequirementMutationMigrationLiveTests.cs` (`M07_g12_rollback_preserves_requirement_history_and_head`).
  - Updated `RequirementMutationLiveTests.G12_L07_L08_Concurrent_mutations_are_serialized` to check `(users || projects) && FOR UPDATE` under the global `users` -> `projects` lock order.
- **Manual Floor-Plan Creation**:
  - Added `PlanSaveService.CreateManualPlanAsync` (`CreateManualPlanCommand`) and `POST /api/v1/projects/{projectId}/plans/manual` (`CreateManualPlan`) in `V3RouteCompletionEndpointModule.cs`.
- **Internal Non-blocking Vastu Tie-Breaker**:
  - Added `_vastu_score` internal tie-breaker in `python-services/app/internal/generation.py` (never blocks generation and never exposes Vastu fields/diagnostics in responses), plus unit test in `python-services/tests/captain/test_internal_api.py`.
- **Portable Docker Stack Bugfixes**:
  - Updated `tools/portable/compose.sh` so `docker compose version` and `SAMPLE_FOUND:` messages print to stderr (`>&2`) rather than corrupting SQL captured by `tools/portable/migrate.sh`.
  - Added `BUILDMIND_PY_INTERNAL_TOKENS_FILE: /run/bm/secrets/python-internal.token` to `python-services` in `deploy/portable/compose.yml`.

## 3. Observed Verification Summary

- `BuildMind.sln` Release build: `0 Warning(s), 0 Error(s)`; `dotnet-ef migrations has-pending-model-changes`: clean.
- Solution tests (`task-823`): `Application.UnitTests` 270/270, `Infrastructure.Tests` 1271/1271, `Requirements.Tests` 499/499, `Workflow.Tests` 197/197, `Reports.Tests` 163/164 (`IndependentParserVerifiesExactSyntheticReport` skipped), `ContractTests` 480/480.
- Python suites (`task-797`): `python-services` 1215/1215, `rag-service` 238/238.
- `BuildMind.Persistence.IntegrationTests`: `184/185` on full disposable MySQL 8.4 + live Python/RAG/Acceptance run (`task-1131`, 1 cold-startup `RD02` timeout) + `ReadinessLiveTests` `2/2` on warm MySQL 8.4 (`task-1165`); 0 leftover `buildmind_test_*` DBs.
- Portable Docker stack (`task-1265`, `task-1471`): migrated #17 -> #18 cleanly, Swagger `126` paths / `148` operations (`POST /api/v1/projects/{projectId}/plans/manual` + M2 land fields verified), end-to-end live HTTP auth -> project -> `PUT /land` (201) -> `PUT /requirements` (200) -> `POST /generation-jobs` (201, 3 candidates, zero `vastu`) -> `POST /plans/manual` (201) passed.
- Complete report written in `docs/captain-development/FASTTRACK-FINAL-BACKEND-INTEGRATION.md`.

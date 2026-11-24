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

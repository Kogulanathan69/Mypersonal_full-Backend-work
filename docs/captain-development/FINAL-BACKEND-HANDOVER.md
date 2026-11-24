# Final backend handover — Captain full-backend completion

| | |
|---|---|
| Branch | `feature/captain-full-backend-completion` (local only; **not pushed, not merged**) |
| Worktree | `C:\Users\kogul\Documents\BuildMind-Worktrees\captain-full-backend-completion` |
| Starting SHA | `fec32547e7b2508f9d7af71206dc651cfebe5794` |
| Final code tree | `7213b42` (`d2d74bc` adds verification tools; the final commit adds these documents) |
| Main workspace | `BuildMind-Platform` on `feature/bm-final-backend-integration`. Its pre-existing uncommitted files (unverified origin) were left untouched and were not imported. |
| Other worktrees | untouched |

## Status by acceptance category

### A. Backend implementation completion: substantially complete for the 2D MVP journey, with recorded gaps

Implemented in this branch, Development-only behind `CaptainIntegration:Enabled`:
- composition profile;
- survey persistence;
- land boundary;
- generation pipeline (Python routes wrapping the existing D02/D04 code, C# orchestration, job lifecycle, idempotency, persistence);
- plan save/edit/apply/reject/history/confirmation (existing M3 services + adapters);
- fail-closed regulatory evidence integration;
- preliminary PDF reports with private storage and download;
- architect requests with explicit consent and withdrawal;
- python-services internal authentication;
- VerifyFull database TLS;
- two concurrency defect fixes in the session store.

**Not implemented:**
- survey extraction (OCR/PDF to boundary) integration;
- natural-language AI-assisted edits (G20, no approved AI provider);
- project lifecycle status transitions (no approved M1 writer);
- real reviewed regulatory constraints (G06, edge-role mapping).

### B. Local functional verification: VERIFIED

The complete customer journey ran live against the local stack:

| Component | Version / mode |
|---|---|
| API | .NET 10 |
| MySQL | 8.4.13, VerifyFull |
| python-services | token required |
| rag-service | token required, no corpus |

Results:
- journey: **107/107** checks;
- all .NET suites and both Python suites green (TEST-EVIDENCE);
- live MySQL **94/94**.

Journey coverage, in order: register → login → JWT → project → survey upload → land validation → requirements → (regulatory retrieval: fail closed) → feasibility/generation → preview → save → edit → validate → revision → history → confirm → PDF report → authorized download → architect request with consent → dashboard → refresh → logout.

### C. Full integration verification: PARTIAL

| Area | Status |
|---|---|
| Live Python and RAG integration | Verified, but RAG only on its fail-closed paths |
| Real regulation corpus | BLOCKED in this environment |
| Survey extraction step | Not available |
| CI | Not run: nothing pushed |
| Independent review | Not obtained (G19) |
| Members' parallel implementations | Not reconciled |

### D. Production readiness: **NO-GO**

- RH09 is DEFERRED/UNRESOLVED.
- R-1/R-3, G03 licensing, G11 hosting/TLS and G19 reviews are open.
- Every new feature is Development-only.
- STEP 8B is ON HOLD.
- Synthetic policy data must be replaced by approved product and regulatory sources.

## Gates preserved

- **STEP 8B:** no rag-service, `RagServiceClient` or M7 registration change.
- **No weakening:** no authentication, ownership or TLS weakening. TLS was strengthened from VerifyCA to VerifyFull.
- **Integrity controls:** the corpus identity control was not bypassed, and Smart App Control was not bypassed.
- **Repository hygiene:** no push, merge, production access or credentials in the repository; no member work imported.
- **No invented regulation:** no regulatory values invented. Generated plans carry `SYNTHETIC_FIXTURE` evidence, warnings and a preliminary notice. Apply, confirmation and reports require explicit acknowledgement of warnings.

## Checkpoints

| SHA | Content |
|---|---|
| `b98a396` | Phase 0 audit docs |
| `98c4f77` | Phase 1: Captain profile (requirements, revision history) |
| `2c876d4` | Phase 2: MySQL environment, surveys, refresh deadlock fix |
| `279ff2f` | LF for scripts |
| `0ebb7d8` | Phase 3: Python internal API |
| `96975a5` | Phase 3: generation workflow (C#) |
| `1574e1f` | Review corrections: lock order, VerifyFull, readiness smoke |
| `1759423` | Phase 4: plan editing and confirmation |
| `dd90e93` | Phase 5: fail-closed regulatory evidence |
| `7213b42` | Phase 6: reports and architect workflow |
| `d2d74bc` | Phase 7: verification tooling |

Size since baseline: 83 files, about 14k insertions:
- src: 35 files;
- tests: 13 files;
- python-services: 8 files;
- rag-service: 0 files.

## Migrations (all additive; applied to the Captain `buildmind_dev` with the Option B ranged script)

| Migration | Adds |
|---|---|
| `20261124095515_AddSurveys` | `surveys` |
| `20261124104612_AddCaptainPlanningWorkflow` | `land_revisions`, `generation_jobs`, `generation_candidates`, `plan_validation_evidence` |
| `20261124112814_AddCaptainPlanLifecycle` | `plan_edit_candidates`, `plan_confirmations` |
| `20261124114516_AddGenerationEvidencePackId` | `generation_jobs.evidence_pack_id` |
| `20261124120204_AddCaptainDeliverables` | `reports`, `architect_requests` |

The M4 schema-pin tests keep their exact M4 assertions plus a `CaptainMigrations` registry, which also checks rollback to the M4 baseline. **M4 single-writer note:** these migrations were authored under the Captain's authority and need M4 review before any shared branch.

## Changes to existing (non-Captain) code, for owner review

- `SessionSecurityStores.cs` (M1/M4): rotation and logout lock the users row first, then the presented token row. This fixes an observed deadlock and a lock-order inversion with logout-all/reset.
- `python-services/app/main.py` (M6 bootstrap): `create_app(auth)` with the fail-closed internal token and new `/internal/v1/*` routes; `/health` unchanged and docs/OpenAPI disabled.
- M4 test pins: `BuildMindMigrationTests`, `RequirementRevisionRepositoryTests`, `PersistenceRegistrationTests`, `DesignTimeFactoryTests`, `SchemaModelTests` (record-namespace rule generalised to the `Persistence.Records.*` tree).
- `BuildMind.Application.csproj`: `InternalsVisibleTo` for tests and Infrastructure.
- `Program.cs`: one call to `AddCaptainLocalIntegration`.

## Decisions needed before any further step

1. Contract approval of the Captain internal routes (`captain-*` schemas) and of the generation-input binding (room dimensions policy, optional adjacency, single floor) by M6/M3/M2.
2. Ownership of the validation-evidence ledger and the server-issued evidence model (M6/M3).
3. A real requirement catalogue, generation policy, freshness window and sharing policy (product).
4. Edge-role mapping and a reviewed constraint table (M6/M7/G06).
5. The survey extraction worker design (M2).
6. M1 lifecycle writer and project status transitions on confirmation.
7. Report retention and architect delivery channel (G12).
8. RH09 resolution path; R-1/R-3; G03 licence.

## How to run

See LOCAL-RUNBOOK. Related documents: DATABASE-SETUP, API-ENDPOINT-INVENTORY, TEST-EVIDENCE, KNOWN-BLOCKERS, DEVELOPMENT-PROGRESS.

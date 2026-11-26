# Final database migration register

**15 migrations** on `feature/captain-full-backend-completion`. All Captain migrations (#5–#15) are **additive**: no drop, rename or type change of existing columns, and every `Down` is the inverse. Rows #1–#10 and #11–#14 are detailed in `DATABASE-MIGRATION-REGISTER.md`. This register adds #15, the application evidence and the corrections.

| # | Migration | Owner | Adds | Destructive | Captain DB | Verified by |
|---|---|---|---|---|---|---|
| 1 | 20261118133137_InitialFeatureSchema | M4 baseline | base schema | — | applied | baseline |
| 2 | 20261119092022_AddProjectAuthorizationVersion | M4 (C-01) | `projects.authorization_version` | no | applied | baseline |
| 3 | 20261120202215_AddSessionSecurity | M1 | session/refresh tables | no | applied | baseline |
| 4 | 20261122080431_AddPlanRevisionHistory | M3 | plan revision ledger | no | applied | baseline |
| 5 | 20261124095515_AddSurveys | Captain | `surveys` | no | applied | live + model pins |
| 6 | 20261124104612_AddCaptainPlanningWorkflow | Captain | land revisions, generation jobs/candidates | no | applied | live + model pins |
| 7 | 20261124112814_AddCaptainPlanLifecycle | Captain | validation ledger, edit candidates, confirmations | no | applied | live + model pins |
| 8 | 20261124114516_AddGenerationEvidencePackId | Captain | nullable `evidence_pack_id` | no | applied | live + model pins |
| 9 | 20261124120204_AddCaptainDeliverables | Captain | reports, architect requests | no | applied | live + model pins |
| 10 | 20261124170241_AddSurveyExtraction | Captain | survey lease/attempt columns, `survey_extractions` | no | applied | live + pins |
| 11 | 20261124182132_AddAdministration | Captain | roles, staff, audit chain, proposals, tasks, policies | no | applied | live + pins |
| 12 | 20261124184000_AddAccountSecurity | Captain | `account_security`, `security_events` | no | applied | live + pins |
| 13 | 20261124185244_AddSurveyExtractionReviews | Captain | `survey_extraction_reviews` | no | applied | live + pins |
| 14 | 20261124190404_AddCatalogueAndArchitectPortal | Captain | catalogue, architect portal, notifications, outbox, preferences | no | applied | live + pins |
| 15 | **20261125030045_AddLandCoordinateFrame** | Captain | `land_revisions.frame_json` (mediumtext, nullable), `land_revisions.geometry_digest` (char(64), nullable) | no | applied (`migrate.sh`) | pinned in both lists; land frame persisted in live journeys |

## Application evidence

| Target | Method | Result |
|---|---|---|
| Captain DB (`bm-captain-mysql`) | `tools/captain/migrate.sh`: **ranged** script from the last applied migration, run by `bm_migrator` over VerifyFull TLS | all 15 applied; 37 base tables |
| Empty container DB (`compose.verify.yml`) | full ordered script from `dotnet ef migrations script`, applied once at volume init | **37 tables, 15 `__EFMigrationsHistory` rows**, an independent replay from zero. API, workers and journeys ran on it (132/132). |
| Test databases | disposable `buildmind_test_*` per persistence-test fixture | created and dropped by the fixture |

`dotnet ef migrations has-pending-model-changes` reports no pending model changes (run by `migrate.sh`).

## Corrections and findings

- The earlier register called the scripts "ranged **idempotent**". `migrate.sh` actually generates **ranged non-idempotent** scripts (`migrations script <from>`).
- The MySQL provider's `--idempotent` output is **rejected by MySQL** (ERROR 1064: `IF NOT EXISTS(...)` blocks outside a stored procedure), as found during container verification. Use ranged or full scripts with history checks, never `--idempotent`, on MySQL.

## Rules applied

- No destructive operation against the Captain database. It was never reset or dropped. The verification volume belongs only to the disposable `bm-captain-verify` project.
- Production: no migration has been applied to any non-local database. Production is **NO-GO**.
- Single writer: M4 coordinates DbContext and migrations. Migrations #5–#15 exist only on this branch and need **M4 review** before any merge. They are timestamped after the shared baseline, so they must be rebased onto M4's migration order at integration time.

## Final remaining-development order

| # | Migration | Owner | Adds | Destructive | Captain DB | Verified by |
|---|---|---|---|---|---|---|
| 16 | **20261125103924_AddLandEdgeRoles** | Captain | `land_edge_roles`: unique (land_revision_id, edge_id); FK RESTRICT to projects, land_revisions and users; indexes on project and reviewer | no | applied by `migrate.sh` (ranged) | pinned in both lists; live persistence suite; journey phase 12 |

**Container upgrade path (new):** `compose-verify.sh up` applies pending migrations to an existing verification volume (the ranged script, run by the migrator over VerifyIdentity TLS) before the API starts. This was found because the final regression failed in containers without it.

**Schema total:** 38 tables (37 mapped + `__EFMigrationsHistory`), 16 migrations.

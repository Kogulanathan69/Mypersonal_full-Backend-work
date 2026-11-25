# Database migration register

**Strategy:** Option B, ranged idempotent SQL scripts applied by the least-privilege `bm_migrator` account (`tools/captain/migrate.sh`); the runtime account `bm_runtime` has DML only.
- All Captain migrations are **additive**. No drop, rename or type change of existing columns. Every `Down` is the inverse.
- Each Captain migration is pinned in two test lists:
  - `BuildMindMigrationTests.CaptainMigrations` (live MySQL: tables + FK delete behaviour);
  - `PersistenceRegistrationTests.CaptainMigrations` (model: tables + FK names).
- `SchemaModelTests` enforce lowercase `pk_/fk_/ix_/ux_` names.

| # | Migration | Owner | Adds | Destructive? | Applied to Captain DB | Verified by |
|---|---|---|---|---|---|---|
| 1 | 20261118133137_InitialFeatureSchema | M4 baseline | base schema | — | yes | existing baseline tests |
| 2 | 20261119092022_AddProjectAuthorizationVersion | M4 baseline (C-01) | `projects.authorization_version` | no | yes | baseline |
| 3 | 20261120202215_AddSessionSecurity | M1 baseline | session/refresh tables | no | yes | baseline |
| 4 | 20261122080431_AddPlanRevisionHistory | M3 baseline | plan revision ledger | no | yes | baseline |
| 5 | 20261124095515_AddSurveys | Captain | `surveys` | no | yes | live + model pins |
| 6 | 20261124104612_AddCaptainPlanningWorkflow | Captain | land revisions, generation jobs/candidates | no | yes | live + model pins |
| 7 | 20261124112814_AddCaptainPlanLifecycle | Captain | validation ledger, edit candidates, confirmations | no | yes | live + model pins |
| 8 | 20261124114516_AddGenerationEvidencePackId | Captain | nullable `evidence_pack_id` column | no | yes | live + model pins |
| 9 | 20261124120204_AddCaptainDeliverables | Captain | reports, architect requests | no | yes | live + model pins |
| 10 | **20261124170241_AddSurveyExtraction** | Captain | `surveys.processing_lease_until_utc` (nullable), `surveys.processing_attempts` (int, default 0), table `survey_extractions` (FKs `fk_survey_extractions_surveys`, `fk_survey_extractions_projects_project_id`, both RESTRICT), indexes `ix_surveys_status_lease`, `ux_survey_extractions_survey_id`, `ix_survey_extractions_project_id` | no | yes (`migrate.sh`, 2026-11-24) | 9 live extraction tests + pins |

## Rules applied

- **No destructive operation** was run against the Captain database. It was never reset or dropped in this execution.
- Tests use disposable `buildmind_test_*` databases created and dropped by the test fixture. The leftover count after every run was 0.
- **Backup check** before applying #10: the migration is additive and `Down` drops only the new objects. The Captain database holds synthetic data only. `DATABASE-SETUP.md` gives the `mysqldump` command for an explicit backup when a non-synthetic database is involved.
- **Production:** no migration has been applied to any non-local database. Production is NO-GO.
- **Single writer:** M4 coordinates DbContext/migrations for the shared branch. These Captain migrations live only on `feature/captain-full-backend-completion` and need M4 review before any merge.

## Extended execution additions (all additive, FK RESTRICT, pinned in both lists, applied to the Captain DB via `migrate.sh`)

| # | Migration | Adds |
|---|---|---|
| 11 | 20261124182132_AddAdministration | `principal_roles`, `staff_profiles`, `admin_audit_events`, `admin_audit_chain_head` (genesis row), `staff_change_proposals`, `staff_tasks`, `staff_task_notes`, `system_policies` |
| 12 | 20261124184000_AddAccountSecurity | `account_security`, `security_events` |
| 13 | 20261124185244_AddSurveyExtractionReviews | `survey_extraction_reviews` (unique extraction + version) |
| 14 | 20261124190404_AddCatalogueAndArchitectPortal | `requirement_catalogue_versions`, `architect_profiles`, `architect_request_responses`, `architect_feedback`, `notifications`, `outbox_messages`, `notification_preferences` |

The Captain database now has 37 base tables (counted by the restore drill).

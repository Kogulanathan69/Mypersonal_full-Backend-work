# Final database schema report

**37 tables (36 mapped + `__EFMigrationsHistory`), 15 migrations, one canonical Captain sequence.** There is no table-count target. This order added **no** migration: every new operation (history, listing, revert, sessions, oversight) reads or writes data the existing schema already holds. The RAG corpus and index are files verified by hash, not tables.

| Area | Tables (principal) | Integrity features |
|---|---|---|
| Identity and sessions | users, refresh_tokens, password_reset_tokens, account_security, security_events | hashed refresh and reset tokens; token families; `token_version`; users-row lock order for every session mutation |
| Projects | projects | `authorization_version` (owner changes invalidate stale authorizations), lifecycle `status`, `revision` |
| Requirements | requirement_revisions, project_requirement_heads, requirement_catalogue_versions | immutable revisions; head pointer; catalogue publish/retire |
| Survey | surveys, survey_extractions, survey_extraction_reviews | SHA-256 of the stored file; lease/attempt columns; unique extraction per survey; versioned reviews |
| Land | land_revisions | immutable revisions; `frame_json`, `geometry_digest` (CRS frame) |
| Generation | generation_jobs, generation_candidates | unique (project, idempotency key); evidence mode; diagnostics |
| Plans | plans, plan_revisions, plan_edit_candidates, plan_validation_evidence, plan_confirmations | immutable revisions with parent links and validation evidence; head CAS; candidate lifecycle + expiry; confirmation bound to revision + geometry hash |
| Deliverables | reports, architect_requests (+ consent) | state_version CAS; artifact hash; consent policy version |
| Admin | principal_roles, staff_profiles, admin_audit_events, admin_audit_chain_head, staff_change_proposals, staff_tasks, staff_task_notes, system_policies | tamper-evident audit chain (verify endpoint); proposals for privileged changes |
| Portal and notifications | architect_profiles, architect_request_responses, architect_feedback, notifications, outbox_messages, notification_preferences | transactional outbox with idempotency keys |

**Constraints:**
- naming: lowercase `pk_`/`fk_`/`ix_`/`ux_`, enforced by `SchemaModelTests`;
- Captain foreign keys are `RESTRICT`, pinned per migration in `BuildMindMigrationTests` and `PersistenceRegistrationTests`;
- the model snapshot is consistent: `has-pending-model-changes` reports no changes.

# Migration verification (this order)

| Check | Result |
|---|---|
| Fresh creation from zero | container stack: full ordered script → 37 tables / 15 history rows; API, workers and journeys run on it |
| Upgrade path | the Captain DB was upgraded incrementally with ranged scripts (`migrate.sh`); currently "no pending migrations" |
| Ordering / snapshot | pinned lists in chronological order; `has-pending-model-changes` clean |
| Persistence suite (disposable databases, live TLS) | **115/115** with the stack running (connection fix verified) |
| Restart / crash recovery | WSL VM crash → InnoDB recovery → data intact; container restart → row counts unchanged |
| Least privilege | runtime account DML only; migrator separate; test account restricted to `buildmind\_test\_%` |
| MySQL `--idempotent` scripts | rejected by MySQL (ERROR 1064); never used |

# Database reliability report (Captain database owner role, local)

| Property | State | Evidence |
|---|---|---|
| Migrations | 14 Captain additive migrations on top of the M4/M1/M3 baseline; every FK RESTRICT; explicit lowercase names; no drops/renames/type changes of existing columns | DATABASE-MIGRATION-REGISTER; `BuildMindMigrationTests` + `PersistenceRegistrationTests` pin lists (tables + FK names + delete behaviour); `SchemaModelTests` naming |
| Clean install / upgrade / rollback | Covered by the existing live migration tests: apply all, roll back Captain migrations to the M4 baseline in disposable databases; the Captain DB was upgraded in place via `migrate.sh` (Option B ranged script, `bm_migrator`) | Live suite |
| Model/snapshot consistency | Each migration generated from the model (`add-migration.sh`); the build fails if the snapshot drifts | Build + pins |
| Least privilege | Runtime `bm_runtime` = DML only; migrations by `bm_migrator` | `mysql-setup.sh` |
| TLS | `SslMode=VerifyFull` with a client certificate | `env.sh` |
| Concurrency tokens | `state_version`, `row_version`, `scope_version`, `version` CAS on surveys, proposals, tasks, profiles, catalogue versions, policies | Live tests Q01–Q04, H01/H02, A02–A07 |
| Lock ordering | Users rows first (ascending id when several) → feature rows → audit chain head last; project row lock (A1–A8) for project-scoped writes | Deadlock found and fixed (A03, 16 clean rounds) |
| Immutable history | Land, requirement, plan revisions, survey reviews, catalogue versions (published/retired kept), audit chain | Append-only stores |
| Timestamps | UTC `datetime(6)`; values truncated to microseconds before CAS comparisons | `MicrosecondTimeProvider`, lease/audit truncation |
| Digest columns | SHA-256 hex, fixed length (64) | configurations |
| JSON | `text`/`mediumtext`, validated by strict parsers before storage | stores |
| Outbox | Transactional (same transaction as the business change), deduplicated, retry/dead-letter | `MySqlOutboxDispatcher` |
| Backup / restore | Verified drill (37 tables, 0 mismatches) | BACKUP-RESTORE-RUNBOOK |
| RH09 | **Unresolved** | RH09-RESOLUTION-REPORT |
| R-1 / R-3 (`KILL QUERY` gating, TLS handshake bounds) | Open (production) | KNOWN-BLOCKERS |
| Connection pools in tests | Captain live contexts unpooled (pool exhaustion fix) | TD-15 |

# Final migration verification

No migration was added in this order; the canonical Captain sequence remains 15 migrations (FINAL-DATABASE-MIGRATION-REGISTER).

| Check | Result | Evidence |
|---|---|---|
| Fresh database from zero | 37 tables / 15 history rows; API, workers and journeys run on it | container stack (FINAL-DOCKER-INTEGRATION-REPORT) |
| Incremental upgrade | Captain DB at the latest migration; `migrate.sh`: "no pending migrations" on every stack restart this execution | restart logs |
| Model/snapshot consistency | `dotnet ef migrations has-pending-model-changes`: none | `migrate.sh` |
| Migration pins (FK RESTRICT, names) | pinned lists in chronological order; tests pass | Infrastructure.Tests, persistence suite |
| Live persistence on disposable databases (TLS VerifyFull) | 115/115 with the live stack running | FINAL-MYSQL-INTEGRATION-REPORT |
| `--idempotent` scripts on MySQL | rejected by MySQL (ERROR 1064); never used | DOCKER-CONTAINER-VERIFICATION |

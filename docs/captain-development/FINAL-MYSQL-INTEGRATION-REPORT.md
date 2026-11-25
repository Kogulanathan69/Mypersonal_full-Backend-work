# Final MySQL integration report

| Item | State |
|---|---|
| Server | MySQL 8.4.13. `--require-secure-transport=ON`; the API uses `SslMode=VerifyFull` + client certificate (RSA PEM, provider-compatible). Server identity is validated. |
| Accounts | `bm_runtime` (DML only), `bm_migrator` (DDL, migrations), `bm_test` (disposable `buildmind_test_*` only) |
| Schema | 15 migrations, 37 tables. Replayed from zero in the container stack (37 tables / 15 history rows). See FINAL-DATABASE-MIGRATION-REGISTER. |
| Containment (RH09) | `max_execution_time` 25 s, `innodb_lock_wait_timeout` 20 s. See FINAL-RH09-RESOLUTION-REPORT. |

## Connection exhaustion: root cause, fix and verification

| | Detail |
|---|---|
| Symptom | The full persistence suite ran next to the live stack: 103/115, 15× `MySqlConnectionGateTimeoutException`, `Max_used_connections` 152 of `max_connections` 151, `Connection_errors_max_connections` 9, 2 leftover test databases |
| Root cause | `DisposableMySqlDatabase.DisposeAsync` dropped each disposable database without clearing **its** provider pool. Every disposable database has a distinct connection string, and therefore a distinct pool, so idle pooled server connections to already-dropped databases accumulated until the provider's idle timeout. With the M4 process-wide open gate, one open stalled at the server limit blocks all other opens, which turned exhaustion into a cascade of gate timeouts. |
| Fix | `MySqlConnection.ClearPool(...)` for the database's connection string before `DROP DATABASE` (`tests/BuildMind.Persistence.IntegrationTests/Support/DisposableMySqlDatabase.cs`) |
| Runtime review | API DbContexts are scoped and disposed per request; workers create one scope per iteration (outbox) or per claimed survey; the open gate bounds connection opening; protected writes use short transactions. No application leak was found. The measurement below covers the API side: the dev-database connections peaked at 5 while two API instances and their workers ran. |
| Verification (realistic concurrency) | Full persistence suite **while the live stack was running** (2 API instances with outbox and survey workers, python-services and rag-service), sampled every 2 s (323 samples). **115/115 passed**; `Max_used_connections` **14**; `Connection_errors_max_connections` **0**; peaks: test DBs 2, Captain DB 5, no-DB/pool 8; 0 leftover test databases. |

## Other verified behaviour

- **Persistence across restart:**
  - process restarts, and a full WSL VM crash and recovery: InnoDB crash recovery, data intact;
  - container `docker compose restart`: row counts equal before and after.
- **Concurrency and atomicity:** the live persistence suite covers concurrent claims, CAS increments, authorization-version races, protected writes and replay semantics (115 tests). The journeys cover duplicate apply (409), stale revert (409) and idempotent report and generation requests.
- **Cross-owner isolation:** every project-scoped route is checked (cross-user requests get 404 in journey phases 3–11).
- **Backup and restore:** `tools/captain/backup-db.sh` and `restore-verify.sh` (restore drill in an earlier order); no destructive operation against the Captain DB in this execution.

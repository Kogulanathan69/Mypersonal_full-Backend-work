# RH09 resolution report

**Verdict:** BLOCKED_BY_EXTERNAL_DEPENDENCY. The provider defect is confirmed and isolated; partial server-side containment is applied and verified. **RH09 is not resolved.**

## Isolated experiment (2026-11-25)

- **Probe:** `tools/captain/rh09` (standalone console project, not in the solution), ADO.NET only, no BuildMind/EF code.
- **Server and account:** MySQL 8.4.13, VerifyFull TLS + client certificate, restricted test account (`BUILDMIND_TEST_MYSQL`).
- **Isolation:** disposable `buildmind_test_rh09_*` schemas, all dropped.
- **Hang handling:** every scenario runs under a 20 s watchdog, so a hang is recorded rather than waited on.
- **Raw output:** `~/bm-captain/results/rh09/rh09-081843.json`.

| Scenario | MySql.Data 26.10.0 (in use via MySql.EntityFrameworkCore 10.0.12) | MySqlConnector 2.4.0 |
|---|---|---|
| S1 `SELECT SLEEP(5)`, CommandTimeout 1 s | **HANG** (>20 s) | bounded 1.06 s (KILL QUERY; SLEEP returns 1) |
| S9 real query (`BENCHMARK`), CommandTimeout 1 s | **HANG** (>20 s) | bounded 1.09 s |
| S2 reuse the same connection after the timeout | `MySqlException`: open DataReader (stale reader) | works |
| S3 new pooled connection | works | works |
| S4 orphaned server query | none | none |
| S5 CancellationToken (1 s) | **ignored**: ran the full 5 s | honoured, 1.04 s |
| S6 transaction + timeout + rollback | **HANG** | rolled back, 0 rows |
| S7 **server** `max_execution_time` 1 s, client timeout 30 s | bounded 1.07 s, connection reusable | bounded 1.0 s |
| S8 **server** `innodb_lock_wait_timeout` 1 s, client timeout 30 s | `Lock wait timeout exceeded` at 1.22 s, connection reusable | same, 1.3 s |

## Conclusions

1. The defect is in **MySql.Data's client-side timeout/cancellation path**, and it is not specific to SLEEP: S9 shows it on a real query.
2. **MySqlConnector does not have it.** It cannot be adopted for EF Core here: its EF provider (Pomelo) has **no EF Core 10 release** (NuGet shows 9.0.0 as the latest, checked 2026-11-25), and BuildMind targets .NET 10 / EF Core 10. Downgrading EF is out of scope and unsupported (EF Core 9 left support on 2026-11-10, per the G03 study).
3. **Containment applied** (Captain MySQL, `SET PERSIST`, recorded in `mysql-setup.sh`), so the server ends the statement before the 30 s client CommandTimeout:
   - `max_execution_time = 25000` (25 s; SELECT statements);
   - `innodb_lock_wait_timeout = 20` (20 s; lock waits).

   S7/S8 show MySql.Data handles these server-side terminations correctly and the connection stays reusable.
4. **Residual (still RH09):**
   - long non-SELECT statements not blocked by locks;
   - network stalls mid-statement;
   - any explicit client cancellation.

   These still reach the hanging client path. No outer `Task.WhenAny` wrapper was added: it would hide a still-running operation, which is forbidden.

## What would resolve it

Any of the following (each is an M4 decision with G03 licence review):
- an EF Core 10-compatible MySQL provider not built on MySql.Data (for example Pomelo for EF Core 10, once released);
- an Oracle MySql.Data fix (report the S1/S9/S5 repro upstream);
- an approved `net9`/EF 9 path. This is not recommended: EF 9 is out of support.

Acceptance before reporting RH09 resolved: S1, S5, S6 and S9 must be bounded on the production provider, plus an HTTP-level 503/504 within bound, plus subsequent requests succeeding.

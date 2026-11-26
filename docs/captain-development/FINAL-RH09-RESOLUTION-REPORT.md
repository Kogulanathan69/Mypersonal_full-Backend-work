# Final RH09 resolution report (database timeout and cancellation)

**Verdict: unresolved provider defect, with bounded containment verified (BLOCKED_BY_EXTERNAL_DEPENDENCY for the provider fix).**
The detailed experiment is in RH09-RESOLUTION-REPORT.md (S1–S9 probe; evidence `~/bm-captain/results/rh09/rh09-081843.json`).

## Compatibility matrix, re-verified 2026-11-25 (NuGet registration API, authoritative package index)

| Package | Latest stable | Relevance |
|---|---|---|
| MySql.EntityFrameworkCore | **10.0.12** (in use); 11.0.0-preview exists | The only EF Core 10 MySQL provider. Built on MySql.Data. |
| MySql.Data | **26.10.0** (in use; latest) | Hangs instead of honouring the client command timeout in the RH09 scenarios |
| Pomelo.EntityFrameworkCore.MySql | **9.0.0** (no EF Core 10 release, stable or preview) | Would bring MySqlConnector to EF, but cannot be used with EF Core 10 |
| MySqlConnector | **3.0.0** (2.4.0 was probed) | Bounded cancellation in the probe; no EF Core 10 provider uses it |

**Conclusion:** no supported EF Core 10 provider without the MySql.Data behaviour exists today. Downgrading to EF Core 9 is rejected: it would mean an unsupported framework/security downgrade, and the order forbids silent downgrades.

## Containment in force (verified)

| Layer | Control | Evidence |
|---|---|---|
| Server | `max_execution_time = 25000` ms (SELECT), `innodb_lock_wait_timeout = 20` s, persisted with `SET PERSIST`; also applied in the container init | RH09 S7/S8: server-side termination before the 30 s client timeout |
| Connection opening | M4 process-wide open gate with a bounded wait (`MySqlConnectionGateTimeoutException` → 503) | Persistence suite; connection-exhaustion analysis |
| Pool hygiene | Disposable test databases clear their pool on dispose. Measured peak connections: **14** (was 152) with the live stack running. | FINAL-MYSQL-INTEGRATION-REPORT |
| Writes | Protected writes are short transactions (row lock, CAS update, commit). Unknown-commit outcomes surface as `TRANSACTION_OUTCOME_UNKNOWN` (M3/M4 codes) and are never reported as success. | existing M3/M4 tests |

## Remaining limitation (exact)

- Long-running **DML**, a **network stall** after the TCP connection is established, and **client-side cancellation** are not bounded by the server settings.
- In those cases a MySql.Data call can outlive the request (RH09 S1–S3, S9).
- Nothing simulates a successful recovery.

**Resolution path:** an EF Core 10 provider on MySqlConnector (for example a Pomelo 10 release). Adopt it in an isolated checkpoint and re-run the S1–S9 probe, migrations and the persistence suite. Decision input I-8 / E-8.

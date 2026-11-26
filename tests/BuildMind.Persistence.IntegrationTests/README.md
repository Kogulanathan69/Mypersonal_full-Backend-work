# BuildMind.Persistence.IntegrationTests — MySQL provider compatibility (BE-M4-D02)

**Status:** 28 tests. **Final run 44301027243: 28/28 passed** on MySQL 8.4.13. (T19D added in STEP 05-E10.) Live runs on MySQL 8.4.13: 44292102702 → 24/26 passed (T06, T19 failed); 44296302240 → 25/27 passed (T06, T19 failed; T21 and T25 passed). They run only in the `database-compatibility` CI job (BE-M4-D02b), on a disposable MySQL 8.4 container. They have not been run locally (Smart App Control, G17). G03 covers internal compatibility testing only; the commercial licence review is still pending.

This project is **not in `BuildMind.sln`** on purpose: the standard `dotnet-build` job never runs it. Only the dedicated CI job (or an approved disposable local server) runs it.

## Safety rules

- Every test creates its own `buildmind_test_<32 hex>` database and drops it afterwards. The helper refuses any other database name and refuses `root`. The CI job checks that no `buildmind_test_*` database is left behind.
- Accounts: `bm_test` (disposable databases only, `REQUIRE SSL`) and `bm_runtime` (DML on `buildmind_dev` only, `REQUIRE SSL`). The least-privilege probe connects **without** selecting `buildmind_dev`.
- Credentials come only from environment variables (random per CI run, masked). Test output records only versions, column types and generated SQL; the CI job blocks the artifact upload if a password appears in the results.
- The model is synthetic (`spike_owners`, `spike_items`). It is **not** a BuildMind feature schema.

## Environment variables

| Variable | Account | Content |
|---|---|---|
| `BUILDMIND_TEST_MYSQL` | `bm_test` | Server, port, user, password and `SslMode=Required`. Any database name is ignored. |
| `BUILDMIND_TEST_MYSQL_RUNTIME` | `bm_runtime` | `Database=buildmind_dev` (the schema the grants must cover) plus `SslMode=Required`. Used only for `SHOW GRANTS` and a `DROP TABLE IF EXISTS` on a random non-existent name, which must be denied (1142). |

If a variable is missing, the tests **fail**; they never skip.

## CLI-generated migration (T21)

`GeneratedSpikeDbContext` has the same synthetic model but **no committed migration**. Before the tests run, the CI job executes:

```sh
dotnet tool run dotnet-ef migrations add CiGeneratedSpike --context GeneratedSpikeDbContext --output-dir GeneratedMigrations \
  --project tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj \
  --startup-project tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj
```

T21 then checks that the generated snapshot matches the model (`HasPendingModelChanges() == false`), applies the migration, writes and reads a row, and rolls back to empty. `GeneratedMigrations/` is git-ignored. Without that step, T21 fails with an explanation.

## Objectives

| Test | Objective |
|---|---|
| T01–T02 | Connectivity (server version 8.4) and DbContext initialisation |
| T03 | Model-to-SQL generation (in-process `dotnet ef` pipeline) |
| T04–T06 | Hand-written migration: apply, roll back to empty, reapply; T06 also pins the idempotent-script provider limitation |
| T07–T09 | Insert/read, update with revision increment, delete |
| T10–T12 | Foreign-key and unique constraints, and translation of duplicate errors (constraint name kept, values dropped) |
| T13–T14 | `TransactionRunner` commit and rollback (no stale tracked entity afterwards) |
| T15–T16 | Optimistic concurrency; conditional revision update with exactly one winner among 8 concurrent writers |
| T19D | Timestamp precision traced through each layer (diagnostic) |
| T17–T20 | GUID, `DECIMAL(18,4)`, UTC `DateTimeOffset` to the microsecond, and JSON semantic round-trips |
| T21 | **CLI-generated** migration and snapshot apply and roll back |
| T22–T23 | Sessions use TLS (`Ssl_version` / `Ssl_cipher`); plaintext login for `bm_test` is refused |
| T24 | Lock wait timeout (1205) rolls back only the statement; `TransactionRunner` rolls back the whole transaction |
| T25 | Supported deployment path: a ranged (non-idempotent) script applied once; migration history prevents reapplication |
| LP | Least privilege of the runtime account |
| SQL | Owner-scoped query uses `ix_spike_items_owner_id` (checked with `EXPLAIN`) |

**Findings and limitations:**
- **T19:** the provider lost microseconds with `datetime` (run 44292102702) and **still** with `datetime(6)` (run 44296302240), so its own `DateTimeOffset` path drops them. Run 44301027243 (T19D) located it: storage and parameters keep `.123456`, and only the **unconverted EF read** returns `.0000000`. `MySqlModelConventions` now converts every `DateTimeOffset` to its UTC `DateTime` in `datetime(6)` and reads it back at +00:00; UTC-only `DateTime`. T19's exact-microsecond assertion is unchanged. **T19D** records, layer by layer, where precision survives (SQL literal, `DateTime` parameter, `DateTimeOffset` parameter, EF unconverted, EF converted, raw stored text, materialisation).
- **T06 (acceptance changed, lead-approved Option B, 2026-11-18):** originally "the idempotent script is safe to rerun". That failed in runs 44292102702 and 44296302240 because `MySql.EntityFrameworkCore` 10.0.x emits SQL Server syntax (`IF NOT EXISTS(...) BEGIN … END;`). T06 now pins this **known provider limitation**: the pattern is present, MySQL rejects it with 1064, and no schema or history damage results. It must be reassessed on a provider upgrade. Supported deployment is the ranged script (T25).
- The hand-written spike migration (T04–T06) has no snapshot, so `PendingModelChangesWarning` is suppressed for `SpikeDbContext` only. T21 covers the generated path with that check enabled.
- If a test process is killed, its `buildmind_test_<guid>` database can be left behind. In CI the container is discarded anyway; locally, remove leftovers by name.

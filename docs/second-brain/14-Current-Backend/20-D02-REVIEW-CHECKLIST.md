# BE-M4-D02 independent reviewer checklist (PR #18 merged, PR #21 open)

For a **human reviewer other than the author**. AI review is not approval. Evidence IDs refer to `17-Progress/03-EVIDENCE-REGISTER.md`.

## Scope and boundaries
- [ ] Only M4-owned paths changed: `src/BuildMind.Infrastructure/Persistence/**`, `Configuration/DatabaseOptions.cs`, `src/BuildMind.API/Configuration/DatabaseOptionsValidation.cs`, DI wiring, `tests/BuildMind.Infrastructure.Tests/Persistence/**`, `tests/BuildMind.Persistence.IntegrationTests/**`, `tools/database/mysql/**`, `.github/workflows/backend-ci.yml`, docs.
- [ ] No feature entity mappings, feature migrations, JWT/password code or RAG changes.
- [ ] Domain and Application have no EF Core/MySQL references (`LayerDependencyTests`).
- [ ] Packages are exactly the G03-authorized set at 10.0.12, with Design as `PrivateAssets=all`. No other additions.

## Configuration and secrets
- [ ] `Database:Enabled` defaults to false. When disabled, no connection, migration or database creation happens.
- [ ] The connection string comes only from `ConnectionStrings:BuildMind` / `BUILDMIND_MIGRATIONS_CONNECTION`.
- [ ] Validation rejects: root; missing server/database/user; `Persist Security Info`; `Allow Load Local Infile`; TLS weaker than Required; anything but `VerifyFull` for non-loopback servers or outside Development. Messages never echo values.
- [ ] No real credential in the repository or CI. CI passwords are random per run, masked, and scanned before artifact upload.

## Transactions and errors
- [ ] `TransactionRunner`: no retries; commit failure → `TransactionOutcomeUnknownException`; rollback failure → `TransactionRollbackUncertainException` (original inner); disposal can't mask the outcome; tracked entities discarded after rollback or an unknown outcome; nesting rejected.
- [ ] `PersistenceErrorClassifier` keeps only kind, error number and constraint name (anchored regex); values and messages are dropped.
- [ ] 1205 is **not** treated as a full rollback (`ServerRolledBackTransaction` is true only for 1213). `IsTransient` does not imply the operation is safe to retry.
- [ ] EF failure events that quote provider messages are suppressed; `PersistenceLog` provides safe diagnostics instead.

## Timestamps (EV-20261118-42..43)
- [ ] All `DateTimeOffset`/`DateTime` values are stored as UTC `datetime(6)` through converters. `DateTime` must be UTC. The original offset is intentionally not kept.
- [ ] The root cause is understood: the provider's **unconverted `DateTimeOffset` materialisation** drops microseconds; storage and parameters keep them (T19D, 7 stages).

## Migrations (lead decision, Option B)
- [ ] Deployment = reviewed **ranged** scripts + EF history + `bm_migrator` + backup + verification. No automatic or production migrations.
- [ ] The procedure in `tools/database/mysql/README.md` covers the history check, single executor, implicit DDL commit and partial-failure recovery, and doesn't claim atomicity or idempotency.
- [ ] T06 acceptance change is understood: the historical failure is preserved (EV-38/40), and the test now pins the provider limitation.

## CI (`database-compatibility`)
- [ ] Workflow token is read-only; `pull_request` only (no `pull_request_target`); `persist-credentials: false`.
- [ ] Image pinned by digest; port bound to loopback; `tmpfs` (no volume); 25-minute timeout; `docker rm -f -v` always runs.
- [ ] `partial_revokes` check and bm_test grant preflight; accounts `REQUIRE SSL`; least privilege verified (LP test).
- [ ] Job-local .NET 10 SDK pin (`global.json` written at run time, not committed).
- [ ] Existing `dotnet-build` and `python-tests` jobs unchanged in behaviour.

## Known limitations to accept or challenge
- [ ] Idempotent migration scripts are unusable with this provider (T06).
- [ ] The provider's unconverted `DateTimeOffset` read path loses microseconds (mitigated by the converter).
- [ ] **T06 evidence gap:** its "provider version" line reports `Microsoft.EntityFrameworkCore 10.0.12.0` (EF Core's generic provider type), so it doesn't prove the Oracle provider assembly version. The major-version guard is therefore weaker than intended (follow-up: read the version from the MySQL options-extension assembly).
- [ ] The spike migration (T04–T06) is hand-written; T21 covers the generated path.
- [ ] G03 commercial licensing and acceptance of a public repository are **unresolved**.

## Evidence to check
Final run [44301027243](https://github.com/Kogulanathan69/BuildMind-Platform/actions/runs/44301027243): all jobs green. MySQL 28/28 (TRX artifact `mysql-compatibility-results`). Earlier runs: 44292102702 (24/26), 44296302240 (25/27).

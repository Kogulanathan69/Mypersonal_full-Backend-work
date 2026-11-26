# EF Core MySQL provider study — PROPOSAL for G03 (BE-M4-D01b)

Status: **research only; G03 remains OPEN pending lead approval.** No provider, tool or package was installed and no database was touched. Research date 2026-11-17. Target stack: net10.0, ASP.NET Core runtime 10.0.12 (installed), MySQL 8.4 LTS (Windows service `MySQL84` present on the lead workstation; never connected).

Evidence labels: **VERIFIED** = read directly from NuGet registry metadata today; **DOCUMENTED** = stated on an official vendor/project page read today; **ASSUMED** = reasonable but not stated by an authoritative source; **TO SPIKE** = must be proven by tests on a disposable MySQL 8.4 database (BE-M4-D02, approval required).

## Candidates

### MySql.EntityFrameworkCore (Oracle)

| Aspect | Finding | Label |
|---|---|---|
| Latest stable | `10.0.12+MySQL26.10.0`, published 2026-10-21, listed; owner `MySQL`, NuGet verified-owner badge | VERIFIED |
| Version history | 10.0.0-rc (2025-10-22), 10.0.1 (2026-01-21), 10.0.7 (2026-05-06), 10.0.9 (2026-07-29), 10.0.12 (2026-10-21); 11.0.0-preview (2026-10-21) | VERIFIED |
| .NET 10 / EF Core 10 | `net10.0` dependency group: `Microsoft.EntityFrameworkCore` and `.Relational` **≥ 10.0.12**, `MySql.Data` ≥ 26.10.0 (also net8.0/net9.0 groups) | VERIFIED (metadata) |
| Official docs on EF Core 10 | Connector/NET EF Core page's version table stops at EF Core 9.0 — **docs lag the packages** | DOCUMENTED (absence) |
| MySQL 8.4 | Docs: "Server version: MySQL 8.0 or later"; 8.4 not named. Download page (search summary) says the connector is backwards compatible with all currently supported servers | 8.4 support ASSUMED; TO SPIKE |
| License | `GPL-2.0-only WITH Universal-FOSS-exception-1.0` (package metadata) | VERIFIED — **legal/licence review required** before adoption; no legal conclusion drawn here |
| Underlying driver | `MySql.Data` (Oracle ADO.NET driver), version line now 26.x | VERIFIED |
| Documented limitation | "Memory-Optimized Tables is not supported" | DOCUMENTED |
| Migrations | Not described on the pages read (examples use `EnsureCreated` and scaffolding) | TO SPIKE |
| JSON columns, DateTime/DateTimeOffset, decimal precision, concurrency tokens, transactions/execution strategy | Not described on the pages read | TO SPIKE |

### Pomelo.EntityFrameworkCore.MySql (Pomelo Foundation, on MySqlConnector)

| Aspect | Finding | Label |
|---|---|---|
| Latest release | `9.0.0`, published 2025-08-17 (172 versions total; nothing newer, including prerelease, on nuget.org) | VERIFIED |
| EF Core range | `Microsoft.EntityFrameworkCore.Relational` **[9.0.0, 9.0.999]** — cannot be combined with EF Core 10 (restore conflict) | VERIFIED |
| EF Core 10 status | PR #2019 "Upgrade to EF Core 10.0.0" **open** since 2025-11-15; maintainer notes 703 failing functional tests at that time; no maintainer ETA visible; community requests unanswered (latest visible 2026-09-01); third-party remark "might never happen" | DOCUMENTED (GitHub PR page, partially loaded) |
| EF Core 9 support | EF Core 9 / .NET 9 (STS) **reached end of support 2026-11-10** (Microsoft) — Pomelo 9 + EF Core 9 would be an unsupported stack | DOCUMENTED (Microsoft lifecycle, via search) |
| MySQL 8.4 | README "Currently tested versions": MySQL 8.4, 8.0, MariaDB LTS lines (for the 9.0 line) | DOCUMENTED |
| License | MIT | VERIFIED |
| Adoption | ≈145 M total downloads (NuGet search) | VERIFIED |
| Forks | A third-party fork advertises EF Core 10 builds | Not evaluated — supply-chain risk; not recommended |

## Feature comparison for BuildMind needs

| Need (V3 04-DATABASE, M3 transaction design) | Oracle 10.0.12 | Pomelo 9.0.0 | Provider-independent approach |
|---|---|---|---|
| net10.0 + supported EF Core | Yes (EF Core 10 LTS, supported to 2028-11) — VERIFIED metadata | No (EF Core 9, out of support) | — |
| Transactions | Standard EF Core `BeginTransaction` — ASSUMED; TO SPIKE incl. rollback | Supported (EF 9) | Feature-specific atomic ports (I5); short transactions |
| Optimistic concurrency | TO SPIKE | — | Application-managed `long Revision` as EF concurrency token (no `rowversion`) — works with any relational provider (EF Core design) |
| Migrations (apply, script, rollback) | TO SPIKE | Supported | Review generated SQL; disposable DB; idempotent scripts |
| Decimal precision | TO SPIKE (`HasPrecision` → `DECIMAL(p,s)`) | — | Explicit precision on every decimal property |
| Date/time | TO SPIKE (`DateTimeOffset` mapping behaviour unknown) | — | Store UTC: value converter `DateTimeOffset` ↔ UTC `DateTime` in `DATETIME(6)`; offset not persisted (all values UTC by convention) |
| JSON storage | TO SPIKE (native `JSON` type, EF `ToJson`) | — | MVP: geometry/requirements JSON as string column (`JSON` or `LONGTEXT`) serialized by the app — M3 already persists `GeometryJson` + SHA-256 hash as a string, so no provider JSON mapping is required |
| Unique-key violation translation | TO SPIKE (MySQL error 1062 surfaced via provider exception) | — | Translator in Infrastructure → 409 `DUPLICATE_REQUEST` |
| Retries / execution strategy | TO SPIKE | — | No automatic retry of non-idempotent writes |
| GUID storage | TO SPIKE (`CHAR(36)` vs `BINARY(16)`) | — | Decide explicitly in mapping; consistent ordering/indexing |

## Options

| Option | Summary | Assessment |
|---|---|---|
| **O1** | Oracle `MySql.EntityFrameworkCore 10.0.12+MySQL26.10.0` with EF Core 10.0.12 | Only supported, stable EF Core 10 MySQL provider found; version aligns with installed runtime 10.0.12. Risks: GPL+FOSS-exception licence review; sparse official docs; feature behaviour unproven |
| O2 | Pomelo 9.0.0 with EF Core 9 on net10.0 | Restores, but EF Core 9 is out of support since 2026-11-10 — security patches stop. Not recommended |
| O3 | Wait for Pomelo EF Core 10 | No ETA; blocks D02 indefinitely |
| O4 | Third-party Pomelo fork | Unverified publisher; supply-chain risk. Not recommended |
| O5 | Dapper/ADO.NET without EF Core | Contradicts approved EF Core direction (V3 D304); more hand-written SQL. Not recommended |

## Recommendation (for approval, not a decision)

**O1, conditionally:**
1. **Licence review** of `GPL-2.0-only WITH Universal-FOSS-exception-1.0` (and `MySql.Data`) by the lead/appropriate authority for this project's distribution and hosting model.
2. **D02 spike** on a disposable MySQL 8.4 database with a least-privilege test account (approval and credentials via user-secrets required): migration apply → rollback → re-apply; transaction commit/rollback; concurrency conflict on `Revision`; unique-key violation translation; `DECIMAL` precision round-trip; UTC `DateTimeOffset` round-trip; JSON-as-string and native `JSON` column; GUID mapping; generated SQL review. Results recorded as evidence before any feature mapping.
3. Exact pins at implementation (separate approval): `MySql.EntityFrameworkCore 10.0.12+MySQL26.10.0`, `Microsoft.EntityFrameworkCore.Design 10.0.12` (migrations, private asset), `dotnet-ef` 10.0.12 as a **local tool manifest** (not global); NuGet vulnerability audit on restore.
4. **Fallback:** if the spike fails a must-have item, reassess Pomelo's EF Core 10 status before considering any other route.

CI note: Linux CI can run a disposable MySQL 8.4 service container for integration tests later — a CI change requiring approval.

## Sources (read 2026-11-17)

- NuGet registry/flat-container/search APIs for `MySql.EntityFrameworkCore`, `Pomelo.EntityFrameworkCore.MySql`, `MySqlConnector` (queried directly).
- [MySQL Connector/NET — Entity Framework Core support](https://dev.mysql.com/doc/connectors/en/connector-net-entityframework-core.html) and its example/scaffold/charset subpages.
- [MySQL Connector/NET download page](https://dev.mysql.com/downloads/connector/net/) (via search summary).
- [Pomelo PR #2019 — Upgrade to EF Core 10.0.0](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/pull/2019); [Pomelo README](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql); [issue #2016](https://github.com/PomeloFoundation/Pomelo.EntityFrameworkCore.MySql/issues/2016).
- [Microsoft: .NET 8 and .NET 9 end of support](https://devblogs.microsoft.com/dotnet/dotnet-8-9-end-of-support/); [EF Core what's new / support dates](https://learn.microsoft.com/ef/core/what-is-new).

---

## STEP 03-B re-verification and selection plan (2026-11-18)

**State: G03 OPEN. Oracle provider = research candidate (lead, 2026-11-17) — BLOCKED for adoption until licence review and an authorized database spike pass.** Nothing installed, no database created, no configuration changed.

### Re-verified evidence (NuGet APIs queried 2026-11-18)

| Item | Result | Label |
|---|---|---|
| `MySql.EntityFrameworkCore` latest stable | `10.0.12` (`+MySQL26.10.0`); latest overall `11.0.0-preview` | VERIFIED |
| Its target frameworks | `net8.0`, `net9.0`, `net10.0`; `net10.0` group requires EF Core / Relational **≥ 10.0.12 (no upper bound)** and `MySql.Data` ≥ 26.10.0 | VERIFIED |
| Its licence (nuspec) | `GPL-2.0-only WITH Universal-FOSS-exception-1.0` | VERIFIED |
| `MySql.Data` latest stable | `26.10.0` | VERIFIED |
| `Pomelo.EntityFrameworkCore.MySql` | latest (stable and any) still `9.0.0` | VERIFIED |
| EF Core 11.0.0 | Stable, published 2026-11-10, targets **`net11.0` only** — cannot resolve into `net10.0` projects; NuGet resolves Oracle's open lower bound to the lowest match | VERIFIED |
| `Microsoft.EntityFrameworkCore(.Design)` 10.0.12 | Present on NuGet | VERIFIED |
| NuGet vulnerability database | No advisories for `MySql.EntityFrameworkCore`, `MySql.Data`, `Pomelo.EntityFrameworkCore.MySql`, EF Core, EF Core Relational/Design; `MySqlConnector` advisory GHSA-473q-m89c-ghf8 for versions < 2.6.1 (Pomelo route only) | VERIFIED (database as of query time) |
| MySQL 8.4 support by Oracle provider | Official docs: "MySQL 8.0 or later"; 8.4 not named | ASSUMED — spike required |
| Official Oracle EF Core docs | Version table still stops at EF Core 9.0 | DOCUMENTED (docs lag packages) |
| Maintenance | Oracle: regular 10.0.x releases Jan–Oct 2026. Pomelo: no release since 2025-08-17; EF Core 10 PR open | VERIFIED (release dates) |

### Conditional recommendation

**Oracle `MySql.EntityFrameworkCore 10.0.12+MySQL26.10.0` remains the only supported, stable EF Core 10 option on the approved .NET 10 platform** — recommended *conditionally*. Adoption requires: (1) licence compatibility review for BuildMind's intended commercial use (GPL-2.0 with Universal FOSS exception for the provider and `MySql.Data`; obligations depend on how the software is distributed or hosted — not determined here); (2) official package and dependency verification at restore time with NuGet audit; (3) the spike below passing on a disposable MySQL 8.4 database. If (1) or (3) fails: reassess Pomelo's EF Core 10 status; do **not** downgrade to EF Core 9 (out of support) or adopt unverified forks.

**Proposed exact versions (to request at D02):** `MySql.EntityFrameworkCore 10.0.12+MySQL26.10.0`; explicit `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Relational` **10.0.12** pins (guards the open-ended dependency); `Microsoft.EntityFrameworkCore.Design 10.0.12` (`PrivateAssets=all`, migrations only); `dotnet-ef 10.0.12` via a repository **local tool manifest**. All match the installed runtime 10.0.12.

### Database identities (security requirement)

| Identity | Purpose | Privileges | Where its secret lives |
|---|---|---|---|
| DBA / admin | Create databases and users | Administrative; **never used by the application** | Lead only, outside the repo |
| Migration | Apply schema migrations | DDL on the BuildMind schema only | User Secrets (dev) / deployment secret store; CI secret only if an approved migration job exists |
| Runtime | Application access | `SELECT, INSERT, UPDATE, DELETE` on the BuildMind schema; no DDL, no `GRANT`, no global privileges | User Secrets (dev) / deployment secret store |
| Integration test | Disposable test schema | DDL + DML on a disposable test schema only | User Secrets (dev); CI service container credentials generated per run |

The `root` account is never the runtime identity. Credentials are never printed, requested in chat, committed or logged.

### Disposable-database spike plan (BE-M4-D02, requires separate authorization)

| # | Test | Pass criterion |
|---|---|---|
| 1 | Connection with runtime identity | Connects; least privilege confirmed (DDL attempt fails) |
| 2 | DbContext initialization | Model builds; provider version logged without secrets |
| 3 | Migration generation | Migration and SQL script generated; script reviewed |
| 4 | Migration application (empty DB) | Applies with migration identity |
| 5 | Migration rollback | Down migration returns to empty schema |
| 6 | Reapplication | Re-applies cleanly; idempotent script works |
| 7 | Foreign keys | Orphan insert rejected |
| 8 | Unique constraints | Duplicate rejected; translated to 409 `DUPLICATE_REQUEST` |
| 9 | Transaction commit | Multi-row write visible after commit |
| 10 | Transaction rollback | Forced failure leaves no partial rows |
| 11 | Optimistic concurrency | Stale `Revision` update affects 0 rows → `DbUpdateConcurrencyException` → 409 `REVISION_CONFLICT` |
| 12 | Conditional revision pointer update | Compare-and-set succeeds once under concurrent attempts |
| 13 | Duplicate request handling | Same command/idempotency key returns the existing outcome |
| 14 | GUID persistence | Round-trip equality; chosen storage type (`CHAR(36)` or `BINARY(16)`) documented and indexed |
| 15 | Decimal precision | `HasPrecision` values round-trip without rounding loss |
| 16 | UTC timestamps | `DateTimeOffset` stored as UTC `DATETIME(6)` and read back equal; offset policy documented |
| 17 | JSON persistence | Geometry JSON string and native `JSON` column round-trip; hash unchanged |
| 18 | Error translation | Provider exceptions mapped to shared codes; no SQL or connection text in responses |
| 19 | Generated SQL inspection | Reviewed for each operation; no unexpected table scans on owner-scoped queries |
| 20 | Least privilege | Runtime identity cannot `DROP`/`ALTER`/`GRANT` |

### Security risk assessment

| Risk | Mitigation |
|---|---|
| Licence non-compliance | Review before adoption |
| Open-ended EF Core dependency drifting | Explicit 10.0.12 pins; EF Core 11 needs `net11.0` |
| Over-privileged runtime account | Separate identities above; spike test 20 |
| Secrets in repo/logs | User Secrets / secret store; logging deny-list (C10) |
| SQL error leakage | Error translation + Problem Details (C3) |
| Sparse provider documentation | Behaviour proven by spike tests, not assumed |

### BE-M4-D02 acceptance criteria

Licence review recorded; packages restored at the exact versions with no audit warnings; all 20 spike tests pass on disposable MySQL 8.4 with evidence recorded; generated SQL reviewed; identities created by the lead with least privilege; no credentials in Git; existing CI unaffected; G03 then moved to a recorded lead decision.

## Lead decision — 2026-11-18

**CONDITIONAL TESTING APPROVAL ONLY** (Team lead (M4 + M7; GitHub `Kogulanathan69`)). Oracle `MySql.EntityFrameworkCore` is the preferred candidate and may be considered for the future BE-M4-D02 disposable-database spike (direction: .NET 10, EF Core 10, MySQL 8.4 LTS, `dotnet-ef` local tool manifest). **G03 remains OPEN.** Final adoption requires: commercial-use licence compatibility review, verified dependency versions, NuGet vulnerability audit, MySQL 8.4 compatibility testing, migration, transaction, concurrency and least-privilege verification, and explicit lead acceptance. This decision does **not** authorize dependency installation, database creation, configuration changes or execution of the spike. All evidence above is preserved unchanged.

## Revalidation — 2026-11-18 (STEP 05-A4, read-only)

No change to the study's conclusion (evidence EV-20261118-24): `MySql.EntityFrameworkCore 10.0.12+MySQL26.10.0` is still the latest stable EF Core 10 MySQL provider. Its net10.0 dependencies are EF Core and Relational 10.0.12 plus `MySql.Data 26.10.0`. The licence is unchanged and covers both the provider and `MySql.Data`. The NuGet registry lists no vulnerabilities for the provider, its dependencies or the EF tooling. Pomelo is still 9.0.0, which needs EF Core 9, and EF Core 9 has been out of support since 2026-11-10. EF Core 10.0.15 exists, but staying on **10.0.12** keeps all `Microsoft.Extensions.*` references at 10.0.12, which avoids NU1605 downgrade conflicts, and matches the installed runtime. MySQL 8.4 support by the provider is still **ASSUMED** until the spike runs. Local server: MySQL Community 8.4.9, service `MySQL84`, port 3306 (EV-20261118-23).

**Licence gate (not a legal conclusion):** BuildMind is intended to be commercial. The provider and `MySql.Data` are GPL-2.0-only with the Universal FOSS exception. Whether copyleft obligations apply depends on how BuildMind is distributed or hosted (for example a hosted service versus software delivered to customers). Possible routes: accept for hosted-only use after legal review, buy a commercial licence from Oracle, or wait for an MIT-licensed provider. **The lead must accept one of these explicitly. Nothing is approved by this note.**

## BE-M4-D02 implementation readiness plan — PROPOSAL (2026-11-18)

Status: **stopped at the provider and licence approval gate.** No package was installed, no database was touched and no code was written. "Confirmed" below means an existing approved decision or verified repository fact; everything else is a proposal.

### Confirmed facts and decisions
- M4 is the single writer for the DbContext and migrations (ownership). The repository adapters belong in `src/BuildMind.Infrastructure/Persistence` (README).
- C8 options pattern: binding and validation live in the API composition root, and secrets are never logged. C3 Problem Details: database errors never leak SQL or connection text. C10 logging safety applies.
- Ports already on `develop` (owned by their feature owners):
  - M1: `IUserRepository`, `IProjectRepository` (`UpdateAsync` takes an expected revision);
  - M5: `IRequirementRevisionRepository`, `IReportRepository`, `IArchitectRequestRepository` (all specify **atomic** operations, plus conflict and no-partial-write semantics);
  - M3: `IPlanRevisionStore` (`TryAppendAtomicallyAsync`) and the provisional integration ports.
- Domain types:
  - `User` and `Project` have private setters with defaults.
  - `Report`, `ArchitectRequest` and `RequirementRevision` are immutable, have no parameterless constructor, and carry nested value objects (`ReportSnapshot`, `RequirementContent`).

### Remaining blockers
1. **G03 licence acceptance** and approval of the exact package set below.
2. **Where the database tests run:** local C# test execution is blocked by Smart App Control (G17), and CI has no MySQL. A database CI job (a `mysql:8.4` service container with per-run generated credentials) needs separate approval to change the CI workflow. Alternatively, G17 is resolved on the workstation.
3. **Disposable database and accounts:** the lead creates these outside the repository. Secrets go only into User Secrets or environment variables and are never shared in chat.
4. **Persistence mapping strategy:** M4 recommends separate persistence models in Infrastructure, mapped to and from domain types through each type's `Rehydrate`, because the domain types are immutable. Feature owners (M1, M3, M5) need to agree.
5. **Feature schemas are not frozen:** D02a ships no feature tables. Each feature's first migration needs its owner's agreement (D02b and later).
6. Earlier open items still apply: the A1/A2 identity scope (affects the `users` table design), G20 (who implements the M3 ports), G12 (consent transaction).

### Architecture (proposal)
| Concern | Proposal |
|---|---|
| Context | `BuildMindDbContext` (Infrastructure). `ApplyConfigurationsFromAssembly` discovers `IEntityTypeConfiguration<T>` classes under `Persistence/Configurations/<Feature>/`. No `DbSet` for feature tables in D02a |
| Options | `DatabaseOptions` (`Database` section): `Enabled` (default **false**, so CI and current tests need no database), `CommandTimeoutSeconds` (1–120), `MaxRetryCount`. The connection string comes only from `ConnectionStrings:BuildMind` (User Secrets or environment variable). It is validated at start-up when `Enabled` is true and never logged or echoed |
| DI | `AddBuildMindPersistence` is called from `AddInfrastructure` and registers the DbContext (scoped) only when the database is enabled. The API binds the options as in C8 |
| Transactions | No generic repository or public unit of work. Each atomic port method runs inside one internal `TransactionRunner.ExecuteAsync`, which uses the provider's execution strategy and one transaction, with no external calls inside. Cancellation means rollback; a commit-unknown outcome maps to `TransactionOutcomeUnknown` where the port defines it |
| Concurrency | Revision/StateVersion columns are concurrency tokens. Compare-and-set updates use `ExecuteUpdateAsync(... WHERE id = @id AND revision = @expected)`: zero rows affected means a conflict result, never a silent overwrite |
| Error translation | An internal `PersistenceErrorTranslator` maps MySQL error numbers to failure kinds: 1062 duplicate → `DUPLICATE_REQUEST`/`CONFLICT`; 1451/1452 FK → `CONFLICT`/not found; 1213 deadlock and 1205 lock timeout → retry or `DEPENDENCY_UNAVAILABLE`; connection failures → `DEPENDENCY_UNAVAILABLE`. Messages are discarded and only the code and type are logged |
| Ownership queries | Every adapter query filters by `OwnerId`/`ProjectId`, as the ports require. Composite indexes start with the scoping key |
| Migrations | Stored in `Infrastructure/Persistence/Migrations`; M4 is the only author. They are applied **only** from a reviewed `dotnet ef migrations script --idempotent` run under the migration identity. **The application never migrates itself at start-up.** Design-time connection comes from `ConnectionStrings__BuildMindMigrations` (environment variable or User Secrets) |
| Health | Readiness (C11b) stays design-only. D02 adds no health endpoint change, and `/health` (liveness) never touches the database |
| Naming | Lowercase `snake_case` tables and columns, because MySQL table-name case sensitivity differs between Windows and Linux. Plural tables (`users`, `projects`). Constraint prefixes `pk_`, `fk_`, `ux_`, `ix_`. Databases: `buildmind_dev`, `buildmind_test_<run>` (disposable) |
| Types (to be confirmed by the spike) | GUID → `CHAR(36)` or `BINARY(16)`, decided by the spike. Timestamps → UTC `DATETIME(6)`. Money/area → `DECIMAL` with explicit `HasPrecision`. Geometry and snapshots → native `JSON` (or `LONGTEXT` plus a stored hash if the round-trip changes bytes) |

### Database accounts (proposal; the lead creates them, and secrets stay outside the repository)
| Account | Host | Grants |
|---|---|---|
| `bm_runtime` | `localhost` | `SELECT, INSERT, UPDATE, DELETE ON buildmind_dev.*`; no DDL, `GRANT` or global privileges |
| `bm_migrator` | `localhost` | `CREATE, ALTER, DROP, INDEX, REFERENCES, SELECT, INSERT, UPDATE, DELETE ON buildmind_dev.*` |
| `bm_test` | `localhost` | `ALL ON \`buildmind\_test\_%\`.*` (disposable spike and test databases only) |
| `root`/DBA | — | Never used by the app, tests or migrations |

Production TLS: `SslMode=Required`. Local development may use `Preferred`, recorded as a decision.

### Dependency approval table (none approved)
| Package / tool | Version | Project | Licence | Purpose |
|---|---|---|---|---|
| `MySql.EntityFrameworkCore` | 10.0.12+MySQL26.10.0 | Infrastructure | GPL-2.0 + FOSS exception | Provider (**licence gate**) |
| `Microsoft.EntityFrameworkCore` | 10.0.12 (explicit pin) | Infrastructure | MIT | Core |
| `Microsoft.EntityFrameworkCore.Relational` | 10.0.12 (explicit pin) | Infrastructure | MIT | Relational, `ExecuteUpdate` |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.12, `PrivateAssets=all` | API (startup project for tooling) | MIT | Migrations design time |
| `dotnet-ef` | 10.0.12 | `.config/dotnet-tools.json` (local) | MIT | Migration tooling |
| Transitive (`MySql.Data` 26.10.0, BouncyCastle 2.6.2, Google.Protobuf 3.35.0, K4os LZ4 1.3.8, ZstdSharp 0.8.8, System.Configuration.ConfigurationManager 10.0.8, System.Security.Permissions 10.0.8) | as resolved | — | GPL+FOSS exc. / MIT / BSD-3 | Approved together with the provider |

The integration tests need no new test packages; xunit and the test SDK are already approved.

### Proposed file inventory
**Production (D02a):**
- `src/BuildMind.Infrastructure/Persistence/BuildMindDbContext.cs`
- `src/BuildMind.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
- `src/BuildMind.Infrastructure/Persistence/Transactions/TransactionRunner.cs`
- `src/BuildMind.Infrastructure/Persistence/Errors/PersistenceErrorTranslator.cs` and `PersistenceFailure.cs`
- `src/BuildMind.Infrastructure/Persistence/DesignTime/BuildMindDesignTimeDbContextFactory.cs`
- `src/BuildMind.Infrastructure/Configuration/DatabaseOptions.cs`
- `src/BuildMind.API/Configuration/DatabaseOptionsValidation.cs`

Edits: `Infrastructure/DependencyInjection.cs`, `API/ApiServiceCollectionExtensions.cs`, `API/appsettings.json` (a `Database` section with no secrets), the Infrastructure and API csproj files, `.config/dotnet-tools.json`, and the Persistence README.

**Tests:**
- Run in CI without a database: `tests/BuildMind.Infrastructure.Tests/Persistence/DatabaseOptionsTests.cs`, `PersistenceRegistrationTests.cs`, `ConnectionSecretLoggingTests.cs`.
- Need a database (spike): a new project `tests/BuildMind.Persistence.IntegrationTests/` containing `Support/DisposableMySqlDatabase.cs` (creates and drops `buildmind_test_<guid>`), `Support/SpikeDbContext.cs` (synthetic test-only entities, **no feature schema**), and `ProviderCompatibilityTests.cs`. It is added to the solution only once the database CI job is approved.

### Spike test plan (disposable `buildmind_test_<guid>`, synthetic entities only)
| # | Test | Pass criterion |
|---|---|---|
| 1 | Connection | Runtime identity connects; provider and server versions recorded without secrets |
| 2 | DbContext init | Model builds; no client evaluation warnings |
| 3 | Migration generation | `migrations add` and an idempotent script are produced and the SQL is reviewed |
| 4 | Migration apply | Applies with `bm_test` (spike) / `bm_migrator` |
| 5 | Rollback | Down migration leaves an empty schema plus the history table |
| 6 | Reapply | Clean reapply; the idempotent script runs twice without error |
| 7 | Insert/read | Values round-trip |
| 8 | Update | Persisted change, with the revision incremented |
| 9 | Delete | Row removed; cascade rules as configured |
| 10 | Foreign key | Orphan insert rejected (1452) |
| 11 | Unique | Duplicate rejected (1062) |
| 12 | Duplicate translation | Becomes a `DUPLICATE_REQUEST`/`CONFLICT` failure kind with no SQL text |
| 13 | Commit | Multi-row write visible |
| 14 | Rollback | Forced failure leaves zero rows |
| 15 | Optimistic concurrency | Stale token raises `DbUpdateConcurrencyException`, translated to a conflict |
| 16 | Conditional revision update | Exactly one of N concurrent compare-and-set attempts wins |
| 17 | GUID round-trip | Equality holds and the storage type is documented |
| 18 | Decimal precision | Exact at the configured precision and scale |
| 19 | UTC timestamps | `DateTimeOffset` UTC microseconds round-trip; non-UTC rejected or normalised per policy |
| 20 | JSON round-trip | JSON column value is semantically equal; the stored hash of a canonical string is unchanged |
| + | Least privilege | `bm_runtime` cannot `CREATE`/`ALTER`/`DROP`/`GRANT` |
| + | SQL inspection | Owner-scoped query plans use indexes (`EXPLAIN`) |

### Implementation order
1. Lead: licence decision and package approval (G03).
2. D02a: options, DI, DbContext, transaction runner, error translator and non-database tests (CI-safe; the database stays disabled by default).
3. Approve the database CI job, or resolve G17; the lead provisions the accounts.
4. Spike project: run the 20 tests and the two extras, and record the evidence.
5. G03 becomes a recorded decision.
6. D02b and later: per-feature mappings and migrations with each owner's agreement (M1 users and projects first), then the M5/M3 atomic adapters.

## BE-M4-D02a implementation — 2026-11-18 (uncommitted, awaiting review)

G03: **CONDITIONAL INTERNAL DEVELOPMENT AND COMPATIBILITY TESTING AUTHORIZATION — PRODUCTION AND COMMERCIAL DISTRIBUTION LICENSING REVIEW PENDING**. G03 stays open.

| Component | Implementation |
|---|---|
| Packages | Infrastructure: `Microsoft.EntityFrameworkCore` and `.Relational` 10.0.12 (explicit pins) and `MySql.EntityFrameworkCore` 10.0.12 (resolves `MySql.Data` 26.10.0). API: `Microsoft.EntityFrameworkCore.Design` 10.0.12 with `PrivateAssets=all`. Root `dotnet-tools.json`: `dotnet-ef` 10.0.12. No vulnerable packages, including transitive ones |
| Configuration | `Database:Enabled` (default **false**) and `Database:CommandTimeoutSeconds` (1–300, default 30). The connection string comes **only** from `ConnectionStrings:BuildMind` (User Secrets or environment variable) and is validated at start-up only when enabled. Rejected: missing server, database or user; `root`; `Persist Security Info=true`; `Allow Load Local Infile=true`; unparseable strings. Messages name keys only |
| Context | `BuildMindDbContext` with no entity types. It discovers `IEntityTypeConfiguration<T>` classes under `BuildMind.Infrastructure.Persistence.Configurations.*` and has no migrations. Registered as scoped; when disabled, resolving it throws a fixed safe message. It never connects, creates databases or migrates at start-up |
| Provider options | One place (`MySqlContextOptions`): `UseMySQL`, command timeout, migrations assembly = Infrastructure, no retrying execution strategy, sensitive-data logging and detailed errors off, and failure events that quote provider messages (`CommandError`, `SaveChangesFailed`, `QueryIterationFailed`, `OptimisticConcurrencyException`) ignored. `Microsoft.EntityFrameworkCore` logs at Warning |
| Design time | `BuildMindDesignTimeDbContextFactory`: only `BUILDMIND_MIGRATIONS_CONNECTION` is read, with no fallback to application configuration; same validation (no root); nothing runs automatically |
| Transactions | Internal `TransactionRunner` (with an `IDbTransactionSource` seam): begin → operation → commit. On an operation failure or cancellation it rolls back and rethrows the original exception; a rollback failure never masks it. Commit is never cancelled midway; a commit failure raises `TransactionOutcomeUnknownException` and is never retried. The transaction is always disposed and nested transactions are rejected. No generic repository or unit of work |
| Error translation | `PersistenceErrorClassifier` produces `PersistenceFailure` (kind, default shared code, transient flag, provider error number, constraint name). Duplicate (1022/1062/1169/1586) → `CONFLICT`; FK (1216/1217/1451/1452) → `CONFLICT`; deadlock 1213 and lock timeout 1205 → `DEPENDENCY_UNAVAILABLE` (transient); connection failures → `DEPENDENCY_UNAVAILABLE`; command timeout → `DEPENDENCY_TIMEOUT`; `DbUpdateConcurrencyException` → `REVISION_CONFLICT`; unknown commit outcome and anything else → `INTERNAL_ERROR`. Only schema constraint names are extracted; values and messages are dropped. Feature adapters choose the final code (for example `DUPLICATE_REQUEST`) |
| Database-free tests (`BuildMind.Infrastructure.Tests/Persistence`, + `LayerDependencyTests`) | 39 methods / 63 cases (suite total 110 / 189). Compiled; **not executed locally (G17)** |
| Spike project `tests/BuildMind.Persistence.IntegrationTests` | 22 tests (T01–T20, LP, SQL), synthetic model and a hand-written migration. **Outside `BuildMind.sln`**; compiled; **not executed** (needs disposable MySQL and approval) |
| Account setup | `tools/database/mysql/001-dev-test-accounts.sql` with placeholders, run manually by the lead, and a README |
| CI | Proposal [19-DATABASE-CI-PROPOSAL](19-DATABASE-CI-PROPOSAL.md); workflow unchanged |

**Not done:** feature mappings and migrations (need owner agreement), repository adapters, readiness and database health integration, and execution of any database test. MySQL 8.4 compatibility is **UNVERIFIED**.

**STEP 05-C review corrections (2026-11-18, commit `2ee8324`):** TLS is mandatory (`SslMode` ≥ Required; accounts `REQUIRE SSL`; production should use `VerifyFull`); tracked entities are discarded after a rollback or an unknown commit; `PersistenceLog` restores safe failure diagnostics; the retry semantics of `IsTransient` are documented; the spike least-privilege probe no longer selects an existing database; the account SQL checks `partial_revokes`. Test inventory: `BuildMind.Infrastructure.Tests` 113 methods / 196 cases (66 new database-free cases in D02a); spike 22. **Not proven by the spike:** CLI-generated migrations with a model snapshot (the spike migration is hand-written; the CI proposal adds a `dotnet-ef` tooling smoke check only).

## BE-M4-D02b — 2026-11-18 (commit `947d8f8`, live results pending)

Corrections: 1205 statement-level rollback semantics; unconfirmed rollback surfaced; disposal can't mask outcomes; `VerifyFull` required for non-loopback servers and non-Development environments. The CI job `database-compatibility` is implemented (see EV-20261118-34). It proves the CLI migration path on the synthetic `GeneratedSpikeDbContext` (T21) separately from the hand-written migration (T04–T06). **MySQL 8.4 compatibility remains UNVERIFIED until the job's actual results are recorded.** G03 open.

### Live results — 2026-11-18 (run 44292102702, MySQL 8.4.13)

24/26 passed. Verified on the live server: connectivity, TLS 1.2, plaintext refusal, least privilege, scoped wildcard grant, CRUD, FK and unique constraints with safe translation, commit and rollback, optimistic concurrency, single-winner compare-and-set, 1205 semantics, GUID `char(36)`, `decimal(18,4)`, JSON (semantic), the CLI-generated migration path, and index use. **Corrected:** timestamp precision (`datetime(6)` convention). **Provider limitation:** idempotent migration scripts are not MySQL-executable, so deployment uses ranged scripts (T25). MySQL 8.4 compatibility is therefore **largely verified, with one open limitation**; the re-run after the corrections is pending. G03 open (licence).

### Run 44296302240 and STEP 05-E10 (2026-11-18)

25/27. **Second provider finding:** `DateTimeOffset` loses microseconds even in `datetime(6)`, so M4 stores timestamps as UTC `DateTime` through a converter (T19D traces the layers). Deployment: Option B (ranged scripts); idempotent scripts are a pinned provider limitation (T06). Both findings must be reassessed on any provider upgrade and weigh on the G03 provider-acceptance decision.

### Final D02b evidence — run 44301027243 (2026-11-18)

**28/28 passed on MySQL 8.4.13** (EV-20261118-43). **Correction to earlier wording:** the `DateTimeOffset` precision loss in `datetime(6)` is in the provider's **unconverted materialisation (read) path**. Storage, `DateTime`/`DateTimeOffset` parameters and EF writes all keep microseconds (T19D). The earlier phrase "`DateTimeOffset` loses microseconds even in `datetime(6)`" refers to that read path, not to writing. Mitigation: the UTC converter. The **compatibility spike passes** for internal development with two documented provider limitations (idempotent scripts; unconverted `DateTimeOffset` reads). **G03 stays OPEN**: commercial licensing and public-repository acceptance are unresolved, and provider acceptance is the lead's decision.

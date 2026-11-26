# M4 delivery and QA report (BE-M4-D07, STEP 10-A)

- **Dates:** 2026-11-18/19.
- **Branch:** `feature/m4-infrastructure-final-qa` from `develop` @ `1ab1f98`.
- **Status:** QA corrections implemented, **NOT committed** (stopped for source review).
- **Not an independent QA approval:** this report is AI-prepared. The D07 acceptance criterion "independent QA approval" is **not met**.

## 1. Verified baseline evidence
- **PR #27:** merged on 2026-11-18T18:26:11Z as `1ab1f98`, with **0 reviews** (G19).
- **Post-merge CI:** run **#52** (id 44369690073, push, `1ab1f98`, success).
- **Downloaded artifacts:** all match run #52 by SHA-256 digest:

| Artifact | SHA-256 |
|---|---|
| `dotnet-test-results` | `19fcd1d6…` |
| `mysql-compatibility-results` | `5ca450cf…` |
| `python-test-results` | `f0865879…` |

- **Results, read independently:**

| Suite | Passed |
|---|---|
| Infrastructure | 412 |
| Requirements | 233 |
| Application | 128 |
| Workflow | 70 |
| Reports | 54 |
| MySQL + live Python | 56 |
| M3 contract checks | 246 |
| Python | 54 |
| **Total** | **1,253** (0 failed, 0 not executed) |

  - `PersistenceConcurrencyLiveTests`: 4/4.
  - `PythonLiveSmokeTests`: 7/7.
- **Migration artifacts:**
  - The three generated C# migration files are identical to the committed files.
  - `buildmind-ranged-from-0.sql` is byte-identical to run #50.
  - The two **synthetic spike** SQL files differ from run #50 **only** in the per-run generated migration ID (`yyyyMMddHHmmss_CiGeneratedSpike`). That is expected; they are not the product migration.
  - Correction to the STEP 10-A prompt: it said "four files byte-identical"; the precise state is above.

## 2. D01–D07 deliverable matrix
Classification legend:
- **RV**: implemented and runtime-verified in CI.
- **IU**: implemented but unverified.
- **PI**: partially implemented.
- **BL**: blocked by another member.
- **PA**: pending approval.
- **NI**: not implemented.

No deliverable is complete: every M4 PR was merged with 0 independent reviews, and the plan says "Done requires … review".

| Task | Item | Status | Evidence / reason |
|---|---|---|---|
| **D01** foundation and contracts | build, `/health`, dependency direction, solution, DI and module registration | **RV** | layer tests, run #52 |
| | error shape C3/C5, config keys C8 | **RV** | Problem Details tests |
| | CORS contract (C6), OpenAPI (C7) | **PA / NI** | decisions open |
| | current-user / password / token contract (A1/A2) | **PA** | `ICurrentUser` adapter exists; no token service |
| **D02** MySQL and identity | provider, DbContext, config validation, least-privilege CI accounts, migration on an empty DB | **RV** | MySQL 56/56 |
| | M1 User/Project mappings | **RV** (schema) / **BL** (repositories) | the M1 rehydration factories don't exist on any branch |
| | JWT validation middleware (item 04) | **NI / PA** | A1/A2 and M1 auth not approved; not simulated |
| | reset/rollback procedure doc | **RV** (CI ranged script) | |
| **D03** schema and migrations | schema, `InitialFeatureSchema`, FK/unique tests, requirement-revision repository | **RV** | |
| | land, survey, plan and generation entities (M2/M3/M6) | **BL** | no entity proposals or contracts |
| **D04** errors and shared security | exception mapping, safe 5xx, log redaction, readiness, security headers, middleware order | **RV** | |
| | CORS allowlist | **PA** (C6) | |
| | auth policies | **PA / BL** (A1/A2, M1) | |
| | request limits | **BL** (M2 upload limits) | Kestrel default 30 MB applies |
| | OpenAPI exposure review | **PA** (C7) | not exposed today |
| **D05** C#-to-Python | typed client, per-environment URL, timeouts, cancellation, `traceparent`, bulkhead, Python readiness, truthful 503/504, live smoke 7/7 | **RV** | |
| | bounded retry for idempotent calls | **NI by design**: no idempotent feature routes exist, so retrying is unsafe | |
| | internal authentication / network restriction (G11) | **PA** | |
| | M2 private storage adapter (item 04) | **BL** | no M2 storage contract |
| | "safe retry does not duplicate jobs" | **BL** | no job contract (M3/M6) |
| **D06** transactions and concurrency | `TransactionRunner`, lead-approved failure mapping, live concurrency tests | **RV** | run #52 |
| | idempotency for duplicate requests | **BL / PA** (C2/C5b) | |
| | production database permission policy (item 04) | **PA** (CI least-privilege only) | |
| | job/version keys with module owners (item 02) | **BL** | |
| **D07** regression and release readiness | clean CI setup and migration from empty | **RV** | |
| | vulnerability audit | **RV** (no vulnerable packages) | |
| | readiness abandoned-probe fix (STEP 10-A/10-B) | **IU** | compiled only |
| | C# + FastAPI auth and CORS verification (item 02) | **NI**: those components don't exist | |
| | setup doc (item 04) | **IU** (§8) | |
| | independent QA approval | **not met** | |

## 3. Implementation inventory (develop + this step)
- **API (composition root):**
  - `GlobalExceptionHandler` and `ProblemDetailsDefaults` (C3/C5);
  - `PersistenceProblemMapping` (D06);
  - `SecurityHeadersMiddleware`;
  - `HttpContextCurrentUser`;
  - options validation (`DatabaseOptionsValidation`, `ServicesOptionsValidation`, start-up validation);
  - readiness (`MySqlReadinessCheck`, `PythonReadinessCheck`, `InternalServiceReadinessCheck`, `ReadinessProbeGate`);
  - the internal transport factory;
  - route module registration (`/api/v1`).
- **Infrastructure:**
  - `BuildMindDbContext` with record configurations and UTC converters;
  - `TransactionRunner`;
  - `PersistenceErrorClassifier`;
  - `RequirementRevisionRepository`;
  - `InternalServiceClient`, `PythonServiceClient`, `InternalServiceBulkhead`;
  - `DatabaseOptions`, `ServicesOptions`.
- **CI:**
  - `dotnet-build` (with TRX and M3 checks);
  - `python-tests` (with JUnit);
  - `database-compatibility`:
    - pinned MySQL 8.4 by digest, per-run random passwords, TLS-only least-privilege accounts;
    - migration generate/verify, live MySQL and Python tests;
    - credential scan before upload, `always()` cleanup.

## 4. Database schema inventory (`20261118133137_InitialFeatureSchema`)
- **Tables:** `users`, `projects`, `requirement_revisions`, `project_requirement_heads`.
- **Foreign keys (4):** `fk_projects_users_owner_id`, `fk_requirement_revisions_projects_project_id`, `fk_project_requirement_heads_projects_project_id`, `fk_project_requirement_heads_requirement_revisions_current`.
- **Indexes (4):** `ux_users_normalized_email`, `ux_requirement_revisions_project_id_revision_number`, `ix_projects_owner_id_created_at_utc`, `ix_project_requirement_heads_project_id_current_revision_id`.
- **Columns:** 1 JSON column; timestamps `datetime(6)` UTC.
- **Concurrency:** `projects.revision` is a concurrency token.

## 5. QA audit findings (STEP 10-A)
### Defect fixed (M4-owned)
**D07-1:** an abandoned readiness probe could keep the single-flight gate.
- **Cause:** `WaitAsync(timeout)` and `CancelAfter(timeout)` use the same duration. When the wait won the race, the linked token source was disposed before it cancelled.
- **Effect:** the abandoned probe was never cancelled. It held the gate until the provider's own timeout, and readiness reported `check_in_progress` meanwhile (**503** for MySQL).
- **Fix:** `ProbeDeadline.Settle` (refined in STEP 10-B, see §13).
- **Tests:** +2 (5 consecutive timeouts each must report `timeout`, never `check_in_progress`, and release the gate).

### Areas audited with no defect
- Layer boundaries (layer tests).
- DI lifetimes: singletons are the gate, bulkhead and `TimeProvider`; the typed client is transient; DbContext and `TransactionRunner` are scoped. The Development test host validates scopes and the build.
- Options validation at start-up.
- Exception handling (D06 audit).
- No raw SQL anywhere: all EF LINQ, sensitive logging off.
- Every async call passes a token. `CancellationToken.None` is used only for commit, rollback and readiness continuations, all intentional.
- Disposal paths.
- Internal destination allowlist and bulkhead (D05 audits).
- Live correlation and no credentials on the wire (PY07).
- CI Python process cleanup (`always()`).
- Credential scan before the MySQL artifact upload. The dotnet and Python artifacts come from jobs that hold no secrets.

### Security and production findings (recorded, NOT implemented; not simulated)
| ID | Finding | Owner / decision |
|---|---|---|
| S1 | No authentication or authorization middleware: `ICurrentUser` is always anonymous | A1/A2, M1 |
| S2 | CORS not configured | C6 |
| S3 | ~~`AllowedHosts: "*"`~~ **host filtering addressed (STEP 05, 2026-11-21):**<br>• default `localhost;127.0.0.1;[::1]`<br>• outside Development, start-up refuses missing, `*`, wildcard or malformed entries ([17 §23](17-SHARED-CONVENTIONS-PROPOSAL.md))<br>**Still open:** HTTPS redirection, HSTS, forwarded headers and deployment host names | hosting decision (G11 / deployment) |
| S4 | ~~No global request size policy (Kestrel default 30 MB)~~ **request size addressed (STEP 05 S4-A, 2026-11-21):**<br>• global body limit 1 MiB<br>• outside Development, start-up refuses unset, 0, or more than 8 MiB<br>• oversized bodies → 413 Problem Details `PAYLOAD_TOO_LARGE` ([17 §24](17-SHARED-CONVENTIONS-PROPOSAL.md))<br>**Rate limiting addressed (S4-B):** per-client token buckets, anonymous ceiling, `auth-strict`, expensive-operation concurrency, 429 `RATE_LIMITED` ([17 §25](17-SHARED-CONVENTIONS-PROPOSAL.md))<br>**Still open:** M2 upload limits, proxy limits and trusted forwarding, multi-instance limits (G11) | M2 limits, lead |
| S5 | ~~GitHub Actions pinned by major tag, not commit SHA~~ **addressed (STEP 05, 2026-11-21):**<br>• every action pinned to the full commit SHA that its major tag pointed to (checkout v4.4.0, setup-dotnet v4.3.1, setup-python v5.6.0, upload-artifact v4.6.2; resolved from the upstream repositories), so CI behaviour is unchanged<br>• `persist-credentials: false` on every checkout<br>**Still open:** automated update proposals (Dependabot) not enabled; updates are manual and must re-resolve SHAs from upstream | — |
| S6 | FastAPI `/docs` and `/openapi.json` exposed | M6 |
| S7 | Internal service authentication / transport | G11 |
| S8 | Every M4 PR merged with 0 reviews | G19 |

## 6. Integration architecture
API → `PythonServiceClient` (allowlisted relative paths, own deadline, bulkhead of 64 per dependency, ≤32 connections, no redirects, proxy or cookies, 4 MB JSON limit, `traceparent`, no retries) → FastAPI `GET /health` only.

Failures map to 503/504 Problem Details. Readiness reports Python as Degraded only. G11 is open.

## 7. Test evidence (compiled vs executed)
| Suite | Executed (run #52) | Compiled now |
|---|---|---|
| Infrastructure | 412 | **417** (+5, not executed) |
| MySQL + live Python | 56 | 56 |
| Others (Requirements/Application/Workflow/Reports/M3) | 233 / 128 / 70 / 54 / 246 | unchanged |
| Python | 54 | 54 (passed locally) |
| **Total** | **1,253** | **1,258** |

## 8. Repeatable setup (names only, no values)
1. Install the .NET 10 SDK (CI pins it with a job-local `global.json`), MySQL 8.4 and Python 3.13.
2. Run `dotnet restore BuildMind.sln`, then `dotnet build -c Release`.
3. Create a database and a least-privilege runtime account (TLS required). Apply the reviewed ranged script (`dotnet ef migrations script 0`), which is Option B. Idempotent scripts are not supported by the provider.
4. Configuration (environment variables):
   - `ASPNETCORE_ENVIRONMENT`
   - `ConnectionStrings__BuildMind`
   - `Database__Enabled`
   - `Database__CommandTimeoutSeconds`
   - `Services__Python__Enabled`, `Services__Python__BaseUrl`, `Services__Python__TimeoutSeconds`
   - `Services__Rag__Enabled`, `Services__Rag__BaseUrl`, `Services__Rag__TimeoutSeconds`
5. Start Python on a private interface only: `python -m uvicorn app.main:app --host 127.0.0.1 --port <port>`.
6. Check `/health` (liveness) and `/health/ready` (MySQL mandatory; Python and RAG Degraded when absent).
7. Test variables: `BUILDMIND_TEST_MYSQL`, `BUILDMIND_TEST_MYSQL_RUNTIME`, `BUILDMIND_TEST_PYTHON_URL`.

**Operational limitations:**
- Windows Smart App Control blocks local C# test execution (G17), so Linux CI is the verification path.
- No authentication, so no feature endpoint can be used for real.

## 9. Cross-member dependencies (state on 2026-11-19)
- **`develop`:** only M4 merges since `fdf7baa`.
- **M1:** `feature/m1-auth-projects` is unmerged, and no `User`/`Project` rehydration factory exists on any branch.
- **M3:** PR #25 is still a draft.
- **M5:** **PR #28** (open, not a draft): 12 files, dashboard only. It touches no M4-owned file and is behind `develop`; it needs review under M7.
- **M2 and M6:** no persistence, storage or job contracts.

**Result:** no previously blocked M4 deliverable became unblocked.

## 10. Required approvals
- A1/A2 (auth).
- C6 (CORS).
- C7 (OpenAPI).
- C2/C5b (idempotency).
- G11 (internal transport, auth and hosting).
- Hosting: AllowedHosts, HTTPS/HSTS.
- Request size and rate limits.
- ~~Action SHA pinning~~ (S5 addressed 2026-11-21).
- Production database account policy.
- G03, G12, G17, G19 (independent reviews of PRs #8–#27), G20.

## 11. Production readiness assessment
**NOT production-ready.**
- **Infrastructure quality (error handling, persistence, integration, CI):** strong and runtime-verified.
- **Missing security essentials:** authentication and authorization, CORS, the hosting and TLS posture, request limits and rate limiting, internal service authentication.
- **No independent review** of any M4 PR.

## 12. Remaining M4 development tasks
1. JWT validation and auth policies, once A1/A2 are approved and M1 delivers.
2. CORS, once C6 is approved.
3. Remaining repositories, once the M1 factories and M2/M3/M6 contracts exist.
4. The M2 storage adapter.
5. Internal authentication, once G11 is decided.
6. Idempotency and job deduplication, once C2/C5b are decided.
7. Request limits and rate limiting.
8. The production database account policy.
9. Independent QA approval of this report and of PRs #8–#27.

## 13. STEP 10-B readiness correction audit (2026-11-19)

| Check | Result |
|---|---|
| Both MySQL and Python abandoned probes are cancelled | ✅ `ProbeDeadline.Settle` in both `finally` blocks |
| Cancellation happens before the source is disposed | ✅ the source is no longer `using`-scoped; `Settle` is its only disposer |
| Gate held until the real probe finishes | ✅ released by the probe's own continuation |
| Gate released after completion or cleanup failure | ✅ `ReleaseAsync` try/finally (MySQL); a plain release (Python, no scope) |
| Caller cancellation distinguishable from timeout | ✅ rethrown when the caller's token is cancelled; otherwise `timeout` |
| Callback failures cannot leak | ✅ observed and dropped; never logged or returned |
| **Blocking callbacks cannot defeat the deadline** | **Defect R2 (fixed).** The 10-A `Cancel()` ran callbacks synchronously on the readiness thread, so a blocking callback (a provider may open a second connection to kill a query) delayed the result past the deadline. Now `CancelAsync()` runs callbacks off-thread. The source is disposed only after BOTH the probe and the callbacks finish (10-A disposed it at method exit, possibly while a callback still ran) |
| Non-cooperative probes cannot accumulate | ✅ at most one outstanding probe per check (single-flight). A probe that never ends keeps reporting `check_in_progress`, which is truthful |
| Tests reliable and deterministic | The two 10-A tests pass deterministically, but whether they **detect** the original race is probabilistic (timer versus wait). Added 3 **deterministic** `Settle` tests: a blocking callback doesn't delay `Settle` (< 2 s) and the source stays alive until the probe ends; callback and probe failures are dropped and the source is still disposed; a finished probe is disposed immediately and never cancelled |

Infrastructure **417** compiled (412 executed + 5 new). Full backend **1,258**.

## 14. STEP 10-E — final QA FAILED: MySql.Data TLS concurrency defect (2026-11-19)

**Final QA is NOT passed.**

### Evidence
- **PR #29** (`b0851f2`): run **#54** (pull_request) passed all three jobs.
- **Post-merge run #55** (id 44376816439, push, `364171a`): `database-compatibility` **failed**.
  - The three downloaded artifacts match run #55 by SHA-256: `mysql-compatibility-results` `0010e66a…`, `dotnet-test-results` `6da674ee…`, `python-test-results` `9604980b…`.
  - **Executed 1,258; passed 1,257; failed 1.** Infrastructure 417, Requirements 233, Application 128, Workflow 70, Reports 54, M3 246, Python 54, MySQL **55/56**.
- **Later `develop` runs on the same suite passed:** #56 (`4ddc2b1`, PR #28 M5) and #57 (`2ad74d1`, PR #25 M3). The failure is **intermittent**.
- **Failed test:** `BuildMindMigrationTests.M03_migration_rolls_back_and_reapplies` (line 79, inside `MigrateAsync`).
  - `System.InvalidOperationException: Operations that change non-concurrent collections must have exclusive access…`
  - Stack: `Dictionary.Remove` ← `MySql.Data.Common.Ssl.StartSSLAsync` ← `NativeDriver.OpenAsync` ← `Driver.CreateAsync` ← `MySqlPool.CreateNewPooledConnectionAsync` ← `MySqlPool.GetPooledConnectionAsync/TryToGetDriverAsync/GetConnectionAsync` ← `MySqlConnection.OpenInternalAsync` ← EF `RelationalConnection.OpenAsync` ← `MySQLMigrator.MigrateAsync`.
- **Versions:**
  - MySql.Data **26.10.0** (assembly 26.10.0.0; net10.0 DLL SHA-256 `adda88e99fcef0b5…`);
  - MySql.EntityFrameworkCore **10.0.12**;
  - MySQL 8.4 (pinned digest);
  - xUnit 2.9.3 (test classes run in parallel by default).
- **Concurrent openers during M03's window (19:07:24.130–25.295 UTC):** `ReadinessLiveTests.RD02` (repeated readiness probes), `RequirementRevisionRepositoryTests.R07` (parallel first saves), and M03 itself. All three were opening TLS connections at the same time.

### Root cause: CONFIRMED from the shipped provider IL
Source: `<StartSSLAsync>d__11.MoveNext` in MySql.Data 26.10.0 (net10.0).
1. `semaphoreSlim.WaitAsync` at IL 336.
2. Reads and writes of the static `tlsRetry`/`tlsConnectionRef` under that semaphore.
3. `Release` at IL 613.
4. `SslStream.AuthenticateAsClientAsync` (the TLS handshake) at IL 716.
5. Then **`tlsConnectionRef.set_Item` (891), `tlsRetry.Remove` (925), `tlsConnectionRef.Remove` (963) and `tlsRetry.get/set_Item` (998–1055), with NO lock held.**

Both fields are **process-wide static `Dictionary` objects** (`private static`, field flags 17). The synchronous `StartSSL` has the same pattern (Wait IL 276, Release IL 412, then unlocked mutations at IL 493–652). Any two TLS handshakes that complete concurrently anywhere in the process can corrupt these dictionaries.

**Upstream:** reported against Connector/NET since 8.0.33 ([Bug #111759](https://bugs.mysql.com/bug.php?id=111759), [#110717](https://bugs.mysql.com/bug.php?id=110717)); independent analyses describe the same fields in 26.7.0. **26.10.0 is the latest MySql.Data on NuGet, so no fixed version exists.**

### What it is not (assessed)
- Not caused by our migration code, the D07 readiness changes, the bulkhead or `TransactionRunner`. They are only some of the concurrent openers.
- **Hypothesis only (not confirmed):** a readiness probe cancelled during its handshake (STEP 10-B `CancelAsync`) may exercise the provider's exception-path mutations more often.
- Not test-only: the **production API** opens new pooled TLS connections concurrently under load, so it has the same exposure.
  - The provider throws `InvalidOperationException`, which the classifier rightly treats as unexpected (500, not 503).
  - **Hypothesis:** a corrupted static dictionary could keep failing later handshakes in that process.

### Correction (implemented, uncommitted, awaiting review)
- **`MySqlConnectionOpenGate`** (Infrastructure) serializes connection **opening** process-wide.
  - An EF Core `DbConnectionInterceptor` is registered in the single `MySqlContextOptions.Apply` (runtime, design-time and tests). It holds the gate from `ConnectionOpening` until `ConnectionOpened`/`ConnectionFailed`, exactly once per connection; a waiter cancelled before acquiring holds nothing.
  - An `OpenAsync` helper covers direct opens: all 12 direct test opens were changed, and the 3 spike test contexts register the interceptor.
- **TLS is unchanged:** `DatabaseOptions` validation requires `SslMode=VerifyFull` in every non-Development environment and for any non-loopback server; `Required` is accepted only for a loopback server in Development/tests. The CI test accounts use TLS-only (`REQUIRE SSL`) loopback connections. The only `SslMode=Disabled` uses are pre-existing negative tests that expect refusal (wording corrected in STEP 10-F).
- **No retries, no provider upgrade, no change to test parallelism, no skipped tests, no migration change.**
- **Trade-off:** a burst of new physical connections opens one at a time. While MySQL is unreachable, waiters queue behind the open in progress, each bounded by its own cancellation; readiness still reports `timeout` truthfully.
- **Remove the gate** only when a provider release is verified (same IL method) to no longer mutate these fields outside a lock.

### Tests (compiled; NOT executed)
- **Infrastructure +6 (`MySqlConnectionOpenGateTests`, deterministic, isolated gate instances):** parallel opens never overlap (max concurrency 1 over 16); a failed open releases the gate and keeps the original exception; cancellation while waiting leaks nothing; the interceptor holds exactly once from opening to opened/failed; a waiter cancelled before acquiring holds nothing; production contexts use the process-wide interceptor.
- **MySQL +1 (`MySqlTlsConcurrencyLiveTests.SSL01`):** 6 rounds × 24 parallel **non-pooled** opens (every open is a new TLS handshake), mixing the production EF path and direct opens. Every connection must open AND report a non-empty `Ssl_cipher` (TLS proven, not weakened).
- **Inventory (compiled):** Infrastructure **431** (= 425 on `develop` `2ad74d1`, which includes M5 PR #28's 8 new cases, + 6); MySQL **57**; Python 54. Run #57's executed totals have not been independently verified here (artifacts not downloaded).

### Resolution criteria (not yet met)
The issue may be declared resolved only after **repeated green CI runs** of `database-compatibility` on the fix branch (proposed: ≥ 5 consecutive runs, including SSL01). One green run proves little for an intermittent race; run #54 was green on the same faulty code.

## 15. STEP 10-F — workaround audit, approval and CI delivery (2026-11-19)

**Lead approval:** explicit in-session answer, 2026-11-19, covering:
1. process-wide serialized MySQL connection opening;
2. acceptance only after **5 consecutive green `database-compatibility` runs on the same candidate commit**;
3. existing TLS policies and provider versions kept.

Deferred: a stress workflow, an ungated reproduction and an upstream bug report.

### Source audit against the shipped EF Core Relational 10.0.12 IL
SHA-256 `a1e4f362…`; `RelationalConnection.OpenInternal` and `<OpenInternalAsync>d__72`.

**Path traced:**
- `ShouldLogConnectionOpen`, then `ConnectionOpening[Async]`.
- `OpenDbConnection[Async]`, skipped if suppressed.
- `ConnectionOpened[Async]`.
- The catch calls `IExceptionDetector.IsCancellation`: a cancellation goes to **`ConnectionCanceled[Async]`**, which calls the interceptor's `ConnectionCanceled`; anything else goes to `ConnectionError[Async]`, which calls `ConnectionFailed[Async]`.

**Paths covered:**
- `MySQLRelationalConnection.Open/OpenAsync` → base `RelationalConnection.Open`.
- `CreateSourceConnection` clones the dependencies, keeping the same logger and interceptors (gated).
- Migrations open through the context connection (gated).
- All 12 direct test opens go through the helper.

| Audit item | Result |
|---|---|
| **G1 — cancelled open** | **CONFIRMED DEFECT (critical, fixed).** A cancelled open goes to `ConnectionCanceled`, never `ConnectionFailed`. The 10-E interceptor didn't override it, so a cancelled open (readiness deadline, request abort) left the process-wide gate held **forever**, deadlocking every later open. Fix: release on `ConnectionCanceled[Async]` too |
| **G2 — bounded wait** | **CONFIRMED DEFECT (fixed).** The sync open (`Wait()`) and token-less async opens could wait for the gate without limit. The wait is now bounded by the connection's own `ConnectionTimeout` (fallback 30 s when 0). On expiry, `MySqlConnectionGateTimeoutException` is classified as `ConnectionFailure`, so 503 `DEPENDENCY_UNAVAILABLE`, not transient and never retried. Worst case ≈ 2 × the connect timeout |
| Interceptors invoked on every open | `ShouldLogConnectionOpen` is per-context. When an interceptor is present, `NeedsEventData` resets the suppression. Verified live by **SSL02** (3 back-to-back opens of one context → 3 acquisitions) |
| Exactly-once release, suppression, exception paths | Per-connection holder table; repeated or unacquired notifications are no-ops. Suppression by another interceptor still reaches `ConnectionOpened`. A failing `ConnectionOpened` goes to the catch, which releases or no-ops |
| Cancellation before or after acquisition | Before: nothing held. After: `ConnectionCanceled` releases (G1) |
| Deadlock and re-entrancy | The gate is held only between the opening and opened/failed/cancelled callbacks; no user code runs in between. The provider's internal connections don't use the gate, so it can't self-deadlock |
| Queries and transactions serialized? | No. Only opening is gated |
| **Residual risk R-1** (not fixable here) | `MySqlConnection.CancelQuery` (MySql.Data IL 17–63) opens its own unpooled connection (same TLS settings) and calls `Open()` directly for `KILL QUERY` when a running command is cancelled or times out. It bypasses EF Core and the gate, so the provider race remains possible in that rare path. Mitigation: provider fix upstream |
| TLS | Unchanged. The STEP 10-E wording "`SslMode=Required` everywhere" was imprecise and is corrected above |

### Tests added in 10-F
- **Infrastructure +4:**
  - cancelled open releases the gate (sync and async; repeated notification is a no-op);
  - gate wait bounded by `ConnectionTimeout` (async, sync and direct; waiters take nothing);
  - fallback bound when the timeout is 0;
  - gate timeout → classified 503, not transient.
- **MySQL +2 (dedicated observable interceptor and gate):**
  - **SSL02:** every EF open is intercepted;
  - **SSL03:** an open to a silent server, cancelled after 500 ms, still releases (canceled or failed path).
- **Timeouts:** SSL01 180 s, SSL02/SSL03 60 s.

### Compiled inventory (NOT executed)
- Infrastructure **435** (425 on `develop` + 10).
- MySQL **59** (56 + 3).
- Python 54.

**The full executed baseline at `2ad74d1` is UNVERIFIED.** Runs #56 (id 44377410926) and #57 (id 44377526848) both succeeded on attempt 1, but their artifacts were not read. Static counts can't reproduce the executed totals: Workflow is 46 static vs 70 executed, Reports 23 vs 54, and the M3 contract checks are runtime-only. **1,258 is NOT the current baseline.**

Run #57 digests: `dotnet-test-results` `dc46aff9…`, `mysql-compatibility-results` `16756899…`, `python-test-results` `e92155c6…`.

### Repeated CI verification
- Required: **5 consecutive** green `database-compatibility` attempts on the **same candidate commit**.
- Each attempt is recorded with run id, attempt number, SHA and the three artifact digests; SSL01, SSL02, SSL03 and M03 must pass, and there must be no skipped tests.
- Any failure resets the count and requires investigation.
- **Final QA stays OPEN until then.**

| # | Run id | Attempt | Commit | Result | Artifact digests |
|---|---|---|---|---|---|
| 1 | PR #30 run (merge `c080a25`) | 1 | `279c3b3` | **FAILED**: 48 database tests (STEP 10-H) | log archive `cdf6a99d…` |
| 2 | pending | | | | |
| 3 | pending | | | | |
| 4 | pending | | | | |
| 5 | pending | | | | |

### Performance trade-off and removal conditions
- **Trade-off:** physical connection opening (TCP + TLS + authentication) is process-wide sequential; pooled reuse is fast. A burst of N new connections takes about N × the handshake time. During an outage, waiters fail after their connect timeout (503).
- **Remove the gate when either holds:**
  1. a MySql.Data release whose `StartSSL`/`StartSSLAsync` IL no longer mutates `tlsConnectionRef`/`tlsRetry` outside a lock (verified the same way, with SSL01 kept as regression);
  2. an approved provider change.

## 16. STEP 10-H — gate held forever by a stalled handshake (2026-11-19)

**Consecutive green database attempts: 0/5 (reset).** Final QA remains OPEN.

### Evidence
- Source: the job log archive downloaded by the lead (`logs_120159306446.zip`, SHA-256 `cdf6a99d…`), from the PR #30 run on `refs/pull/30/merge`, `HEAD is now at c080a25 Merge 279c3b3 into 2ad74d1`.
- Totals read from the logs:
  - Reports 54, Workflow 123, Requirements 233, Application 128, Infrastructure 435 (all passed);
  - M3 contract checks 266 passed; Python 54 passed;
  - MySQL **11/59**;
  - **1,352 executed, 1,304 passed, 48 failed.**
- **Reconciled `develop` (`2ad74d1`) executed baseline:** **1,339** (Reports 54, Workflow 123, Requirements 233, Application 128, Infrastructure 425, M3 266, MySQL 56, Python 54). M5 #28 and M3 #25 changed Workflow (70 → 123) and M3 (246 → 266).

### Timeline (xUnit clock)
1. **00:00.29:** start.
2. **~00:01.9:** SSL03 opens an EF context, configured through **`MySqlContextOptions.Apply`** (so with the **process-wide** gate), against a loopback listener that accepts TCP but never sends the MySQL greeting.
3. From ~00:02, every other open (EF and direct) waits its 15 s limit and fails with `MySqlConnectionGateTimeoutException`: **94 occurrences, 47 tests**. Some failed after 2 × 15 s (R07, M03, CC02).
4. **01:01.93:** xUnit abandons SSL03 after 60 s, but the open keeps running.
5. **07:50:** end. The gate was held for the whole run.

### Root cause (confirmed)
1. **The provider open never completed**, despite a 500 ms cancellation token and `ConnectionTimeout=5`. If it had completed (successfully, failed or cancelled), SSL03's own `await` would have returned and failed fast on its assertions; instead the call never returned. So MySql.Data 26.10.0 doesn't bound or cancel the handshake once TCP is connected.
2. **The open-gate design holds the process-wide gate for the entire provider open.** So one stalled handshake blocks every open in the process, and every waiter fails after its bounded wait (503).
3. **SSL03's design** pointed the production gate at a silent server. That turned a test of the dedicated interceptor into a process-wide outage.

**Ruled out:**
- a missing release callback (the open never returned to EF Core);
- the original provider TLS race;
- duplicate registration: none exists, since `Apply` is called once per builder and the spike contexts register once.

**Also observed:** `MySQLExecutionStrategy` wraps the gate timeout in an `InvalidOperationException` that suggests `EnableRetryOnFailure`. It still classifies as 503, and **no retry is configured** (forbidden).

### Lead decision (explicit in-session answer, 2026-11-19)
**"Stop; revisit design".** The gate semantics (a strict hold until the provider open ends) are **not** changed now; the bounded-lease alternative is deferred.

So **risk R-3 is OPEN and blocks production readiness:** one stalled MySQL handshake (a server or network path that accepts TCP and then stalls) blocks all connection opens process-wide until it ends. Waiters fail with 503 after `ConnectionTimeout`.

Alternatives for the later decision:
- **(a) Bounded lease:** release after the connect timeout while the stalled open continues ungated. Residual: a rare provider race on that open (R-2).
- **(b) Strict hold**, as now.
- **(c) Another design.**

### Corrections in 10-H (within the existing design)
1. **Per-connection ownership moved into `OpenGate`.** A connection that already holds the gate re-enters without waiting (counted as `Reentries`); the semaphore is released exactly once, at the outermost release; releasing a non-holder is a no-op.
   - This removes the latent self-deadlock when the shared interceptor is registered twice, or when two interceptors share one gate (previously a 15 s wait and then a failure).
2. **SSL03 rewritten:**
   - process-isolated: a dedicated context **without** `MySqlContextOptions.Apply`, so it never touches the process gate;
   - doesn't depend on the provider honouring cancellation: after a bounded wait, the server side is closed, forcing the stalled open to fail (≤ 20 s);
   - records whether the provider honoured the 500 ms token (evidence);
   - then asserts exactly one acquisition and a released gate.
3. **SSL04 added:** a refused open through the production configuration, then successful EF and direct TLS opens (the gate recovers).

### Tests (compiled; NOT executed)
- **Infrastructure +6:**
  - duplicate registration of the same interceptor (no self-wait, exactly-once release);
  - two interceptors sharing one gate (sync path);
  - re-entrant acquisition counted and released once;
  - a failed open followed by a successful one;
  - recovery right after a waiter timed out;
  - a gate timeout wrapped by the execution strategy is still 503.
- **MySQL:** SSL03 replaced, SSL04 added.
- Existing objectives kept; no timeouts were raised (the SSL limits are unchanged and the 20 s cap applies only to the forced failure).
- **Compiled inventory:** Infrastructure **441**, MySQL **60**. Expected full backend once executed: 1,352 − 435 + 441 − 59 + 60 = **1,359**.

### Verification requirement (unchanged)
5 consecutive green `database-compatibility` attempts on the **corrected** candidate commit, recorded per attempt.

## 17. STEP 10-I — reviewed corrections for development/CI (2026-11-19)

**Scope (lead):** development and CI verification only. **Production approval is NOT granted.** No bounded lease. R-1 and R-3 stay unresolved.

### Audit of the 10-H diff
| Check | Result |
|---|---|
| Duplicate interceptor registration | ✅ Same-operation re-entry: no self-wait, exactly one semaphore release |
| **Re-entry vs independent concurrent opens of the same connection** | **DEFECT in 10-H (fixed).** Ownership was keyed only by the `DbConnection`, so a second independent open of the same connection (e.g. two concurrent `OpenAsync` calls) would have re-entered and run concurrently. Now keyed by **open operation**: EF Core's `(ConnectionId, StartTime)`, which is identical for all callbacks of one open because `OpenInternal` reads `UtcNow` once (IL 12); direct opens use a fresh unique identity. An independent open waits (bounded) like any other opener |
| **Duplicate or delayed callbacks releasing another operation's hold** | **DEFECT in 10-H (fixed).** A late `Opened`/`Failed`/`Canceled` from an earlier open of the same connection could release a later open's hold. Release is now accepted only from the holding operation; late, duplicate or foreign callbacks are no-ops |
| SSL03 isolation | ✅ Dedicated context without `MySqlContextOptions.Apply`, so it never touches the process gate |
| **SSL03 socket cleanup** | **Gap (fixed).** If the accept failed, the open was never awaited and the sockets weren't closed; and `await using` disposed the context before the listener closed, while the open could still be in flight. Now `try/finally`: close both socket ends, stop the listener, wait at most 20 s for the open, and dispose the context only once the open has ended. The ambiguous `IsNotType<TimeoutException>` check was replaced by explicit `IsCompleted` and faulted/cancelled assertions |
| SSL04 recovery | ✅ Refused open through the production config (fast refusal), then successful EF and direct TLS opens |
| TLS policies | ✅ No connection-string or `SslMode` changes |

### Tests (compiled; NOT executed)
- **Infrastructure +3:**
  - independent opens of one connection are serialized (no re-entry);
  - concurrent direct opens of one connection never overlap;
  - late, duplicate and foreign callbacks never release another operation's hold.
- All unit tests now pass real EF `ConnectionEventData` / `ConnectionEndEventData` / `ConnectionErrorEventData`. The constructors were verified in the EF Core 10.0.12 metadata: no null checks; `ConnectionId` and `StartTime` are carried.
- **Inventory:** Infrastructure **444**, MySQL **60**, Python 54. Expected full backend: **1,362**.

### Architecture alternatives
See [27-MYSQL-CONNECTION-ARCHITECTURE-DECISION.md](27-MYSQL-CONNECTION-ARCHITECTURE-DECISION.md). It is proposed, not approved.

### Verification
- The 5-consecutive-run count **restarts at 0** on the new commit.
- **Even 5/5 does NOT resolve R-3 (or R-1).**

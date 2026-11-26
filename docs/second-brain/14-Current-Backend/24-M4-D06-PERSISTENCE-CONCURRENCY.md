# BE-M4-D06: Persistence, transactions and concurrency (STEP 09-A)

Status: **implemented and audited on `feature/m4-persistence-concurrency`** (STEP 09-A, with STEP 09-B audit corrections). The mapping was **lead-approved on 2026-11-18** (STEP 09-B, explicit in-session answer). The C# tests compile but have not run: Windows Smart App Control (G17) blocks local execution, so Linux CI is the first run.

## Baseline verified before starting
- `develop` = `894fc4a` (PR #26 merged on 2026-11-18T17:49:16Z with **0 reviews**; G19 still applies).
- Post-merge CI **run #50** (push, `894fc4a`, success). The three downloaded artifacts match run #50 by SHA-256 digest (`dotnet-test-results` `f624c7c4…`, `mysql-compatibility-results` `0206c48d…`, `python-test-results` `94a09a50…`). They do not match PR run #49.
- Independently read results: Infrastructure 388, Requirements 233, Application unit 128, Workflow 70, Reports 54, MySQL integration 52 (45 + the 7 live C#-to-Python smoke tests), M3 contract checks 246, Python 54. **Total: 1,225 passed, 0 failed, 0 not executed.**
  - Correction to the STEP 09-A prompt: "MySQL 45/45 + smoke 7/7" is one TRX of **52**. The 7 smoke tests are inside it, not additional.
- The generated migration files (`InitialFeatureSchema.cs`, `.Designer.cs`, `ModelSnapshot.cs`) are identical to the committed files after line-ending normalisation.

## Audit of what already exists (not repeated)
| Area | Existing (D02a/D03) | D06 finding |
|---|---|---|
| Atomic transaction | `TransactionRunner`: begin → operation → commit, one transaction, nesting rejected | Adequate |
| Commit outcome uncertainty | `TransactionOutcomeUnknownException`, never retried, tracked state discarded | Adequate |
| Rollback failure | `TransactionRollbackUncertainException` (original failure kept as inner) | Adequate |
| Cancellation | honoured until commit; commit is never cancelled midway | Adequate |
| Isolation | `ReadCommitted` by default, overridable per call | Adequate |
| Deadlock / lock timeout | classified (1213 / 1205), `IsTransient` only; no automatic retry | Adequate |
| Disposal | the transaction is always disposed quietly, never hiding the outcome | Adequate |
| Partial writes | rollback on any failure; `DiscardTrackedChanges` | Adequate |
| Optimistic concurrency | `projects.revision` `IsConcurrencyToken()`; requirement revisions use a unique `(project, revision_number)` plus repository compare-and-set (D03 tests) | Adequate; **live lost-update test added** |
| Cross-project isolation | requirement repository filtered by project (D03 live tests) | Adequate |
| **Public mapping of an escaped database failure** | **missing**: a stale token, a duplicate key, MySQL unreachable or a timeout all fell through to **500** | **Defect, fixed** |

## Defects fixed
1. **Database failures that escaped a feature were all returned as 500.** This contradicted the C11b design ("MySQL unreachable → 503 `DEPENDENCY_UNAVAILABLE`") and the D06 acceptance criterion "stale token returns the agreed conflict".
   - `PersistenceErrorClassifier.TryClassifyDatabaseFailure` classifies **only** real database exception chains (MySQL, EF `DbUpdateException`, unknown commit, unconfirmed rollback). An unrelated `TimeoutException` stays 500, never 504.
   - `PersistenceProblemMapping` (API) maps by kind, so not every failure gets one status:

| Kind | Status | Code |
|---|---|---|
| ConcurrencyConflict (stale token) | 409 | `REVISION_CONFLICT` |
| DuplicateKey | 409 | `CONFLICT` |
| ConnectionFailure, Deadlock, LockTimeout | 503 | `DEPENDENCY_UNAVAILABLE` |
| CommandTimeout | 504 | `DEPENDENCY_TIMEOUT` |
| ForeignKeyViolation, TransactionOutcomeUnknown, RollbackUnconfirmed, Unexpected | 500 | `INTERNAL_ERROR` (never guessed; features translate FK violations themselves) |

   - `GlobalExceptionHandler` applies it and writes a safe structured log, EventId **1006**: kind, provider error number and schema constraint name only. It never logs the provider message, SQL, values or connection details.
   - Feature adapters remain the primary place to translate failures into port results; this is only the fallback.

## Concurrency, duplicate requests and integration
- **Duplicate requests and idempotency:** no approved idempotency contract exists (C2/C5b open), so none was invented. A duplicate natural key returns 409 `CONFLICT`.
- **External calls inside transactions:** today no persistence type depends on `HttpClient` or `BuildMind.Infrastructure.Integration`. The architecture test is a **structural guard only**: it checks constructor parameters and fields. It cannot detect indirect calls, for example a delegate passed to `TransactionRunner` that calls a service, or an interface from another namespace. "No external call inside a transaction" therefore remains a code-review rule.
- **Integration:** the Python bulkhead, readiness gates and the 503/504 mapping were re-verified in run #50 (7/7 live). No automatic retries exist anywhere.

## Tests added
- **Infrastructure:**
  - `PersistenceProblemMappingTests`: 7 provider-number cases, stale token, distinct statuses, classification boundaries, 4 end-to-end Problem Details cases (no secret in the body or logs; safe log fields present) and the no-HTTP-in-persistence guard.
  - Total: **+15 cases → 403**.
- **MySQL (disposable database plus the committed migration), `PersistenceConcurrencyLiveTests` CC01–CC04:**
  - CC01: a stale `projects.revision` is rejected → 409 `REVISION_CONFLICT`, and the first write survives (no lost update).
  - CC02: 8 parallel compare-and-set updates → exactly one winner.
  - CC03: a real duplicate `ux_users_normalized_email` → 409 `CONFLICT`, with the table and key name but no value.
  - CC04: a real unreachable server → 503.
  - Total: **+4 → 56**.

## Cross-member dependencies (latest `develop` and remote branches, 2026-11-18)
Nothing has been newly unblocked:
- **M1:** `feature/m1-auth-projects` has repositories and DTOs, but it is **unmerged**, and `User`/`Project` rehydration factories are still absent on `develop` (D03 blocker).
- **M3:** draft PR #25 (authorization contracts) is unmerged.
- **M5:** dashboard source on an unmerged branch.
- **M2 and M6:** no persistence contracts.

No persistence component was added for these members.

## Remaining blockers and open items
- **G11:** internal auth.
- **C2/C5b:** idempotency contract.
- **M1:** rehydration factories.
- **M2/M3/M6:** job and revision persistence contracts.
- **M5:** report snapshot.
- **D06 checklist 04** (database permission review): least-privilege CI accounts already exist (D02b). A production account policy is not approved.
- **G17 and G19:** still open.

## STEP 09-B — approval, audit and corrections (2026-11-18)

**Approval:** the lead explicitly approved, in session, the fallback mapping in the table above. No automatic write retries.

**Audit results (the full STEP 09-A diff was reviewed):**

| Check | Result |
|---|---|
| Classification boundaries | Only MySQL/EF/commit/rollback chains; unrelated timeouts stay 500 ✅ |
| Nested exceptions (depth ≤ 16) | ✅ |
| Commit uncertainty precedes inner provider errors | **Defect:** only when it was the outer exception. A wrapper (e.g. `DbUpdateException` around `TransactionOutcomeUnknownException`) could produce 409/503/504. **Fixed:** uncertainty anywhere in the chain wins |
| Unconfirmed rollback never reported as a conflict | Covered by the same fix, plus end-to-end tests |
| Cancellation not misclassified | **Defect:** `OperationCanceledException` wrapping a database timeout could become 504. **Fixed:** cancellation anywhere in the chain is not a database failure (stays 500 server-side; client abort still 499) |
| Problem Details status/code/type | ✅ type = `urn:buildmind:problem:<code>`; now asserted |
| No SQL, values, connection details or provider messages | ✅ in responses and Production logs |
| Development logging | **Defect:** a 500 database failure went through the Development detail logger, which writes the full exception text (provider messages can contain SQL and values). **Fixed:** database failures are never logged with the exception object in any environment (EventId 1006 warning for 409/503/504, **1007** error for 500) |
| Constraint names trusted | ✅ `[A-Za-z0-9_]{1,64}`, anchored at the end of the message; existing injection tests |
| Architecture guard wording | **Overclaim corrected:** the test was renamed and documented as structural-only |
| Deadlock and lock-wait distinct internally | ✅ different kinds; `ServerRolledBackTransaction` true only for a deadlock; now tested |
| Failed operations leave no tracked changes | ✅ existing `TransactionRunnerTests` (`Discards`) |

**Tests added in 09-B:** infrastructure **+9 → 412**:
- deadlock/lock-wait distinctness;
- uncertainty precedence (4 chains);
- cancellation boundaries;
- 3 new end-to-end cases (unconfirmed rollback around a stale token → 500; unknown commit around a timeout → 500; cancellation around a database timeout → 500);
- 3 Development-logging cases.

MySQL stays **56**. Full backend inventory: **1,253** (the STEP 09-B expected 1,244 + 9).

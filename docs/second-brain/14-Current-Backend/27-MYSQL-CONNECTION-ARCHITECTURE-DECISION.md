# ADR proposal: MySQL connection reliability (R-1, R-3)

- **Status:** **PROPOSED, not approved.** No option here is implemented or authorized. The lead has explicitly excluded a bounded lease from implementation.
- **Date:** 2026-11-19 (STEP 10-I).
- **Owner:** M4. **Decision:** lead (M4 + M7).

## 1. Problem
- **Provider defect:** MySql.Data 26.10.0 (the latest) mutates static TLS-state dictionaries outside its own lock (doc 25 §14). Concurrent TLS handshakes in one process can corrupt them.
- **Current mitigation:** `MySqlConnectionOpenGate` serializes connection opening process-wide. Two residual risks remain open:
  - **R-1:** MySql.Data opens its own ungated connection for `KILL QUERY` when a running command is cancelled or times out (`CancelQuery`, IL 17–63). The race is still possible there.
  - **R-3:** MySql.Data didn't bound or cancel a handshake after TCP connect in PR #30's CI (500 ms token and `ConnectionTimeout=5` both ignored). A stalled handshake therefore holds the process-wide gate, and **every other connection open fails with 503** after its wait limit, which is a process-wide outage.
- **R-3 can't be resolved by passing CI.** Five green runs only show the tests don't trigger it.

## 2. Options
| # | Option | Resolves R-1 | Resolves R-3 | Cost / risk | Approval needed |
|---|---|---|---|---|---|
| A | **Status quo**: strict gate | No | No (*creates* R-3) | None now; a stalled handshake → process-wide DB outage | Already in place for dev/CI only |
| B | **Upstream provider fix** (report MySQL Bug #111759 follow-up; adopt a fixed MySql.Data) | Yes, if fixed | Yes (no gate needed) if the handshake honours timeouts | No timeline control; gate stays until IL-verified | External report (approval); later a dependency upgrade |
| C | **Locally patched MySql.Data fork** | Yes | Partly | Licensing (GPLv2 + FOSS exception, ties to G03); long-term maintenance; supply-chain ownership | Lead + licensing decision; **not recommended** |
| D | **Alternative provider: MySqlConnector (ADO.NET, MIT) + Pomelo.EntityFrameworkCore.MySql** | Expected yes (different TLS implementation, no shared static TLS state; to be verified) | Expected yes (cancellation and connect timeouts cover the handshake; to be verified) | **Must verify** EF Core **10** support of the Pomelo release before any work; provider-specific migration annotations, type mapping (GUID, JSON, `datetime(6)`), the error numbers used by `PersistenceErrorClassifier`, `MySqlConnectionStringBuilder` validation; the existing `InitialFeatureSchema` must remain applicable (no initial-migration change allowed). Re-runs the D02 provider study (doc 18) | Lead approval for a **spike branch** (new dependencies); separate approval to adopt |
| E | **Circuit breaker on the gate**: when the holder exceeds a threshold, new opens fail fast with 503 instead of each waiting `ConnectionTimeout` | No | No (limits harm: fast failure, no queued requests) | Small; still an outage while the handshake is stalled | Lead approval (behaviour change) |
| F | **Stall detection + process recovery**: readiness reports Unhealthy when the gate has been held longer than N × connect timeout; the hosting platform restarts or drains the instance | No | Mitigates (recovery instead of an indefinite outage) | Depends on the undecided hosting/orchestration (G11); needs a liveness-vs-readiness policy so restarts don't loop | Lead + hosting decision |
| G | **Process isolation of database access** (separate worker process per DB pool, killable on stall) | Contains | Contains | High complexity; crosses Clean Architecture boundaries; latency | Not recommended for the MVP |
| H | **Bounded lease** (release after the connect timeout; stalled open continues ungated) | No | Yes for availability; residual rare race R-2 | Small | **Excluded by the lead (STEP 10-H/10-I)** |

## 3. Assessment
1. **The only options that address both root causes are B and D**, because the defect lives in the provider. B depends on Oracle's timeline; D is within our control but is a significant provider change that must be proven first.
2. **E and F don't fix anything**, but they turn a silent, indefinite outage into a fast, observable and recoverable one. They complement A, B or D.
3. **C and G are disproportionate** for an MVP.

## 4. Recommendation (for the lead's decision, not implemented)
1. **Approve a time-boxed spike for option D** on a separate branch: MySqlConnector + Pomelo EF Core provider. Exit criteria:
   - EF Core 10 support confirmed from official release notes;
   - all 60 database tests pass, including a provider-race stress run *without* the gate and SSL03-style stalled-handshake tests honouring cancellation;
   - `InitialFeatureSchema` applies unchanged or with an approved, reviewed migration plan;
   - the classifier error numbers are re-verified;
   - licensing is reviewed (G03).
2. **In parallel, approve option F's design** (no code yet): define the stall threshold, the readiness/liveness policy, and how hosting reacts. This depends on G11.
3. **Approve reporting the defect upstream** (option B; an external action).
4. **Keep A** for development and CI only. **Production remains NOT approved** while R-1 and R-3 are open.

## 5. Evidence references
- Doc 25 §14: provider IL analysis.
- Doc 25 §15: EF Core open-path IL analysis.
- Doc 25 §16: PR #30 stalled-handshake failure.
- Doc 25 §17: 10-I operation-scoped ownership.
- Doc 18: original provider study.

## 6. Fact-based re-evaluation (2026-11-19, final integration step 01)
**Context:** PR #30 (the strict gate) is merged on `develop`; R-1 and R-3 are open there.

**NuGet facts (queried 2026-11-19):**

| Package | Latest | EF Core support | Consequence |
|---|---|---|---|
| MySql.Data | 26.10.0 | n/a (ADO.NET) | **No fixed release** (the race is verified in 26.10.0 IL), so option B isn't available today |
| MySql.EntityFrameworkCore | 10.0.12 (11.0.0-preview) | EF Core 10 | The current provider; it depends on MySql.Data |
| MySqlConnector | 3.0.0 | n/a (ADO.NET) | A candidate ADO.NET driver |
| **Pomelo.EntityFrameworkCore.MySql** | **9.0.0** | **EF Core `[9.0.0, 9.0.999]` only** | **Option D is not viable on EF Core 10.0.12**; it would need an EF Core 9 downgrade across all projects, migrations and tests (high risk, not recommended) |

**Already-present mitigation for R-3 (option F, partly in place):** MySQL readiness opens its connection through the same process-wide gate. A stalled handshake that holds the gate makes the MySQL readiness probe exceed its 3 s deadline → `Unhealthy/timeout` → `/health/ready` **503**. A hosting platform that removes or restarts instances failing readiness therefore already recovers from R-3. What's still missing is the hosting/orchestrator policy (G11 hosting) and a liveness-vs-readiness restart rule.

**Revised recommendation (for the lead; nothing implemented):**
1. **Keep A (strict gate)** for the MVP.
2. **Adopt F operationally** once hosting is chosen: route traffic on readiness, restart after N consecutive readiness 503s. This needs no provider change.
3. **Track upstream** (B, report approval pending) and **Pomelo EF Core 10** (re-evaluate D when a Pomelo 10.x release exists).
4. **E (circuit breaker)** only if production telemetry shows queued opens. The lease (H) stays excluded.

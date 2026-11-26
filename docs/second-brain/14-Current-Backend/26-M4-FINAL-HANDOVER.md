# M4 final handover (BE-M4-D01 to D07, STEP 10-B)

- **Date:** 2026-11-19.
- **Prepared by:** AI-assisted M4 lead session.
- **Not an independent review.** The full QA detail is in [25-M4-FINAL-QA-REPORT.md](25-M4-FINAL-QA-REPORT.md).
- **M4 is NOT complete and NOT production-ready.**

## 1. Status by category
| Category | Items |
|---|---|
| **Implemented and runtime-verified** (CI run #52, 1,253 passed) | `/health` liveness; `/health/ready` (MySQL mandatory, Python/RAG Degraded); Clean Architecture layering and tests; module route registration `/api/v1`; RFC 9457 Problem Details with C5 codes; safe exception handling and logging (C3/C10); security response headers; start-up options validation; MySQL provider and DbContext; UTC `datetime(6)` converters; `InitialFeatureSchema` (4 tables); requirement-revision repository; `TransactionRunner`; lead-approved database-failure mapping; optimistic concurrency (live 4/4); hardened Python client with bulkhead (live 7/7); CI with pinned MySQL, least-privilege TLS accounts, credential scan and result artifacts |
| **Implemented, executed only after the next CI run** | STEP 10-A/10-B readiness cancellation hardening (`ProbeDeadline`), +5 infrastructure tests |
| **Partially implemented** | D01 contracts (CORS/OpenAPI/auth contract missing); D02 (JWT missing); D03 (only User/Project/Requirement tables); D04 (CORS, auth policies, request limits missing); D05 (internal auth, storage adapter missing); D06 (idempotency, production database account policy missing); D07 (independent QA missing) |
| **Blocked by another member** | M1: `User`/`Project` rehydration factories, auth implementation (none on any branch). M2: storage contract and upload limits. M3: plan/job contracts (PR #25 draft). M6: generation/job contracts, FastAPI docs exposure. M5: report snapshot |
| **Awaiting approval** | A1/A2 (auth), C6 (CORS), C7 (OpenAPI), C2/C5b (idempotency), G11 (internal transport/auth/hosting), hosting posture (AllowedHosts, HTTPS/HSTS), request size and rate limits, GitHub Actions SHA pinning, production database account policy, G03, G12, G20 |
| **Awaiting independent review** | Every M4 PR #8–#27 (0 reviews, G19), the D07 PR, this handover and doc 25. M5 PR #28 is open |

## 2. Infrastructure inventory
- **API:**
  - composition root (`Program.cs`, `ApiServiceCollectionExtensions`);
  - `GlobalExceptionHandler`, `ProblemDetailsDefaults`, `PersistenceProblemMapping`;
  - `SecurityHeadersMiddleware`;
  - `HttpContextCurrentUser` (anonymous until auth exists);
  - options validators;
  - readiness (`MySqlReadinessCheck`, `PythonReadinessCheck`, `InternalServiceReadinessCheck`, `ReadinessProbeGate`, `ProbeDeadline`).
- **Infrastructure:**
  - `BuildMindDbContext` and configurations;
  - `TransactionRunner`, `PersistenceErrorClassifier`;
  - `RequirementRevisionRepository`;
  - `InternalServiceClient`, `PythonServiceClient`, `InternalServiceBulkhead`;
  - `DatabaseOptions`, `ServicesOptions`.
- **Database:** `users`, `projects` (`revision` concurrency token), `requirement_revisions` (JSON payload, unique `(project_id, revision_number)`), `project_requirement_heads`; 4 foreign keys; 4 indexes; ranged script deployment (Option B).

## 3. Protections in place
- **Transactions:** one atomic transaction per operation; unknown commit outcome and unconfirmed rollback are reported, never guessed and never retried; tracked state discarded after failure; nesting rejected.
- **Concurrency:** stale revision → 409 `REVISION_CONFLICT`; duplicate key → 409 `CONFLICT`; database outage, deadlock or lock-wait → 503; command timeout → 504; anything uncertain → 500. No automatic write retries.
- **Integration:**
  - SSRF-safe path allowlist; no redirects, proxy or cookies;
  - bounded connect time, deadline and response size;
  - bulkhead (64 per dependency), fail-fast 503/504;
  - `traceparent` sent, no credentials forwarded;
  - abandoned readiness probes cancelled without blocking the readiness result.
- **Security:** no exception text, SQL, values or connection details in responses or logs, in any environment; `nosniff`, `DENY`, `no-referrer`, `no-store` on `/api`; no server header.

## 4. Test evidence
| Suite | Executed (run #52) | Compiled now |
|---|---|---|
| Infrastructure | 412 | **417** |
| Requirements / Application / Workflow / Reports | 233 / 128 / 70 / 54 | same |
| M3 contract checks | 246 | 246 |
| MySQL + live Python | 56 | 56 |
| Python | 54 | 54 (passed locally) |
| **Total** | **1,253** | **1,258** |

## 5. Exact outstanding M4 work (for continuation)
1. **JWT validation and auth policies** (D02-04, D04-04). Prerequisites: A1/A2 approval and M1 token service. Must reject invalid and expired tokens.
2. **CORS allowlist for Angular** (D01-03, D04-02), after C6.
3. **OpenAPI exposure decision** (D01-03, D04-05), after C7.
4. **Remaining repositories** (D03): User/Project once M1 factories exist; land/survey/plan/generation once M2/M3/M6 entity proposals exist. Every schema change goes through a new migration coordinated by M4.
5. **Private storage adapter** for M2 (D05-04), after the M2 storage contract.
6. **Internal service authentication and network restriction** (D05-03), after G11.
7. **Idempotency and job deduplication / safe retry** (D05 acceptance, D06), after C2/C5b and the M3/M6 job contracts.
8. **Request size limits and rate limiting** (D04-04), after M2 limits and lead approval.
9. **Hosting posture** (AllowedHosts, HTTPS/HSTS, reverse-proxy headers) and the **production database account policy** (D06-04).
10. **GitHub Actions SHA pinning**, if approved.
11. **End-to-end C# + FastAPI verification of auth and CORS** (D07-02), once 1 and 2 exist.
12. **Independent QA approval** (D07 acceptance) and retrospective review of PRs #8–#27 (G19).

## 6. Operating notes
- Local C# test execution is blocked by Windows Smart App Control (G17, kept enabled). Linux CI is the verification path: download `dotnet-test-results`, `mysql-compatibility-results` and `python-test-results`, and check their digests against the run.
- Setup steps and environment variable names: doc 25 §8.
- No real secrets in the repository. CI passwords are random per run and masked.

## 7. STEP 10-E update (2026-11-19): final QA failed, correction pending
Post-merge run #55 failed one MySQL test because of a confirmed **MySql.Data 26.10.0 defect**: static TLS dictionaries are mutated outside the provider's lock (doc 25 §14).

- **Correction:** `MySqlConnectionOpenGate` serializes connection opening process-wide, with TLS unchanged. It is on `feature/m4-mysql-ssl-concurrency-fix` and **uncommitted, awaiting review**.
- **Outstanding work (adds to §5):**
  - **13.** Verify the fix with ≥ 5 consecutive green `database-compatibility` runs.
  - **14.** Track upstream Connector/NET for a real fix; remove the gate only after IL verification.
  - **15.** Measure the connection-open latency impact under load once real endpoints exist.
- **M4 production readiness remains BLOCKED.**

## 8. STEP 10-F update (2026-11-19)
- The workaround was **lead-approved**. The audit found and fixed two critical gate defects: **G1** (a cancelled open leaked the gate, deadlocking all opens) and **G2** (unbounded gate wait).
- **Residual risk R-1:** MySql.Data's internal `KILL QUERY` connection bypasses the gate.
- **Outstanding:** 5 consecutive green database runs on the candidate commit (doc 25 §15), then independent review, then a separately verified post-merge CI run.
- **Final QA OPEN; production readiness BLOCKED.**

## 9. STEP 10-H update (2026-11-19)
- PR #30's first CI attempt **failed** (48 database tests): a stalled test handshake held the process-wide open gate for the whole run (doc 25 §16).
- Corrected within the existing design: per-connection gate ownership (no self-deadlock), and an isolated, bounded SSL03.
- **Lead deferred the gate-semantics decision.** Risk **R-3** (one stalled handshake blocks all opens) is OPEN and blocks production readiness.
- **Outstanding:**
  - 16. Decide R-3 (bounded lease vs strict hold vs other).
  - 17. 5 consecutive green database runs on the corrected commit.
- **Final QA OPEN; M4 not production-ready.**

## 10. STEP 10-I update (2026-11-19)
- Gate ownership is now per open operation: no concurrent opens of one connection, and no foreign releases. SSL03 cleanup is guaranteed.
- Architecture alternatives: doc 27 (proposed). **Production NOT approved; R-1 and R-3 open.**
- **Outstanding:**
  - 18. 5 consecutive green database attempts on the 10-I commit.
  - 19. Lead decision on doc 27 (provider spike, stall detection, upstream report).

## 11. Consolidated MVP handover (2026-11-19, phase 03)
The current single source of truth for M4 + M7 MVP status, dependencies and production blockers is [28-MVP-STATUS-AND-DEPENDENCIES.md](28-MVP-STATUS-AND-DEPENDENCIES.md) (§5 = final state).

- Setup: [30-LOCAL-BACKEND-RUNBOOK.md](30-LOCAL-BACKEND-RUNBOOK.md).
- Proposed M6–M7 integration: [29](29-M6-M7-REGULATORY-CONSTRAINT-CONTRACT-PROPOSAL.md).
- **Note:** PR #30 merged without the agreed 5 verified runs or a review. R-1/R-3 remain open on `develop`.

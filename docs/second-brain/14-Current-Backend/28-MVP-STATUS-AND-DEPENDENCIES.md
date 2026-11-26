# M4 + M7 MVP status and cross-member dependencies (2026-11-19, accelerated delivery)

**Not production-ready.** "Runtime-verified" means executed in CI or in a recorded local run. "Compiled" means not executed yet: Smart App Control (G17) blocks C# test execution locally.

## 1. M4 (backend infrastructure)
| Task | MVP state | Evidence | Open |
|---|---|---|---|
| D01 foundation | Runtime-verified (build, `/health`, layering, Problem Details, config validation) | CI #52 | C6 CORS, C7 OpenAPI, A1/A2 |
| D02 MySQL/identity | Runtime-verified (provider, DbContext, migration, least-privilege CI) | CI #52 | JWT (A1/A2 + M1), production database account policy |
| D03 schema | Runtime-verified (4 tables, migration, requirement repository) | CI #52 | M1 factories; M2/M3/M6 entities |
| D04 errors/security | Runtime-verified (error mapping, log safety, readiness, headers) | CI #52 | CORS, auth policies, request/rate limits |
| D05 C#↔Python | Runtime-verified (hardened client, bulkhead, live smoke 7/7) | CI #52 | G11 auth; M2 storage adapter; job dedup |
| D06 concurrency | Runtime-verified (approved failure mapping, live concurrency 4/4) | CI #52 | Idempotency (C2/C5b) |
| D07 QA | **Final QA OPEN**: MySql.Data TLS race (run #55); open-gate fix in **PR #30** | Run #59 attempts 1–2 **green by public metadata; artifacts not read** (0/5 verified) | 5 verified green runs; **R-1, R-3 unresolved production blockers** (doc 27); independent review |

## 2. M7 (RAG and leadership/QA)
| Task | MVP state | Evidence | Open |
|---|---|---|---|
| D01 audit | Done (2026-11-17) | Audit doc | — |
| D02 corpus/tests | **Done.** Existing suite 93/93 run read-only; 3 approved docs / 2,629 chunks | EV-20261119-11 | Legal currency (G06) |
| D03 contract | **Done.** Evidence contract v1 lead-approved and implemented | EV-20261119-11 | — |
| D04 integration | **Implemented** (branch `feature/m7-rag-retrieval-service`): rag-service FastAPI; C# `IRegulationEvidenceSource`/`RagServiceClient`; **real RAG readiness** (identity, contract version, corpus, bounded, single-flight, never Unhealthy) | rag-service 44/44 local; real corpus 6/6; real loopback end-to-end; C# compiled | First CI run (PR not yet opened); G11 auth |
| D05 M6 evidence | **BLOCKED: no M6 code or contract exists** on any branch (no `feature/m6-*`). M6's plan expects "M7 sourced rules/constraints", which needs a new M6+M7 contract (structured, reviewed constraints, not passages). Nothing was invented | Branch and port audit, 2026-11-19 | M6 implementation; joint contract (lead approval) |
| D06 cross-service QA | Partial: per-service suites and the shared contract example; no M6 end-to-end | — | M6 |
| D07 go/no-go | **No-go for production**; demo-capable for retrieval only | This document | Independent review |

**Deterministic validation stays authoritative.** Retrieved passages are advisory evidence only:
- `legal_status_verified` and `answer_generated` are always false;
- no code path turns a passage into a pass/fail plan decision;
- M6's authoritative validator (`IPlanValidationEvidenceVerifier`, M6-owned) is unchanged and unimplemented by M7.

## 3. Consolidated cross-member dependencies
| # | Needed from | What | Blocks |
|---|---|---|---|
| 1 | **M6** | Any generation/feasibility code; a joint "sourced regulatory constraint" contract with M7 | M7-D05/D06, M4 job persistence |
| 2 | **M1** | `User`/`Project` rehydration factories; auth/token service (A1/A2) | M4-D02 JWT, D03 repositories, D04 auth policies |
| 3 | **M2** | Storage contract, upload limits, land/survey entities | M4-D05 storage adapter, D04 request limits, D03 schema |
| 4 | **M3** | Plan/job contracts (PR #31 draft); `IAiEditProposalPort` and `IValidationFreshnessPolicy` implementers (G20) | M4 job/version keys; idempotency |
| 5 | **M5** | Report snapshot contract | M4-D06 integration |
| 6 | **Lead** | G11 internal auth/transport/hosting; C6; C7; C2/C5b; R-3 decision (doc 27); production database account policy; Actions SHA pinning | Production readiness |
| 7 | **Lead (GitHub)** | Authenticated download of CI artifacts (run #59 attempts) | M4 5-run verification |
| 8 | **Independent reviewers** | PRs #8–#30, the M7 PR (G19) | Every "Done" |
| 9 | **Domain/legal (G06)** | Legal currency and amendment status of the 3 sources | Any regulatory claim |

## 4. Verification run for this phase
See the evidence register (EV-20261119-12). Local results:
- rag-service 44/44;
- python-services 54/54;
- Release build 0 warnings;
- no vulnerable packages.

C# test execution happens only in CI.

## 5. Phase 03 update (2026-11-19): final MVP handover state

**Repository facts (GitHub API, verified):**
- **PR #30** (MySQL open gate, `b221024`) was **merged into `develop` as `d5db6f8`** on 2026-11-19T05:57Z, with **0 reviews**.
  - Pre-merge: run #59 attempts 1–2 succeeded, by public metadata only; their artifacts were never read in this session.
  - Post-merge: run #66 (push, `d5db6f8`) succeeded, also by metadata.
  - The approved 5-consecutive-verified-runs criterion was **not demonstrated** before the merge (G19). **R-1 and R-3 are now on `develop`**, still unresolved production blockers (doc 27).
- **PR #31 (M3)** and **#33 (M5)** were also merged.
- **PR #32 (M7):**
  - run #61 (`43f779b`, merge `45679ff`) **failed 2 tests**. Verified from the lead's job-log archive (SHA-256 `a44187eb…`): Infrastructure 448/450; all other suites green (Reports 54, Workflow 123, Requirements 233, Application 128, MySQL 56, Python 54, rag-service 44). Cause: the tests read request content after disposal. Fixed in `54108a8`.
  - run #64 (`f5a0753`) **succeeded on all jobs**, by metadata; counts unverified until the artifacts are read.
  - `develop` was then merged into the branch (`ea60f3c`; append-only Second Brain conflicts resolved by keeping both entries). **PR #32 now merges cleanly.**

**D07 item 02 (start C# + FastAPI and verify health), 2026-11-19, local:**
- python-services `/health` ✅;
- rag-service `/health` ✅ with the real corpus loaded (`corpus_loaded:true`);
- **C# API blocked by Windows Smart App Control** ("An Application Control policy has blocked this file (0x800711C7)"). Not bypassed (G17).
- API readiness against live services is therefore verified only in Linux CI: live Python 7/7, and RAG through stubbed-transport readiness tests.

**New documents:**
- [29: M6–M7 regulatory constraint contract (PROPOSAL)](29-M6-M7-REGULATORY-CONSTRAINT-CONTRACT-PROPOSAL.md), for team coordination; nothing implemented;
- [30: local backend runbook](30-LOCAL-BACKEND-RUNBOOK.md).

**Final MVP verdict:**
- **Functional, tested backend MVP for:** shared infrastructure (M4 D01–D06) and regulation retrieval (M7 D02–D04).
- **Not production-ready.** Blockers:
  - R-1/R-3 (MySQL provider);
  - G11 (no authentication between services, no hosting decision);
  - no end-user auth/CORS (A1/A2, C6);
  - M6 absent (generation, feasibility and M7-D05);
  - G06 (legal currency);
  - **0 independent reviews** on merged PRs (G19).

## 6. Phase 05 (2026-11-19)
- **CI:** PR #32 run **#69** (`ad46382`, first run with the G11 C# auth tests) succeeded on all 3 jobs (metadata; artifacts unread).
- **Members:** `develop` unchanged (`d5db6f8`). Member PRs #31 and #33 and the unmerged M5 commit touch **no** M4/M7-owned files, and no member code consumes the internal clients yet, so there's no integration conflict.
- **Local regression (all green):** build 0 warnings; python-services 54; rag-service 68; real corpus 6; existing engine 93; workspace unchanged.
- **New:** [33: backend integration checklist](33-BACKEND-INTEGRATION-CHECKLIST.md), covering rules, per-member status, the M6 shared-auth coordination plan and the final integration gate.

## 7. Final MVP delivery status: M4 + M7 (2026-11-19, final integration step 02)

**CI evidence, PR #32 run #72** (id 44474193455, `9a0a086`, attempt 1): all 3 jobs succeeded. GitHub API step results (`database-compatibility`) are all success:
- start RAG service (live, per-run masked token);
- MySQL + live integration tests (including the live C#→RAG and C#→Python tests);
- **full-stack smoke** (built API + MySQL + Python + RAG; the script fails on any status, HTTP or leakage mismatch);
- credential scan (including the RAG token);
- sanitized upload;
- RAG stop.

Artifacts: `dotnet-test-results` `a38f4316…`, `mysql-compatibility-results` `9503196c…`, `python-test-results` `ec18b9b7…`. **Per-test counts are not yet verified from the artifacts** (no authenticated download in the session).

**MySQL decision held:** the existing provider (MySql.Data 26.10.0 / MySql.EntityFrameworkCore 10.0.12) and the strict open gate are kept **for controlled MVP development only**. **R-1 and R-3 stay open production blockers** (doc 27 §6).

| | M4 infrastructure | M7 RAG |
|---|---|---|
| **Delivered and CI-verified** | D01–D06 core; readiness (MySQL/Python/RAG); hardened internal client and bulkhead; G11 token (RAG side); CI full-stack smoke | D02 corpus evidence; D03 contract v1; D04 retrieval service, C# port, readiness, token auth (PR #32 green) |
| **Awaiting independent review** | PRs #8–#30 (merged without review, G19) | **PR #32** (clean, mergeable; reviewer guide prepared) |
| **Genuinely outstanding implementation** | JWT + auth policies (needs A1/A2 + M1); CORS (C6); storage adapter + request limits (M2 contract); job/version persistence and idempotency (M3/M6 contracts); python-services token (M6 custodian; C# side ready); production hosting/TLS (G11) | D05 regulatory-constraint integration (M6 + doc 29 approval); D06 end-to-end with M6 |
| **Production blockers** | R-1/R-3; G11 transport/hosting; A1/A2/C6; G19 | G06 legal currency; G11; G19 |

**Nothing else is implementable without a decision or another member's delivery.**

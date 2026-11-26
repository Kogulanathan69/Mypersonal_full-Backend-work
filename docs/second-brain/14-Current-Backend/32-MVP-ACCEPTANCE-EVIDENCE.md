# MVP acceptance and delivery evidence: M4 + M7 (2026-11-19)

Every acceptance test from the original task plans is checked against **actual** evidence.
- **MET:** executed in CI or in a recorded local run.
- **COMPILED:** written but not executed yet.
- **NOT MET**, **BLOCKED** and **N/A** are stated explicitly. Nothing is self-certified as Done: independent review (G19) is required for "Done".

## M4: infrastructure
| Task | Acceptance test (plan) | Status | Evidence |
|---|---|---|---|
| D01 | `dotnet build` passes | MET | CI #52 / #66 |
| D01 | `/health` contract stable | MET | Liveness tests; CI |
| D01 | No secrets committed | MET | Secret scans each step; CI credential scan |
| D01 | Dependency direction verified | MET | Layer tests (CI) |
| D02 | Restore/build pass; migration applies to an empty DB | MET | MySQL M02 (CI) |
| D02 | FK/unique constraints tested | MET | R10, CC03, T10/T11 (CI) |
| D02 | Invalid/expired token rejected | **NOT MET** | No JWT: A1/A2 + M1 auth not approved or delivered |
| D02 | No real secrets in files | MET | Scans; per-run random CI passwords |
| D03 | Fresh migration; valid references; invalid FK rejected | MET | M01–M04, R-series (CI) |
| D03 | Rollback tested on a disposable DB only | MET | M03, T05 (CI) |
| D04 | Invalid requests → expected 4xx; unexpected → safe 5xx | MET | Problem Details suites (CI) |
| D04 | CORS and authorization tests | **NOT MET** | C6 / A1 not approved |
| D04 | Secrets absent from logs | MET | Log-safety tests (CI) |
| D05 | Unavailable Python → truthful failure | MET | PY04 live (CI) |
| D05 | Timeout/cancel handled | MET | PY05/PY06 live (CI) |
| D05 | Internal auth/config not exposed | **PARTIAL** | RAG token auth (doc 31; Python tests local, C# compiled); python-services pending the M6 custodian |
| D05 | Safe retry does not duplicate jobs | N/A / BLOCKED | No jobs exist (M3/M6); no retries at all |
| D06 | Failed transaction rolls back | MET | T14, TransactionRunner tests (CI) |
| D06 | Stale token → agreed conflict | MET | CC01 → 409 `REVISION_CONFLICT` (CI) |
| D06 | Migration test passes | MET | CI |
| D06 | Least-privilege config verified | MET (CI accounts) | LP test; production policy open |
| D07 | Clean setup works | PARTIAL → CI full-stack smoke added | CI clean setup ✅; local Python + RAG ✅; **local C# API blocked by Smart App Control** (G17). New: CI starts the **built API + MySQL + Python + RAG** and checks `/health` and `/health/ready` (`tools/smoke/full-stack-smoke.sh`); first execution pending CI |
| D07 | Critical security/DB failures resolved | **NOT MET** | R-1/R-3 open on `develop` (doc 27) |
| D07 | No untested feature marked ready | MET | This matrix |
| D07 | Independent QA approval | **NOT MET** | 0 reviews on merged PRs (G19) |

## M7: RAG
| Task | Acceptance evidence (plan) | Status | Evidence |
|---|---|---|---|
| D01 | Branch/hash, file inventory, gaps | MET | Audit 2026-11-17 |
| D02 | Real source IDs / page trace | MET | 93/93 existing suite (read-only); real-corpus 6/6; citations reconstruct exact page text |
| D02 | OCR gaps | **PARTIAL** | Extraction warnings carried per passage; no systematic OCR-gap audit |
| D03 | Contract examples; no-hit/conflict tests | MET | Contract v1 + shared example (Python + C#); no-hit, unknown jurisdiction/source, partial coverage, ambiguous/naming-conflict tests |
| D04 | Auth, timeout, provenance contract tests (+ live C#→RAG: identity, token accepted/rejected, truthful 503, readiness) | MET (Python) / COMPILED (C# new, live `RagLiveSmokeTests` RG01–RG04) | rag-service 68/68 local + CI step; real E2E with auth; C# client, readiness and auth tests (readiness and client CI-green in run #64; auth tests pending CI) |
| D05 | Known/unknown evidence distinct; no invented constraints | **BLOCKED** | No M6 code; proposal doc 29 |
| D06 | Actual integrated test evidence | PARTIAL | Per-service and contract tests; no M6 end-to-end |
| D07 | Explicit go/no-go; CI logs; PRs; no self-certification | **GO for retrieval demo / NO-GO for production** | Docs 28 and 32 |

## Delivery state
**Branches and PRs:**
- `develop` = `d5db6f8` (PR #30 merged).
- **PR #32 (M7), open:** retrieval service, C# port and client, RAG readiness, G11 token auth.
- No merges or deployments were done by the assistant.

**CI evidence held (artifacts or logs read):**
- run #52 (`develop` `1ab1f98`): 1,253 passed;
- run #55: failed (provider race);
- run #61: 2 test failures, fixed.

**Metadata only (artifacts not read):** runs #59 (both attempts), #64, #66, #67 and #68.

**Production blockers:** see doc 28 §5. In short: R-1/R-3, G11 transport/hosting, A1/A2/C6, M6 absent, G06 and G19.

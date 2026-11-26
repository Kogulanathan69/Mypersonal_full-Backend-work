# Master API endpoint matrix

Requirement IDs `R-xx` follow the order of the V3 register (`05-API-CONTRACTS.md`). Paths omit `/api/v1`.

- **Impl:** IMPLEMENTED / PARTIAL / MISSING / BLOCKED / OUT_OF_SCOPE.
- **Verify:** NOT_TESTED / UNIT / INTEGRATION / LIVE (LIVE = the real local stack: API + MySQL 8.4 VerifyFull + python-services [+ rag-service], via `tools/captain/smoke_journey.py`).
- **Gate:**
  - CORE = `Authentication:Enabled` (Development only);
  - CAPT = the Captain profile `CaptainIntegration:Enabled` (Development only; production activation NOT approved);
  - RAG = `Regulations:Retrieval:Enabled`.
- **Auth:** all product routes need JWT + the active-session check unless marked anon.
- **Ownership:** every project-scoped route uses a fresh owner check; missing and unauthorized both return 404.

The status column reads **start of this execution → end** (end state verified at commit `86b9593`). The machine-readable contract is `openapi.captain-dev.json` (Development Swagger export, 57 operations).

| ID | Module | Method & path | Impl (start → end) | Verify (end) | Gate | Notes |
|---|---|---|---|---|---|---|
| R-01 | M1 | POST /auth/register | IMPLEMENTED | LIVE | CORE, anon | auth-strict limiter |
| R-02 | M1 | POST /auth/login | IMPLEMENTED | LIVE | CORE, anon | |
| R-03 | M1 | POST /auth/forgot-password | PARTIAL | UNIT | CORE + `PasswordRecovery:Enabled` (Dev) | No email provider (non-delivering sender) |
| R-04 | M1 | POST /auth/reset-password | PARTIAL | UNIT (+ live store tests) | same | same |
| R-05 | M1 | POST /auth/logout | IMPLEMENTED | LIVE | CORE + RefreshTokens | |
| R-06 | M1 | GET /users/me | MISSING → IMPLEMENTED | LIVE | CAPT | Existing M1 profile handler; the base M1 route inventory (`/profile`) is unchanged |
| R-07 | M1 | PUT /users/me | MISSING → IMPLEMENTED | UNIT (shared handler) | CAPT | Same handler as PUT /profile |
| R-08 | M1 | POST /projects | IMPLEMENTED | LIVE | CORE | |
| R-09 | M1 | GET /projects | IMPLEMENTED | LIVE | CORE | |
| R-10 | M1 | GET /projects/{id} | IMPLEMENTED | LIVE | CORE | |
| R-11 | M1 | PATCH /projects/{id} | IMPLEMENTED | UNIT | CORE | atomic owner/version/revision CAS |
| R-12 | M2 | POST /projects/{id}/surveys | IMPLEMENTED | LIVE | CAPT | |
| R-13 | M2 | GET /projects/{id}/surveys/{surveyId} (+ `/extraction`) | PARTIAL → IMPLEMENTED | LIVE + INTEGRATION | CAPT | Durable worker; local PDF-text/Tesseract; values are unverified evidence (OCR always NEEDS_REVIEW) |
| R-14 | M2 | GET /projects/{id}/land | IMPLEMENTED | LIVE | CAPT | 204 when no land yet |
| R-15 | M2 | PUT /projects/{id}/land | IMPLEMENTED | LIVE | CAPT | validates + confirms |
| R-16 | M2 | POST /projects/{id}/land/validate | MISSING → IMPLEMENTED | LIVE | CAPT | Dry run; persists nothing (verified) |
| R-17 | M2 | POST /projects/{id}/land/confirm | MISSING → IMPLEMENTED | LIVE | CAPT | Same protected write as PUT land; never infers geometry from the survey |
| R-18 | M5 | GET /dashboard/summary | IMPLEMENTED | LIVE | CORE | |
| R-19 | M5 | GET /projects/{id}/requirements | IMPLEMENTED | LIVE | CAPT | |
| R-20 | M5 | PUT /projects/{id}/requirements | IMPLEMENTED | LIVE | CAPT | protected write |
| R-21 | M5 | GET /projects/{id}/requirements/spaces | IMPLEMENTED | UNIT | CAPT | |
| R-22 | M5 | PUT /projects/{id}/requirements/spaces | IMPLEMENTED | UNIT | CAPT | |
| R-23 | M5 | GET /projects/{id}/requirements/relationships | IMPLEMENTED | UNIT | CAPT | |
| R-24 | M5 | PUT /projects/{id}/requirements/relationships | IMPLEMENTED | UNIT | CAPT | |
| R-25 | M5 | GET /projects/{id}/requirements/summary | IMPLEMENTED | UNIT | CAPT | |
| R-26 | M5 | PUT /projects/{id}/design-method | IMPLEMENTED | UNIT | CAPT | |
| R-27 | M6 | POST /projects/{id}/generation-jobs | IMPLEMENTED | LIVE | CAPT | three distinct alternatives or truthful INFEASIBLE |
| R-28 | M6 | GET /projects/{id}/generation-jobs/{jobId} | IMPLEMENTED | LIVE | CAPT | |
| R-29 | M6 | GET /projects/{id}/generation-jobs/{jobId}/alternatives | MISSING → IMPLEMENTED | LIVE | CAPT | |
| R-30 | M6 | POST /projects/{id}/generation-jobs/{jobId}/select | MISSING → IMPLEMENTED | LIVE | CAPT | Body `{alternativeId}`; reuses `PlanSaveService` |
| R-31 | M3 | POST /projects/{id}/plans | MISSING → IMPLEMENTED | LIVE | CAPT | Body `{jobId, alternativeId}` |
| R-32 | M3 | GET /projects/{id}/plans/{planId} | IMPLEMENTED | LIVE | CAPT | |
| R-33 | M3 | POST /projects/{id}/plans/{planId}/edits/validate | MISSING → IMPLEMENTED | LIVE | CAPT | M3 edit preview + server validation; no persistence (verified) |
| R-34 | M3 | POST /projects/{id}/plans/{planId}/ai-edit/proposals | BLOCKED (G20) → BLOCKED, route truthful | LIVE | CAPT | Existing M3 orchestration; no provider → 503 `AI_PROVIDER_NOT_CONFIGURED` |
| R-35 | M3 | POST /projects/{id}/plans/{planId}/versions | MISSING → IMPLEMENTED | LIVE | CAPT | Server re-validates and issues evidence; head CAS; stale base 409; invalid geometry 422 |
| R-36 | M3 | GET /projects/{id}/plans/{planId}/versions | IMPLEMENTED | LIVE | CAPT | |
| R-37 | M3 | GET /projects/{id}/plans/{planId}/versions/{versionId} | IMPLEMENTED | INTEGRATION | CAPT | |
| R-38 | M3 | POST /projects/{id}/plans/{planId}/confirm | MISSING → IMPLEMENTED | LIVE | CAPT | Alias of `/confirmation` (same handler) |
| R-39 | M5 | POST /projects/{id}/reports | IMPLEMENTED | LIVE | CAPT | |
| R-40 | M5 | GET /projects/{id}/reports/{reportId}/download | IMPLEMENTED | LIVE | CAPT | |
| R-41 | M5 | POST /projects/{id}/architect-requests | IMPLEMENTED | LIVE | CAPT | recorded, never dispatched (G09/G12) |
| R-42 | M5 | GET /projects/{id}/architect-requests/{requestId} | IMPLEMENTED | LIVE | CAPT | |
| R-43 | M5 | POST /projects/{id}/architect-requests/{requestId}/withdraw | IMPLEMENTED | LIVE | CAPT | |
| R-44 | M4 | GET /health | IMPLEMENTED | LIVE | always, anon | |
| R-45 | M4 | GET /health/ready | IMPLEMENTED | LIVE | always, anon | exact `mysql` entry asserted |
| R-46 | M7 | POST /regulations/retrieve | IMPLEMENTED | LIVE (fail closed) | RAG | real corpus blocked in WSL |
| R-47 | TBD | Admin / staff / proposal routes | OUT_OF_SCOPE (G09) | — | — | requires an approved module, roles and contracts |

## Counts (46 V3 product method+route pairs, R-01…R-46; R-47 is an unspecified placeholder)

| State at end | Count | IDs |
|---|---|---|
| IMPLEMENTED | 43 | all except below |
| PARTIAL | 2 | R-03, R-04 (no approved email provider) |
| BLOCKED (route answers truthfully) | 1 | R-34 (G20) |
| OUT_OF_SCOPE | placeholder | R-47 (G09) |

Verification at end: LIVE 35, INTEGRATION 1 (R-37), UNIT 10 (R-03, R-04, R-07, R-11, R-21…R-26).

## Implemented supporting routes beyond the V3 register (not counted as requirements)

| Method & path | Purpose |
|---|---|
| POST /auth/refresh, POST /auth/logout-all | Session rotation and revocation (CORE + RefreshTokens) |
| GET/PUT /profile | Original M1 route (base inventory, unchanged) |
| GET /projects/{id}/plans | Plan list |
| POST /projects/{id}/generation-jobs/{jobId}/candidates/{candidateId}/save | Original Captain save route |
| POST /projects/{id}/plans/{planId}/candidates, …/{candidateId}/apply, …/reject | M3 edit candidate workflow |
| POST /projects/{id}/plans/{planId}/confirmation | Original Captain confirmation route |
| GET /projects/{id}/surveys/{surveyId}/extraction | Survey extraction evidence (part of R-13) |
| GET /projects/{id}/reports | Report list (owner-scoped, newest first, max 100) |
| GET /projects/{id}/reports/{reportId} | Report status |
| GET /projects/{id}/architect-requests | Request list |

## Internal service endpoints (never public)

| Service | Routes |
|---|---|
| python-services | GET /health; POST /internal/v1/land/validate, /generation/run, /plans/validate, /plans/edit-preview, **/survey/extract** (new; internal token; 9 MiB body cap) |
| rag-service (M7, unchanged) | GET /health; POST /internal/v1/regulations/search, /regulations/constraints; GET /internal/v1/regulations/sources |

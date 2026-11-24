# Technical decision register — Captain backend

Status: **LOCAL** = taken within the Captain worktree under the execution order. None of these is a ratified shared contract; each needs owner review before any merge.

| ID | Decision | Rationale | Alternatives rejected | Owner to ratify |
|---|---|---|---|---|
| TD-01 | All Captain features sit behind a Development-only profile (`CaptainIntegration:Enabled`) that requires authentication | Production activation is not approved; it keeps the base host unchanged | Feature flags per route in the base host | Team / M4 |
| TD-02 | All .NET build and test work runs in WSL from a one-way copy | G17 Smart App Control blocks fresh assemblies; it must not be bypassed | Disabling SAC (forbidden) | — |
| TD-03 | Protected writes reuse the ratified A1–A8 lock through `ProtectedPlanningWriter` | No second authorization mechanism | Per-feature locks | M4 / M1 |
| TD-04 | `MicrosecondTimeProvider` for M3 services | MySQL `datetime(6)`; M3 compares timestamps exactly | Changing M3 comparisons | M3 |
| TD-05 | Python internal API uses a fail-closed shared token (`X-BuildMind-Internal-Token`) | Same pattern as M7; loopback only | Unauthenticated loopback | M6 / G11 |
| TD-06 | Database TLS `VerifyFull` with a local CA and client certificate | Do not weaken TLS | `Required` without verification | M4 |
| TD-07 | Survey extraction is a **DB-backed lease queue** on `surveys` (no broker): CAS claim Queued v1 → Processing v2; reclaim on expiry; max 3 attempts → `EXTRACTION_ABANDONED`; publication only with the matching lease token | Durable, crash-recoverable, no new infrastructure, no transaction held during OCR | In-memory channel (loses work); message broker (new infrastructure) | M2 / M4 |
| TD-08 | Processing status is separate from extraction accuracy: NEEDS_REVIEW → survey `Completed`, outcome stored separately; extraction FAILED → survey `Failed` | Do not map NEEDS_REVIEW to verified, and do not fail usable evidence | A single status | M2 |
| TD-09 | OCR uses local Tesseract (eng/sin/tam) + pdftoppm; OCR output is always NEEDS_REVIEW with values REVIEW_REQUIRED | No private documents to external providers; OCR is error-prone | Cloud OCR (forbidden) | M2 / M6 |
| TD-10 | Python unavailable or timed out → leave the claim to expire (bounded retry); malformed response → fail | Transient versus permanent failures | Immediate failure; unbounded retry | M2 |
| TD-11 | Extraction never produces geometry. Land is confirmed only by explicit customer input (`land/confirm`, `PUT land`) | No inference of CRS, bearings or polygons | Auto-polygon from lengths (forbidden) | M2 |
| TD-12 | V3 register routes are added as a Captain module reusing existing services (aliases where a route already existed under another name). The base M1 inventory is unchanged. | Do not silently change ratified contracts | Renaming existing routes | Owners per row |
| TD-13 | `POST …/versions` re-runs server validation and issues its own evidence; the client's geometry is never trusted | Single source of validation truth | Trusting client validation | M3 |
| TD-14 | AI edit proposals go through the existing M3 `AiEditOrchestrationService` with an explicit "unconfigured" port → 503 `AI_PROVIDER_NOT_CONFIGURED` | Provider-independent; truthful | Fake/rule-based "AI" | M3 / G20 |
| TD-15 | Captain live-test contexts disable connection pooling | One pool per disposable database exhausted `max_connections` (152/151) | Raising `max_connections`; changing the M4 fixture | M4 (fixture) |
| TD-16 | Admin/staff routes are not implemented | G09: no approved roles or contracts; do not invent privileged roles | Placeholder admin role | Product |

# Backend implementation matrix (final, 2026-11-24)

Baseline `fec3254` → final HEAD of `feature/captain-full-backend-completion`. Each state is backed by source plus executed tests (see TEST-EVIDENCE).

States:
- IMPLEMENTED: code exists.
- VERIFIED-LIVE: exercised against the real local stack.
- VERIFIED-UNIT: automated tests only.
- PARTIAL: some required pieces are missing.
- MISSING: no code.
- BLOCKED: needs an external decision or environment.
- DEFERRED: knowingly postponed.

**Every Captain-profile feature is Development-only (`CaptainIntegration:Enabled`). Production activation is NOT approved.**

| Feature | Baseline | Final state | Notes |
|---|---|---|---|
| Composition root, RFC 9457, headers, body limit, rate limiting, CORS, readiness | IMPLEMENTED | VERIFIED-LIVE | 413, 429 and readiness entries verified live |
| Register / login / JWT | IMPLEMENTED | VERIFIED-LIVE | Expired, wrong-key and tampered JWT → 401 |
| Refresh / logout / logout-all | IMPLEMENTED (gated) | VERIFIED-LIVE, **defect fixed** | Replay revocation, family revocation, users-first lock order |
| Password recovery | PARTIAL (provisional) | PARTIAL | Non-delivering sender; no email approved; not activated |
| Profile | IMPLEMENTED | VERIFIED-UNIT | Unchanged |
| Project CRUD + AuthorizationVersion | IMPLEMENTED | VERIFIED-LIVE | Cross-user → 404 |
| Dashboard | IMPLEMENTED | VERIFIED-LIVE | Owner scoping verified |
| Survey upload/status | PARTIAL | VERIFIED-LIVE | New `surveys` table + protected repository; private storage |
| Survey extraction (OCR/PDF) | PARTIAL (Python library) | **MISSING (integration)** | No worker or route. The land boundary is user-entered instead. |
| Land boundary (CRS/metric, validation) | library only | VERIFIED-LIVE | `land_revisions` + Python validation route; local metric frame only (no CRS transformation from survey data) |
| Requirements | PARTIAL | VERIFIED-LIVE | Protected atomic writes; operator catalogue file |
| Feasibility, footprint, room placement, candidates | library only | VERIFIED-LIVE | Existing D02/D04 wrapped by a Python route; three distinct candidates; truthful INFEASIBLE / NEEDS_REVIEW |
| Generation job lifecycle, idempotency, concurrency | MISSING | VERIFIED-LIVE | Unique project+actor+key; protected writes; stale-input binding |
| Candidate ranking | MISSING | IMPLEMENTED (deterministic D04 enumeration order; rank 1..3) | No quality score was invented |
| Plan save / edit / apply / reject / history | PARTIAL | VERIFIED-LIVE | Existing M3 services + Captain adapters; server-issued validation ledger |
| Final confirmation | PARTIAL | VERIFIED-LIVE | One per plan; **no project lifecycle transition** (no approved M1 writer) |
| AI-assisted edit (natural language) | BLOCKED (G20, no AI provider) | BLOCKED | `IAiEditProposalPort` unimplemented; no external AI provider introduced |
| Regulation retrieval (RAG) | IMPLEMENTED (gated) | VERIFIED-LIVE (fail-closed paths only) | Real corpus BLOCKED in this environment |
| Regulatory constraints → generation | MISSING | VERIFIED-UNIT + live fail-closed | Never REVIEWED (edge-role mapping and G06 open); evidence pack id persisted |
| Reports (preliminary PDF, private storage, integrity, download) | PARTIAL | VERIFIED-LIVE | Existing M5 candidates + renderer; independent PDF text check |
| Architect requests + consent + withdrawal | PARTIAL | VERIFIED-LIVE | Synthetic directory; policy version; no dispatch |
| Python internal authentication | MISSING | VERIFIED-UNIT + live | Fail closed (503 unconfigured, 401 invalid) |
| Database TLS | VerifyCA-capable | VerifyFull VERIFIED-LIVE | Local CA; MySql.Data requires a client cert + PKCS#1 key |
| MySQL migrations | 4 | 4 + 5 Captain (additive) | See DATABASE-SETUP |
| RH09 | — | **DEFERRED / UNRESOLVED** | Provider hang |

# API contract register — summary (Second Brain V3.0)

> **Reconciled 2026-11-17 (task BE-M7-D01).** The detailed proposed register is [05-API-CONTRACTS](second-brain/14-Current-Backend/05-API-CONTRACTS.md); this file is a summary and implementation-status tracker. **No contract below is frozen or approved.** API prefix (`/api/v1` proposed), JSON casing and error envelope remain open (G04). The previous V2-era table is preserved under [Historical](#historical-superseded--initial-register).

Do not consider an endpoint implemented until code, integration tests and PR evidence are linked.

Cross-cutting conventions — `/api/v1` prefix, response/status rules, RFC 9457 Problem Details, validation errors, error codes, trace IDs, CORS, OpenAPI exposure, configuration keys — are **PROPOSED** in [17-SHARED-CONVENTIONS-PROPOSAL](second-brain/14-Current-Backend/17-SHARED-CONVENTIONS-PROPOSAL.md) (BE-M4-D01b). Not approved; G04 remains open. Decision states as of 2026-11-18 (§15 there): C1, C3, C4, C5, C6–C10 PENDING LEAD APPROVAL; C2, C5b, C12 details PENDING CONSUMER APPROVAL; C11 liveness VERIFIED, readiness PROPOSED, C11b PENDING LEAD APPROVAL. The lead provisionally accepted the C1–C12 direction for detailed review on 2026-11-17. **Lead decision 2026-11-18:** C1, C3, C4, C5, C8, C9, C10 **APPROVED** and C11b **APPROVED AS ARCHITECTURAL DESIGN**, with boundaries in [§20](second-brain/14-Current-Backend/17-SHARED-CONVENTIONS-PROPOSAL.md#20-lead-decision-record--2026-11-18); `/internal/v1`, C2, C5b, C6, C7, C12 and feature diagnostics remain pending. Approved conventions are **not yet implemented** (BE-M4-D01c / D04).

## Areas, owners and status

| Area | Contract owner (V3) | Service | Contract status | Implementation status |
| --- | --- | --- | --- | --- |
| Auth/session, users, projects | M1 (M4: shared auth/error infrastructure) | ASP.NET | Proposed | Not started |
| Survey upload/extraction, land/location | M2 | ASP.NET → Python (internal) | Proposed | Not started |
| Requirements, dashboard, design method | M5 | ASP.NET | Proposed | Not started |
| Generation jobs, three 2D alternatives, selection | M6 | ASP.NET → Python (internal) | Proposed | Not started |
| Plan editor, edit validation, AI edit proposals, versions, confirmation | M3 (M6 authoritative validation) | ASP.NET → Python (internal) | Proposed; M3 draft geometry contract in [`docs/proposals/m3-plan-editor`](proposals/m3-plan-editor/GEOMETRY-CONTRACT-PROPOSAL.md) (canonical approval pending, G07) | Provisional Application contracts/services and Python `plan_editor` engine merged (`b03430e`); **no public or internal HTTP route yet** |
| Preliminary report, architect requests | M5 | ASP.NET | Proposed | Not started |
| Regulatory evidence retrieval / citations | M7 (M4: shared C# client infrastructure) | ASP.NET → RAG (internal) | Candidate route `POST /regulations/retrieve`; assistant route TBD | Not started. Existing RAG engine exposes a Python function, **no HTTP route** (see [08-RAG](second-brain/14-Current-Backend/08-RAG.md)) |
| Health: liveness `GET /health` | M4 | ASP.NET | Proposed (current body shape below) | **Implemented; CI-tested on develop `183ecf9`** (run 44074803430, part of 22/22 xUnit cases) |
| Health: readiness `GET /health/ready` | M4 | ASP.NET | Proposed | Not implemented (returns 404, verified 2026-11-17) |
| Python internal `GET /health` | M6 bootstrap custodian | FastAPI (internal) | Starter | **Implemented; covered by Python CI** (42 passed on develop `183ecf9`, incl. M3 tests) |
| Admin / staff / proposal routes | Unassigned (G09) | ASP.NET | Not defined | Not started |
| Shared error responses (RFC 9457, codes, traceId) — all API routes | M4 | ASP.NET | **APPROVED** (C3/C4/C5, 2026-11-18) | Implemented on `feature/m4-shared-infrastructure` — build verified; tests pending Linux CI; not merged |
| `/api/v1` route group (product API root) | M4 | ASP.NET | **APPROVED** (C1) | Implemented (no product endpoints yet) — build verified; tests pending CI; not merged |

Frontend consumers are tracked by the frontend repository; this register does not assign them.

## Verified existing endpoints (evidence: [2026-11-17 audit](second-brain/17-Progress/07-AUDIT-2026-11-17.md))

| Service | Route | Observed response | Notes |
| --- | --- | --- | --- |
| ASP.NET `BuildMind.API` | `GET /health` | `200 {"service":"buildmind-api","status":"ok"}` | Liveness only; does not check DB/Python/RAG. No `/api/v1` prefix. |
| FastAPI `python-services` | `GET /health` | `200 {"service":"buildmind-python","status":"ok"}` (via TestClient) | Internal only. |

Out of scope for this register: 3D and construction cost endpoints (excluded by D311), payment/subscription (open, D314).

---

## Historical (superseded) — initial register

> **HISTORICAL — not current.** Preserved from commit `763a9a7`. Conflicts with V3: auth/session and projects are M1 contracts (M4 only provides shared infrastructure); requirements consumer and plan/editor owners changed (M3 editor/versions/confirmation, M6 generation); UI consumer columns referred to V2 frontend roles.

| Area | Contract owner | UI consumer | Service | Status |
| --- | --- | --- | --- | --- |
| Auth/session | M4 | M1 | ASP.NET | Proposed |
| Projects/dashboard | M4 | M1 | ASP.NET | Proposed |
| Survey upload/land details | M4 + M6 | M2 | ASP.NET → Python | Proposed |
| Ground-floor requirements | M5 | M2 | ASP.NET | Proposed |
| Three 2D plan alternatives | M5 + M6 | M3 | ASP.NET → Python | Proposed |
| Room editing/validation | M5 + M6 | M3 | ASP.NET → Python | Proposed |
| Regulation citations | M7 + M6 | M2/M3 via main API | ASP.NET → Python/RAG | Proposed |
| Confirm revision | M5 | M3 | ASP.NET | Proposed |

**Status note (2026-11-18):** the shared conventions approved in [17 §20](second-brain/14-Current-Backend/17-SHARED-CONVENTIONS-PROPOSAL.md) (C1, C3, C4, C5, C8, C9, C10) are implemented on `develop` (`0a4d70a`, PR #11): `/api/v1` route group, RFC 9457 Problem Details with `code`/`traceId`, shared error codes. `/health` unchanged. CORS, OpenAPI, JWT and readiness remain unimplemented and pending decisions.

**Readiness (BE-M4-D04, uncommitted):** `GET /health/ready`, anonymous, outside `/api/v1`. 200 when Healthy/Degraded, 503 when Unhealthy. Body `{"status":"Healthy|Degraded|Unhealthy","checks":[{"name":"mysql|python|rag","status":"…","reason":"reachable|not_configured|unreachable|unverified|unavailable"}]}`. Not for frontend business logic. `/health` liveness unchanged.

**CORS and OpenAPI (STEP 05, 2026-11-21, proposed implementation, not merged):**
- **CORS:** exact-origin allowlist from `Cors:AllowedOrigins`; provisional Development origin `http://localhost:4200`.
- **OpenAPI:** Development-only Swagger document and UI at `/swagger` (Swashbuckle, pending package and C7 approval).
- **Product endpoints:** none added.
- **Details:** [17 §22](second-brain/14-Current-Backend/17-SHARED-CONVENTIONS-PROPOSAL.md).

**Dependency failures (BE-M4-D05, uncommitted):** a feature call whose private dependency fails returns Problem Details **503 `DEPENDENCY_UNAVAILABLE`** (unavailable, disabled, unexpected/malformed/oversized response) or **504 `DEPENDENCY_TIMEOUT`** (timeout); no internal URL or host in the body. Readiness `python` check now verifies Python's `GET /health`. Internal Python routes: only `GET /health` exists; feature routes are owned by M2/M3/M6 and not yet defined.

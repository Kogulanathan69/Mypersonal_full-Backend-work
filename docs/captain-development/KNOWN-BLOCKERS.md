# Known blockers — Captain full-backend completion (final)

**Production integration and deployment: NO-GO. STEP 8B: ON HOLD. RH09: DEFERRED / UNRESOLVED.**

| ID | Blocker | State | Effect |
|---|---|---|---|
| RH09 | MySql.Data 26.7.0/26.10.0 hang in command-timeout recovery and reader cleanup (reproduced without BuildMind/EF; `SELECT SLEEP(5)` with a 1 s timeout hangs). Diagnostic cleanup completed earlier. | **DEFERRED / UNRESOLVED** | Not re-investigated. No suite containing it is reported as passed. Blocks production readiness. |
| STEP 8B | M7 step on hold; scope not defined in the repository | **ON HOLD** | No change to rag-service, `RagServiceClient`, the retrieval module or its guard |
| R-1 / R-3 | MySql.Data `KILL QUERY` gating; unbounded TLS handshake holding the open gate (doc 27) | OPEN (production) | Development only |
| G03 | Commercial licence review of `MySql.EntityFrameworkCore` | OPEN | Internal testing only |
| G06 | Legal currency of sources; no reviewed real constraint table | OPEN | Synthetic setbacks only via an explicit development policy; generated plans are labelled `SYNTHETIC_FIXTURE` |
| EDGE-ROLE | Mapping front/rear/side constraint kinds to parcel edges (doc 29 §11, M6) | OPEN | rag-constraints evidence is never REVIEWED; generation answers NEEDS_REVIEW |
| RAG-CORPUS-WSL | Approved manifest pins Windows-separator source identities; the loader (correctly) refuses normalisation; the API cannot run on Windows (G17); WSL is in NAT mode | BLOCKED (environment) | Real-corpus retrieval not re-verified; fail-closed paths verified live |
| G11 | Internal transport/TLS/hosting. The shared-token pattern is now applied to python-services as well. | PARTIAL | Loopback only |
| G17 | Smart App Control blocks freshly built .NET assemblies on Windows | OPEN (not bypassed) | All .NET work runs in WSL |
| G19 | Merged PRs without independent review | OPEN | This branch is local; no push or merge |
| G20 / AI provider | No approved AI provider; the Captain profile binds `IAiEditProposalPort` to an explicit "unavailable" implementation | OPEN | `POST …/ai-edit/proposals` answers 503 `AI_PROVIDER_NOT_CONFIGURED`; structured edits work |
| Lifecycle writer | No approved M1 project transfer/revocation or status-transition writer | OPEN | Confirmation does not change project status; Captain writers use the ratified A1–A8 lock |
| G09 / G12 | Architect portal, recipients, sharing channels and retention | OPEN | Synthetic architect directory; requests are recorded, never dispatched |
| Survey extraction | Worker, internal route, persistence, local OCR and result API built (`86b9593`) | **RESOLVED (Development)** | Values are unverified evidence; land stays customer-confirmed. Without python-services, surveys stay `Queued` (fail closed) |
| Upload cleanup on unknown commit | M2 upload deletes the blob when the metadata commit outcome is unknown; if the commit actually succeeded, metadata points to a missing file | OPEN (M2-owned code, not changed) | Worker fails that survey closed with `SURVEY_FILE_MISSING`; the customer re-uploads |
| OCR quality | Tesseract eng/sin/tam locally; no evaluation corpus of real surveys | OPEN | OCR results are always NEEDS_REVIEW; nothing is auto-applied |
| C6 / CORS | Exact-origin allowlist exists (`Cors:AllowedOrigins`, proposed C6) but no approved frontend origin is recorded | OPEN | Left empty (no CORS policy); dev proxy or an operator-set local origin (FRONTEND-INTEGRATION-GUIDE) |
| Requirement catalogue, generation policy, freshness window, candidate lifetime | Product decisions | PROVISIONAL | Operator-supplied files and configuration; labelled synthetic/development |

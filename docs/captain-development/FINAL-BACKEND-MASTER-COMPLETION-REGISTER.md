# Final backend master completion register

| Item | Value |
|---|---|
| Branch | `feature/captain-full-backend-completion` |
| Start | `8317061` (verified) |
| Snapshot | the final commit of this order |
| Excluded | cost estimation, BOQ, customer 3D, payments (no code, not counted) |

**Status:**
- **DONE** = implemented, registered, persisted where needed, authorized, error-handled, tested and live-verified.
- **PARTIAL** = core done, a named gap remains.
- **OPEN** = actionable, not done.
- **EXT** = external input needed.

| # | Feature | Status | Existing implementation / evidence | Gap → register |
|---|---|---|---|---|
| 1 | Registration, login, JWT, refresh rotation (replay detection), logout, logout-all | DONE | journey 2/7; M1 tests | — |
| 2 | Password recovery (hashed single-use tokens, enumeration-safe) + email verification | DONE (local capture) | journey 9; admin smoke | external SMTP E-3/E-4 |
| 3 | Session management (list / revoke one session) | DONE | journey 11 (revoked session cannot refresh) | — |
| 4 | Profile / users/me, account security events | DONE | journey 8; admin smoke | — |
| 5 | Projects CRUD + lifecycle | DONE | journey | — |
| 6 | Survey upload, listing, extraction worker (lease/heartbeat), local OCR, review/correction | DONE | journey 2/8/11; live DB 12/12 | original-file download R-02; OCR accuracy E-7 |
| 7 | Land: validate/confirm, revisions + history, CRS normalisation, local frame, digest parity, edge identity + lineage | DONE | journey 3/10/11; 13 py + 3 .NET | — |
| 8 | Boundary roles (frontage/side/rear) review + setback evidence + buildable area | OPEN | edges `UNASSIGNED`, never guessed | R-01, E-5 |
| 9 | Requirements catalogue, revisions + history, validation | DONE | journey; Requirements.Tests 456 | — |
| 10 | Regulation retrieval (real approved corpus, citations, jurisdiction, fail-closed), API→RAG mTLS | DONE | rag_acceptance 33/33 (18/18 citations verified on the PDFs); RAG mTLS 12/12; journey 5 | embeddings R-08 (decision) |
| 11 | Regulation-verified constraint values in generation | EXT | `rag-constraints` mode fails closed | E-5 |
| 12 | Generation: 3 distinct alternatives / truthful INFEASIBLE; doors, windows, circulation; idempotent jobs + listing | DONE | journey 3/8/10/11 | async jobs R-13 (decision) |
| 13 | Plan editor: edits, validation, candidates (preview/apply/reject/expiry, list/detail), revisions, head concurrency | DONE | journey 4/11 | — |
| 14 | Undo/redo via revision semantics (`revert`) | DONE | journey 11 | — |
| 15 | Final confirmation + read-back | DONE | journey 4/11 | — |
| 16 | Real AI editing (local LLM): schema-bound intent, server command computation, ambiguity guard, transparency, benchmark | DONE (local) | benchmark 19/20, 0 unsafe; smoke_llm 10/10 | external provider E-1/E-2 |
| 17 | Reports: exact-revision binding, real PDF, atomic publication, idempotency, download, REPORT_READY | DONE | journey 6/9; Reports.Tests 164 (PDF QA) | regulation citations in the PDF R-03 |
| 18 | Architect: directory, consent-bound request, portal (respond/feedback), withdrawal revokes access | DONE | journey 9 | Completed state + consent expiry R-04 |
| 19 | Notifications + transactional outbox email | DONE (local and TLS capture) | journey 9; containers | external SMTP E-3/E-4 |
| 20 | Admin: accounts, roles, staff modules, tasks, policies, monitoring (projects/jobs/surveys/reports/requests), audit chain, project detail | DONE | admin smoke 65/65; journey 11 | — |
| 21 | Staff: tasks, notes, proposals, project review, monitoring | DONE | admin smoke | escalation endpoint not separate (status transitions cover it) |
| 22 | System: liveness `/health`, readiness `/health/ready`, worker status, dependency diagnostics | DONE | journey 2/7/11 | — |
| 23 | Internal mTLS (API→python, API→RAG) incl. negatives + rotation | DONE | 12/12 + 12/12 | production PKI (E-9) |
| 24 | MySQL persistence, migrations, least privilege, connection-exhaustion fix | DONE | persistence 115/115 with the stack running; peak 14 connections | — |
| 25 | RH09 timeout/cancellation | PARTIAL | containment verified; matrix re-verified | provider R-07 |
| 26 | OpenAPI + TypeScript contracts + runtime acceptance matrix | PARTIAL | 142 ops; 97 typed success bodies; strict tsc; 105 live-verified ops | R-05 (47 untyped/unexercised) |
| 27 | Docker stack (API, MySQL, python, RAG, TLS mail capture, optional LLM) | see FINAL-DOCKER-INTEGRATION-REPORT | — | — |
| 28 | 180+ operations | PARTIAL | 142 justified operations | R-06 explanation |

# API contract register — proposed V3 routes

Public C# base prefix `/api/v1` proposed, align with actual existing routes before freezing. Routes below omit prefix. Python internal routes are never public frontend endpoints. Stable authenticated principal from token, not client user ID. M4 owns global error/auth convention; feature owners own DTOs and endpoint behavior; M7 coordinates register.

| Owner | Method and path | Contract |
|---|---|---|
| M1 | POST /auth/register; POST /auth/login | Input credentials; safe user/session result |
| M1 | POST /auth/forgot-password; POST /auth/reset-password; POST /auth/logout | Generic recovery acknowledgement; one-time token; session policy |
| M1 | GET/PUT /users/me | Allowlisted self profile |
| M1 | POST/GET /projects; GET/PATCH /projects/{id} | Owner-scoped create/list/read/update |
| M2 | POST /projects/{id}/surveys | Multipart private upload -> survey ID/state |
| M2 | GET /projects/{id}/surveys/{surveyId} | Extraction/status with source and uncertainty |
| M2 | GET/PUT /projects/{id}/land | Location/draft geometry, revision |
| M2 | POST /projects/{id}/land/validate; POST /projects/{id}/land/confirm | Candidate checks; exact validated boundary revision |
| M5 | GET /dashboard/summary | Owner-scoped counts/status |
| M5 | GET/PUT /projects/{id}/requirements | Current requirements revision |
| M5 | GET/PUT /projects/{id}/requirements/spaces; GET/PUT /projects/{id}/requirements/relationships | Optional fields, validated references |
| M5 | GET /projects/{id}/requirements/summary; PUT /projects/{id}/design-method | Persisted snapshot and readiness |
| M6 | POST /projects/{id}/generation-jobs | Start generation exact input revisions, idempotency |
| M6 | GET /projects/{id}/generation-jobs/{jobId}; GET same + /alternatives | Real state and comparison |
| M6 | POST /projects/{id}/generation-jobs/{jobId}/select | Exact current alternative reference |
| M3 | POST /projects/{id}/plans; GET same + /{planId} | Create draft and retrieve |
| M3 | POST /projects/{id}/plans/{planId}/edits/validate | Candidate, diagnostics, no persistence |
| M3 | POST /projects/{id}/plans/{planId}/ai-edit/proposals | Propose only, base revision |
| M3 | POST/GET /projects/{id}/plans/{planId}/versions; GET same + /{versionId} | Atomic save; list/retrieve immutable versions |
| M3 | POST /projects/{id}/plans/{planId}/confirm | Explicit acknowledge exact validated version |
| M5 | POST /projects/{id}/reports; GET same + /{reportId}/download | Snapshot/report artifact owner check |
| M5 | POST /projects/{id}/architect-requests; GET same + /{requestId}; POST same + /{requestId}/withdraw | Consent, truthful status, permitted transition |
| M4 | GET /health; GET /health/ready | Process liveness versus dependency readiness |
| M7 | POST /regulations/retrieve (candidate); assistant route TBD | Source-linked evidence; no compliance guarantee |
| TBD | Admin/staff/proposal routes | Approve module, permissions and contracts first |

## Internal Python proposal
- M2: `POST /survey/extract`; `POST /geometry/land/validate`.
- M3: `POST /plans/editor/apply` (pure edit transform; candidate only).
- M6: `POST /geometry/footprint/feasibility`; `POST /plans/generate`; `POST /plans/validate`.
- M7: RAG retrieval route discovered from actual engine; do not invent existing URL.

## Contract conventions (approval required)
JSON camelCase public; explicit Pydantic aliases if internal style differs. 400 malformed; 401 unauthenticated; 403 or consistent 404 non-owner; 409 stale version/conflict; 413 upload too large; 422 domain validation where agreed; 429 throttling; 503 missing dependency; 504 timeout. Problem Details with trace ID and safe errors; no stack traces. Async job 202 with status route; state names frozen in contract. Date/time ISO UTC. Pagination limits. Mutation uses expected revision where required. Never auto-retry non-idempotent mutations. Version breaking changes require ADR, consumer review, migration and tests. Examples are design sketches only. Refer to each member's detailed plan for payload intent.

## Conventions proposal (2026-11-17)
Detailed options and recommendations for prefix, status codes, Problem Details, validation errors, error codes, trace IDs, CORS, OpenAPI, configuration, secrets, logging, health semantics and the Angular contract: [17-SHARED-CONVENTIONS-PROPOSAL](17-SHARED-CONVENTIONS-PROPOSAL.md). **PROPOSED only** — the conventions paragraph above stays the baseline until decisions C1–C12 are approved.

STEP 03 update (2026-11-18): decision matrix with states, compatibility, security and frontend assessments in [17-SHARED-CONVENTIONS-PROPOSAL.md §15](17-SHARED-CONVENTIONS-PROPOSAL.md); health/readiness contract in §16. Still PROPOSED / PENDING approval.

## Approved shared conventions — lead decision 2026-11-18
Approved by the Team lead (M4 + M7; GitHub `Kogulanathan69`): **C1** (`/api/v1`, route groups, root health endpoints, Angular via the C# API), **C3** (RFC 9457 Problem Details, built-in handling, safe translation, stable codes, trace IDs), **C4** (400 malformed/structural, 422 domain rules, shared validation infrastructure), **C5** (UPPER_SNAKE shared codes, W3C Trace Context, `traceId`, feature codes preserved), **C8**, **C9**, **C10**, and **C11b as architectural design**. Boundaries, exclusions and pending items (C2, C5b, C6, C7, C12, `/internal/v1`, feature diagnostics, readiness implementation) are recorded in [17-SHARED-CONVENTIONS-PROPOSAL.md §20](17-SHARED-CONVENTIONS-PROPOSAL.md). These are approved conventions, **not** implemented behaviour: implementation is BE-M4-D01c / D04, each needing separate execution approval. The conventions paragraph above remains valid where it does not conflict with §20.

## Implementation status of approved conventions (2026-11-18)
BE-M4-D01c implements C1, C3, C4, C5 (shared codes) and the C8/C9/C10 infrastructure on `feature/m4-shared-infrastructure` — **build verified, automated tests pending Linux CI, not merged**. Behaviour: every non-2xx response from the API is RFC 9457 `application/problem+json` with `code` and `traceId` (when the client accepts JSON); `/api/v1` hosts no product endpoints yet. See [17 §21](17-SHARED-CONVENTIONS-PROPOSAL.md).

## Database failure fallback mapping (BE-M4-D06; lead-approved 2026-11-18, STEP 09-B)
A database failure that escapes a feature returns:
- stale token → 409 `REVISION_CONFLICT`;
- duplicate key → 409 `CONFLICT`;
- unreachable, deadlock or lock wait → 503 `DEPENDENCY_UNAVAILABLE` (implements C11b for persistence);
- database command timeout → 504 `DEPENDENCY_TIMEOUT`;
- foreign key, unknown commit outcome, unconfirmed rollback or unexpected → 500 `INTERNAL_ERROR`.

Non-database exceptions are unaffected. Feature adapters keep precedence. No idempotency contract is implied (C2/C5b open). See [24-M4-D06-PERSISTENCE-CONCURRENCY.md](24-M4-D06-PERSISTENCE-CONCURRENCY.md).

## Regulation evidence contract v1 (M7; lead-approved 2026-11-19)
- **Endpoint:** `POST /internal/v1/regulations/search` on the private `rag-service`.
- **Request:** `{query (1–500), jurisdiction? (exact label), document_ids? (1–3 × 64-hex), k (1–10, strict int)}`. Unknown fields are rejected.
- **Response:** `{status, passages[], warnings[], legal_status_verified:false, answer_generated:false}`.
  - Status is one of `EVIDENCE_FOUND`, `NO_MATCHING_EVIDENCE`, `INSUFFICIENT_JURISDICTION_EVIDENCE`, `INSUFFICIENT_SOURCE_EVIDENCE`, `AMBIGUOUS_EVIDENCE`.
  - Each passage: `{passage_id, document_id, source_title, jurisdiction, page (one-based PDF position), start_char, end_char, excerpt, score (BM25 relevance, not legal confidence), evidence_status (EVIDENCE_CANDIDATE | AMBIGUOUS_EVIDENCE), warnings[]}`.
  - Passages appear only for `EVIDENCE_FOUND` and `AMBIGUOUS_EVIDENCE`.
- **Errors:** `422 VALIDATION_FAILED` (no input echo), `413 PAYLOAD_TOO_LARGE`, `503 CORPUS_UNAVAILABLE`, `500 INTERNAL_ERROR`.
- **Additive:** `GET /internal/v1/regulations/sources` (approved source labels) and `GET /health`.
- **C#:** `IRegulationEvidenceSource.SearchAsync` → `RegulationEvidence`. A contract violation becomes `MalformedResponse`, and unavailability becomes 503/504 via `DependencyFailureException`.
- **Shared example:** `rag-service/contract/search_response.v1.example.json`.

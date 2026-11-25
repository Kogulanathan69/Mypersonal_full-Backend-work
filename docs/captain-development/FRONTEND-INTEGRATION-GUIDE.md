# Frontend integration guide (Captain Development profile)

Audience: the Angular client team. Scope: the local Development stack only. **Production is NO-GO**; no production URL, origin or credential exists, and nothing here approves one.

- **Contract source:** `openapi.captain-dev.json` (142 operations, exported from the running API and enriched with live-observed response schemas) and the generated TypeScript declarations `contracts/buildmind-api.d.ts` (`ApiOperations["METHOD /path"]` gives path, query, body and response types; strict-tsc clean). Per-operation live evidence is in FINAL-API-ACCEPTANCE-MATRIX.md. Do not hand-edit; regenerate with `tools/captain/openapi_enrich.py` and `openapi_to_ts.py`.
- **Route status:** see MASTER-API-ENDPOINT-MATRIX.md.

## 1. Base URL, transport and CORS

- **API:** `http://127.0.0.1:5180/api/v1` (loopback only; started by `tools/captain/restart-stack.sh`).
- **CORS** is an exact-origin allowlist read from `Cors:AllowedOrigins` (C6, proposed). It is **empty**, because no approved frontend origin is recorded, so no CORS policy is active. Two options:
  - **Recommended:** run the Angular dev server with a proxy (`proxy.conf.json`: `/api` → `http://127.0.0.1:5180`). The browser then sees same-origin requests and no CORS is needed.
  - **Alternative:** the operator sets one exact local origin for their own machine, e.g. `Cors__AllowedOrigins__0=http://localhost:4200`. Wildcards are rejected at start-up. Plain `http` is accepted only in Development. Do not commit an origin.
- **Exposed headers:** `Location` only.

## 2. Authentication and sessions

| Step | Call | Notes |
|---|---|---|
| Register | `POST /auth/register {email, password}` | Anonymous; strict rate limit (429 when exceeded) |
| Login | `POST /auth/login` → `{accessToken, refreshToken, …}` | Keep the access token in memory. Never log tokens. |
| Authenticated calls | `Authorization: Bearer <accessToken>` | Every product route. The server also checks that the account/session is still active. |
| Refresh | `POST /auth/refresh {refreshToken}` | Rotation: the old refresh token is consumed. A reused token revokes the session family, so re-login. |
| Logout | `POST /auth/logout`, `POST /auth/logout-all` | |
| Current user | `GET/PUT /users/me` (same as `/profile`) | |

On **401**, refresh once, then send the user to login. Do not retry in a loop.

## 3. Error model

- Errors are RFC 7807 `application/problem+json` with `type`, `status`, `code` (stable, machine-readable), `traceId` and, for validation, `errors`. Show messages by `code`, never by parsing `detail`.

| Status | Meaning for the UI |
|---|---|
| 400 `VALIDATION_FAILED` | Fix the input (field errors in `errors`) |
| 401 | Not authenticated / session ended |
| 404 `NOT_FOUND` | Missing **or not yours**. Deliberately indistinguishable; never show "access denied". |
| 409 | Stale data (`STALE_BASE_REVISION`, `REVISION_CONFLICT`, `STALE_INPUT_REVISIONS`, `NOT_CURRENT_REVISION`, `ALREADY_CONFIRMED`, `IDEMPOTENCY_KEY_REUSED`, …). Reload the latest state and let the user retry. |
| 415 | Send `Content-Type: application/json` |
| 422 | Rejected by domain validation (e.g. `POLYGON_INVALID`, `GENERATION_UNSUCCESSFUL`, `WARNING_ACKNOWLEDGEMENT_REQUIRED`) |
| 429 | Rate limited; back off |
| 503 / 504 | Dependency unavailable / timed out (`DEPENDENCY_UNAVAILABLE`, `DEPENDENCY_TIMEOUT`, `AI_PROVIDER_NOT_CONFIGURED`). Show "temporarily unavailable"; never fabricate a result. |

All responses carrying private data are `Cache-Control: no-store`. Do not cache them in service workers.

## 4. Optimistic concurrency

Every write that changes a versioned resource takes the revision the user saw:

| Resource | Field |
|---|---|
| Requirements | `expectedRevisionId` (`null` for the first save) |
| Land | `expectedCurrentLandRevisionId` (`null` for the first boundary) |
| Generation | `expectedLandRevisionId` + `expectedRequirementsRevisionId` + client-generated `idempotencyKey` (reuse the same key when retrying the same request) |
| Plan edits/versions | `baseRevisionId` / `expectedBaseRevisionId` |
| Confirmation | exact `revisionId` + `expectedGeometryHash` |

On 409, reload and show the user the newer state. Never silently overwrite.

## 5. Customer journey (all project routes are `/projects/{projectId}/…`)

1. **Project:** `POST /projects`, then `GET /projects`, `GET /projects/{id}`.
2. **Requirements:** `PUT /requirements` (full snapshot) or the section routes (`/requirements/spaces`, `/relationships`, `/design-method`); `GET /requirements/summary`.
3. **Survey upload:** `POST /surveys` (multipart field `file`; PDF/JPEG/PNG ≤ 6 MiB) → `202 {surveyId}`.
4. **Survey review:** poll `GET /surveys/{surveyId}/extraction` every 2–5 s, up to about 2 minutes, until `processingStatus` is `Completed` or `Failed`.
   - `extraction.evidence.measurements[]` are **unverified evidence**: `value`, `unit`, source page and `review_status`.
   - `extraction.outcome = NEEDS_REVIEW` (always for OCR) means the customer must check each value against the document.
   - Show the returned `notice`.
   - The backend never converts lengths into coordinates, bearings or a polygon. The UI must not do this silently either.
   - If processing stays `Queued`, the extraction service is not running. Let the user enter the boundary manually.
5. **Land:**
   - `POST /land/validate {exterior}` is a dry run (`valid`, `code`, `areaM2`, `persisted: false`).
   - `POST /land/confirm {exterior, expectedCurrentLandRevisionId, surveyId?}` saves the boundary **the customer confirmed**. `PUT /land` is equivalent.
   - Coordinates are metres in a local frame.
6. **Generate:** `POST /generation-jobs` → three ranked alternatives, or 422 `GENERATION_UNSUCCESSFUL` with outcome `INFEASIBLE` (a truthful failure: show it, and do not offer fake plans). Then `GET /generation-jobs/{jobId}/alternatives`.
7. **Select:** `POST /generation-jobs/{jobId}/select {alternativeId}` or `POST /plans {jobId, alternativeId}` → plan revision 1.
8. **Edit:**
   - **Validate only:** `POST /plans/{planId}/edits/validate`, with the M3 edit command body (see OpenAPI `PlanEditRequestDto`). Returns `candidateGeometry`, `validation` and `persisted: false`.
   - **Save:** either `POST /plans/{planId}/versions {expectedBaseRevisionId, geometry}` (the server re-validates; 422 when invalid), or the candidate flow (`/candidates` → `/apply {acknowledgedWarnings}`).
   - **History:** `GET /plans/{planId}/versions`, `GET /plans/{planId}/versions/{revisionId}`.
   - **AI edits:** `POST /plans/{planId}/ai-edit/proposals` currently always returns **503 `AI_PROVIDER_NOT_CONFIGURED`**. Show the feature as unavailable.
9. **Confirm:** `POST /plans/{planId}/confirm {revisionId, expectedGeometryHash, acknowledgedWarnings: true}` (or `/confirmation`). Show the non-approval notice.
10. **Report:**
    - Create: `POST /reports {reportId (client UUID), planId, revisionId, confirmationId}`.
    - Track and fetch: `GET /reports`, `GET /reports/{reportId}`, `GET /reports/{reportId}/download` (PDF).
11. **Architect request:** `POST /architect-requests` (recipients from the synthetic directory), `GET /architect-requests[/{id}]`, `POST …/withdraw`. Requests are **recorded, not sent**.

## 6. Mandatory customer-facing wording

| Where | Required wording |
|---|---|
| Every plan, report and confirmation screen | Shows the server's `notice` (preliminary 2D layout, not construction-ready, not an architectural or regulatory approval) |
| Validation | When `regulatoryEvidenceStatus` is `SYNTHETIC_FIXTURE`, label it as synthetic development rules, not real regulations |
| Vastu | Never displayed (internal and nonblocking) |
| Cost / 3D | Not part of the MVP; do not show estimates |

## 7. Final-order additions

Each portal is listed with the routes the frontend must not guess.

### Customer: history and recovery

All are owner-scoped: another user's id returns 404.

| Route | Response |
|---|---|
| `GET /projects/{projectId}/surveys` | `SurveyListDto` |
| `GET /land/revisions`, `/land/revisions/{id}` | `LandRevisionListDto` / `LandRevisionDetailDto`; `isCurrent`, `georeferenced`, `geometryDigest` |
| `GET /requirements/revisions`, `/requirements/revisions/{id}` | `RequirementRevisionListDto` / `RequirementRevisionDetailDto` |
| `GET /generation-jobs` | `GenerationJobListDto` |
| `GET /plans/{planId}/candidates`, `/candidates/{id}` | `EditCandidateListDto` / `EditCandidateDetailDto`; `status`, `expired`, `expiresAtUtc` |
| `GET /plans/{planId}/confirmation` | `PlanConfirmationDto`; 404 when not confirmed |

**Undo/redo:**
- Call `POST /plans/{planId}/revert` with `{ expectedBaseRevisionId, targetRevisionId }`.
- It returns 201 with the new head revision.
- History is never rewritten: undo appends a revision equal to an earlier one, and redo appends the later one again.
- If the head has moved, it returns 409: reload, then retry.

### Sessions

| Route | Behaviour |
|---|---|
| `GET /users/me/sessions` | `SessionListDto` (`sessionId`, `active`, start, last refresh, expiries; no token material) |
| `DELETE /users/me/sessions/{sessionId}` | 204. That session's refresh token stops working at once. An access token it already issued stays valid until it expires (short-lived), so for immediate global revocation use `POST /auth/logout-all`. |

### AI edits

`POST /plans/{planId}/ai-edit/proposals` with `{ expectedBaseRevisionId, instruction }`.

| Outcome | Response |
|---|---|
| Success | 201 `{ candidate, proposalSource ("LLM:<model>" or "RULE_BASED_LOCAL"), applied: false, proposedCommand: { commandType, targetId, payload } }` |
| Model refused or instruction ambiguous | 422 with a specific `code`: `AMBIGUOUS_TARGET`, `MODEL_DECLINED_OR_INVALID`, `INSTRUCTION_NOT_SUPPORTED`, `MODEL_OUTPUT_INVALID` |
| Server rejected the resulting geometry | 422 `VALIDATION_EVIDENCE_INVALID` with `proposedCommand`, so the UI can explain what was attempted |
| Stale base revision | 409 |
| Provider down or not configured | 503 `AI_PROVIDER_UNAVAILABLE` / `AI_PROVIDER_NOT_CONFIGURED` |

- Nothing is applied until the customer calls `POST …/candidates/{candidateId}/apply`. Show warnings and require acknowledgement first.
- With the local model, show progress while you wait: accepted proposals take about 6–50 s.

### Regulation evidence

`POST /regulations/retrieve` with `{ query, maxResults?, jurisdiction?, documentIds? }` returns:
- `status`: `EVIDENCE_FOUND`, `AMBIGUOUS_EVIDENCE`, `NO_MATCHING_EVIDENCE`, `INSUFFICIENT_JURISDICTION_EVIDENCE` or `INSUFFICIENT_SOURCE_EVIDENCE`;
- `passages[]`, each with `documentId`, `sourceTitle`, `jurisdiction`, `page`, `startChar`, `endChar`, `excerpt`, `evidenceStatus` and `warnings`;
- `legalStatusVerified: false`, `answerGenerated: false`, and a `disclaimer` that must be shown.

Never present passages as approval or as the applicable law for the parcel.

### Admin

The admin portal re-reads privileges on every request.

| Route | Response |
|---|---|
| `GET /admin/projects/{projectId}` | `AdminProjectDetailDto` (workflow counts) |
| `GET /admin/system/workers` | `WorkerStatusListDto` (heartbeats; `healthy`) |
| `GET /admin/system/dependencies` | `DependencyReportDto` (mysql/python/rag status and duration) |

A non-admin caller gets 403 `PERMISSION_DENIED`.

### Large uploads

Bodies over 1 MiB are refused with 413 `PAYLOAD_TOO_LARGE`. Browsers that do not send `Expect: 100-continue` may see a connection reset instead of a readable 413, so check the file size before uploading.

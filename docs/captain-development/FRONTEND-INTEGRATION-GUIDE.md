# Frontend integration guide (Captain Development profile)

Audience: the Angular client team. Scope: the local Development stack only. **Production is NO-GO**; no production URL, origin or credential exists, and nothing here approves one.

- **Contract source:** `openapi.captain-dev.json` in this folder, exported from the running Development API (`/swagger/v1/swagger.json`, 57 operations). Regenerate it after backend changes; do not hand-edit.
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

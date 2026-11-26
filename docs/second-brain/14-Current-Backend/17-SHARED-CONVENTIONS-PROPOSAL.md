# Shared backend conventions and infrastructure boundaries — PROPOSAL (BE-M4-D01b)

Status: **Mixed — see the [lead decision record §20](#20-lead-decision-record--2026-11-18) (2026-11-18) for approved items and boundaries; everything not listed there as approved remains PROPOSED.** Original status line (2026-11-17): **PROPOSED — nothing in this note is approved** ( branch `feature/m4-shared-contracts` from develop `183ecf9`). Each section lists options, a recommendation and its justification. Approval is recorded per decision ID in the [checklist](#decision-checklist); until then [05-API-CONTRACTS](05-API-CONTRACTS.md) conventions remain the baseline and G04/G11 stay open. No code, package or configuration was changed to produce this note.

Inputs reviewed: V3 notes 03/05/06/11/12/13; merged M3 Application code (`Features/PlanEditor`, develop `183ecf9`); **unmerged, read-only** branches `feature/m3-plan-integration` (`01278d7`: transaction design, error codes, fixture locator) and `feature/m1-auth-projects` (`e0d2fa8`: user/project repositories, auth DTOs). Unmerged branch content is treated as work in progress, not baseline.

## 1. API versioning and route prefixes (C1)

| Option | Pros | Cons |
|---|---|---|
| A. URL segment `/api/v1/...` | Visible, cache/proxy friendly, trivial for Angular, matches V3 proposal | New major version duplicates routes |
| B. Header / media-type versioning | Clean URLs | Harder to test/debug; CORS preflight header; needs extra library |
| C. No versioning | Simplest | Breaking changes need coordinated big-bang releases |

**Recommendation: A**, implemented with an ASP.NET Core route group (`MapGroup("/api/v1")`) — no versioning package needed. Health endpoints stay **unversioned at the root** (`/health`, `/health/ready`) because they are operational, not product API. Python internal routes are private and versioned by payload `schemaVersion` plus a path prefix chosen by the M6 bootstrap custodian (suggested `/internal/v1`); they are never exposed to Angular. A new major version requires an ADR (05-API-CONTRACTS).

## 2. HTTP response conventions (C2)

**Recommendation:**
- Success bodies are the resource/DTO itself (no `{data: ...}` envelope); errors are always Problem Details (§3). Lists: `{ "items": [...], "page": 1, "pageSize": 20, "totalCount": n }`, `pageSize` max 100 (page-based chosen over cursors for MVP simplicity; revisit for large histories).
- JSON camelCase; enums as strings in the frozen V3 state style (`QUEUED`, `SUCCEEDED`…); timestamps ISO 8601 UTC with `Z`; IDs as GUID strings; nulls serialized explicitly (simpler for TypeScript than optional-vs-missing).
- Status codes (refines V3 list): 200 read/update, 201 create + `Location`, 202 async job + status URL, 204 no content; 400 malformed/shape validation; 401 unauthenticated; **404 (not 403) for resources owned by another user** to avoid ID enumeration — 403 only for authenticated-but-not-permitted *operations* (e.g. staff module scope); 409 stale revision/duplicate/state conflict; 413; 415; 422 domain/geometry rule failure; 429; 503 dependency unavailable; 504 dependency timeout; 500 unexpected (safe body only).
- Optimistic concurrency: request body `expectedRevision` (matches V3 and the M1 branch's `long expectedRevision`); `ETag`/`If-Match` kept as a later option, not mixed in MVP.
- Async generation POSTs accept an `Idempotency-Key` header (M6 to confirm); never auto-retry non-idempotent mutations.

## 3. RFC 9457 Problem Details (C3)

| Option | Notes |
|---|---|
| A. ASP.NET Core built-in `AddProblemDetails()` + `IExceptionHandler` + status-code pages | In shared framework; RFC 9457-shaped; extensible |
| B. Custom error envelope | Re-invents standard; Angular libraries expect problem+json less often but it is simple to parse |
| C. Third-party middleware package | Extra dependency for no MVP gain |

**Recommendation: A.** Media type `application/problem+json`. Members:

```json
{
  "type": "urn:buildmind:problem:revision-conflict",
  "title": "Revision conflict",
  "status": 409,
  "detail": "The plan has changed since revision 7 was loaded.",
  "instance": "/api/v1/projects/{id}/plans/{planId}/versions",
  "code": "REVISION_CONFLICT",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
}
```

`type` uses a `urn:buildmind:problem:<kebab-code>` URN (RFC 9457 permits non-dereferenceable URIs; avoids inventing a public domain). `detail` is safe, user-presentable text; never stack traces, SQL, connection strings, file paths, internal URLs, Vastu labels or other users' data. `instance` is the request path without query string. Illustrative example only.

## 4. Validation error responses (C4)

**Recommendation:**
- **400 `VALIDATION_FAILED`** for request shape/format failures (missing/invalid fields, model binding), with `errors`: `{ "<camelCase JSON path>": ["message", ...] }` — the ASP.NET `ValidationProblemDetails` shape, keys normalized to camelCase paths (e.g. `rooms[2].width`).
- **422 with a feature code** for domain/rule failures on well-formed input (geometry overlap, outside land boundary, invalid opening), with a `diagnostics` array reusing the feature's existing diagnostic DTO (M3 `PlanEditDiagnosticDto`) rather than a new shape.
- Malformed JSON body → 400 `MALFORMED_REQUEST`. Unknown JSON members: rejected only where a feature already uses strict parsing (M3 `PlanEditorJson.StrictOptions`); global strictness is a separate decision.

## 5. Error codes and trace IDs (C5)

**Codes:** UPPER_SNAKE_CASE, stable, never reused. M4 owns a small shared set; features own feature codes and register them in the API contract register. M3's proposed codes (`INVALID_EDIT_COMMAND`, `GEOMETRY_OVERLAP`, `CANDIDATE_EXPIRED`… on the unmerged integration branch) already follow this style and are accepted as-is — no M4 duplicates.

| Shared code (proposed) | Status |
|---|---|
| `MALFORMED_REQUEST`, `VALIDATION_FAILED` | 400 |
| `UNAUTHENTICATED` | 401 |
| `FORBIDDEN` | 403 |
| `NOT_FOUND` (also used for other-owner resources) | 404 |
| `CONFLICT`, `REVISION_CONFLICT`, `DUPLICATE_REQUEST` | 409 |
| `PAYLOAD_TOO_LARGE` / `UNSUPPORTED_MEDIA_TYPE` | 413 / 415 |
| `RATE_LIMITED` | 429 |
| `DEPENDENCY_UNAVAILABLE` / `DEPENDENCY_TIMEOUT` | 503 / 504 |
| `INTERNAL_ERROR` | 500 |

**Trace IDs:** use W3C Trace Context (`traceparent`), which ASP.NET Core and `HttpClient` propagate natively via `Activity`. `traceId` = current `Activity.TraceId`, returned in every Problem Details and included in every log scope. Outbound internal calls carry `traceparent` automatically; where an M3 port takes a `correlationId` string (`PlanEditorComputeRequest`), pass the trace ID. A client-supplied `traceparent` is accepted for correlation only and never trusted for security decisions. No separate `X-Correlation-Id` header (avoids two competing IDs).

## 6. CORS allowlist (C6)

**Recommendation:** one named policy built from configuration `Cors:AllowedOrigins` (array of exact origins; startup fails on `*` or an empty list outside Development). Development value `http://localhost:4200` is an **assumption** (Angular CLI default) — confirm with the frontend repo. Methods `GET, POST, PUT, PATCH, DELETE`; request headers `Authorization, Content-Type, Idempotency-Key, traceparent`; exposed headers `Location`; preflight max-age 600 s. `AllowCredentials` **off** while bearer tokens are used (decision depends on A1 below); never combine credentials with wildcard origins. CORS is not an authorization control.

## 7. OpenAPI exposure (C7)

| Option | Notes |
|---|---|
| A. `Microsoft.AspNetCore.OpenApi` (first-party, .NET 9+) document at `/openapi/v1.json`, Development only | Needs one package (10.0.x available on NuGet; pin to match runtime at implementation) |
| B. Swashbuckle / NSwag | Additional third-party dependency and UI |
| C. No OpenAPI; contracts in Markdown only | No machine-readable contract for Angular |

**Recommendation: A**, mapped only when `IsDevelopment()`; no UI package initially (frontend can import the JSON). Staging exposure only behind authentication if later approved; never in Production. Document lists auth scheme, Problem Details responses and `/api/v1` routes. Package installation is a separate approval.

## 8. Environment configuration keys (C8)

ASP.NET Core hierarchical keys; environment variables use `__` (e.g. `Services__Python__BaseUrl`). Bound to options classes validated at startup (`ValidateOnStart`).

| Proposed key | Replaces (`.env.example`) | Secret? | Owner |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `BUILDMIND_ENVIRONMENT` (C# side; Python may keep its own) | No | M4 |
| `ConnectionStrings:BuildMind` | `DATABASE_CONNECTION_STRING` note | **Yes** | M4 |
| `Services:Python:BaseUrl`, `Services:Python:TimeoutSeconds` | `PYTHON_SERVICE_URL` | No | M4 (+M6) |
| `Services:Rag:BaseUrl`, `Services:Rag:TimeoutSeconds` | `RAG_SERVICE_URL` | No | M4 (+M7) |
| `Services:*:Auth:*` | — | Yes | Pending G11 |
| `Cors:AllowedOrigins` | — | No | M4 |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes` | — | No | M4 + M1 |
| `Jwt:SigningKey` | — | **Yes** | M4 + M1 |
| `Storage:PrivateRoot`, `Upload:MaxBytes` | — | No | M4 + M2 |

Base URLs are a fixed allowlist from configuration — never taken from requests. `.env.example` would be updated to list names only, after approval.

## 9. Secure secret management (C9)

**Recommendation:** Development → `dotnet user-secrets` (adds a `UserSecretsId` to the API project — a source change for D01c); CI → no secrets needed for current tests, GitHub Actions encrypted secrets only when an approved job needs them (never echoed); deployed environments → environment variables from a host secret store/vault (hosting undecided, G11/G15). Never commit secrets in `appsettings*.json`; `appsettings.Development.json` is currently git-ignored — proposal: keep it ignored and add a committed, secret-free `appsettings.Development.example.json` (decision C9b). MySQL accounts least-privilege (separate migration and runtime accounts) per 04-DATABASE. Rotate any secret that ever reaches Git history.

## 10. Logging and sensitive-data redaction (C10)

| Option | Notes |
|---|---|
| A. Built-in `ILogger` + JSON console formatter (non-Development) + message templates + scopes | No new dependency |
| B. Serilog | Rich sinks; extra dependencies |
| C. `Microsoft.Extensions.Compliance.Redaction` data classification | Strong guarantees; extra packages and type annotations |

**Recommendation: A now**, C reconsidered when PII-bearing logs appear. Rules: structured templates (no string concatenation of user data); `traceId` scope on every request; **never log** passwords, password hashes, tokens/`Authorization` headers, signing keys, connection strings, reset tokens, raw survey files/OCR text, full geometry payloads or retrieved document bodies; emails masked; request/response body logging off; HTTP logging (if enabled) with an explicit header allowlist. Log levels: Information for lifecycle, Warning for handled dependency failures, Error for unexpected exceptions (with exception, still no secrets). Vastu heuristics never in client-visible output. Security audit records (V3 11-SECURITY) are a separate persisted store, not logs.

## 11. Liveness and readiness (C11)

- **Liveness `GET /health`** (exists, verified): process is up; no dependency checks; anonymous; body `{"service":"buildmind-api","status":"ok"}` unchanged.
- **Readiness `GET /health/ready`** (BE-M4-D04): ASP.NET Core health checks (in shared framework) tagged `ready`: MySQL (after D02), Python, RAG. Each check bounded (≈2 s). Response: `{"status":"Healthy|Degraded|Unhealthy","checks":[{"name":"mysql","status":"..."}]}` — **no exception messages, hosts or connection strings**.
- **Decision C11b (open):** which dependencies are hard. Recommendation: MySQL Unhealthy → 503; Python/RAG Unhealthy → `Degraded` with HTTP 200, because auth/projects/requirements can still serve; feature endpoints that need Python/RAG return 503 `DEPENDENCY_UNAVAILABLE` themselves. Alternative: any Unhealthy → 503 (simpler, stricter).
- D01a's `ReadinessTests` ("not successful while unimplemented") are replaced by dependency-driven tests when D04 lands.

## 12. Angular frontend integration contract (C12)

- Angular calls **only** the C# API at `environment.apiBaseUrl` + `/api/v1`; never Python or RAG.
- Auth header `Authorization: Bearer <token>`; token storage strategy (memory vs cookie) is an M1/frontend security decision (A1) and drives CORS credentials.
- Error handling keys off `code` (not `title`/`detail` text); 401 → re-authenticate; 404 → not found/not yours (indistinguishable by design); 409 `REVISION_CONFLICT` → reload and re-apply; 422 → show `diagnostics`; 503/504 → retry later message; always surface `traceId` for support.
- Mutations send `expectedRevision`; async jobs poll the returned status URL; job POSTs send `Idempotency-Key`.
- Dates parsed as UTC ISO strings; IDs treated as opaque strings.
- Local dev: no `launchSettings.json` exists, so the API port is not fixed — D01c should add one (or document `--urls`) so `apiBaseUrl` is stable.

## 13. Shared infrastructure interfaces (I1–I9)

Principle: **reuse feature-owned ports; add a shared abstraction only where several features need the same capability.** No generic repository, no duplicate of an M1/M3 port.

### Existing M3 ports (merged, `Features/PlanEditor`) and proposed implementers

| Port | Proposed implementer | Notes |
|---|---|---|
| `IProjectOwnershipGate` | M1 business rule, adapter in Infrastructure | Can use M1 branch `IProjectRepository.GetByIdAndOwnerAsync` |
| `IProjectLifecycleEligibilityPort` | M1 | Lifecycle states not frozen |
| `IConfirmedLandRevisionPort` | M2 | |
| `IRequirementRevisionPort` | M5 | |
| `IApplicableInputRevisionSource` | M2 + M5 composite | Ownership of the composite needs agreement |
| `IPlanEditorComputeClient` | **M4** typed HTTP client (BE-M4-D05) | Route/payload owned by M3 |
| `IAiEditProposalPort` | **Gap** — M3/M7 (AI provider G11) | Not assigned |
| `IPlanValidationEvidenceVerifier` | M6 | Authoritative validation |
| `IValidationFreshnessPolicy` | **Gap** — M6 policy proposed | Rule not defined |
| `IPlanRevisionStore` | **M4** EF Core adapter with M3 mapping | See I8 |
| `IFinalConfirmationTransaction` | **M4** (with M1 lifecycle transition) | See I9 |
| `IConfirmedPlanSnapshotSink` | **M4** outbox enqueue + M5 consumer | Behaviour change needs M3 approval |
| `TimeProvider` (BCL, used by M3 services) | **M4** registers `TimeProvider.System` | Tests use fakes |

### Proposed shared abstractions

- **I1 Current user context** — `ICurrentUser` in `Application/Abstractions`: `bool IsAuthenticated`, `Guid UserId` (throws if unauthenticated), `IReadOnlySet<string> Roles`. Implemented in the API project from validated claims (`sub`). M3 services already take `authenticatedUserId` parameters; endpoints pass `ICurrentUser.UserId`, so **no M3 change is required**. Never read a user ID from the request body.
- **I2 Authentication/authorization** — M4 configures JWT bearer validation (`Microsoft.AspNetCore.Authentication.JwtBearer`, package approval needed): issuer, audience, signing key, lifetime, clock skew ≤ 1 min. Policies `Customer`, `Admin`; delegated-staff module policies after G09. Resource ownership stays in feature handlers. Rate limiting (built-in middleware) on auth endpoints.
- **I3 Token service** — `IAccessTokenIssuer` (M4 Infrastructure) issues signed access tokens from `Jwt:*` options for M1's login use case; M1 owns credentials, registration and recovery logic. Password hashing abstraction `IPasswordHasher` — implementer (M4 vs M1) and algorithm/package to agree with M1 (decision A2). Refresh tokens only if approved (V3 04-DATABASE).
- **I4 Repositories** — feature-specific ports owned by features (`IProjectRepository`, `IUserRepository` on M1 branch; `IPlanRevisionStore` M3), implemented in Infrastructure with EF Core after G03. Owner-scoped query methods (`GetByIdAndOwnerAsync`) preferred over post-filtering.
- **I5 Transactions** — no ambient transaction API exposed to Application. Simple single-aggregate writes: `IUnitOfWork.SaveChangesAsync(ct)`. Multi-row atomic operations: **feature-specific atomic ports** (pattern already used by `IFinalConfirmationTransaction`), implemented with an explicit transaction inside the provider's execution strategy. No transaction spans an HTTP call to Python/RAG. Concurrency token: application-managed `long Revision` (already in the M1 branch) configured as an EF concurrency token — provider-portable, avoids SQL Server `rowversion`. `DbUpdateConcurrencyException` → 409 `REVISION_CONFLICT`; unique-key violations → 409 `DUPLICATE_REQUEST` via a provider-specific translator.
- **I6 Internal Python/RAG clients** — typed `HttpClient` per service via `IHttpClientFactory`; base URL from §8 allowlist; outbound `traceparent`; JSON with explicit schema version. Adapters for M3 ports return the port's own result/status types (e.g. `AiEditProposalStatus.Unavailable`) instead of throwing, matching M3's style; elsewhere a shared `ExternalDependencyException { Kind: Unavailable | Timeout | InvalidResponse }` is mapped to 503/504.
- **I7 Timeouts and cancellation** — every async port takes `CancellationToken` (M3 already does); request-aborted tokens flow through; per-client timeouts from configuration (proposed defaults: Python compute 10 s, RAG 5 s, readiness checks 2 s — to be measured, not SLAs). **No automatic retries initially**; later retries only for idempotent GETs, via a resilience package if approved. Timeouts never reported as success.
- **I8 Plan revision persistence** (`IPlanRevisionStore`, M4) — append = insert immutable revision + conditional update of the plan's current-revision pointer from `expectedBaseRevisionId`, in one transaction; unique `(PlanId, VersionNumber)` and `(PlanId, EditCommandId)`; 0 affected rows → `RevisionConflict`; duplicate command → `DuplicateCommand`. **Gap (from M3's own transaction design, unmerged):** the interface has no explicit plan concurrency token or uncertain-commit reconciliation query; any change to the interface is M3's to make, reviewed by M4.
- **I9 Final confirmation transaction** (`IFinalConfirmationTransaction`, M4) — single MySQL transaction: insert confirmation (unique per exact revision), conditional M1 project lifecycle update with its revision, insert outbox message (unique `(eventType, confirmationId)`); all-or-nothing. Valid only because M1 and M3 data share one database per V3 architecture; if that changes, a saga is required (as M3 notes). `IConfirmedPlanSnapshotSink` becomes outbox-facing only with M3 approval.

## 14. M3 contract runner integration (G18)

Facts (read-only, develop `183ecf9`): `tests/BuildMind.Application.ContractTests` is an `Exe` (not xUnit) with **22** `Run(...)` checks; prints `N passed, M failed`; **exits 1 on any failure, 0 otherwise** (CI-compatible); not in `BuildMind.sln`; not run by CI. **Never executed in this audit** (Windows Smart App Control, G17) — its checks are excluded from verified totals.

**Fixture path issue — confirmed by static path resolution:** with no argument, `Path.Combine(AppContext.BaseDirectory, "../../../../docs/...")` from `tests/BuildMind.Application.ContractTests/bin/<Config>/net10.0/` resolves to `<repo>/tests/docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json`, which does not exist (one level short; five levels would reach `<repo>/docs/...`). A second check (line 92) throws "Fixture path is required." without an argument. So a no-argument run fails at least those two checks; an explicit repo path argument should satisfy both. The unmerged `feature/m3-plan-integration` branch replaces this with a `FixtureLocator` that searches upward from both the working directory and `AppContext.BaseDirectory`, which would fix it once merged.

| Option | Summary | Assessment |
|---|---|---|
| A | M4 adds the project to `BuildMind.sln`; CI step `dotnet run --project tests/BuildMind.Application.ContractTests -c Release --no-build -- docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json` | Works with current code; explicit arg avoids the bug |
| B | Same as A but after M3's `FixtureLocator` is merged, run without arguments | Cleaner; depends on M3 merge |
| C | M3 converts the runner to xUnit so `dotnet test` discovers it | Best long-term reporting; M3's decision and effort |

**Recommendation:** B if M3's integration branch merges soon, otherwise A; C as M3's longer-term choice. Either way the CI step is separate and **its count is reported separately** from the 22 xUnit cases. To verify on CI: that `dotnet test BuildMind.sln` skips the non-test `Exe` project cleanly once added. Requires M3 agreement before any change.

## Decision checklist

| ID | Decision | Recommendation | Approver |
|---|---|---|---|
| C1 | `/api/v1` route group; root health endpoints; internal `/internal/v1` | Approve | Lead + M1–M6 consumers |
| C2 | No success envelope; status-code table; 404 for other-owner resources; page-based lists | Approve | Lead + frontend |
| C3 | Built-in Problem Details, URN `type`, `code` + `traceId` | Approve | Lead |
| C4 | 400 `VALIDATION_FAILED` with `errors`; 422 domain with feature `diagnostics` | Approve | Lead + M3/M6 |
| C5 | Shared code set; feature-owned codes; W3C trace context | Approve | Lead |
| C6 | Config-driven exact-origin CORS; dev origin to confirm | Approve after frontend confirms port | Lead + frontend |
| C7 | `Microsoft.AspNetCore.OpenApi`, Development only | Approve (package approval separate) | Lead |
| C8 | Configuration key table | Approve | Lead + M1/M2/M6/M7 |
| C9 / C9b | user-secrets / env vault; committed example dev settings | Approve | Lead |
| C10 | Built-in logging + redaction rules | Approve | Lead |
| C11b | Python/RAG down → Degraded (200) vs 503 | Decide | Lead |
| C12 | Angular contract | Approve after frontend review | Lead + frontend |
| A1 | Token storage (memory bearer vs cookie) | Open | M1 + frontend + lead |
| A2 | Password hasher implementer and algorithm | Open | M1 + M4 |
| I1–I9 | Shared interfaces and port implementers above; gaps `IAiEditProposalPort`, `IValidationFreshnessPolicy`, composite input source | Approve owners / assign gaps | Lead + M1/M2/M3/M5/M6/M7 |
| G18 | M3 runner: option B (else A); C long-term | Agree with M3 | Lead + M3 |

Implementation of approved items would be a separate code slice (proposed **BE-M4-D01c**: route group, Problem Details + exception handler, CORS, OpenAPI dev, options binding + validation, `ICurrentUser`, `TimeProvider`, launch settings) with tests, before or alongside BE-M4-D04.

---

# STEP 03 technical decision finalization (2026-11-18)

Scope: BE-M4-D01b STEP 03-A … 03-E, documentation and read-only review only. Sections 1–14 above are preserved as written on 2026-11-17; the sections below refine them. **Decision states** follow the lead's vocabulary: PROPOSED, PENDING LEAD APPROVAL, PENDING CONSUMER APPROVAL, CONDITIONALLY APPROVED, APPROVED, IMPLEMENTATION PENDING, BLOCKED, VERIFIED. No item below is APPROVED or CONDITIONALLY APPROVED: the only approval evidence is the lead's 2026-11-17 review, which **provisionally accepted C1–C12 for detailed review, not frozen**, and set C11b and G03 directions as proposals.

> The *State* column of the matrix below records the position on 2026-11-18 **before** the lead decision; current states are in §20.

Baseline inspected: develop / `feature/m4-shared-contracts` `183ecf9`; `src/BuildMind.API/Program.cs`, `appsettings.json`, both `DependencyInjection.cs`, merged M3 `Features/PlanEditor` (12 ports), unmerged PR #9 (`01278d7`) and M1 branch (`e0d2fa8`) read-only.

## 15. STEP 03-A — Shared API conventions decision matrix

| ID | Preferred direction | State | Compatibility with current source | Security impact | Frontend impact | Consumers to agree |
|---|---|---|---|---|---|---|
| C1 | `/api/v1` route group; root `/health`, `/health/ready`; internal `/internal/v1` | PENDING LEAD APPROVAL | No product routes exist; only root `/health`. Adding a group is non-breaking | Keeps internal routes off the public surface | Base URL `…/api/v1` | M1–M6 (route names); M6 custodian for `/internal/v1` |
| C2 | Direct DTOs; status table (200/201/202/204/400/401/403/404/409/413/415/422/429/500/503/504); 404 conceals other-owner resources; list `{items,page,pageSize,totalCount}`, default 20, max 100 | PENDING CONSUMER APPROVAL | M1 draft DTOs are direct DTOs; M1 `UpdateProjectRequest.Revision` vs proposed `expectedRevision` naming | 404 concealment prevents ID enumeration | Typed models per DTO; one list shape | Frontend, M1, M5 |
| C3 | RFC 9457 via `AddProblemDetails()` + `IExceptionHandler` + `UseExceptionHandler` + `UseStatusCodePages`; fields `type,title,status,detail,instance,code,traceId` | PENDING LEAD APPROVAL | **No exception handling today**: unhandled exceptions return an empty 500 in Production; unknown routes return an empty 404. Developer exception page appears only in Development | Closes stack-trace/internal-detail exposure; requires a deny-list review of `detail` text | One error parser keyed by `code` | All feature owners |
| C4 | 400 `MALFORMED_REQUEST`/`VALIDATION_FAILED` + `errors{camelCase path: [..]}`; 422 + feature `diagnostics` | PENDING LEAD APPROVAL | M3 has `PlanEditDiagnosticDto` (merged) and `PlanEditorDiagnostic` (PR #9) — reuse, do not replace | Validation messages must not echo secrets or other users' data | Field-level form errors | M3, M6, M1 |
| C5 | Shared UPPER_SNAKE codes (14 listed in §5); feature codes preserved; W3C `traceparent`; `traceId` in errors and logs | PENDING LEAD APPROVAL | M3 PR #9 codes `REVISION_CONFLICT`, `DEPENDENCY_UNAVAILABLE`, `DEPENDENCY_TIMEOUT` already match; M3 `UNEXPECTED_INTERNAL_FAILURE` is a **feature diagnostic code** and stays; HTTP-level 500 uses shared `INTERNAL_ERROR`; mapping rule, no rename | Client correlation IDs never used as identity/authorization evidence | Show `traceId` for support | M3 (mapping rule), all owners |
| C5b | Serialization: camelCase, GUID strings, ISO 8601 UTC, explicit nulls, contract-defined string enums | PENDING CONSUMER APPROVAL | M3 uses explicit `[JsonPropertyName]` camelCase and string status constants with strict unmapped-member handling — compatible | Strict parsing limits mass-assignment | Generated TS types | Frontend, M3 |
| C6 | Config `Cors:AllowedOrigins` exact list; no wildcard outside Development; credentials off until A1 approved | PENDING LEAD APPROVAL; origin **unverified** | No CORS today; `AllowedHosts:"*"` in `appsettings.json` (host filtering, separate from CORS) should become environment-specific | Prevents credentialed wildcard CORS | Dev origin required | Frontend: **frontend repository has no Angular project on any branch (checked 2026-11-18)**, so `http://localhost:4200` remains an assumption |
| C7 | `Microsoft.AspNetCore.OpenApi` (first-party) at `/openapi/v1.json`, Development only, no UI package | PENDING LEAD APPROVAL; package install needs separate approval | Not present; `net10.0` 10.0.x packages exist on NuGet | No production exposure | Machine-readable contract | Frontend |
| C8 | Options hierarchy in §8, strongly typed, `ValidateOnStart` | PENDING LEAD APPROVAL | `.env.example` names differ (`PYTHON_SERVICE_URL` etc.); `AddInfrastructure` already receives `IConfiguration` | Fixed URL allowlist; fail-fast on missing secrets | — | M1 (Jwt), M2 (Upload/Storage), M6, M7 |
| C9 | User Secrets (dev), GitHub Actions secrets (CI, only when needed), deployment secret store (prod) | PENDING LEAD APPROVAL | No `UserSecretsId` yet; no secrets in repo (scanned) | Never print/commit secrets | — | Lead |
| C10 | Built-in structured `ILogger`, trace scope, body logging off, deny-list, audit separate | PENDING LEAD APPROVAL | Only default logging config exists | Prevents credential/PII leakage in logs | — | All owners |
| C11 | Liveness/readiness split (§16) | Liveness VERIFIED (D01a); readiness PROPOSED; **C11b PENDING LEAD APPROVAL** | `/health` implemented and CI-tested; `/health/ready` not mapped | See §16 | Frontend should not depend on readiness | Lead, M6, M7, hosting |
| C12 | Angular uses only the C# API; never MySQL, Python, RAG or private storage | Direction accepted by lead 2026-11-17 (not frozen); details PENDING CONSUMER APPROVAL | Consistent with architecture | Keeps internal services private | Auth storage (A1), origins, DTOs pending | Frontend, M1 |

**Implementation acceptance criteria (for D01c, once approved):** every non-2xx response from `/api/v1` is `application/problem+json` with `code` and `traceId`; no stack trace, SQL, connection string, path or internal URL in any response (tested by forcing exceptions); unknown route → Problem Details 404; malformed JSON → 400 `MALFORMED_REQUEST`; CORS preflight allowed only for configured origins; OpenAPI document reachable only in Development; startup fails fast on invalid required options; existing 22 xUnit cases and the `/health` contract unchanged.

**Proposed documentation updates after approval:** freeze conventions into 05-API-CONTRACTS; update `.env.example` names (no values); API register shared-code table.

## 16. STEP 03-C — Health checks and dependency failure behaviour

**Liveness contract (VERIFIED, unchanged):** `GET /health` → 200 `{"service":"buildmind-api","status":"ok"}`; anonymous; no dependency checks; never claims database or service integration.

**Readiness contract (PROPOSED, not implemented):** `GET /health/ready`; ASP.NET Core health checks (`AddHealthChecks`, shared framework) tagged `ready`. Body `{"status":"Healthy|Degraded|Unhealthy","checks":[{"name":"mysql","status":"Healthy"}]}` with names and statuses only. Database check after D02 (custom `IHealthCheck` using the DbContext, or an EF health-check package if approved).

**Dependency classification (C11b proposal — PENDING LEAD APPROVAL):**

| Dependency / condition | Readiness status | HTTP for readiness | Feature behaviour |
|---|---|---|---|
| MySQL unreachable | Unhealthy | 503 | All persistence-backed endpoints 503 `DEPENDENCY_UNAVAILABLE` |
| Required startup options invalid / missing secret | Not ready (startup fails fast) | 503 or process exit | — |
| Dependency **not configured** (no base URL) | Unhealthy for that check, reported as `not_configured` | 503 if mandatory, Degraded if optional | Never reported Healthy |
| Python unavailable | Degraded | 200 | Survey extraction, land validation, generation, editor preview, validation → 503; timeout → 504 |
| RAG unavailable | Degraded | 200 | Evidence-required operations → 503; advisory evidence → explicit `unavailable` status, never guessed constraints |
| Python/RAG timeout during a request | — | — | 504 `DEPENDENCY_TIMEOUT` |

**Workflow dependency assessment:** Python is **mandatory** for survey extraction (M2), land geometry validation (M2), generation (M6), editor transformation (M3) and authoritative validation (M6); auth, profile, project CRUD and requirements (M1/M5) do not need it. RAG is mandatory only where M6/M7 define evidence as required (open); otherwise advisory. Therefore Python and RAG are **capability-mandatory, instance-optional**: readiness reports Degraded and each capability fails with 503 rather than degrading silently. Geometry validation and required regulatory evidence are never bypassed.

**Failure scenario matrix:**

| Scenario | Expected result | Must never happen |
|---|---|---|
| Python connection refused | 503 `DEPENDENCY_UNAVAILABLE`, `traceId`, retry-safe message | Fabricated geometry/validation, 200 |
| Python exceeds timeout | 504 `DEPENDENCY_TIMEOUT`; request cancelled downstream | Hanging request; automatic retry of a mutation |
| Python returns schema-invalid or wrong `schemaVersion` | 503 with `INVALID_DEPENDENCY_RESPONSE` diagnostic (M3 code) or shared mapping | Partially applied result |
| Client disconnects | Cancellation propagated; transaction rolled back | Orphaned writes |
| MySQL deadlock/conflict | 409 `REVISION_CONFLICT` / `CONFLICT` | Silent overwrite |
| RAG no hit / conflicting sources | Evidence `not verified`, explicit gap | Invented setback/rule |

**Health-check security:** anonymous but minimal (no versions, hosts, exception messages, connection details); per-check timeout ≈2 s; results cached ≈5–10 s; consider restricting `/health/ready` to internal networks; rate-limit if public; alert on Degraded.

**Automated test plan (D04):** liveness unaffected by dependency failures; readiness Healthy with stubbed healthy checks; Unhealthy (503) when MySQL check fails; Degraded (200) when Python/RAG fail; `not_configured` never Healthy; response body contains no exception text; check timeout enforced; D01a `ReadinessTests` replaced by these.

## 17. STEP 03-D — Interface inventory, ownership and transaction boundaries

**Existing shared M4 code:** `AddApplication`, `AddInfrastructure` (register nothing), API composition root. **Existing shared abstraction:** `Application/Abstractions/IProjectRepository` (starter; M1 branch extends it, unmerged).

**Merged M3 ports (develop `183ecf9`) — interface owner M3; none has an implementation or DI registration:**

| Port | Proposed implementer | Consumer | Persistence / dependency | Security | Transaction guarantee | Error handling | Tests | Coordination |
|---|---|---|---|---|---|---|---|---|
| `IProjectOwnershipGate` | M1 rule; Infrastructure adapter | Confirmation (and PR #9 preview) | M1 `IProjectRepository.GetByIdAndOwnerAsync` (unmerged) | Identity from `ICurrentUser` only | Read | Not owner → `Unauthorized` status → HTTP 404 | Owner A/B isolation | M1 |
| `IProjectLifecycleEligibilityPort` | M1 | Confirmation | Project status | Owner-scoped | Read | `BlockingCode` | Each lifecycle state | M1 (states not frozen) |
| `IConfirmedLandRevisionPort` | M2 | Editor | Land revisions | Owner via project | Read | `null` → not found | Exact revision | M2 (no code pushed) |
| `IRequirementRevisionPort` | M5 | Editor | Requirement revisions | Owner via project | Read | `null` → not found | Exact revision | M5 (no code pushed) |
| `IApplicableInputRevisionSource` | **G20** (recommend M3 composite over M2/M5 "current" lookups) | Confirmation | Land + requirements current revisions | — | Consistent read | Stale → `StaleInputs` | Mismatch detection | M2, M3, M5 |
| `IPlanEditorComputeClient` | **M4** typed HttpClient (D05) | Editor | Python M3 route | Internal auth (G11) | None (no DB transaction across call) | Unavailable/timeout → 503/504 | Stub server failure tests | M3 route, M6 custodian |
| `IAiEditProposalPort` | **G20** (recommend M7 adapter) | Editor AI | AI provider (G11) | Untrusted output; proposal only | None | `Unavailable` until provider approved | Injection/invalid command tests | M3, M7 |
| `IPlanValidationEvidenceVerifier` | M6 | Versioning | Validation evidence | Bound to exact geometry hash | Read | `Unavailable`/`Rejected` | Hash mismatch | M6 (no code pushed) |
| `IValidationFreshnessPolicy` | **G20** (recommend M6 semantics) | Confirmation | Rule/engine versions | Integrity | Pure | Stale → `ValidationStale` | Version change invalidates | M6, M3 |
| `IPlanRevisionStore` | **M4** EF adapter | Versioning, confirmation | PlanVersions, FloorPlan pointer | Owner-scoped keys | Insert revision + conditional pointer update, one transaction; unique `(PlanId,VersionNumber)`, `(PlanId,EditCommandId)` | 0 rows → `RevisionConflict`; duplicate → `DuplicateCommand` | Disposable MySQL (D02/D06) | M3 (interface gaps: concurrency token, reconciliation query) |
| `IFinalConfirmationTransaction` | **M4** | Confirmation | Confirmation, Project lifecycle, Outbox | Owner re-check inside | One transaction: confirmation + conditional lifecycle update + outbox; all-or-nothing | Conflict → `AlreadyConfirmed`/409 | Rollback on each failure point | M1, M3, M5 |
| `IConfirmedPlanSnapshotSink` | **M4** outbox enqueue | Confirmation | Outbox | No PII beyond need | Inside confirmation transaction (needs M3 approval) | Delivery failure never rolls back confirmation | Outbox idempotency | M3, M5 |
| `TimeProvider` (BCL) | **M4** registers `TimeProvider.System` | Versioning, confirmation | — | — | — | — | Fake time in tests | — |

**Unmerged PR #9 adds (not approved contracts until merged):** `ICandidatePreviewStore` and `ICandidatePreviewTransaction` (→ M4 persistence; candidate apply = consume candidate + append revision atomically; reject/expire conditional), `ICandidateValidationEvidenceSource` (→ M6). `ICandidatePreviewStore.GetAsync(candidateId)` is not owner-scoped — implementations must keep the service-level ownership recheck.

**Unmerged M1 branch (not approved):** `IUserRepository`, extended `IProjectRepository` (`GetByIdAndOwnerAsync`, `UpdateAsync(..., expectedRevision)`), `User`/`Project` entities with known mutability defects (see 2026-11-17 integration audit).

**Proposed shared abstractions (M4, PROPOSED):** `ICurrentUser` (API implementation from validated `sub`/role claims); JWT validation (issuer, audience, signature, lifetime, ≤1 min skew), `Customer`/`Admin` policies, auth endpoint rate limiting; `IAccessTokenIssuer`; `IPasswordHasher` (A2 pending); `IUnitOfWork.SaveChangesAsync`; feature-specific atomic ports; typed HttpClients with configured base URLs, bounded timeouts, cancellation, `traceparent`, `schemaVersion` validation and invalid-response detection; `ExternalDependencyException` only where a port has no status result. **No generic repository.** Feature repositories (User, Project, Land, Requirements) are owned and implemented by their feature owners following M4 conventions; M4 implements transactional adapters (Plan revision, Confirmation, Candidate preview, Outbox).

**Transaction boundary rules:** application-managed `long Revision` concurrency token; conditional `UPDATE … WHERE Revision = @expected`; unique constraints as the final idempotency defence; never span a DB transaction across an HTTP call; never auto-retry non-idempotent writes; uncertain commit → reconcile by command/candidate/confirmation key before any retry (per M3 transaction design).

**Implementation sequence:** D01c (`ICurrentUser`, `TimeProvider`, options, Problem Details) → D02 (provider spike, DbContext) → JWT/token infrastructure after A1/A2 → D05 typed clients after G11 → D06 transactional adapters (`IPlanRevisionStore`, confirmation, candidate preview, outbox) after M3 interface gaps are agreed.

**Unresolved gaps:** G20 ports (plus PR #9's `ICandidateValidationEvidenceSource` for M6); M2/M5/M6 have no pushed code; M3 interface lacks explicit concurrency token and reconciliation query; A1/A2; G11; G03.

**Acceptance criteria (for later implementation):** each adapter has owner-isolation tests; conflict and duplicate paths return the port's documented status; rollback verified on a disposable MySQL database; no M3 contract renamed or changed without M3 approval.

## 18. STEP 03-E — M3 contract runner (revalidated 2026-11-18)

Revalidated on develop `183ecf9` (read-only): `tests/BuildMind.Application.ContractTests` is `OutputType Exe`, `net10.0`, referencing only `BuildMind.Application`; **22** `Run("…")` checks; exits **1** on any failure, **0** on success; **not** in `BuildMind.sln`; **not** run by `.github/workflows/backend-ci.yml`. Default fixture path `Path.Combine(AppContext.BaseDirectory, "../../../../docs/…")` resolves to `<repo>/tests/docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json` (absent) for both Debug and Release output folders; the command-sample check throws without an explicit argument. PR #9 (`01278d7`, open, CI green, unreviewed) adds `FixtureLocator` (searches upward from working directory and `AppContext.BaseDirectory`, rejects more than one argument, explicit missing path → `FileNotFoundException`) and returns exit code 2 on fixture discovery failure. **The runner has never been executed in this audit** (Smart App Control, G17); none of its checks are counted as passed.

| Option | Requires | Risk | State |
|---|---|---|---|
| B (preferred) | PR #9 reviewed and merged; M3 agreement | Depends on M3 merge timing | PROPOSED, pending M3 |
| A (fallback) | M3 agreement; explicit argument `docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json` | Works with current code; argument must be kept in sync | PROPOSED, pending M3 |
| C (long-term) | Separate M3 task | M3 effort | PROPOSED |

**Proposed future CI step (not applied):**

```yaml
      - name: Run M3 contract checks (console runner, reported separately)
        run: dotnet run --project tests/BuildMind.Application.ContractTests --configuration Release --no-build
```

(Option A appends `-- docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json`.) The project must first be added to `BuildMind.sln` (M4, solution custodian) so `--no-build` finds the Release output.

**Verification plan:** solution builds with the runner included; `dotnet test BuildMind.sln` still reports exactly the xUnit count and does not treat the Exe as a test project; runner prints `N passed, 0 failed` and exits 0 on CI; deliberately missing fixture → non-zero exit (2 on PR #9 code); invalid fixture JSON → non-zero exit; CI evidence records xUnit and runner counts in separate rows; `LayerDependencyTests.Solution_registers_all_source_projects_and_this_test_project` still passes.

**Acceptance criteria:** M3 agreement recorded; runner green on Linux CI for two consecutive runs; counts reported separately; no change to M3 source by M4.

## 19. Recommended next slice — BE-M4-D01c (plan only, not started)

**Can proceed after lead approval of C1/C3/C4/C5/C8/C9/C10 (no other-member approval needed):**

| Item | Files to create | Files to modify |
|---|---|---|
| Problem Details + exception handler + status-code pages | `src/BuildMind.API/Errors/GlobalExceptionHandler.cs`, `src/BuildMind.API/Errors/ProblemDetailsDefaults.cs` | `Program.cs` |
| Shared error codes | `src/BuildMind.Application/Abstractions/Errors/SharedErrorCodes.cs` | — |
| `/api/v1` route group (empty) | — | `Program.cs` |
| Typed options + `ValidateOnStart` (Services, Cors) | `src/BuildMind.Infrastructure/Configuration/*Options.cs` | `Infrastructure/DependencyInjection.cs`, `appsettings.json` (non-secret defaults only) |
| `TimeProvider.System` registration | — | `Infrastructure/DependencyInjection.cs` |
| `ICurrentUser` abstraction + claims implementation (unauthenticated until JWT) | `Application/Abstractions/ICurrentUser.cs`, `API/Security/HttpContextCurrentUser.cs` | `Program.cs` |
| Launch settings (stable local port) | `src/BuildMind.API/Properties/launchSettings.json` | — |
| `UserSecretsId` | — | `BuildMind.API.csproj` |
| Tests | `tests/BuildMind.Infrastructure.Tests/Api/ProblemDetailsTests.cs`, `…/Configuration/OptionsValidationTests.cs`, `…/Security/CurrentUserTests.cs` | — |

**Requires additional approvals (separate):** CORS policy (C6 origin confirmation), OpenAPI (C7 package), JWT/auth (A1/A2, package `Microsoft.AspNetCore.Authentication.JwtBearer`), readiness (C11b; D04).

**Dependencies:** no new packages for the independent part (Problem Details, health checks, options validation, `TimeProvider` are in the shared framework / BCL; Infrastructure may need `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.12 for `ValidateOnStart` binding — verify at implementation and request approval).

**Sequence:** error codes → Problem Details/handler + tests → route group → options + validation + tests → `TimeProvider`/`ICurrentUser` + tests → launch settings/user secrets → docs.

**Security requirements:** no exception detail in responses (forced-exception tests); no secrets in `appsettings.json`; fail-fast options validation; identity only from claims.

**Test requirements:** existing 22 cases unchanged and passing; new tests for unknown route 404 Problem Details, forced exception 500 without internals, malformed JSON 400, options validation failure at startup, `ICurrentUser` unauthenticated behaviour; Linux CI is the execution path (G17).

**Acceptance criteria:** as §15; CI green; independent human review recorded (unlike PR #8).

**Risks / rollback:** behaviour change for error bodies (no consumers yet, low risk); options validation could block startup in misconfigured environments (mitigated by explicit defaults and tests); rollback = revert the D01c PR (no data or schema impact).

## 20. Lead decision record — 2026-11-18

**Decision owner:** Team lead (M4 + M7; GitHub `Kogulanathan69`). **Date:** 2026-11-18. **Source:** explicit lead instruction "STEP 03-G — Technical Decision Approval" (BE-M4-D01b). **No other member's approval is implied.** This record approves architecture and implementation *direction* only; it does **not** authorize any implementation, package installation, database work, CI change or readiness endpoint — each needs separate execution approval.

### 20.1 Approved architecture and implementation direction

| ID | State | Approved scope | Explicit boundary (not approved) |
|---|---|---|---|
| C1 | **APPROVED** | Public ASP.NET Core product API prefix `/api/v1`; ASP.NET Core route groups; root-level operational health endpoints; Angular communicates through the ASP.NET Core API | Internal `/internal/v1` convention stays **PROPOSED** until M6 and the relevant service owners confirm |
| C3 | **APPROVED** | RFC 9457 Problem Details; built-in ASP.NET Core error handling (`AddProblemDetails()`, `IExceptionHandler`); safe exception translation; stable error codes; trace identifiers; no sensitive internal details in public responses | — |
| C4 | **APPROVED** | HTTP 400 for malformed or structurally invalid requests; HTTP 422 for applicable domain-rule failures; shared validation error infrastructure | Exact feature diagnostics and consumer-facing validation schemas **PENDING** module-owner agreement |
| C5 | **APPROVED** | UPPER_SNAKE_CASE shared error codes; W3C Trace Context; `traceId` correlation; preserve feature-owned codes; correlation identifiers never treated as authentication evidence | C5b serialization contract **PENDING CONSUMER APPROVAL** |
| C8 | **APPROVED** | Strongly typed options; configuration validation; environment-specific settings; configured internal service destinations; no hardcoded secrets | Feature-specific configuration names and mandatory values need owner coordination; **unimplemented or optional integrations must not unnecessarily prevent the foundation from starting** |
| C9 | **APPROVED** | .NET User Secrets (local); GitHub Actions secrets for authorized CI needs; approved deployment secret management; secret-free example configuration files; no credentials in Git | Deployment secret store product not chosen (hosting open) |
| C10 | **APPROVED** | Built-in structured `ILogger`; trace correlation; sensitive-data exclusion and redaction; request/response body logging off by default; security audit records separate from operational logs | — |
| C11b | **APPROVED AS ARCHITECTURAL DESIGN** | Liveness independent of external dependencies; mandatory database unavailable → readiness 503; Python/RAG unavailable → instance-level Degraded where independent capabilities remain operational; a feature needing an unavailable dependency → 503; dependency timeout → 504; missing mandatory configuration never reported healthy; geometry validation and mandatory regulatory evidence never bypassed | **Readiness implementation not authorized** (pending its implementation task, BE-M4-D04) |

### 20.2 Conditional testing approval

| ID | State | Scope | Required before adoption |
|---|---|---|---|
| G03 | **CONDITIONAL TESTING APPROVAL — G03 OPEN** | Oracle `MySql.EntityFrameworkCore` (EF Core 10, .NET 10, MySQL 8.4 LTS, `dotnet-ef` local tool manifest) may be considered for the future BE-M4-D02 disposable-database spike | Commercial-use licence compatibility review; verified dependency versions; NuGet vulnerability audit; MySQL 8.4 compatibility, migration, transaction, concurrency and least-privilege tests; explicit lead acceptance. No installation, database creation, configuration change or spike execution is authorized by this record |

### 20.3 Recorded preferences and recommendations (not approvals)

| ID | State | Content |
|---|---|---|
| G18 | **PREFERRED STRATEGY RECORDED — G18 OPEN** | Option B preferred (use M3's corrected `FixtureLocator` after PR #9 is approved and merged; add the console runner to the solution and CI in a separately authorized slice; report console checks separately from xUnit). Option A fallback subject to M3 agreement; Option C long-term. **No M3 approval claimed**; M3 code untouched; PR #9 not merged |
| G20 | **RECOMMENDATION ONLY — G20 OPEN** | `IAiEditProposalPort` → M7 adapter; `IValidationFreshnessPolicy` → M6; `IApplicableInputRevisionSource` → M3 composite with M2 and M5 coordination. Not assigned without team agreement; existing feature interfaces preserved; no duplicate abstractions |

### 20.4 Still pending (not resolved by this record)

| Item | State |
|---|---|
| C2 consumer-facing HTTP response conventions | PENDING CONSUMER APPROVAL |
| C5b serialization contract | PENDING CONSUMER APPROVAL |
| C6 frontend CORS origin | PENDING CONSUMER APPROVAL (origin unverified; no Angular project in the frontend repository) |
| C7 OpenAPI and package | PENDING LEAD APPROVAL (package approval) |
| C12 detailed frontend integration contract | PENDING CONSUMER APPROVAL |
| A1 token storage; A2 password hashing strategy and ownership | PENDING (M1 + frontend + lead) |
| G01, G03 (adoption), G04 (remaining contracts), G09, G10, G11, G13, G17, G18, G19, G20 | OPEN |
| G14 Google Sheets automation | DEFERRED — must not block development |

### 20.5 Implementation and verification status

| Area | Implementation | Verification |
|---|---|---|
| `GET /health` liveness | Implemented (D01a) | VERIFIED — historical CI run 44074803430 (22/22 C#) |
| C1, C3, C4, C5, C8, C9, C10 | IMPLEMENTATION PENDING (BE-M4-D01c, not started; needs separate execution approval) | NOT EXECUTED |
| C11b readiness | IMPLEMENTATION PENDING (BE-M4-D04) | NOT EXECUTED |
| G03 provider | Not installed; spike not authorized | NOT EXECUTED |
| G18 runner | Not in solution/CI | NOT EXECUTED |

## 21. Implementation references — BE-M4-D01c (2026-11-18)

Status: **IMPLEMENTED — BUILD VERIFIED; TEST EXECUTION PENDING (Linux CI)** on `feature/m4-shared-infrastructure` (from develop `3f8f439`, uncommitted). Implements only the approved portions of C1, C3, C4, C5, C8, C9, C10 (§20). Not implemented: C2, C5b, C6 (CORS), C7 (OpenAPI), C11b readiness, C12, JWT/A1/A2, `/internal/v1`, database, external service calls.

| Decision | Implementation | Source |
|---|---|---|
| C1 | `/api/v1` route group; feature modules implement `IApiEndpointModule` and are mapped from DI (no `Program.cs` edits needed); no product modules registered; `/health` unchanged and unversioned | `src/BuildMind.API/Routing/*`, `Program.cs` |
| C3 | `AddProblemDetails` + `IExceptionHandler` (`GlobalExceptionHandler`) + `UseExceptionHandler` + `UseStatusCodePages`; every problem gets `type` (`urn:buildmind:problem:<code>`), `title`, `status`, `instance`, `code`, `traceId`; unsafe extension keys removed; fixed safe `detail` texts in all environments | `src/BuildMind.API/Errors/*` |
| C3 mapping | `BadHttpRequestException` keeps its 4xx; `UnauthenticatedAccessException` → 401; client-aborted request → 499 without body; all other exceptions (incl. `ArgumentException`, server-side `OperationCanceledException`) → 500 `INTERNAL_ERROR`. **No 504 mapping** (no approved dependency failure type yet) | `GlobalExceptionHandler.cs` |
| C4 | `ApiProblems.ValidationFailed` (400 `VALIDATION_FAILED` + `errors`), `ApiProblems.DomainRuleViolation` (422 + feature UPPER_SNAKE code + caller-named extensions; no diagnostic schema frozen) | `ApiProblems.cs` |
| C5 | `SharedErrorCodes` (14 approved codes) in Application; status→code defaults; W3C `Activity` trace ID with `TraceIdentifier` fallback; client correlation headers ignored | `Application/Abstractions/Errors/*`, `TraceIdentifiers.cs` |
| C8 | `ServicesOptions` / `InternalServiceOptions` (Infrastructure) with validation (absolute http/https, no credentials/query/fragment, timeout 1–300 s, URL required only when `Enabled`); bound and `ValidateOnStart` in the API composition root; disabled services never block start-up; no connections opened | `Infrastructure/Configuration/*`, `API/Configuration/*`, `ApiServiceCollectionExtensions.cs`, `appsettings.json` |
| C9 | `UserSecretsId` in `BuildMind.API.csproj`; no secrets created or stored; `appsettings.json` secret-free | `BuildMind.API.csproj` |
| C10 | Structured `[LoggerMessage]` logging with `TraceId`; outside Development the exception **type only** is logged (messages may hold secrets); logged paths use the escaped URI form (no CR/LF injection); framework `ExceptionHandlerMiddleware` diagnostics suppressed **only** when `GlobalExceptionHandler` handled the exception (`ExceptionHandlerOptions.SuppressDiagnosticsCallback`, matching the .NET 10 default) so handler failures still reach logs; console scopes on; no HTTP/body logging registered | `GlobalExceptionHandler.cs`, `ApiServiceCollectionExtensions.cs`, `appsettings.json` |
| Shared | `TimeProvider.System` (`TryAddSingleton`, Infrastructure); `ICurrentUser` (Application) + `HttpContextCurrentUser` (API, scoped) reading only authenticated identities, fail-closed on missing/malformed/empty/conflicting identifiers, no assumed roles | `Infrastructure/DependencyInjection.cs`, `Application/Abstractions/ICurrentUser.cs`, `API/Security/*` |
| Dev | `Properties/launchSettings.json` with **proposed** port `http://localhost:5080` (not confirmed with the frontend) | `src/BuildMind.API/Properties/launchSettings.json` |

**Dependency note:** binding options inside Infrastructure would need `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.12 (not installed, not approved); binding is therefore in the API composition root and no package was added.

**STEP 04-J security review corrections (2026-11-18):** (1) replaced the blanket `ExceptionHandlerMiddleware` log-category suppression with the narrow `SuppressDiagnosticsCallback`; (2) `ICurrentUser` takes identifier and roles from exactly one authenticated identity (no cross-identity role mixing; several identifier-bearing identities fail closed); (3) any HTTP 500 problem — including endpoint-supplied ones — is reduced to generic `title`/`detail`, `code` (well-formed feature code or `INTERNAL_ERROR`) and `traceId`; (4) `instance` and logged paths use the escaped URI form. Regression tests added (9 cases; 126 total).

**Known limitations:** non-500 problems keep endpoint-supplied `detail` and extensions — removing a few known-unsafe extension names is a backstop, not a guarantee, so endpoints must not put sensitive data there; if an exception occurs after the response started, the framework (not BuildMind) records its diagnostic including the exception; Production exception logs omit messages and stack traces, reducing diagnosability until a redaction approach is approved; problem bodies are written only when the client accepts JSON (`Accept` negotiation by the framework); claim names for `ICurrentUser` are provisional (M1).

**Delivery status (2026-11-18):** D01c merged to `develop` as `0a4d70a` (PR #11, squash). Test-only corrections after review/CI: `27d320d` (synthetic route rename) and `2d2a474` (start-up validation test lifecycle race). Final PR and post-merge CI runs passed; exact final counts not retrieved; independent review outstanding. Evidence EV-20261118-17..22.

**BE-M4-D04 delivery note (2026-11-18, uncommitted):** C11b readiness implemented as designed (`/health/ready`, `ready` tag; MySQL 503; Python/RAG Degraded until D05). C6, C7, A1/A2 still pending, so not implemented.

## 22. STEP 05 implementation proposal: C6 (CORS) and C7 (OpenAPI), 2026-11-21

**Status: PROPOSED IMPLEMENTATION. Not merged; C6 and C7 lead approval and the C7 package approval are still pending.**
- **Lead direction (STEP 05, 2026-11-21):** the CORS *design* (configuration-driven exact-origin allowlist; no wildcard; no unrestricted credentials; explicit methods and headers; environment-specific configuration) is approved. `http://localhost:4200` stays the *provisional* Development origin until the frontend confirms it.
- **Built and tested against `develop` `ed10592`:** full solution 1,579/1,579 C# tests and 413/413 contract checks (scratch run, 2026-11-21). The later Swagger UI test was run with its class only (21/21). CI on the PR is still required.

| Item | Implementation | Source |
|---|---|---|
| C6 policy | One named policy `BuildMindFrontend` from `Cors:AllowedOrigins`:<br>• exact `scheme://host[:port]` only;<br>• start-up fails on wildcards, paths, queries, fragments, user information or malformed entries;<br>• `http` accepted only in Development; methods `GET, POST, PUT, PATCH`;<br>• request headers `Authorization, Content-Type`; exposed `Location`;<br>• credentials off; preflight max-age 600 s | `API/Security/CorsConfiguration.cs` |
| C6 configuration | Development origin from the tracked `Properties/launchSettings.json` (`Cors__AllowedOrigins__0`). `appsettings.Development.json` is git-ignored by design, so it is not used | `launchSettings.json` |
| C6 order | `UseRouting` → `UseCors` (before any future authentication, so preflight is answered without credentials) | `Program.cs` |
| C7 | Swagger document `/swagger/v1/swagger.json` and UI `/swagger`, **Development only** (not registered or mapped elsewhere; Production returns 404). A bearer scheme is declared only when end-user authentication exists (not on `develop`) | `API/Documentation/SwaggerConfiguration.cs` |
| Tests | 20 (`CorsAndSwaggerTests`) plus a test host that applies settings before `Build()` (`StartupSettingsApiFactory`) | `tests/BuildMind.Infrastructure.Tests` |

**Deviations from the C6/C7 recommendations above (§6, §7, §15), for the lead's decision:**
1. **C7 package:**
   - The recommendation was option A: first-party `Microsoft.AspNetCore.OpenApi` at `/openapi/v1.json`, no UI.
   - The proposal uses **option B: Swashbuckle.AspNetCore 11.0.1**, carried over from the lead's local Swagger work, because it provides the UI used for manual endpoint testing.
   - Option A needs a package download that has not been approved (it is not in the local cache), so it is not built.
   - Alternatives: A (document only), A plus a UI-only package, or B.
2. **C6 empty list:** the recommendation was "start-up fails on an empty list outside Development". The proposal registers **no CORS policy** when no origin is configured, so the API is same-origin only. This is safer than an empty policy and keeps hosts that have no browser frontend (tests, internal tools) working. Making origins mandatory outside Development can be added when hosting is decided (G11).
3. **C6 methods and headers:**
   - `DELETE`, `Idempotency-Key` and `traceparent` are **not** allowed yet: no current endpoint uses them (Decision 6: explicit *required* methods and headers).
   - They are added when an approved endpoint needs them (C2/C5b idempotency).

## 23. Host-header filtering (STEP 05, lead decisions H1–H4 and H8, 2026-11-21)

**Status:** implemented on `feature/m4-allowed-hosts` (not merged). Closes the host-filtering part of QA finding S3.

| Decision | Implementation |
|---|---|
| H1 default list | `appsettings.json`: `"AllowedHosts": "localhost;127.0.0.1;[::1]"` (the framework host-filtering middleware; ports are not part of the match) |
| H2 start-up validation | `API/Configuration/AllowedHostsValidation.cs` (`IValidateOptions<HostFilteringOptions>` + `ValidateOnStart`). Outside Development (Testing, Staging, Production, any other environment), start-up fails when the key is missing or empty, because the framework would otherwise fall back to `*`; when it contains `*`; or when an entry has a scheme, port, path, query, fragment or user information, or is not a valid host name or IP (IPv6 must be bracketed) |
| H3 | No subdomain wildcards (`*.domain`) outside Development |
| H4 | Development is unrestricted: a developer may set `AllowedHosts` to `*` or add names through an environment variable or user secrets (runbook §5a) |
| H8 | The framework's standard rejection is kept: **400**, a static 334-byte HTML page "Bad Request - Invalid Hostname". It is written before BuildMind's middleware, so it carries no BuildMind security headers, but it reflects no input and reveals no application detail |

- **Deployment host names are not configured:** Staging and Production must supply them through deployment configuration (G11).
- **Interplay with CORS:** host filtering runs before CORS, so a request to an unlisted host is rejected before any CORS evaluation. `AllowedHosts` lists the API's own names; frontend origins stay in `Cors:AllowedOrigins`.
- **Deferred to G11:**
  - proxy topology and forwarded headers. Recommended: the proxy preserves `Host`; only `X-Forwarded-For`/`X-Forwarded-Proto` are accepted, and only from known proxies;
  - HTTPS redirection, HSTS and certificates;
  - the Host used by health probes.
- **Tests:** `HostFilteringTests` (40).

## 24. Request-body size limit (STEP 05 S4-A, lead decisions S4-1..S4-4, 2026-11-21)

**Status:** implemented on `feature/m4-request-size-limit` (not merged). Rate limiting (S4-B) is a separate change.

| Decision | Implementation |
|---|---|
| S4-1 global limit | Kestrel `Limits.MaxRequestBodySize` = **1 MiB** (1,048,576 bytes). The value is in `appsettings.json` (`Kestrel:Limits:MaxRequestBodySize`) **and applied explicitly** in `ApiServiceCollectionExtensions` (`RequestBodyLimitValidation.ReadConfiguredLimit`). The host does **not** bind `Kestrel:Limits` from configuration by itself: without the explicit code Kestrel kept its 30,000,000-byte default (verified 2026-11-21). An absent or empty key means 1 MiB, never 30 MB; a non-numeric value stops start-up |
| S4-2 larger bodies | Only per endpoint (request-size metadata on that endpoint), proposed by the owning member and approved by M4. Never by raising the global value |
| S4-3 ceiling | `RequestBodyLimitValidation` (`IValidateOptions<KestrelServerOptions>` + `ValidateOnStart`). Outside Development the limit must be set and in 1 byte .. 8 MiB. In every environment 0 is refused. Development is otherwise unrestricted |
| S4-4 connections | `MaxConcurrentConnections` unchanged (unset) until G11 |

**Observed runtime behaviour** (real Kestrel, real composition root):
- A body of exactly 1,048,576 bytes is accepted.
- **1,048,577 bytes** → **413**, `application/problem+json`, `code: PAYLOAD_TOO_LARGE`, `type: urn:buildmind:problem:payload-too-large`, with `traceId` and the BuildMind security headers (`nosniff`, `DENY`, `no-referrer`). The body is not echoed.
- A chunked body (no `Content-Length`) over the limit → the same 413.
- The limit applies **when the body is read**: an oversized body sent to a route that does not read it gets that route's normal answer (e.g. 405).

**Compatibility risks:**
- **M2 uploads** (survey PDFs and images) cannot rely on Kestrel's old ~28.6 MiB default. Each upload endpoint needs its own approved limit, streaming to storage, and multipart/form caps. Designed with M2.
- **M3 full-plan payloads:** the geometry contracts allow up to tens of thousands of walls and openings, which could exceed 1 MiB. If a public M3 endpoint needs more, it gets an endpoint-specific limit.
- **Reverse proxy (G11):** the proxy body limit (e.g. `client_max_body_size`) must be at most the API limit, and must mirror per-endpoint raises.
- **Clients:** no current client is known to send more than 1 MiB.

**Tests:** `RequestBodyLimitTests` (30). The Kestrel cases use `WebApplicationFactory.UseKestrel`, because TestServer does not apply Kestrel limits.

## 25. Rate limiting (STEP 05 S4-B, lead decisions R1–R9, 2026-11-21)

**Status:** implemented on `feature/m4-rate-limiting` (not merged). Code in `API/Security/RateLimitingConfiguration.cs`; uses the built-in ASP.NET Core rate limiter (no package).

| Item | Implementation |
|---|---|
| Middleware | A single `app.UseRateLimiter()` in `Program.cs`, after `UseRouting` and `UseCors` |
| Future order with authentication | `UseAuthentication` → `UseRateLimiter` → per-request account-state check → `UseAuthorization`. Feature modules must not call `UseRateLimiter` |
| Global limiter | A chained partitioned limiter evaluated once per request (see the chain below) |
| `auth-strict` | Named policy: fixed window per peer address, 10/min, queue 0. Attached by endpoint owners with `.RequireRateLimiting(RateLimitingConfiguration.AuthStrictPolicy)` |
| Expensive operations | Endpoint marker `.RequireExpensiveOperationLimit()` |
| Exemptions | `/health` and `/health/ready` (partition guard). Swagger is answered before routing and never reaches the limiter. CORS preflights are answered before the limiter |
| Rejection | 429; `Retry-After` from the limiter metadata, rounded up and at least 1 s; for concurrency rejections a fixed 2 s. The body is the status-code-pages Problem Details (`RATE_LIMITED`, `traceId`, security headers) |
| Logging | Endpoint name, trace id and retry seconds only: no IP, user id, query or body |
| Validation | Outside Development: rate limiting cannot be disabled, and `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is refused (it trusts forwarded headers from any sender; shown by a Development test where spoofed `X-Forwarded-For` values each got a new bucket). All counts are bounded; per-user concurrency ≤ global; IPv6 prefix 48..128; ceiling ≥ per-IP rate |

**The global chain, in order** (the first rejection stops the chain, and leases already taken are released):
1. **Anonymous ceiling:** one shared token bucket, 3,000/min.
2. **Per client:** a token bucket keyed by validated user (`sub` via `HttpContextCurrentUser.ResolveFrom`), 300/min with burst 300; otherwise by TCP peer, 120/min with burst 120. Peer keys:
   - IPv4 per address;
   - IPv4-mapped IPv6 normalized to IPv4;
   - IPv6 grouped by /64;
   - no address → one shared bucket.
   - Forwarded headers are **never** read.
3. **Expensive operations, per client:** concurrency 2.
4. **Expensive operations, global:** concurrency 8.

**Memory evidence (tests):**
- Because the ceiling is first, a rejected anonymous request never creates a per-client partition. 2,000 distinct addresses inside one ceiling window created 60–62 partitions, not 2,000.
- Idle partitions are disposed by the framework about 10 s after their limiter becomes idle. Measured on .NET 10.0.15: 5,000 idle partitions → 0 between 10 and 11 s. A test shows a partition being recreated after idling.

**Limitations:**
- State is in memory per instance (multiple instances multiply the limits).
- Behind a proxy without trusted forwarding, all anonymous clients share the proxy's bucket (G11).
- A token taken by an earlier limiter in the chain is not refunded when a later limiter rejects. Concurrency permits are released.
- Request-level concurrency does **not** bound background jobs (M6 must bound its own job execution).

**M1 integration requirements (handover):**
- remove M1's own `UseRateLimiter()` and its `M1Authentication` policy;
- attach `auth-strict` to register, login, refresh, forgot-password and reset-password;
- add per-account failed-login throttling in the M1 service;
- keep the order above.

**Tests:** `RateLimitingTests` (40), including real-Kestrel cases.

# Security and nonfunctional controls

Threat model: account takeover, IDOR, uploaded malicious documents, unsafe path traversal, secret leakage, SQL injection, stale edits, arbitrary internal URLs, prompt injection, unauthorized report sharing, denial of service. Enforce server-side ownership checks on every resource, not only UI guards. JWT signature/issuer/audience/expiry; short-lived single-use hashed recovery tokens; rate-limit auth; generic forgot-password response; role/module policies; never log secrets or password hashes. CORS exact configured origins. HTTPS for public environments; private Python service access authenticated, fixed allowlisted base URLs. Do not forward arbitrary user URLs or private credentials.

Upload: allowlist formats PDF/JPG/PNG only per approved contract, content signatures, configurable limits, generated private storage IDs, scan/quarantine policy, safe temp directory, isolated parser/OCR with time/resource bounds, no raw public paths, authorized downloads. Report data snapshot and recipient consent; expiring access if approved. No real customer files in public repo, fixtures synthetic. Database least privilege, secrets via local secret store/environment/deployment vault, backups and restore test. Security audit history is separate from UI visibility. Limit model input/output and treat retrieved text as untrusted; no silent edits. Never expose internal Vastu heuristic. Service errors map to safe Problem Details. Review licenses, storage retention, data residency and production deployment security before release.

## Implemented controls — BE-M4-D01c (2026-11-18; merged `0a4d70a`, CI passed, independent review outstanding)
- Public errors: fixed safe `detail` texts; no exception message, stack trace, SQL, connection string, path or internal URL in any environment; unsafe extension keys stripped; covered by Development and Production tests with synthetic secret markers.
- Identity: `ICurrentUser` uses only authenticated identities' claims; headers, query strings, bodies and unvalidated bearer values are ignored (spoofing test); missing/malformed/empty/conflicting identifiers fail closed; roles never assumed. **No authentication handler exists yet** (JWT, A1, A2 pending).
- Configuration: internal service URLs validated (http/https, no embedded credentials/query/fragment); validation messages never echo values; no secrets in `appsettings.json`; User Secrets ID configured, store not populated.
- Logging: trace-correlated structured logs; Production logs exception type only; escaped paths (no log-line injection); framework exception-middleware diagnostics suppressed only for exceptions BuildMind's handler handled (handler failures remain visible); no request/response body logging.
- HTTP 500 problems are always reduced to generic fields, even when an endpoint supplies its own detail/extensions; other statuses rely on endpoints keeping `detail`/extensions free of sensitive data.
- Current user: identifier and roles come from exactly one authenticated identity; claims of unrelated identities are never combined.
- Not yet implemented: CORS, OpenAPI exposure control, rate limiting, readiness, security audit store.

### Test reliability note — start-up validation (2026-11-18, `2d2a474`)

Start-up failure tests must prove the failure is an `OptionsValidationException`. With minimal hosting, `WebApplicationFactory` can surface `ObjectDisposedException` instead (start-up race). That exception is accepted only when the host itself recorded an `OptionsValidationException`; the credential-leak logging test also requires the host failure entry to be captured, so it cannot pass with empty logs.

### Database controls — BE-M4-D02a (2026-11-18, uncommitted)

- Connection strings are never committed: they come only from `ConnectionStrings:BuildMind` or `BUILDMIND_MIGRATIONS_CONNECTION` (User Secrets or environment variables). Validation messages name keys only; `root`, `Persist Security Info` and `Allow Load Local Infile` are rejected.
- No automatic database creation or migration. Least-privilege accounts are proposed: `bm_runtime` (DML only), `bm_migrator` (DDL on `buildmind_dev`), `bm_test` (`buildmind\_test\_%` only).
- EF sensitive-data logging and detailed errors are off. EF failure events that quote provider messages (which can contain stored values such as emails) are suppressed. The error classifier keeps only kind, error number and constraint name.
- A commit failure is reported as an unknown outcome and never retried.

- (STEP 05-C) Database TLS is mandatory: `SslMode` must be Required, VerifyCA or VerifyFull (production: VerifyFull), and the proposed accounts `REQUIRE SSL`. After a rollback or an unknown commit, tracked entities are discarded.

- (D02b) Server identity: `SslMode=VerifyFull` is required outside Development and for any non-loopback server. A rollback that can't be confirmed is reported as such, never as a clean rollback. CI workflow token is read-only and runs on `pull_request` only; database CI credentials are random per run, masked and never uploaded.

- (D02b) Timestamps are stored as unambiguous UTC (`datetime(6)`); non-UTC `DateTime` values are rejected. Migrations: single authorized executor, history check, backup before DDL, no automatic or production migrations.

- (D03) Repositories filter every query by project; ports are not authorization guards, so callers must establish ownership first. Constraint violations are translated without values (for example a duplicate email); stored customer content is never echoed in errors. FKs use RESTRICT (no cascading deletes).

- (STEP 06-F) Risk noted for M1: `IUserRepository.UpdateAsync` has no concurrency check, so concurrent profile/password/activation updates would be last-writer-wins. Decide before the user repository is implemented. Password hashes are stored only as provided (`password_hash` varchar(512)); hashing (A1/A2) is not implemented by M4.

### Implemented controls — BE-M4-D04 (2026-11-18, uncommitted)

- **Readiness** `GET /health/ready` (C11b): MySQL mandatory (503 when not configured or unreachable); Python/RAG Degraded and never Healthy until real checks exist (D05). Response = names, statuses and fixed reason codes; no hosts, users, exception text or connection details. `/health` liveness unchanged and dependency-free.
- **Response headers** on every response (including errors): `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`; `/api` responses default to `Cache-Control: no-store`. Kestrel `Server` header disabled.
- **Log hygiene regression:** bearer tokens, cookies, API keys and query-string tokens never appear in logs (tested in Production and Development). Precondition: `Microsoft.AspNetCore` stays at `Warning` (request-start logs include the query string), and **tokens must never be sent in query strings**.
- **Not yet in place (blocked):** CORS allowlist (C6), authentication/JWT and shared authorization policies (A1/A2, M1), request body limits (M2; Kestrel's 30 MB default applies), OpenAPI (C7, Development only). Until authentication exists, `ICurrentUser` is always anonymous and no endpoint may rely on it for ownership.

- (STEP 07-B) EF Core `ConnectionError` events are suppressed (they logged the server host and database name). MySQL readiness enforces its own 3 s deadline and runs at most one probe at a time per host; abandoned probes dispose their scope when they finish. The Kestrel `Server` header setting does not control headers added by a reverse proxy or load balancer.

- (D05) Internal calls: destinations only from the validated configuration (no user-supplied URLs, relative-path SSRF guard); no redirects followed; no cookies; user `Authorization`/`Cookie` never forwarded; bounded time and response size; failures carry no URL/host/payload; framework HttpClient request logs removed (they include internal URLs). **No internal service authentication yet (G11)**: Python must stay network-private until it is decided.

- (STEP 08-B) Internal paths are allowlisted (`[A-Za-z0-9_-]` segments; no encodings, dot segments, query or fragment); private traffic bypasses system proxies; ≤32 connections per internal service; Python readiness has a hard deadline and single-flight. **Configuration and DNS are the trust boundary for internal destinations** (no claim of complete SSRF immunity). Python's FastAPI `/docs` and `/openapi.json` are exposed by default: to be disabled outside development (M6/G11).

- (STEP 08-C) Internal calls are bulkheaded (≤64 in-flight + queued per dependency, fail fast), so a slow dependency cannot accumulate unbounded waiting requests. Readiness single-flight gates are always released, even if cleanup throws. The CI Python service binds to 127.0.0.1 only and is stopped in an `always()` step.

- (STEP 09-A) Escaped database failures are logged as EventId 1006 with the failure kind, provider error number and schema constraint name only; never the provider message, SQL, values or connection string. Problem bodies carry fixed text. An architecture test forbids HTTP or internal-service dependencies in persistence types (no external calls inside transactions).

- (STEP 09-B) Database failures are never logged with the exception object, **including in Development** (EventId 1006 warning, 1007 error), because provider messages can contain SQL and values. Commit or rollback uncertainty anywhere in an exception chain is always 500 and never a conflict or outage. Cancellation is never classified as a database failure. The persistence architecture test is structural only and cannot detect indirect calls.

- (STEP 10-A) Readiness probes abandoned at their deadline are now always cancelled, so a hung dependency cannot pin the single-flight gate. Open security findings S1–S8 (no authn/authz, no CORS, `AllowedHosts: *`, no HTTPS/HSTS, no request size or rate limits, Actions not SHA-pinned, FastAPI docs exposed, G11, 0 reviews) are recorded in [25](25-M4-FINAL-QA-REPORT.md). None is simulated.

- (STEP 10-B) Abandoned readiness probes are cancelled with `CancelAsync()`: cancellation callbacks never run on the readiness thread, so a blocking callback cannot delay `/health/ready`. Callback failures are dropped, never logged.

- (STEP 10-E) MySql.Data 26.10.0 TLS-state race (static dictionaries mutated outside the provider lock). Mitigated by serializing connection opening process-wide. **TLS is never disabled or weakened as a workaround**: the existing TLS policy is unchanged: `DatabaseOptions` validation requires `SslMode=VerifyFull` in every non-Development environment and for any non-loopback server; `Required` is accepted only for a loopback server in Development/tests. The CI test accounts use TLS-only (`REQUIRE SSL`) loopback connections. The only `SslMode=Disabled` uses are pre-existing negative tests that expect refusal. The live test SSL01 asserts a negotiated cipher on every parallel connection (wording corrected in STEP 10-F).

- (STEP 10-H) **Availability risk R-3 (open):** with the strict process-wide open gate, a single stalled MySQL handshake blocks all new database connections until it ends (waiters fail with 503 after `ConnectionTimeout`). TLS, retries and timeouts were not changed to hide it. The design decision is deferred by the lead.

- (M7 MVP) `rag-service`: internal only (loopback or private network; G11 open). Interactive docs and OpenAPI are disabled; bodies are limited to 16 KB; validation errors don't echo input; unexpected errors are a generic 500 with only the exception type logged. The corpus stays outside the public repo and is identity-verified by SHA-256 at start-up. The C# client rejects any response claiming verified legal status, a generated answer, unsupported passages or path-bearing source titles. Retrieved text, including embedded instructions, is treated as data and never executed or followed.

- (M7 phase 02) RAG readiness reports reasons only: no URL, exception text or response body. Its probe is bounded to 3 s even if the transport ignores cancellation, and it has its own single-flight gate, separate from Python and MySQL.

- (G11 MVP) rag-service requires the internal shared-secret token on every path except `/health` (default-deny, constant-time compare, two-token rotation, fail closed: 503 when required but unconfigured). C# sends it only to the validated base URL, never as `Authorization`, and never logs it. Outside Development an enabled RAG without a token fails start-up. See doc 31.

- (M6-review corrections) Strict raw-JSON validation before deserialization on both sides; duplicate JSON properties rejected (API input, internal responses, rag-service requests); repeated internal-token headers rejected; tokens refused over plaintext to network hosts outside Development; constraint tables refused unless every citation reconstructs from the hash-verified corpus.

- (Constraint integrity) Evidence packs are verified by independent BM-JCS-1 digest recomputation. Constraint tables and reviewer registries are integrity-sealed and pinned. Reviewer authorization is enforced at load. Synthetic evidence is isolated to Development/Testing. Production has no plaintext internal transport and no exception flag.

- (Cross-member integration) Internal C# contracts are exact-case and strictly typed. rag-service never reflects submitted names or values in 422 responses. The RAG auth opt-out requires a second, explicit development declaration. The test-only acceptance service cannot run without a token.

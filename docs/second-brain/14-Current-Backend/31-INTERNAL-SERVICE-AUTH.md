# Internal service authentication (G11, MVP scope)

- **Decision (lead, explicit in-session answer, 2026-11-19):** a **per-service shared-secret token**, implemented now for **rag-service + the C# client**.
- **python-services** gets the same mechanism once the M6 bootstrap custodian agrees (it is shared M2/M3/M6 code).
- mTLS and signed JWT were considered and deferred. G11 **transport and hosting** remain open.

## Design
| Aspect | Rule |
|---|---|
| Header | `X-BuildMind-Internal-Token`. Never `Authorization`, so user credentials are never forwarded to internal services |
| Secret source | Environment variables or user secrets only: `Services__Rag__AuthToken` (C#), `BUILDMIND_RAG_INTERNAL_TOKENS` (rag-service, comma-separated, at most 2 for rotation) |
| Format | 32–256 printable ASCII characters, no whitespace (header-safe, no CR/LF injection). Validated at start-up on both sides; validation messages never echo the value |
| Sending (C#) | Added by the hardened `InternalServiceClient` only to the validated base URL (allowlisted path, no redirects, no proxy); framework HTTP logging is removed for internal clients |
| Checking (Python) | Middleware, **default-deny**: every path except `/health` needs the token; checked **before** routing, validation and size limits; `hmac.compare_digest` against every accepted token |
| Fail closed | **C#:** outside Development, an enabled RAG without a token fails start-up validation. **rag-service:** authentication is required unless `BUILDMIND_RAG_AUTH=disabled` is set explicitly (development only); required with no token configured returns **503 `AUTH_NOT_CONFIGURED`**, never open access |
| Errors | `401 {"code":"UNAUTHENTICATED"}` with no detail; tokens never logged or echoed |
| Rotation | (1) configure the new token as the second accepted value in rag-service; (2) switch `Services__Rag__AuthToken` in the API; (3) remove the old value |
| `/health` | Open: it returns only identity, contract version and corpus state, for readiness probes |

## Evidence (2026-11-19)
- **rag-service:** 24 new auth tests, **68/68** total locally:
  - missing, wrong, truncated, extended and case-changed tokens → 401;
  - both rotation tokens accepted;
  - `/health` open;
  - authentication runs before validation and size checks;
  - default-deny on unknown paths, `/docs` and `//internal/...`;
  - fail-closed 503;
  - explicit development opt-out only;
  - invalid token configuration rejected without echo.
- **Real-corpus suite with authentication:** 6/6.
- **Live loopback run (real corpus, random tokens):** no token → 401; wrong token → 401; token 1 → 200 `EVIDENCE_FOUND`; token 2 → 200; the token never appears in the server log.
- **C#:** `InternalServiceAuthTests`, 10 cases, compiled (they run in CI):
  - the header is sent only when configured and never as `Authorization`;
  - unsafe tokens are rejected without echo;
  - an enabled RAG requires a token only outside Development;
  - Production start-up fails closed without a token;
  - Development starts without one;
  - the token never appears in logs.

## Limits (not solved by this MVP mechanism)
- **No transport encryption:** loopback or a private network is still required. TLS and hosting are G11-open.
- **A shared secret is not caller identity.** Anyone holding the token can call; protect it like a password.
- **python-services is not protected yet.** It needs the M6 custodian's agreement.
- No replay protection beyond the token itself (a signed short-lived JWT would add it, as a future option).

## Hardening (2026-11-19, M6-review corrections)
- **Repeated token headers** are rejected (401): proxies and frameworks disagree on which copy wins.
- **Transport enforcement (outside Development):** a service with `AuthToken` must use `https` or a loopback address. Plaintext to a network host fails start-up unless `Services:<Name>:AllowPlaintextPrivateNetwork=true` is set explicitly, as a reviewable exception until hosting/TLS (G11) is decided.
- **Strict internal JSON:** responses are validated raw before deserialization (duplicates, comments, trailing commas, depth, object root). Unknown members are rejected; API input rejects duplicate properties (400).
- **Verified by tests:** rotation (Python), timeout and cancellation (C# constraint client), and safe 503/504 mapping.

## Production transport and synthetic isolation (2026-11-19, constraint integrity)
- **Production:** every enabled internal service (token or not) must use `https` or a loopback address; `AllowPlaintextPrivateNetwork=true` fails start-up. Staging keeps the explicit exception until G11.
- **Synthetic constraint evidence:** `Services:Rag:AllowSyntheticConstraints` only in Development/Testing (`Testing` environment name); never for Python.
- **python-services authentication:** coordination item with the python-services maintainer (M6 bootstrap owner). The C# transport rules already cover Python.

## RAG auth opt-out needs two settings (2026-11-19, M6 H4-R1 C09)
`BUILDMIND_RAG_AUTH=disabled` is honoured **only** together with `BUILDMIND_RAG_ENVIRONMENT=development`; otherwise rag-service refuses to start. Any other mode value still means authentication is required. Validation errors return a fixed body (`{"code":"VALIDATION_FAILED"}`) and never reflect submitted names or values.

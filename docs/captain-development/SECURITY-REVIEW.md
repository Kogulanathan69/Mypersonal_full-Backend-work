# Security review — Captain backend (Development profile)

- **Reviewed tree:** commit `86b9593`.
- **Method:** code reading of every Captain-added path, plus executed negative tests (unit, live MySQL, live journey phases 2–8).
- **Not covered:** this is not a penetration test or an independent audit. G19 (independent review) remains open. **Production: NO-GO.**

## Controls verified

| Area | Control | Evidence |
|---|---|---|
| Authentication | JWT validation and an active-session/account check on every product route. The Captain profile refuses to start without authentication or outside Development. | Journey: invalid JWT → 401, revoked session → 401, `/users/me` without a token → 401 |
| Sessions | Refresh rotation with reuse detection; users-first lock order (no deadlock) | Live S08/S09; journey phase 7 |
| Ownership | Fresh owner + authorization-version check per request. Missing and foreign resources both return 404 (no enumeration). | 30+ cross-user journey checks, all 404 |
| Protected writes (A1–A8) | Project row `FOR UPDATE` + owner + authorization version, inside one transaction, for every Captain write. This includes survey extraction publication and `POST …/versions`. | Live tests (Q04: revoked owner → `PROJECT_ACCESS_REVOKED`, nothing stored) |
| Background work | The worker never bypasses authorization: it rechecks the uploader's ownership before reading the file and again at publication | Q04, code |
| Concurrency | Atomic CAS claims with a lease token. A stale claimer cannot publish. Bounded attempts. | Q01 (6 racers → 1 winner), Q02, Q03 |
| File handling | Private storage outside the web root, GUID-only paths, reparse points refused, exact length + SHA-256 before use, 6 MiB cap. The original file name is never used as a path. | P01 (missing/corrupt/truncated → fail closed, Python not called) |
| Private documents | Survey bytes go only to the loopback python-services, authenticated with the internal token. No external OCR or AI provider. OCR runs on local Tesseract in a temp dir that is deleted afterwards. | Code; `ocr.py` |
| Logging | No document content, tokens, passwords or connection strings in logs. Worker logs carry only outcome labels and survey ids. OCR errors use fixed messages. | Python test `test_ocr_failures_never_leak_document_content`; code |
| Input validation | Strict JSON (unmapped members rejected, case-sensitive for M3 DTOs); 415 for non-JSON; body size caps (python-services 9 MiB for extraction, 2 MiB otherwise) | Journey phase 7; Python tests |
| Internal service auth | `X-BuildMind-Internal-Token`, fail closed (missing configuration → refuse to serve) | Python tests |
| Database TLS | `SslMode=VerifyFull` against the Captain CA, with a client certificate | `env.sh`; live suite |
| Rate limiting | Strict auth limiter; expensive-operation limiter on generation, report generation, regulation retrieval and AI proposals | Journey: 429 without any 5xx |
| Truthfulness | No AI provider → 503, never a fabricated proposal. INFEASIBLE generation → 422 with no candidates. Synthetic evidence is labelled `SYNTHETIC_FIXTURE`. Extraction values are labelled unverified, and OCR is always NEEDS_REVIEW. | Journey phases 3 and 8 |
| Geometry | The server re-validates every saved geometry. Client-supplied validation is never trusted. The survey never becomes a polygon automatically. | Journey: out-of-parcel version → 422 |

## Findings and residual risks

| ID | Severity | Finding | Status |
|---|---|---|---|
| SR-1 | Medium (production) | RH09 provider hang on command timeout (MySql.Data) | DEFERRED, blocks production |
| SR-2 | Low | M2 upload: the blob is deleted when the commit outcome is unknown, so metadata may reference a missing file | Fail-closed (`SURVEY_FILE_MISSING`); M2-owned code; documented |
| SR-3 | Low | The worker's poll query is not project-scoped (by design; it is a system process). It processes only rows whose owner re-validates. | Accepted |
| SR-4 | Medium (production) | The internal shared token is a static secret; no mTLS between services (G11) | Loopback only; production NO-GO |
| SR-5 | Info | OCR (Tesseract/pdftoppm) parses untrusted files: bounded 60 s per page, ≤10 pages, isolated temp dir, same OS user as python-services | Production needs sandboxing (separate user/container) |
| SR-6 | Info | CORS is empty because no approved origin exists | Documented in FRONTEND-INTEGRATION-GUIDE |
| SR-7 | Fixed this round | The `/users/me` alias was first added to the base M1 module, changing the ratified route inventory. The pinned test caught it. | Moved to the Captain module; the M1 inventory is unchanged |

There are no secrets in the repository: `~/bm-captain/secrets` is outside the repository with mode 0700, and the OpenAPI export was scanned for secrets (0 hits).

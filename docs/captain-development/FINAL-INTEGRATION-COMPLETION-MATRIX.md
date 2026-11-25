# Final integration completion matrix

Each row is a cross-component boundary. **Live** means it was exercised by real processes, with no mocks, in the final runs at HEAD `a69086a`:
- process stack: journey 202/202, admin 65/65, mTLS 12/12;
- local containers: 132/132 and 110/111;
- local LLM: 10/10.

This supersedes INTEGRATION-COMPLETION-MATRIX.md.

| From → To | Mechanism | Auth / TLS | Failure behaviour | Status | Verified |
|---|---|---|---|---|---|
| Angular client → API | JSON `/api/v1`; OpenAPI 127 ops; generated TypeScript declarations (`contracts/buildmind-api.d.ts`, strict tsc) | JWT + active session; server-side owner/role checks | RFC 7807 with stable `code` | IMPLEMENTED (contract) | Contract only: no frontend in this repository. Response bodies are not declared in OpenAPI. |
| API → MySQL 8.4 | EF Core 10 / MySql.Data 26.10.0 | `VerifyFull` + client certificate; `bm_runtime` DML only | readiness unhealthy → 503; server containment 25 s / 20 s | IMPLEMENTED; RH09 PARTIAL | LIVE (persistence suite + journeys + containers) |
| API → python-services | HTTPS JSON, strict schemas | **mTLS** (server-CA-only trust, hostname check) + internal token | 503 `DEPENDENCY_UNAVAILABLE` / 504 | IMPLEMENTED | LIVE (mTLS 12/12, journey 7 outage, containers) |
| API worker → python-services (survey extraction) | same | same | lease/attempt-bounded retries, stable codes | IMPLEMENTED | LIVE |
| API → python land frame (CRS) | `POST /internal/v1/land/frame` | same | `{valid:false, code}`; digest mismatch → dependency failure | IMPLEMENTED | LIVE (journey 10) |
| python-services → local OCR | subprocess, temp dir, timeouts | local | `OCR_UNAVAILABLE` / `OCR_FAILED` → manual review | IMPLEMENTED | local tests |
| API → rag-service | existing M7 `RagServiceClient` (unchanged) | internal token (no mTLS: STEP 8B) | fail closed (503, no passages) | BLOCKED_PROTECTED_SCOPE (corpus) | LIVE fail-closed only |
| rag-service → approved corpus | manifest identity validation | — | refuses to load | BLOCKED_PROTECTED_SCOPE | Root cause diagnosed (REAL-REGULATION-RAG-REPORT) |
| API → LLM (OpenAI-compatible) | `/v1/chat/completions`, strict JSON schema | loopback; optional bearer key file | 503 `AI_PROVIDER_UNAVAILABLE`; 422 specific reason | IMPLEMENTED (local); external BLOCKED_EXTERNAL (I-1) | LIVE local (10/10) |
| API → SMTP | transactional outbox → `SmtpEmailSender` | loopback capture, or external with STARTTLS required | retry with backoff; business state unaffected | IMPLEMENTED; external BLOCKED_EXTERNAL (I-2/I-3) | LIVE to Mailpit only |
| API → private file storage | directories outside web root, GUID paths | OS permissions (0700; container volume uid 1654) | fail closed | IMPLEMENTED | LIVE (incl. container restart persistence) |
| Architect ↔ customer | in-app portal, consent and withdrawal, notifications | role + consent checks | 404 after withdrawal | IMPLEMENTED | LIVE (journey 9) |
| Container orchestration | Compose v5.7.0 `compose.verify.yml` | internal network; loopback publish | health-gated start; restart persistence | IMPLEMENTED (local) | LIVE local |
| Any → production infrastructure | — | — | — | not touched | Production **NO-GO** |

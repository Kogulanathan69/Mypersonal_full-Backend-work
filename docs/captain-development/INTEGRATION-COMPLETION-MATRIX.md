# Integration completion matrix

Each row is a cross-component boundary in the local Development stack. "Live" means it was exercised by the real processes (no mocks) during the final journey run (136/136) at commit `86b9593`.

| From → To | Mechanism | Auth / TLS | Failure behaviour | Verified |
|---|---|---|---|---|
| Angular client → API | HTTPS/HTTP JSON, `/api/v1` | JWT + active session | RFC 7807 with stable `code` | Contract only (OpenAPI export); no frontend in this repository |
| API → MySQL 8.4 | EF Core 10 / MySql.Data 26.10.0 | `VerifyFull` + client certificate; `bm_runtime` DML only | Readiness unhealthy; 503 | Live (103 live tests + journey) |
| API → python-services (land, generation, plan validation, edit preview) | Loopback HTTP JSON, strict schemas | Internal token (fail closed) | 503 `DEPENDENCY_UNAVAILABLE` / 504; readiness shows `python` unhealthy | Live (journey 3/4/7/8) |
| API worker → python-services (survey extraction) | Loopback HTTP JSON (base64 file, SHA-256) | Internal token | Deferred (lease expiry, max 3 attempts) or failed with a stable code | Live (journey 8) + live DB tests |
| python-services → local OCR (Tesseract 5.3.4, pdftoppm) | Subprocess, temp dir, timeouts | Local only | `OCR_UNAVAILABLE` / `OCR_FAILED` → manual review | Local tests (3, real Tesseract) |
| API → rag-service (retrieval, constraints) | Existing M7 `RagServiceClient` (unchanged) | Internal token | Fail closed (503, no passages) | Live (journey 5, without a corpus) |
| rag-service → approved corpus | Manifest identity validation | — | Refuses to load | BLOCKED in WSL (RAG-CORPUS-WSL) |
| API → private file storage (surveys, reports) | Local directories outside the web root | OS permissions; GUID paths | Fail closed (`SURVEY_FILE_*`) | Live |
| API → email provider | — | — | Non-delivering sender | Not integrated (no provider approved) |
| API → architect delivery channel | — | — | Recorded only | Not integrated (G09/G12) |
| API → AI provider | `IAiEditProposalPort` | — | 503 `AI_PROVIDER_NOT_CONFIGURED` | Not integrated (G20) |
| Any → production infrastructure | — | — | — | Not touched (NO-GO) |

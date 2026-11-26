# Security verification evidence (self-verification, NOT an independent audit)

All results below come from executed local tests at commit `cb48556`+ (journeys) unless stated.

| Category | Executed checks (evidence) |
|---|---|
| Broken access control / IDOR / BOLA | 40+ cross-user checks return 404 across projects, land, surveys, extraction, reviews, plans, edits, versions, AI proposals, lifecycle, reports, conversation (`smoke_journey.py`); staff cannot open customer projects (404); architect sees only addressed, non-withdrawn requests (`smoke_admin.py`, phase 9) |
| Privilege escalation | Customer → admin/staff/architect APIs 403; staff → admin APIs 403; staff proposal in an unassigned module 403; staff cannot approve or cancel; admin cannot change own roles; last-admin protection (live A02, A03 8-round concurrency) |
| Mass assignment | `roles` field on staff creation → 400 (strict JSON, unmapped members rejected) |
| Stale authorization | Role/scope/status change → the existing session gets 401 on its next request (privileges are never in the JWT); suspended account cannot log in |
| Session replay / token expiry | Refresh replay revokes the family; expired/wrong-key/tampered JWT 401; password change and reset revoke sessions (phase 7, `smoke_admin.py`) |
| Concurrency | Single winner on survey claims (Q01), leases (H01/H02), proposal review CAS, task version CAS, review version CAS, admin mutual revocation (no deadlock after fix) |
| Injection | EF parameterised queries; interpolated raw SQL uses `ExecuteSqlInterpolated`/`SqlQuery` (parameters); malformed JSON → 400 with no SQL or stack (phase 7) |
| Path traversal / malicious uploads | GUID-only blob paths, reparse points refused, length + SHA-256 checks (P01); media type + signature validation (M2); OCR header limits block decompression bombs (sandbox tests) |
| SSRF | No user-supplied URL is ever fetched; outbound targets are fixed configuration (Python, RAG, SMTP loopback-only by default) |
| Rate-limit bypass | auth-strict 429 without 5xx (phase 7); expensive-operation limiter on generation, reports, retrieval, AI proposals |
| Information leakage | No password, JWT, refresh token, DB password, internal token or signing key in service logs (phase 7 log scan); invitation/reset codes never in API responses or audit details (`smoke_admin.py`); 404 for both missing and foreign |
| Audit tampering | Hash-chain verification detects a modified row (A08); `GET /admin/audit/verify` intact after the journeys |
| Secret exposure | Tracked-file scan: only synthetic test literals and random generators; no key/PEM/env files tracked; runtime secrets live in `~/bm-captain/secrets` (0700, outside the repository) |
| Dependency vulnerabilities | `dotnet list BuildMind.sln package --vulnerable --include-transitive` (2026-11-25): **no vulnerable packages in all 10 projects** given the NuGet sources. Python: `pip-audit` is not installed, so not run. |
| Internal service auth | Fail-closed token; token file support (3 tests). **mTLS not implemented** (G11) |
| TLS | MySQL VerifyFull with client certificate. Untrusted-certificate / wrong-identity negative tests were run at CLI level only (earlier evidence) |

Not done:
- penetration testing;
- fuzzing;
- an independent review;
- mTLS certificate negative tests (no mTLS exists).

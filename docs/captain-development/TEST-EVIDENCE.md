# Test evidence — Captain full-backend completion

All results come from actual runs in this session (2026-11-24). Unless stated, they ran in WSL Ubuntu-24.04 with .NET SDK 10.0.115 on the one-way copy `~/bm-captain/src`; TRX files are under `~/bm-captain/results/`.
- **Python:** `~/bm-captain/venv-*` (Python 3.13.9, created by a pinned, checksum-verified uv 0.9.5), and the existing Windows `python-services/.venv` (Python 3.13) where noted.
- **MySQL:** 8.4.13 in container `bm-captain-mysql`, with `SslMode=VerifyFull` since commit `1574e1f`.

Nothing below is inferred from file existence. Columns: PASS / FAIL / SKIP; NOT RUN gives a reason; DEFERRED means knowingly postponed.

## Final results (code tree of `86b9593`, 2026-11-24; earlier round: `7213b42`)

| Suite | PASS | FAIL | SKIP | Command |
|---|---:|---:|---:|---|
| Release build (`BuildMind.sln`) | — | 0 errors | — | 0 warnings |
| Application.UnitTests | 252 | 0 | 0 | `tools/captain/unit-suites.sh` |
| Infrastructure.Tests | 1160 | 0 | 0 | same (run without live connection-string variables) |
| Reports.Tests | 164 | 0 | 0 | `python-and-pdf-checks.sh` (`BUILDMIND_PDF_QA_PYTHON` set, so the PDF-QA test runs; `unit-suites.sh` alone shows 163 + 1 skip) |
| Requirements.Tests | 456 | 0 | 0 | `unit-suites.sh` |
| Workflow.Tests | 197 | 0 | 0 | `unit-suites.sh` |
| Application.ContractTests (console) | exit 0 | — | — | `unit-suites.sh` |
| Persistence.IntegrationTests (live MySQL, VerifyFull) | 103 | 0 | 0 | `tools/captain/persistence-tests.sh`; 0 leftover test databases; includes 9 survey-extraction tests (Q01–Q04, P01×3, P02, P03) |
| python-services pytest (WSL venv) | 981 | 0 | 0 | includes 27 Captain internal-API tests (7 survey extraction) |
| python-services `tests_local` (real Tesseract 5.3.4 + pdftoppm) | 3 | 0 | 0 | not collected by CI; OCR read "North boundary length: 12 m" → 12 |
| rag-service pytest (WSL venv) | 238 | 0 | 0 | unchanged M7 suite |
| **Live journey, phases 2–8** (real API + MySQL + python-services + rag-service) | 136 | 0 | — | `tools/captain/smoke_journey.py` (RAG=1 stack); log `~/bm-captain/results/journey-full.log` |
| OpenAPI export (Development Swagger) | 57 operations | — | — | `openapi.captain-dev.json`; secret scan 0 hits |
| Independent PDF content check (pypdf) | pass | — | — | `check_report_pdf.py`: 3 pages; the preliminary notice, non-approval and synthetic statements are present |

### NOT RUN / DEFERRED / BLOCKED

| Item | State | Reason |
|---|---|---|
| RH09 command-timeout classification | **DEFERRED / UNRESOLVED** | Provider hang (MySql.Data 26.7.0/26.10.0). The test is not in this baseline and was not re-investigated. |
| `PythonLiveSmokeTests`, `RagLiveSmokeTests`, `RagConstraintAcceptanceTests` (Persistence project) | NOT RUN | They expect their own CI-provisioned services and environment variables. Live API↔Python and API↔RAG paths are instead covered by the journey. |
| Real-corpus regulation retrieval | **BLOCKED** (environment) | Manifest identity pins Windows paths, the loader correctly refuses normalisation, G17 prevents running the API on Windows, and WSL is in NAT mode |
| OCR accuracy on real surveys | NOT RUN | No approved real-survey evaluation set; only synthetic documents were used |
| Windows-native .NET execution | NOT RUN | G17 Smart App Control (not bypassed) |

## Live journey coverage (136 checks)

### Phase 2: auth, projects, requirements, survey
- **Readiness:** the `mysql` entry is exactly Healthy/reachable; `python` is Healthy.
- **Auth:** register and login for two users; invalid JWT → 401.
- **Projects:** create; cross-user read → 404.
- **Requirements:** save; stale revision → 409.
- **Survey:** upload → 202.

### Phase 3: land and generation
- **Land:** self-intersecting boundary → 422 `POLYGON_INVALID`; valid boundary → 201 (area 168); stale → 409; cross-user → 404.
- **Generation:** 3 distinct candidates ranked 1..3, labelled synthetic with "access not evaluated", 5 rooms each, no openings, with a preliminary notice.
- **Idempotency:** replay returns the same job; key reuse with other inputs → 409.
- **Denials:** stale inputs → 409; cross-user generate and read → 404.
- **Save:** candidate saved as revision 1 citing server-issued `SYNTHETIC_FIXTURE` evidence; duplicate save → 409; history shows [1].
- **Truthful failure:** impossible requirements → 422 `INFEASIBLE` with no candidates.

### Phase 4: plan list, edits, apply, confirmation
- **Plans:** list and read; cross-user list/read → 404.
- **Edit validation:** out-of-parcel edit → 422 `VALIDATION_EVIDENCE_INVALID`; stale base → 409/404; cross-user edit → 404.
- **Apply:** valid edit → validated candidate; apply without acknowledgement → 422; cross-user apply → 404; apply → v2; duplicate apply → 409.
- **History:** [2, 1]; edit on the superseded revision → 409; explicit reject → 200.
- **Confirmation:** wrong hash → 409; superseded revision → 409; no acknowledgement → 422; cross-user → 404; success → 201 with the non-approval notice; second confirmation → 409; plan list shows the confirmation.

### Phase 5: regulation retrieval (fail closed)
- **Readiness:** rag = Degraded/unavailable.
- **Retrieval:** anonymous → 401; authenticated with no corpus → 503 with no passages.
- **rag-constraints generation:** 503 `DEPENDENCY_UNAVAILABLE`, and no job is published.

### Phase 6: reports and architect requests
- **Reports:** hash mismatch → 404; cross-user generate/status/download → 404; generate → 201 Ready; idempotent repeat → 200; download is a PDF attachment with `no-store`; unknown report → 404.
- **Architect requests:** missing consent → 422; unknown recipient → 422; stale policy → 409; cross-user → 404; create → 201 Pending (recorded, not dispatched); list; cross-user read → 404; withdraw → 200; second withdraw → 409.

### Phase 7: dashboard and security
- **Dashboard:** owner sees their projects; other users never see them.
- **JWT:** expired (validly signed) → 401; wrong key → 401; tampered → 401.
- **Refresh and sessions:**
  - refresh rotation → 200, and the rotated token works;
  - replayed refresh token → 401, the issued access token is revoked → 401, and the whole family is revoked;
  - re-login; logout → 204, then the logged-out refresh token → 401;
  - logout-all → 204, then the access token → 401; other users are unaffected.
- **Invalid requests:** malformed JSON → 400 with no stack trace or SQL in the body; unsupported media type → 415/404 (sent by a non-owner, so 404 is also accepted; this is a weak check); 2 MiB body → 413.
- **Logs:** no password, JWT, refresh token, DB password, internal token or signing key in any service log.
- **Dependency failure:** python-services stopped → land validation 503 `DEPENDENCY_UNAVAILABLE`, readiness python not Healthy, and the non-owner is still denied.
- **Rate limiting:** an auth-strict burst gives 429 with no 5xx.

### Phase 8: V3 register routes (runs before phase 7)
- **Users:** `GET /users/me` → caller's profile; without a token → 401.
- **Survey extraction:**
  - a synthetic text-layer PDF uploads with 202;
  - the durable worker completes within the poll window (method `PDF_TEXT`);
  - the 14 m length comes back as unreviewed evidence with the unverified-evidence notice;
  - a cross-user read → 404.
- **Land:**
  - `land/validate` dry run → valid, `persisted=false`; a bow-tie is reported invalid;
  - `GET land` afterwards → 204, so nothing was saved;
  - cross-user → 404;
  - `land/confirm` → 201 (area 168).
- **Alternatives:** 3 distinct; cross-user → 404.
- **Select / create plan:** unknown alternative → 404; `select` → revision 1; `POST /plans` with another alternative → a new plan; cross-user → 404.
- **Edit validation:** `edits/validate` returns validated geometry with `persisted=false`; history stays [1]; cross-user → 404.
- **Versions:**
  - save → revision 2;
  - superseded base → 409;
  - out-of-parcel geometry → 422 (server validation);
  - cross-user → 404.
- **AI proposals:** 503 `AI_PROVIDER_NOT_CONFIGURED`.
- **Confirmation:** the `confirm` alias → 201.
- **Reports:** the list shows the phase 6 report; cross-user → 404.

## Earlier phase-specific evidence (still valid; same tests are included in the final runs above)

| Item | Result |
|---|---|
| Refresh-rotation deadlock fix (Phase 2) | `SessionSecurityLiveTests` 10/10 consecutive runs |
| Users-first lock order across rotation, replay, logout, logout-all and reset (review correction) | `SessionSecurityLiveTests` (incl. S08/S09, 8 mixed rounds each) 5/5 consecutive runs |
| TLS negative check | MySQL CLI `VERIFY_IDENTITY` refuses the container bridge IP (not in the SAN) and accepts 127.0.0.1. CLI-level only. |
| Phase 2 smoke (`smoke_phase2.py`) | 19/19 with the exact readiness assertion |

## Defects found and fixed in this session

| Defect | Where | Fix and evidence |
|---|---|---|
| Intermittent deadlock on concurrent refresh of one token (S03 failed in 1 of 3 runs) | `RefreshTokenStore.RotateAsync` | Token-row lock, then users-first lock order everywhere; 10/10 and 5/5 repeated runs |
| Rotation lock order opposite to logout-all/reset | session stores | users row locked first in rotation and logout; S08/S09 |
| `smoke_phase2.py` readiness accepted any "Healthy" substring; requirements read-back compared `None == None` | tooling | exact `mysql` entry; compares `revisionId` |
| Harness leaked live connection strings into unit suites (1 false failure) | tooling | `env.sh` is sourced only by the live suite |
| `/users/me` alias first added to the base M1 module changed the ratified route inventory (M1 Swagger pin failed) | `ProfileEndpointModule` | Alias moved to the Captain module; Infrastructure.Tests 1160/1160 |
| `POST …/versions` body rejected: M3 strict JSON is case-sensitive without a naming policy | V3 module | explicit `JsonPropertyName` on the wrapper; journey phase 8 green |
| Full live suite hit `Too many connections` (152/151) after adding 9 disposable-DB tests | Captain live helpers | unpooled Captain contexts; 103/103, 0 leftovers |
| Journey expected 404 for "no land"; the API returns 204 | tooling | expectation corrected (existing API behaviour) |

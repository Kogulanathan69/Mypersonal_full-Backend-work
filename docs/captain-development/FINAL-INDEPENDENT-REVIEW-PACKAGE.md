# Final independent review package

**Status: INDEPENDENT_REVIEW_REQUIRED.** No independent review has taken place. The implementer cannot review their own work. This package tells reviewers what to look at, in what order, and how to reproduce the evidence.

## Branch and scope

| Item | Value |
|---|---|
| Branch | `feature/captain-full-backend-completion` (local only; **not pushed**) |
| Base | `64f6cbb` (`origin/develop` merge base) |
| Closure-order commits | 13 commits `ce4cf1f..HEAD` (66 files, +5857/−84) |
| Whole branch | 37 commits since base (including the final docs commit) |
| Excluded scope | cost estimation, BOQ, 3D, payments: no code |

## Review order (highest risk first)

| # | Area | Files | Reviewer | What to check |
|---|---|---|---|---|
| 1 | Authorization and data isolation | `src/BuildMind.API/Endpoints/Planning/*`, `CaptainIntegration/*Admin*`, `ProtectedPlanningWriter` | M1 + security | owner scoping on every route, fresh privilege reads, consent and withdrawal, lock ordering |
| 2 | Migrations #5–#15 | `Persistence/Migrations/2026112*`, configurations, pin lists | **M4** | additive only, FK RESTRICT, naming, rebase onto the shared migration order |
| 3 | Internal mTLS | `InternalMtls.cs`, `CaptainLocalIntegration.cs`, `tools/captain/pki.sh` | security | trust anchors, hostname/EKU checks, no fallback to plaintext, rotation |
| 4 | LLM adapter | `OpenAiCompatibleEditProposalPort.cs`, `RecordingAiEditProposalPort.cs` | M3 + security | prompt minimisation, schema re-validation, bounds, logging, provider data policy (G20) |
| 5 | CRS frame | `python-services/app/internal/land_frame.py`, `PlanningServices.SubmitAsync`, `LandFrameDigest` | **M2 / M6** | EPSG:5235 parameters, no ballpark transforms, digest parity, ring handling |
| 6 | Openings and validation | `python-services/app/internal/openings.py`, `generation.py`, `plan_validation.py` | **M6 / M3** | access model, severity policy (CRITICAL vs WARNING), regression fix `27e327d` |
| 7 | Email and notifications | `SmtpEmailSender.cs`, `DeliverableAdapters.cs` | M5 | STARTTLS requirement, transactional outbox, idempotency keys |
| 8 | Containers | `deploy/captain/*`, `.dockerignore`, `tools/captain/compose-verify.sh` | DevOps | hardening, secret handling, generated material kept outside the repo |
| 9 | RH09 | `tools/captain/rh09/*`, `mysql-setup.sh` containment | **M4** | probe validity, containment side effects |
| 10 | M4 test change | `RequestBodyLimitTests.cs` (`Expect: 100-continue`) | **M4** | the assertion still proves the real Kestrel limit |
| 11 | Shared python bootstrap | `python-services/app/main.py` (frame route) | **M6** | route contract `captain-land-frame/1` |

## Reproduction (WSL Ubuntu 24.04; see LOCAL-RUNBOOK)

1. `~/bm-captain/sync.sh`, then `RAG=1 DETACH=1 bash tools/captain/restart-stack.sh`.
2. Unit tests: `BUILDMIND_PDF_QA_PYTHON=<venv python with pypdf> bash tools/captain/unit-suites.sh`. Do **not** source `env.sh` first; this test project asserts the absence of connection strings.
3. Live persistence tests: `bash tools/captain/persistence-tests.sh`.
4. Python suites: `pytest` in `python-services` and `rag-service`.
5. Live checks:
   - `python3 tools/captain/mtls_tests.py`;
   - `python3 tools/captain/smoke_admin.py`;
   - `python3 tools/captain/smoke_journey.py`.

   Wait 61 s between them because of the auth limiter.
6. Real LLM: `bash tools/captain/run-llm.sh`, then `LLM_PORT=8093 bash tools/captain/restart-api.sh`, then `python3 tools/captain/smoke_llm.py`.
7. Containers:
   - `bash tools/captain/compose-verify.sh prepare && … build && … up`;
   - `python3 tools/captain/smoke_journey.py http://127.0.0.1:8080 --phases 2,3,4,6,8,10`.

## Known weaknesses reviewers should probe

- Response bodies are not declared in OpenAPI. The TypeScript contracts type them as `unknown`.
- Edits do not move openings. Interior-facing windows after edits are warnings only.
- H01 (survey heartbeat) is timing-sensitive under heavy parallel load: it failed once in a full run and passed 12/12 in isolation.
- LLM quality was not measured over a prompt suite.
- RAG is unverified beyond fail-closed behaviour (STEP 8B).
- The journey's static-scan coverage (85/127 operations) is conservative but approximate.

## Evidence index

See FINAL-LIVE-INTEGRATION-EVIDENCE (all counts with log paths), FINAL-SECURITY-VERIFICATION, DOCKER-CONTAINER-VERIFICATION, INTERNAL-MTLS-VERIFICATION, RH09-RESOLUTION-REPORT, REAL-LLM-INTEGRATION-REPORT, REAL-EMAIL-INTEGRATION-REPORT, REAL-REGULATION-RAG-REPORT, CRS-GEOMETRY-INTEGRATION-REPORT, DOOR-WINDOW-GENERATION-REPORT and FINAL-DATABASE-MIGRATION-REGISTER.

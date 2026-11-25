# Final backend handover: Captain full-backend completion (closure execution)

| Item | Value |
|---|---|
| Branch | `feature/captain-full-backend-completion` (local; **not pushed, not merged, not deployed**) |
| Closure range | `ce4cf1f..HEAD` |
| Date | 2026-11-25 |
| Production | **NO-GO** |
| Excluded scope | cost estimation, BOQ, 3D, payments: not implemented, not counted |

The previous version of this file (master order, `831b829`) is in git history. This version supersedes it.

## Per-phase acceptance (order §12)

| Phase | Verdict | Key evidence | Remaining gate |
|---|---|---|---|
| 1 Docker Compose / containers | **COMPLETE_AND_VERIFIED** (local) | images built; journeys 132/132 + 110/111; restart persistence; hardening inspected | I-9 supply chain; staging compose not run |
| 2 CRS / geometry | **COMPLETE_AND_VERIFIED** | 13 py + 3 .NET parity; journey 10; live 202/202 | edge roles (regulation-dependent); M2/M6 review |
| 3 Doors / windows | **COMPLETE_AND_VERIFIED** | test_openings 13; journeys; regression found and fixed (`27e327d`) | M6/M3 review |
| 4 Internal mTLS | **COMPLETE_AND_VERIFIED** (API→python) | 12/12 negatives, rotation, containers | RAG link: BLOCKED_BY_EXPLICIT_PROTECTED_SCOPE |
| 5 RH09 | **BLOCKED_BY_EXTERNAL_DEPENDENCY** | provider defect isolated; containment verified | I-8 (EF Core 10 provider not built on MySql.Data) |
| 6 Real LLM | **COMPLETE_AND_VERIFIED** (local model) | smoke_llm 10/10 | external provider: IMPLEMENTED_AWAITING_EXTERNAL_ACTIVATION (I-1) |
| 7 Email | **IMPLEMENTED_AWAITING_EXTERNAL_ACTIVATION** | STARTTLS policy 7/7; new notifications live; Mailpit only | I-2/I-3 |
| 8 Regulation RAG | **BLOCKED_BY_EXPLICIT_PROTECTED_SCOPE** | root cause diagnosed; PDFs verified intact; change request | STEP 8B / I-4 |
| 9 API / persistence / integration testing | **COMPLETE_AND_VERIFIED** | 3632 automated tests; 279 live checks at HEAD; OpenAPI 127; TS strict; P-13, P-14 | — |
| 10 Security / production / CI / review | **INDEPENDENT_REVIEW_REQUIRED** | audits clean; secret scan 0; review package | G19 review, CI, production gates (FINAL-PRODUCTION-READINESS-MATRIX) |

No phase is FAILED_ACCEPTANCE.

## Commits in this closure execution

| Commit | Content |
|---|---|
| `834b2ff` | Doors/windows, circulation and access validation |
| `2e1f167` | Internal mTLS API → python (PKI, rotation, negatives) |
| `91c8c06` | RH09 isolated comparison + server-side containment |
| `2745372` | Captain coordinate frame (CRS, anchor, digest parity, edges, lineage) |
| `ba2c2be` | Real LLM natural-language editing (local model) |
| `27e327d` | Edit-validation regression fix (interior-facing openings are warnings) |
| `add53f8` | REPORT_READY / ARCHITECT_REQUEST_WITHDRAWN notifications; external SMTP requires STARTTLS |
| `a168cf1` | Containerized verification stack (Compose v5.7.0) |
| `0730afd` | Real email integration report |
| `afb5dc2` | Real regulation RAG report |
| `8213f69` | P-13 deterministic 413 assertions (M4 test) |
| `8070e9c` | P-14 specific AI rejection reasons (Captain decorator) |
| `a69086a` | Migration register, CRS / door-window reports, external decisions, contract generators |
| (final) | Final documents, OpenAPI re-export, TypeScript contracts, endpoint inventory |

## Changes to code owned by other members (for their review)

| File | Change | Owner |
|---|---|---|
| `python-services/app/main.py` | new `POST /internal/v1/land/frame` route | M6 |
| `Persistence/Migrations/20261125030045_AddLandCoordinateFrame*`, model snapshot, two pin lists | additive columns | M4 |
| `tests/.../Api/RequestBodyLimitTests.cs` | `Expect: 100-continue` for Content-Length requests | M4 |
| `.dockerignore` (new, repository root) | build-context hygiene | DevOps / M4 |

M3's `AiEditOrchestrationService`, M7's `RagServiceClient` and registration, rag-service, and the corpus identity controls are **unchanged**.

## Operational notes (learned in this execution)

- **Persistence tests need a quiet MySQL server.** Running them alongside the live stack exhausted `max_connections` (151); stop the API first.
- **Do not source `tools/captain/env.sh` before `unit-suites.sh`.** One test asserts that no connection string is configured.
- `restart-stack.sh` ends with `wait`, by design. Use `DETACH=1` inside a script that continues in the same session.
- The auth limiter needs about 61 s between smoke tools.
- On MySQL, never use `dotnet ef migrations script --idempotent`; MySQL rejects its output.
- Browsers uploading over 1 MiB may see a connection reset instead of a readable 413 (Kestrel early response). The frontend should check the size before upload.

## Decisions needed

See FINAL-EXTERNAL-DECISION-REGISTER (I-1 … I-9) and FINAL-PRODUCTION-READINESS-MATRIX.

## Document index (order §11)

| Required document | File |
|---|---|
| Pending development register | FINAL-PENDING-DEVELOPMENT-REGISTER.md |
| API endpoint inventory | FINAL-API-ENDPOINT-INVENTORY.md (+ `contracts/buildmind-api.d.ts`, `openapi.captain-dev.json`) |
| Feature completion matrix | FINAL-FEATURE-COMPLETION-MATRIX.md |
| Integration completion matrix | FINAL-INTEGRATION-COMPLETION-MATRIX.md |
| Docker container verification | DOCKER-CONTAINER-VERIFICATION.md |
| CRS geometry integration report | CRS-GEOMETRY-INTEGRATION-REPORT.md |
| Door/window generation report | DOOR-WINDOW-GENERATION-REPORT.md |
| Internal mTLS verification | INTERNAL-MTLS-VERIFICATION.md |
| RH09 resolution report | RH09-RESOLUTION-REPORT.md |
| Real LLM integration report | REAL-LLM-INTEGRATION-REPORT.md |
| Real email integration report | REAL-EMAIL-INTEGRATION-REPORT.md |
| Real regulation RAG report | REAL-REGULATION-RAG-REPORT.md |
| Database migration register | FINAL-DATABASE-MIGRATION-REGISTER.md |
| Live integration evidence | FINAL-LIVE-INTEGRATION-EVIDENCE.md |
| Security verification | FINAL-SECURITY-VERIFICATION.md |
| Production readiness matrix | FINAL-PRODUCTION-READINESS-MATRIX.md |
| External decision register | FINAL-EXTERNAL-DECISION-REGISTER.md |
| Independent review package | FINAL-INDEPENDENT-REVIEW-PACKAGE.md |
| Backend handover | this file |

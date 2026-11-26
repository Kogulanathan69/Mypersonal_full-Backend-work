# Final live integration evidence

All results below were **executed in this closure execution (2026-11-25)** on the WSL Ubuntu 24.04 host (3.9 GB RAM, 2 CPUs). Historical results from earlier orders are not repeated as new.
- Logs are under `~/bm-captain/results/` (WSL; not committed: they contain synthetic account identifiers).
- Failed and rerun results are listed, not hidden.

## 1. Automated test suites (final regression)

| Suite | Result | Code state | Notes |
|---|---|---|---|
| BuildMind.Application.UnitTests | **255/255** | `afb5dc2` | — |
| BuildMind.Infrastructure.Tests | **1186/1186** | final code (P-13/P-14 included) | First run 1179/1180 was a **harness error**: my script exported `ConnectionStrings__BuildMind` (via `env.sh`), and `PersistenceRegistrationTests.Enabled_database_without_connection_string_fails_at_startup` correctly asserts its absence. Rerun in a clean environment. |
| BuildMind.Reports.Tests | **164/164, 0 skipped** | `afb5dc2` | PDF QA **enabled** (`BUILDMIND_PDF_QA_PYTHON` = venv with pypdf 7.1.1); previously skipped |
| BuildMind.Requirements.Tests | **456/456** | `afb5dc2` | — |
| BuildMind.Workflow.Tests | **197/197** | `afb5dc2` | — |
| BuildMind.Application.ContractTests (console) | exit 0, 27 PASS lines | `afb5dc2` | — |
| BuildMind.Persistence.IntegrationTests (live MySQL, TLS) | **115/115**, 0 leftover DBs | final code | See the run history below |
| python-services pytest | **1021 passed** | final code | — |
| rag-service pytest | **238 passed** | final code (unchanged) | — |

Code-state note: P-13 and P-14 changed only API, Infrastructure and test code after `afb5dc2`. The solution was rebuilt (0 errors, 0 warnings), Infrastructure.Tests was rerun, and the live runs in §2 were executed at `a69086a`.

**Totals for this regression:**
- .NET xUnit: 255 + 1186 + 164 + 456 + 197 + 115 = **2373** passed, plus 27 contract checks;
- Python: 1021 + 238 = **1259** passed;
- **3632** automated tests in total, with each suite counted once.

**Persistence run history (all real runs, none hidden):**

| Run | Result | Classification |
|---|---|---|
| 1 | 114/115 | `H01_heartbeat_keeps_a_long_extraction_from_being_reclaimed`: connection open cancelled under load (1 s heartbeat / 3 s lease, timing-sensitive). Rerun in isolation: SurveyExtractionLiveTests **12/12**. |
| 2 | 103/115, 2 leftover test DBs | The live stack (2 API instances + workers) was running against the same MySQL. **Connection exhaustion:** `max_connections` 151, `Max_used_connections` 152, `Connection_errors_max_connections` 9; 15× `MySqlConnectionGateTimeoutException`. This is an environment conflict, not a code defect. The leftover disposable `buildmind_test_*` databases were dropped; the Captain DB was untouched (37 tables). |
| 3 | **115/115** on a quiet server | Final |

## 2. Live integration runs (real processes, no mocks)

| Run | Stack / commit | Result | Log |
|---|---|---|---|
| Journey phases 2–10, RAG enabled (fail-closed), python outage (phase 7), architect portal + notifications + Mailpit email (phase 9), CRS (phase 10), specific AI reasons (phase 8) | process stack, `a69086a` | **202/202** | `final-live/journey.log` |
| Admin / staff / RBAC / audit smoke | process stack, `a69086a` | **65/65** | `final-live/admin.log` |
| mTLS negatives | process stack, `a69086a` | **12/12** | `final-live/mtls.log` |
| OpenAPI export | process stack, `a69086a` | 108 paths / **127 operations** | `final-live/openapi.json` → `openapi.captain-dev.json` |
| Containers run A (phases 2,3,4,6,8,10) | Compose `compose.verify.yml`, pre-P-14 | **132/132** | `journey-containers.log` |
| Containers run B (phases 3,4,6,9) | same | **110/111** (email capture is not wired in containers, by policy) | `journey-containers-3469.log` |
| Container restart persistence | same | rows 6/11/1 equal before and after; report artefact persisted | — |
| Real LLM (llama.cpp b12237 + Qwen2.5-1.5B-Instruct Q4_K_M) | process stack, `ba2c2be` | **10/10** | `llm-journey.log` |
| P-13 mechanism diagnostic (32 MiB vs 1 MiB) | temporary, not committed | no Expect: 15/15 broken pipe; with Expect: 15/15 clean 413 | — |
| P-13 loops | original test 25 idle + 20 loaded: 0 failures; fixed test 20 loaded: 0 failures | — | `p13-*.log` |
| RH09 provider probe S1–S9 | isolated | see RH09-RESOLUTION-REPORT | `rh09/rh09-081843.json` |

Container and process journeys exercise overlapping scenarios on different deployments, so they are reported separately and not summed. Live checks on the final HEAD: 202 + 65 + 12 = **279**.

## 3. Audits

| Audit | Result |
|---|---|
| pip-audit | python-services 0/19, rag-service 0/14 |
| NuGet `--vulnerable --include-transitive` | **11/11** .NET projects with no vulnerable packages (10 solution projects + persistence tests) |
| Secret scan (tree + 36 branch commits + OpenAPI export) | 0 findings |
| TypeScript contracts | `contracts/buildmind-api.d.ts` (127 operations) compiles under `tsc 5.9.3 --strict --noUncheckedIndexedAccess` |
| Endpoint inventory | 127 operations IMPLEMENTED; 85 LIVE_VERIFIED by static scan of the smoke scripts |

## 4. Not executed (honest)

- External SMTP delivery, an external LLM provider and real RAG retrieval (external or protected).
- Phases 5 and 7 against containers.
- An LLM quality suite.
- Image vulnerability scanning.
- CI (no push permitted).
- Independent review.

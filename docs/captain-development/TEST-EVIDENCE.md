# Test evidence — Captain full-backend completion

All results are from actual runs in this session. Runs used WSL Ubuntu-24.04 with .NET SDK 10.0.115 on the one-way copy `~/bm-captain/src`, and TRX files under `~/bm-captain/results/`. Python runs used `python-services/.venv` (Python 3.13). Nothing below is inferred from file existence.

Result columns:
- PASS / FAIL / SKIP: counts from the run.
- NOT RUN: not executed, with the reason.
- DEFERRED: knowingly postponed.

## Phase 0 baseline (`fec3254`)

| Suite | PASS | FAIL | SKIP | Note |
|---|---:|---:|---:|---|
| Release build | — | 0 | — | 0 warnings |
| Application.UnitTests | 233 | 0 | 0 | |
| Infrastructure.Tests | 1140 | 0 | 0 | |
| Reports.Tests | 163 | 0 | 1 | `IndependentParserVerifiesExactSyntheticReport` needs `BUILDMIND_PDF_QA_PYTHON` |
| Requirements.Tests | 456 | 0 | 0 | |
| Workflow.Tests | 197 | 0 | 0 | |
| Application.ContractTests | exit 0 | | | console runner |
| python-services pytest | 954 | 0 | 0 | |
| rag-service pytest | 238 | 0 | 0 | |
| Persistence.IntegrationTests | NOT RUN | | | no database at that point (Phase 2) |
| RH09 | DEFERRED | | | test not present at `fec3254`; provider defect, see KNOWN-BLOCKERS |

## Phase 1

| Suite | PASS | FAIL | SKIP |
|---|---:|---:|---:|
| Infrastructure.Tests, filter `CaptainIntegration` | 18 | 0 | 0 |
| Infrastructure.Tests (full) | 1158 | 0 | 0 |

## Phase 2 (live MySQL 8.4.13, verified TLS)

| Run | PASS | FAIL | Note |
|---|---:|---:|---|
| Persistence.IntegrationTests, baseline (Python/RAG live excluded) | 81 | 0 | 0 leftover test databases |
| Same suite with the `AddSurveys` migration, before updating the inventory pins | 82 | 6 | 5 schema-inventory pins (expected; then updated) + 1 `SessionSecurityLiveTests.S03` **deadlock** (intermittent) |
| Same suite after the updates | 88 | 0 | includes 7 new Captain live tests |
| `SessionSecurityLiveTests` × 10 after the deadlock fix | 70 | 0 | 10/10 consecutive runs green |
| Live API smoke (`tools/captain/smoke_phase2.py`) | 19 | 0 | real API + MySQL; synthetic users |

Excluded and NOT RUN in Phase 2: `PythonLiveSmokeTests`, `RagLiveSmokeTests`, `RagConstraintAcceptanceTests`. Those services were not running yet; they are covered in Phase 5/7.

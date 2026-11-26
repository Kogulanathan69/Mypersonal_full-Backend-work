# BE-M6-D01 test evidence

**Date:** 2026-11-19  
**Owner:** Member 6  
**H2 baseline:** `3af9eba4983cc5a3a26fef0ea188cf47cf09b252` plus the 27-file M6 overlay  
**Latest verified backend (H4-R1):** `bf72343ce95655447b431c076dcf62d02c32c6de` plus the unchanged M6 source/test overlay

This report uses the D01-H2 isolated and active execution logs. H3 inspected the source, fixture corpus and logs and did not rerun the suites. H2 used Ubuntu 24.04.5 LTS on WSL2 x86-64 with Linux .NET SDK 10.0.115 and .NET and ASP.NET Core runtimes 10.0.15. The Python service used its existing Windows Python 3.13.17 virtual environment. RAG used a separate Windows Python 3.13 environment created from `rag-service/requirements-dev.txt`. The isolated and active Linux builds each used their own ignored `obj/h2-linux` directories.

## Execution results and accounting

| Command or suite | Isolated | Active | Exit code | Elapsed, isolated / active |
|---|---:|---:|---:|---:|
| NuGet restore | 10 projects | 10 projects | 0 / 0 | 25.20 s / 17.66 s |
| Release solution build | 10 projects, 0 warnings/errors | 10 projects, 0 warnings/errors | 0 / 0 | 64.72 s / 70.04 s |
| Database-free C# solution tests | 1,196 passed | 1,196 passed | 0 / 0 | 38.83 s / 43.67 s |
| Registered console contract checks | 276 passed | 276 passed | 0 / 0 | 3.03 s / 2.99 s |
| Isolated M6 C# runner | 38 representation + 82 semantic/boundary passed | Same 120 named checks passed | 0 / 0 | 20.71 s / 22.84 s |
| Complete Python service suite | 628 passed | 628 passed | 0 / 0 | 12.68 s / 9.55 s |
| M6 Python subset | 574 passed | 574 passed | 0 / 0 | 7.11 s / 7.21 s |
| Python service `pip check` | No broken requirements | No broken requirements | 0 / 0 | 3.36 s / 2.28 s |
| RAG synthetic-corpus suite | 68 passed | 68 passed | 0 / 0 | 6.98 s / 8.37 s |
| RAG byte-provenance check | 1 passed | 1 passed | 0 / 0 | 3.81 s / 3.71 s |
| RAG `pip check` | No broken requirements | No broken requirements | 0 / 0 | 1.79 s / 2.15 s |

| C# solution assembly | Isolated passed | Active passed |
|---|---:|---:|
| `BuildMind.Application.UnitTests` | 128 | 128 |
| `BuildMind.Infrastructure.Tests` | 539 | 539 |
| `BuildMind.Reports.Tests` | 54 | 54 |
| `BuildMind.Requirements.Tests` | 352 | 352 |
| `BuildMind.Workflow.Tests` | 123 | 123 |
| **Solution total** | **1,196** | **1,196** |

Solution test logs show zero failed and zero skipped in all five assemblies. Both console harnesses reported zero failures. The complete Python and RAG suites each reported one Starlette `TestClient` deprecation warning concerning `httpx`. These are test-library warnings. H2 verified the RAG byte-provenance check with Windows Git `core.autocrlf=true` and LF bytes for the copied engine and smoke script after PR #34.

**Counting rule.** Count one synchronized run of each suite: 1,196 + 276 + 38 + 82 + 628 + 68 = **2,288 checks**. M6 accounts for **694 checks** (574 + 38 + 82). The M6 Python subset is already included in 628, the standalone provenance test in 68, and the isolated and active runs exercise the same cases. Build, restore, dependency checks, and historical independent assertions are excluded. Representation and semantic checks deliberately exercise different validation paths even when they use the same fixture.

## Before and after backend synchronization

| Metric | Step 10-B at `fdf7baa` | H2 at `3af9eba` |
|---|---:|---:|
| Release projects built | 10/10 | 10/10 |
| Compiler warnings / errors | 0 / 0 | 0 / 0 |
| C# solution tests | 681 | 1,196 |
| Registered console checks | 246 | 276 |
| Isolated M6 C# representation / semantic and boundary | 38 / 82 | 38 / 82 |
| Complete Python / M6 subset | 628 / 574 | 628 / 574 |
| RAG synthetic-corpus suite | Outside the baseline run | 68 |
| Recorded semantic gaps rejected | 22/22 | 22/22 |

## Test and fixture inventory

| Path relative to repository root | Evidence |
|---|---|
| `python-services/tests/m6_contract/test_generation_contracts.py` | Valid input, nonnil UUIDs, exact revision IDs, supported units/catalogue, dimensions and bounds, closed exterior/holes, adjacency and rule references, evidence states, seed and collection limits |
| `python-services/tests/m6_contract/test_feasibility_contracts.py` | Positive area and footprint, empty versus unchecked measurements, evidence status consistency, invalid areas, diagnostic code and JSON Pointer rules |
| `python-services/tests/m6_contract/test_validation_contracts.py` | Required nullable flags, hashes and timestamps, revision envelope, blocking diagnostics |
| `python-services/tests/m6_contract/test_generation_response_contracts.py` | Explicit unavailability, exactly three distinct structural alternatives, M3 geometry and wall references, candidate and validation binding, warning versus blocking states |
| `python-services/tests/m6_contract/test_generation_job_contracts.py` | All snapshot states, full transition matrix, idempotency bounds, timestamps, result and failure separation, definitive infeasibility |
| `python-services/tests/m6_contract/test_diagnostic_contracts.py` | Registered code and severity, open diagnostics, matching reasons and failure diagnostics |
| `python-services/tests/m6_contract/test_contract_hardening.py` | Duplicate keys, safe errors, mutated and preconstructed models, job publication, lifecycle history and diagnostic redaction |
| `python-services/tests/m6_contract/test_shared_contract_fixtures.py` | Python execution of all 13 positive fixtures and 34 mutations |
| `python-services/tests/m6_contract/fixtures/generation-input.v0.1-draft.json` | Synthetic complete request with explicitly missing evidence |
| `python-services/tests/m6_contract/fixtures/generation-unavailable.v0.1-draft.json` | Explicit unavailable result with zero alternatives |
| `python-services/tests/m6_contract/fixtures/parity/shared-contract-cases.json` | Shared seven-model corpus with 13 positive examples and 34 classified variants |
| `tests/BuildMind.Application.ContractTests/Integration/M6GenerationContractScenarios.cs` | Representation checks: 13 reads, 13 wire round trips, nine JSON rejections and three expected M3 omission differences |
| `tests/BuildMind.Application.ContractTests/Integration/M6SemanticContractScenarios.cs` | Semantic and boundary checks: 13 positive payloads, all 34 variants and 35 targeted checks |

Positive examples include valid metric land, missing-evidence metadata, unchecked and empty feasibility, validation, unavailability, structural success, and all five job states. Negative cases exercise missing or misspelled fields, unknown values, cross-project and cross-revision substitutions, invalid structural geometry and false terminal outcomes. Boundary coverage includes Int32 seed limits, finite values, zero versus null, required nullable versus omitted optional fields, UTC offset and precision, and supported collection and string sizes. Security cases include nested and escaped duplicate keys, malformed Unicode, submitted values in errors, untrusted diagnostic metadata, immutable identity, and terminal state rewrites.

## Closure of the 22 recorded semantic gaps

Each row passed as a named case in both H2 runs. For these cases the C# scenario exercises typed validation directly and the JSON boundary. The two paths are assertions within the same registered case.

| Fixture mutation | Rejected condition |
|---|---|
| `nil-uuid` | Empty UUID in a required identity |
| `unsupported-schema` | Unsupported exact schema version |
| `unsupported-room-literal` | Room type outside the synthetic catalogue |
| `nonfinite-coordinate` | Raw overflow number `1e999` as a coordinate |
| `negative-area` | Negative metric area |
| `seed-below-zero` | Seed below the portable range |
| `uppercase-geometry-hash` | Hash outside lowercase SHA-256 wire form |
| `short-input-fingerprint` | Truncated job input digest |
| `revision-mismatch` | Result and alternative input revision mismatch |
| `unclosed-ring` | Exterior ring opens at the end |
| `reversed-dimension-bounds` | Maximum width below minimum |
| `contradictory-evidence` | Missing evidence carrying a ruleset version |
| `unsupported-job-state` | State outside the defined lifecycle |
| `queued-start-timestamp` | Start metadata on a queued job |
| `terminal-before-start` | Completion before start |
| `result-on-failed-job` | Result attached to a failed job |
| `infeasible-search-exhausted` | Search exhaustion used as proof of infeasibility |
| `invalid-alternative-count` | Successful result with fewer than three alternatives |
| `blocking-validation` | Successful alternative with blocking validation |
| `duplicate-alternative-id` | Repeated alternative identity |
| `duplicate-signature` | Repeated supplied diversity signature |
| `idempotency-key-resource-limit` | Key longer than 128 permitted characters |

The representation serializer wrote UTC `+00:00` for the shared fixture’s `Z` values. The opt-in M6 boundary normalizes valid UTC input through offsets of ±23:59 and publishes `Z` with up to microsecond precision. M3’s three omitted collection cases are expected differences: Python supplies empty defaults for `rooms`, `walls` and `openings`, and C# requires the fields. Neither compatibility expectation is counted as a semantic gap closure.

## Logs and replay notes

The external H2 evidence directory contains `incoming-inventory.txt` and paired `pre-*` and `post-*` logs. Key names are `build.log`, `solution-tests.log`, `shared-harness.log`, `m6-harness.log`, `python.log`, `m6-python.log`, `pip.log`, `rag.log`, `rag-provenance.log` and `rag-pip.log` with the corresponding phase prefix. The H2 report records command wall times and exit codes. Tool-reported build time and pytest timing exclude some launcher overhead.

In a separately authorized verification session, run the solution restore, Release build, database-free solution tests and existing console project with the approved Linux SDK. For the M6 cases, use an external console project referencing the real Application project and compiling the two M6 scenario files with the existing `FixtureLocator.cs`. Invoke `Register` and `RegisterSemantics` from the external entry point while the working directory resolves the repository fixture path. Shared `Program.cs` has no M6 registration. Run pytest from each service directory using its relevant manifest and environment.

**Unavailable or unexecuted:** real-corpus RAG tests lacked the approved corpus, manifest and index on the H2 host. The database-dependent persistence project and live smoke cases packaged with it were excluded. H2 did not execute a production authorization, transport, persisted job replay, editor-freshness or reviewed-rule end-to-end path. The separate regulatory audit inspected GitHub CI #73 logs for the retrieval backend. Its live RAG verification intentionally used no corpus and is outside the H2 totals.

## H4-R1 regression after PR #35

Both phases used Ubuntu WSL SDK 10.0.115, the established Windows Python service and RAG environments, and phase-specific ignored `obj/h4r1-linux` outputs. The RAG dependency manifests remained unchanged across the one-commit range. The isolated phase used a detached external checkout with all 30 H4 files copied and hash-verified. The active phase ran after fast-forward to the same commit.

| Command or suite | Result in both phases | Isolated / active elapsed |
|---|---|---:|
| NuGet restore | 10/10 projects | 14.07 s / 10.54 s |
| Release build | 10/10, 0 warnings, 0 errors | 55.35 s / 62.75 s |
| Database-free C# solution | 1,234 passed, 0 failed or skipped | 30.24 s / 50.41 s |
| Shared console | 276 passed, 0 failed | 2.50 s / 3.05 s |
| Isolated M6 C# runner | 38 representation + 82 semantic/boundary passed | 18.82 s / 23.64 s |
| Complete Python | 628 passed, 1 warning | 8.49 s / 8.67 s |
| M6 Python subset | 574 passed | 5.15 s / 8.73 s |
| Python `pip check` | No broken requirements | 2.22 s / 4.29 s |
| RAG synthetic-corpus | 139 passed, 1 warning | 9.57 s / 7.50 s |
| RAG `pip check` | No broken requirements | 1.76 s / 2.30 s |

All listed commands exited 0. Project totals were Application 128, Infrastructure 577, Reports 54, Requirements 352 and Workflow 123. The Python and RAG warnings were the existing Starlette `TestClient` deprecation. H4-R1 adds 38 Infrastructure and 71 RAG cases compared with H2. The **2,397** non-repeated checks are 1,234 + 276 + 38 + 82 + 628 + 139, including **694** M6-specific checks. The 574-case subset, pre/post repeats and focused executions are excluded from the total.

An isolated focused run passed **38 C#** `StrictContractTests` (6.46 s) and **71 Python** provider and strict JSON cases (4.66 s), both exit 0. These cover the shared pack, missing and conflicting evidence, exact scope and revision mismatch, unsupported units, raw JSON and request bounds, auth-header handling, and transport, timeout and cancellation. The shared M6 harness remained unregistered. The two M6 runner logs report the same 120 named cases, including all 22 recorded semantic gaps.

The R1 external directory retains `incoming-inventory.txt`, `pre-*` and `post-*` logs, and the isolated checkout. In particular, see `pre-strict-constraints.log`, `pre-rag-constraints.log`, `post-solution.log`, `post-m6cs.log`, and `post-rag.log`. PR #35 GitHub CI run #77 had three successful jobs, separate from the local evidence. The approved real corpus was unavailable for the local run, and the database-dependent project, persisted replay, and real reviewed-rule M6 end-to-end integration were outside scope.

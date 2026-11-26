# BE-M6-D04 test evidence report

Verified baseline `3d72f3767759f6f451457e193bebffd3cb7dddc0`, local branch `feature/m6-layout-diversity-validation`. Python 3.13.17 with Shapely 2.1.2 and GEOS 3.13.1. WSL .NET SDK 10.0.115.

## Original D04 execution

| Gate | Actual result |
| --- | --- |
| D01 Python contract regression | 574 passed, included in full Python |
| D02 area, feasibility, footprint, parcel | 89 passed, included in full Python |
| D03 placement | 71 passed, included in full Python |
| D04 diversity, validation and generation | 64 passed, included in full Python |
| Full Python services | 852 passed |
| RAG synthetic-corpus regression | 238 passed |
| WSL Release build | 10 projects built, zero warnings or errors |
| Database-free WSL solution tests | 1,559 passed. Application 128, Infrastructure 732, Reports 120, Requirements 400, Workflow 179 |
| Shared WSL contract harness | 405 passed |
| Standalone M6 WSL harness | 38 representation and 82 semantic assertions passed |
| Dependency checks | Python and RAG both reported no broken requirements |

Unique suite total **3,174 passed**, zero failed or skipped in the final runs. Focused Python runs are subsets. Repeated executions, build and dependency checks are excluded from the total. The Python and RAG suites emitted the existing Starlette TestClient deprecation warning. The first focused D04 run caught an incorrect test fixture literal for separation. The standalone C# harness first attempted to link `FixtureLocator.cs` from the wrong directory. Both were corrected and rerun successfully.

## Commands

Python service root, existing Windows venv:

- `.venv/Scripts/python.exe -m pytest -q`
- `.venv/Scripts/python.exe -m pytest -q <D01 M6 contract modules excluding test_d02_*, test_d03_*, test_d04_*>`
- `.venv/Scripts/python.exe -m pytest -q <four test_d02_*.py files>`
- `.venv/Scripts/python.exe -m pytest -q tests/m6_contract/test_d03_placement.py`
- `.venv/Scripts/python.exe -m pytest -q tests/m6_contract/test_d04_layout.py`
- `.venv/Scripts/python.exe -m pip check`

RAG root, same existing environment:

- `../python-services/.venv/Scripts/python.exe -m pytest -q`
- `../python-services/.venv/Scripts/python.exe -m pip check`

WSL, repository root:

- `dotnet build BuildMind.sln -c Release`
- `dotnet test BuildMind.sln -c Release --no-build`
- `dotnet run --project tests/BuildMind.Application.ContractTests -c Release --no-build`
- Ran `dotnet run --project` in Release configuration against the original temporary standalone M6 project, `Runner.csproj`. The runner references the Application project and links the two existing M6 scenario sources and `Integration/FixtureLocator.cs`. It calls both scenario registration methods separately.

## Focused evidence

- The reported 10 m × 8 m fixture emits exactly three layouts with pair scores 100, 125 and 150. Every candidate has a 2 m shared wall and an immutable reviewed synthetic evidence payload. Twenty generated parcel sizes reproduce ordering, identities and validation results on repeated runs.
- Permuting room order or serialized fields and changing candidate IDs preserves equivalence. Swapping required slot positions and a meaningful movement accompanied by loss of a shared wall change the comparison. Numerically negligible movements cannot qualify through topology alone.
- Corrupt room bounds, forged footprints, holes, outside containment, collisions, counts, IDs, types, spans, minimum area, mandatory relationships, corner contact, malformed values and geometry limits are exercised.
- Fresh synthetic setbacks pass. Violations beyond 10 nm and conservative-margin fit failures yield different classifications. Missing, unknown, stale, unverified or conflicting evidence yields explicit diagnostics. An old D02 empty-footprint result is rejected after changing the evidence and fresh D02 permits generation.
- Mocked Shapely import and GEOS failures return fixed codes. A forged D03 success with no rooms fails and omitted D03 wall metadata is independently reconstructed. Search with one or two valid results returns zero candidates. Unavailable production M7 provider behavior remains an upstream integration gate. The absent reviewed snapshot locally fails closed.

## Backup verification

The original external backup is named `BuildMind-D04-backup-2026-11-20`. Git reported a complete history and verified the 52-ref bundle. Two archives of the fetched baseline produced the same SHA-256. The deliverable snapshot under `deliverables/` within that backup is checked per file against the workspace, with all six hashes recorded in its `SHA256SUMS.txt`.

| Artifact | SHA-256 |
| --- | --- |
| before-d04.bundle | ddb081b3b48f041191214e21cab9154f01bc019817a40994f156f8c9bf4b5342 |
| baseline.zip | e33cbaf319b502f7b4cc396ed9ec9ccb18c2a93aebf246471682d4969e1223c2 |

## R01 executed regression

The table above records the original D04 run. R01 independently ran the same commands and versions, including the standalone WSL harness and both dependency checks.

| Suite | R01 passed | Skipped |
| --- | ---: | ---: |
| Full Python service | 862 | 0 |
| D01 subset | 574 | 0 |
| D02 subset | 89 | 0 |
| D03 subset | 71 | 0 |
| D04 subset | 74 | 0 |
| Database-free C# | 1,559 | 0 |
| Shared contract harness | 405 | 0 |
| Standalone M6 | 120 (38 representation, 82 semantics) | 0 |
| RAG synthetic | 238 | 0 |

**3,184 unique passing checks** in the historical R01 run, with zero failures or skips. The WSL .NET 10.0.115 Release build passed for 10 projects with zero warnings or errors. Both dependency checks reported no broken requirements. Python and RAG each emitted the existing Starlette TestClient warning about deprecated httpx integration. The 54 Python cases outside the M6 subtotal are 25 M3 schema, 16 M3 operation, 12 M3 advanced geometry and 1 health case, verified by collection. They were included in both the original 852 and R01 862 full totals.

The 10 new D04 tests cover required access with shared-wall, corner and detached geometry, absent and unsupported scope, contradictory adjacency, repeatability and existing failure precedence. The existing suites cover all seven statuses, the exact-three numerical fixture, stale positive and negative evidence, unsupported or conflicting evidence and geometry dependency failure. M3's pre-existing tests include a valid draft door, a missing wall, an opening beyond wall extent and non-finite geometry. These exercise the draft M3 models. D04 graph-positive and graph-negative fixtures require the approved access and candidate-bound opening contracts described in the handoff.

The refreshed eight-file backup is named `BuildMind-D04-R01-backup`. Its `SHA256SUMS.txt` records read-back SHA-256 checks against the workspace copies. The original backup remains separate.

The historical standalone harness directory had expired. R01 recreated the temporary standalone M6 project, `Runner.csproj`, and ran it in Release configuration with `dotnet run --project` from the repository root.

## Final release readiness audit

The focused and full Python runs used the existing Windows virtual environment and the current D04 working files. The .NET checks used WSL SDK 10.0.115 against a temporary archive of tracked HEAD `3d72f3767759f6f451457e193bebffd3cb7dddc0`.

| Check | Verified result |
| --- | --- |
| Focused D04 | 76 passed, zero failures or skips |
| Full Python services | 864 passed, including the 76 D04 tests, with zero failures or skips and one existing Starlette TestClient/httpx deprecation warning |
| .NET Release build | 10 projects built, zero warnings or errors |
| .NET Release tests | 1,559 passed, zero failures or skips |
| Application contract tests | 405 passed, zero failures, including M3 Mutation Fingerprint V1 golden vectors |
| Python dependency check | `pip check` reported no broken requirements |
| RAG synthetic tests | 238 passed in the earlier audit. The final audit did not rerun them. |

Python commands from `python-services`:

```powershell
& .\.venv\Scripts\python.exe -B -m pytest -q -rs -p no:cacheprovider tests/m6_contract/test_d04_layout.py
& .\.venv\Scripts\python.exe -B -m pytest -q -rs -p no:cacheprovider
& .\.venv\Scripts\python.exe -B -m pip check
```

Commands from the temporary WSL archive:

```bash
dotnet build BuildMind.sln -c Release --nologo
dotnet test BuildMind.sln -c Release --no-build --no-restore --nologo
dotnet run --project tests/BuildMind.Application.ContractTests/BuildMind.Application.ContractTests.csproj -c Release --no-build --no-restore
```

The two regressions reproduced the bugs before the fixes. The current tests verify that mandatory adjacency and diversity topology use the same tolerance-aware predicate, duplicate-only search exhausts with one validation, and skipped duplicates still allow a valid triple within three unique validations. The search has a maximum of 260 raw variants and 96 unique candidate validations. Runs with `PYTHONHASHSEED=1` and `PYTHONHASHSEED=777` produced identical outcomes, counters and candidate IDs. Existing fail-closed physical-access behavior is preserved.

The standalone M6 harness remains historical evidence. The final audit records suite results individually.

**Gate 2 OPEN. JC-01–JC-08 joint approval BLOCKED.** M2 mandatory review and M5 official confirmation remain pending. M4 and M7 technical drafts have been received and await owner verification. The 25 joint QA scenarios remain **UNEXECUTED** and the 47 deferred M3 scenarios remain **OUTSTANDING**. Production validator integration is **NOT AUTHORIZED**. D05 remains **ON HOLD** pending its specific authorization.

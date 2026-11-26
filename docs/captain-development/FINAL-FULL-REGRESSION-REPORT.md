# Final full regression report

**Code under test:** `9adfb97` (last code commit; later commits are documentation only).
**Raw logs and exit codes:** `~/bm-captain/results/final-regression/` (WSL), `EXIT-CODES.txt`.

## Final regression run (fresh, after the last implementation change)

| Stage | Result | Exit |
|---|---|---|
| Build (Release, whole solution) | 0 errors, 0 warnings | 0 |
| BuildMind.Application.UnitTests | 255/255 | 0 |
| BuildMind.Infrastructure.Tests (clean environment) | 1192/1192 | 0 |
| BuildMind.Reports.Tests (PDF QA enabled, **0 skipped**) | 164/164 | 0 |
| BuildMind.Requirements.Tests | 456/456 | 0 |
| BuildMind.Workflow.Tests | 197/197 | 0 |
| BuildMind.Application.ContractTests (console) | 27 checks | 0 |
| python-services pytest | 1021 passed | 0 |
| rag-service pytest | 238 passed | 0 |
| rag-service `tests_local` (real approved corpus) | 7 passed | 0 |
| Stack start (API, python, RAG with corpus, Mailpit) | readiness Healthy: mysql, python, rag | 0 |
| Live persistence (MySQL VerifyFull TLS) **with the stack running** | **115/115**; 0 leftover test databases | 0 |
| mTLS negatives API→python | 12/12 | 0 |
| mTLS negatives API→RAG | 12/12 | 0 |
| RAG acceptance (real corpus, 18/18 citations verified on the PDFs) | 33/33 | 0 |
| `smoke_admin.py` | **NOT COMPLETED**: the run was stopped by the host (critically low memory) | — |
| `smoke_journey.py` phases 2–11 | **NOT RUN in this final regression** (stopped before this stage) | — |

**Automated totals (each counted once):**

| Suite group | Tests |
|---|---|
| .NET xUnit | 2264 + 115 live persistence = **2379** |
| Python | **1266** |
| **Total** | **3645**, plus 27 contract checks |

There were **0 failures and 0 skipped tests** in every stage that ran.

## Journey evidence for the final API code (from runs in this execution, not this final regression)

| Run | Code | Result |
|---|---|---|
| Process stack, phases 2–11 (with capture) | `babac7c` | 253/253 |
| `smoke_admin.py` | `babac7c` | 65/65 |
| Containers, phases 2,3,4,6,8,9,10,11 | API image built after `00cfbcb`; the API code is identical to `9adfb97` | **216/216** |
| `smoke_llm.py` (process and container) | `00cfbcb` / `9adfb97` | 10/10 and 10/10 |

The changes between `babac7c` and `9adfb97` touch only the AI proposal path (unit-tested 23/23, benchmarked, `smoke_llm` 10/10, and container-journey verified) and Docker/tooling.

**Outstanding:** a final process-stack `smoke_journey` + `smoke_admin` run at `9adfb97`. It was not restarted after the host stopped the run for memory pressure; rerun on request with `final-regression.sh`.

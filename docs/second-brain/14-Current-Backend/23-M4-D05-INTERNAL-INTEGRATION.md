# BE-M4-D05 — Internal Python integration: design, audit and smoke-test proposal (2026-11-18)

Status: **implemented on `feature/m4-python-service-integration`**. Compiled; mocked-transport tests compiled, **not executed** locally (G17). **G11 (internal transport, authentication, hosting) stays OPEN.**

## Confirmed contract surface (repository facts, no inventions)
- Python FastAPI (`python-services/app/main.py`): only **`GET /health`** → `200 application/json {"service":"buildmind-python","status":"ok"}` (pytest `test_health`; **live-verified**, see below).
- No land, survey, generation, validation or plan-editor HTTP routes or DTOs. No RAG HTTP service. `/internal/v1` PROPOSED (live probe returns 404).

## Implementation summary
`Application/Abstractions/Errors/DependencyFailureException` · `Infrastructure/Integration/InternalServiceClient` + `PythonServiceClient` (`CheckHealthAsync` only) · API wiring through `IHttpClientFactory` · `GlobalExceptionHandler` mapping (timeout → 504 `DEPENDENCY_TIMEOUT`, other → 503 `DEPENDENCY_UNAVAILABLE`) · Python readiness check.

## STEP 08-B security audit (findings fixed)
| # | Finding | Fix | Regression tests |
|---|---|---|---|
| 1 | Path guard rejected only a literal `..`, `:` and `\`, so **percent-encoded traversal** (`%2e%2e`, `%2f`, `%5c`, double encoding), `?query` and `#fragment` passed and were sent as-is (a server or proxy may decode them) | Strict allowlist: `[A-Za-z0-9_-]` segments separated by single `/` (≤256 chars). The resolved URI must keep the base scheme, host, port and path prefix, with no query or fragment | 14 new destination cases |
| 2 | Handler honoured the **system proxy** (`HTTP(S)_PROXY`/`ALL_PROXY`) for private traffic | `UseProxy = false` | transport configuration test |
| 3 | **Unbounded outstanding connections** per internal service | `MaxConnectionsPerServer = 32` (excess requests queue) | same test |
| 4 | Python readiness relied on cancellation only (weaker than MySQL) | Hard `WaitAsync` deadline (3 s) and per-check single-flight gate; an abandoned probe releases the gate only when it finishes | 2 tests |
| 5 | Client trusted any absolute base URL (startup validation only) | Defence in depth: only http/https, with host and without credentials, query or fragment; otherwise the client stays disabled | 6 cases |

**Verified, unchanged:**
- Caller cancellation (`OperationCanceledException`) stays distinct from dependency timeout (`DEPENDENCY_TIMEOUT`).
- One deadline covers connect, headers and the streamed body.
- Responses are disposed on success and failure; chunked or oversized bodies can't bypass the limit (stream counting).
- No retries; concurrent calls are isolated.
- JSON: media type `application/json` or `+json`; empty, truncated, `null`, wrong-shape and wrong-identity bodies are malformed; `MaxDepth = 64` explicit.
- No user `Authorization`/`Cookie` is forwarded.
- `traceparent` is set from the request activity (W3C) and never duplicated.
- Failure messages carry only a fixed dependency name and kind.
- Framework HttpClient logging is removed (it logged internal URLs).
- `SocketsHttpHandler` diagnostics are `EventSource`/`Activity` (not `ILogger`); with no exporter configured they don't reach application logs.
- Development and Production log only the safe EventId 1005 warning for dependency failures.

**Not claimed:** complete SSRF immunity. The destinations are configuration-controlled, so **configuration is the trust boundary**: anyone who can change `Services:*:BaseUrl` or the DNS or network that resolves it can redirect traffic. Production must pin the internal hostnames and private network (G11).

## Live cross-service evidence (2026-11-18, lead workstation)
The real FastAPI app ran under `uvicorn` on `127.0.0.1:18918` (disposable process):
- `GET /health` returned `200 application/json {"service":"buildmind-python","status":"ok"}`, matching the C# validator.
- After stopping the process, the endpoint was unreachable.
- **This proves the Python contract live, NOT C#-to-Python connectivity.** C# execution is blocked locally by Smart App Control (G17). The mocked-transport tests prove client behaviour only.

**Finding for M6 / G11 (not changed by M4):** the FastAPI app serves `/docs` and `/openapi.json` (200) by default. For a private service, disable them outside development (`FastAPI(docs_url=None, redoc_url=None, openapi_url=None)` behind configuration).

## Linux CI smoke test — IMPLEMENTED (STEP 08-C; lead-approved 2026-11-18 in session, recorded below)

_Original proposal kept for history:_
In the existing `database-compatibility` job (or a new job), before the integration tests:
```yaml
      - name: Start the Python service (loopback only)
        run: |
          python -m pip install -r python-services/requirements.txt
          (cd python-services && python -m uvicorn app.main:app --host 127.0.0.1 --port 8099 --log-level warning &)
          for i in $(seq 1 30); do curl -fsS http://127.0.0.1:8099/health && exit 0; sleep 1; done; exit 1
```
Then add `BUILDMIND_TEST_PYTHON_URL: http://127.0.0.1:8099` to the test step env, plus a test class (`PythonLiveSmokeTests`) that calls `PythonServiceClient.CheckHealthAsync` (expects success), then stops the process (or uses a closed port) and expects `DependencyFailureException(Unavailable)`. The test **fails** when the variable is missing (no skips), so it is added only together with the approved workflow change.

## Still blocked
Feature routes and DTOs (M2/M3/M6), internal authentication and transport (G11), `/internal/v1`, the M2 private storage adapter, the RAG contract, and a safe-retry idempotency contract.

## STEP 08-C — approvals, implementation and further audit (2026-11-18)

**Approvals (lead, explicit answer in the development session on 2026-11-18; no other approval is claimed):** (1) the live C#-to-Python smoke-test change to `.github/workflows/backend-ci.yml`; (2) uploading machine-readable test results from the `dotnet-build` (TRX) and `python-tests` (JUnit XML) jobs.

**Workflow changes:**
- `dotnet-build`: `dotnet test … --logger trx --results-directory TestResults/dotnet`; M3 contract-check output captured with `tee` (`pipefail` keeps failures failing); `always()` upload `dotnet-test-results` (14 days).
- `python-tests`: `pytest -q --junitxml=../TestResults/python/pytest-junit.xml`; `always()` upload `python-test-results`.
- `database-compatibility`: `setup-python` 3.13, `pip install -r python-services/requirements.txt` (existing dependencies), **real FastAPI app** via uvicorn on **127.0.0.1:8099** only, bounded readiness (30 × 1 s; hard failure with the log tail if the process exits or never becomes ready), `BUILDMIND_TEST_PYTHON_URL` for the tests, `always()` stop (TERM then KILL). The service log stays in `$RUNNER_TEMP` and is not uploaded.

**Live tests `PythonLiveSmokeTests` (7, production `PythonServiceClient` + production `SocketsHttpHandler`, real loopback sockets):** PY01 real `/health` (identity and status validated); PY02 live validation rejects a wrong identity; PY03 readiness Healthy against the live service; PY04 closed port → `Unavailable`; PY05 silent server → `Timeout` within the bound; PY06 caller cancellation is not a dependency failure; PY07 on-the-wire request carries `traceparent` and no `Authorization`/`Cookie`/`Proxy-*`. Missing variable or non-loopback URL → hard failure. *Dependency failure after stopping the real service* is covered by PY04 (a closed real port) rather than stopping uvicorn mid-run.

**Further defects fixed:**
1. **Unbounded request queue:** `MaxConnectionsPerServer = 32` bounds connections, but `SocketsHttpHandler` queues extra requests without limit (each bounded only in time by the client deadline). → `InternalServiceBulkhead` (singleton): ≤64 in-flight + queued calls per dependency; saturation fails fast as 503 `DEPENDENCY_UNAVAILABLE` without network I/O; slots released on success, failure, timeout and cancellation.
2. **MySQL readiness gate could be held forever:** if disposing the probe scope threw, `Release()` never ran, so every later probe reported `check_in_progress`. → release in `finally` (also on the synchronous probe-failure path).

Tests added: infrastructure +3 (bulkhead saturation/recovery, slot release after timeout and cancellation, gate release when disposal throws) → **388**; MySQL project +7 live smoke → **52**. All compiled; none executed locally (G17).

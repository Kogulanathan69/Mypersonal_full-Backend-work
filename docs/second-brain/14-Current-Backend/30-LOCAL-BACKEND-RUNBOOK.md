# Local backend startup and integration runbook (MVP, development only)

- **Scope:** development and testing on a developer machine. **Not a production deployment guide**: production hosting, TLS termination, authentication and G11 are not decided.
- **Secrets:** use **environment variables or user secrets only**. Never commit values.

## 0. Prerequisites
| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.x (CI pins it with a job-local `global.json`) | `dotnet --info` |
| Python | 3.13 | One virtual environment per service is recommended |
| MySQL | 8.4 LTS | Only needed with `Database__Enabled=true` |
| Approved RAG corpus | The STEP 4 corpus folder (PDFs, extracted records, manifest, index) | Outside the repository; never commit it |

**Windows Smart App Control (G17):** on the lead's machine it blocks locally built .NET assemblies. The API fails with *"An Application Control policy has blocked this file (0x800711C7)"*, verified 2026-11-19.
- **Do not bypass it.**
- Run the C# API and tests on a machine without this policy, in Linux CI, or in an approved Linux environment.
- The Python services run normally.

## 1. Build
```powershell
dotnet restore BuildMind.sln
dotnet build BuildMind.sln -c Release
```

## 2. python-services (M2/M3/M6 FastAPI)
```powershell
cd python-services
python -m venv .venv; .\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
.\.venv\Scripts\python.exe -B -m uvicorn app.main:app --host 127.0.0.1 --port 8000
# check: GET http://127.0.0.1:8000/health -> {"service":"buildmind-python","status":"ok"}
```

## 3. rag-service (M7, evidence contract v1)
```powershell
cd rag-service
python -m pip install -r requirements-dev.txt     # or reuse a venv with the same pins
$env:BUILDMIND_RAG_CORPUS_ROOT = '<corpus root containing input_pdfs\ and output\documents\>'
$env:BUILDMIND_RAG_MANIFEST    = '<...>\output\regulation_retrieval_step4\approved_corpus_manifest.json'
$env:BUILDMIND_RAG_INDEX       = '<...>\output\regulation_retrieval_step4\multi_document_index.json'
$env:BUILDMIND_RAG_INTERNAL_TOKENS = '<32+ random printable chars>'   # G11 MVP; up to 2 comma-separated for rotation
# development-only opt-out (never elsewhere; BOTH lines are required): $env:BUILDMIND_RAG_AUTH = 'disabled'; $env:BUILDMIND_RAG_ENVIRONMENT = 'development'
# optional, human-reviewed constraint table (no real table exists yet): $env:BUILDMIND_RAG_CONSTRAINTS = '<path to table json>'
# required with a table: $env:BUILDMIND_RAG_REVIEWERS = '<path to sealed reviewer registry json>'
# synthetic fixtures only, isolated dev/testing: $env:BUILDMIND_RAG_ALLOW_SYNTHETIC_CONSTRAINTS = '1'
python -B -m uvicorn app.main:app --host 127.0.0.1 --port 8100
# check: GET http://127.0.0.1:8100/health -> {"service":"buildmind-rag","status":"ok","contract_version":"1","corpus_loaded":true}
```
- **Corpus integrity:** start-up fails fast if any approved PDF or record hash doesn't match.
- **Without the corpus variables**, the service runs with `corpus_loaded:false`: search returns 503 and API readiness reports RAG `Degraded/unavailable`.

## 4. MySQL (optional)
1. Create a database and a **non-root, TLS-only** runtime account.
2. Apply the reviewed ranged migration script (Option B; idempotent scripts are a provider limitation). This is the same invocation CI uses. The design-time factory reads `BUILDMIND_MIGRATIONS_CONNECTION`; script generation needs no connection, but the variable must be set:
   ```powershell
   dotnet tool restore
   $env:BUILDMIND_MIGRATIONS_CONNECTION = 'Server=127.0.0.1;Port=3306;Database=buildmind_dev;User ID=<admin>;SslMode=Required'  # no password needed to generate
   dotnet tool run dotnet-ef migrations script 0 --configuration Release `
     --project src/BuildMind.Infrastructure --startup-project src/BuildMind.API `
     --context BuildMindDbContext --output buildmind-from-0.sql
   ```
3. Review the script, then apply it with your MySQL client as an administrator.
4. Remember the TLS policy: `SslMode=VerifyFull` is required in every non-Development environment and for any non-loopback server. `Required` is accepted only for loopback in Development.

## 5. C# API
```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:8080'
$env:Services__Python__Enabled = 'true'; $env:Services__Python__BaseUrl = 'http://127.0.0.1:8000/'
$env:Services__Rag__Enabled = 'true';    $env:Services__Rag__BaseUrl    = 'http://127.0.0.1:8100/'
$env:Services__Rag__AuthToken = '<same value as one BUILDMIND_RAG_INTERNAL_TOKENS entry>'   # required outside Development
# optional database:
# $env:Database__Enabled = 'true'; $env:ConnectionStrings__BuildMind = '<from user secrets / environment>'
dotnet run --project src/BuildMind.API -c Release
```

**Checks:**
- **Liveness:** `GET /health` → `{"service":"buildmind-api","status":"ok"}`.
- **Readiness:** `GET /health/ready`.
  - MySQL is mandatory: not configured or unreachable → **503**.
  - Python and RAG are Degraded-only (never 503). RAG is Healthy only with the right identity, contract v1 and a loaded corpus.

### 5a. API documentation and browser access (Development only; C6/C7 pending lead approval)
- **Swagger UI:** `http://<API>/swagger`; document `/swagger/v1/swagger.json`. Mapped only when `ASPNETCORE_ENVIRONMENT=Development`; other environments return 404.
  - Readiness (`/health/ready`) is a health-check endpoint and does not appear in the document.
  - Product endpoints appear only once their feature modules are registered (none on `develop` today).
- **CORS:** browser calls from another origin need `Cors__AllowedOrigins__0` (more with `__1`, …).
  - The tracked launch profile sets the provisional Angular origin `http://localhost:4200`. When starting with `dotnet run --no-launch-profile` or plain environment variables as above, set it explicitly:
    ```powershell
    $env:Cors__AllowedOrigins__0 = 'http://localhost:4200'   # exact origin only: no '*', path or trailing slash
    ```
  - Invalid entries stop start-up with a message naming the entry. Outside Development only `https` origins are accepted.
  - Without any origin, the API answers same-origin only.
- **Allowed hosts:** the API answers only requests whose `Host` is `localhost`, `127.0.0.1` or `[::1]` (any port). Other hosts get the framework's `400 Bad Request – Invalid Hostname` page.
  - To reach the API through another name or address in Development (for example the WSL IP), widen the list locally without editing tracked files:
    ```powershell
    $env:AllowedHosts = 'localhost;127.0.0.1;[::1];172.25.230.10'   # Development may also use '*'
    ```
  - Outside Development, start-up fails unless `AllowedHosts` lists exact host names (no `*`, no wildcards, no scheme, port or path).
- **Request body limit:** every request body is limited to **1 MiB** (`Kestrel:Limits:MaxRequestBodySize`, bytes). A larger body gets **413** Problem Details `PAYLOAD_TOO_LARGE`; this happens when the endpoint reads the body.
  - Development may use any positive value, for example:
    ```powershell
    $env:Kestrel__Limits__MaxRequestBodySize = '4194304'
    ```
  - Outside Development the value must be between 1 byte and 8 MiB, or start-up fails.
  - Endpoints that need more (for example M2 uploads) get their own per-endpoint limit, approved by the owner and M4. The global value is never raised for them.
- **Rate limiting** (`RateLimiting:*`):
  - **Limits:**
    - anonymous clients 120 requests/min per IP (IPv6 grouped by /64), all anonymous traffic together 3,000/min;
    - signed-in users 300/min per user;
    - endpoints with the `auth-strict` policy 10/min per IP;
    - endpoints marked as expensive 2 in flight per client and 8 overall.
  - **Rejections:** `429` Problem Details `RATE_LIMITED` with `Retry-After`.
  - **Not limited:** `/health`, `/health/ready` and Swagger.
  - **Local load testing:** in Development you may set `RateLimiting__Enabled=false` (refused elsewhere) or raise the numbers through environment variables.
  - **Forwarded headers:** `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` is refused outside Development, because it would trust forwarded headers from any sender. Trusted proxies are a hosting (G11) decision.
  - **Behind a proxy without trusted forwarding**, every anonymous client appears as the proxy and shares one bucket. Resolve this before go-live.
- **Smart App Control (G17):** on the lead's machine, run the API from WSL2 Ubuntu (`/home/buildmind/projects/BuildMind-Platform`, .NET SDK 10.0.1xx), which was used for STEP 05 verification. The Windows MySQL84 service is reachable from WSL through the WSL gateway address (`ip route | awk '/default/{print $3}'`).

## 6. Tests
| Suite | Command | Where |
|---|---|---|
| C# (all solution tests) | `dotnet test BuildMind.sln -c Release` | CI (blocked locally by G17) |
| MySQL + live Python integration | `dotnet test tests/BuildMind.Persistence.IntegrationTests` with `BUILDMIND_TEST_MYSQL`, `BUILDMIND_TEST_MYSQL_RUNTIME`, `BUILDMIND_TEST_PYTHON_URL` | CI `database-compatibility` |
| python-services | `cd python-services; python -m pytest -q` | Local + CI |
| rag-service (synthetic corpus) | `cd rag-service; python -B -m pytest -q -p no:cacheprovider` | Local + CI |
| Full-stack smoke (built API + MySQL + Python + RAG) | `bash tools/smoke/full-stack-smoke.sh` with the API environment variables plus `SMOKE_EXPECT_MYSQL/_PYTHON/_RAG` (`<Status>:<reason>` or `skip`) | CI `database-compatibility`; any machine without Smart App Control |
| rag-service against the real corpus | `cd rag-service; python -B -m pytest -p no:cacheprovider tests_local` (the 3 corpus variables set) | Local only (corpus never in CI) |

## 7. Ports used in this runbook
| Service | Port |
|---|---|
| API | 8080 |
| python-services | 8000 |
| rag-service | 8100 |

All bind to **127.0.0.1**. Do not expose the Python or RAG services publicly. rag-service requires the internal token (doc 31); python-services has no authentication yet. G11 transport and hosting remain open.

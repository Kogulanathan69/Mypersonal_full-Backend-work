# Captain local runbook (Development only)

Everything runs in **WSL Ubuntu-24.04** as user `buildmind`, from the one-way copy `~/bm-captain/src` of the Windows worktree `C:\Users\kogul\Documents\BuildMind-Worktrees\captain-full-backend-completion`. Windows .NET execution is blocked by Smart App Control (G17); it is not bypassed.

Services listen on **loopback only**. Secrets live in `~/bm-captain/secrets` (0700 / 0600) and are never printed.

> wsl.exe re-parses inline `bash -c '…'` (variables expand early). Put commands in a script file and run
> `MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu-24.04 -- bash /mnt/c/…/script.sh`.

## One-time setup

```bash
~/bm-captain/sync.sh                                  # copy the worktree (excludes bin/obj/.venv/.git)
bash ~/bm-captain/src/tools/captain/mysql-setup.sh    # MySQL 8.4.13 container, accounts, Captain CA, VerifyFull TLS
bash ~/bm-captain/src/tools/captain/python-setup.sh   # Python 3.13 venvs (pinned uv 0.9.5) + internal tokens
```

`~/bm-captain/sync.sh` is a one-line `rsync -a --delete` that excludes bin/obj/.venv/.git (see DEVELOPMENT-PROGRESS). The uv binary was downloaded from the official GitHub release and its SHA-256 verified before use.

## Start, verify, stop

```bash
# Build -> migrate -> python-services :8091 -> API :5180 [-> rag-service :8092 + RAG-mode API :5181 when RAG=1].
# Stays in the foreground (a detached wsl.exe session would terminate the services).
RAG=1 bash ~/bm-captain/src/tools/captain/restart-stack.sh
RAG=1 bash ~/bm-captain/src/tools/captain/wait-ready.sh            # in another session
python3 ~/bm-captain/src/tools/captain/smoke_journey.py              # phases 2-8 (8 runs before 7); phase 7 stops python-services at the end
pkill -f BuildMind.API.dll; pkill -f 'uvicorn app.main:app'          # stop (from a script file)
```

The auth-strict rate limiter allows 10 requests per minute: wait at least 60 s between journey runs.

## Configuration set by `run-api.sh` (environment only)

| Setting | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Development |
| `Authentication__Enabled`, `Authentication__RefreshTokens__Enabled`, `Database__Enabled` | true |
| `ConnectionStrings__BuildMind` | `bm_runtime`, `SslMode=VerifyFull` + CA + client certificate (from `env.sh`) |
| `Jwt__*` | issuer/audience `buildmind-captain-local`; key from `secrets/jwt-signing.key` |
| `CaptainIntegration__Enabled` | true (the Captain profile; refused outside Development) |
| `CaptainIntegration__RequirementCatalogueFile` | `docs/captain-development/samples/requirement-catalogue.synthetic.json` |
| `CaptainIntegration__GenerationPolicyFile` | `…/generation-policy.synthetic.json` (or `…rag.json` on :5181) |
| `CaptainIntegration__SurveyStorageDirectory`, `__ReportStorageDirectory` | `~/bm-captain/data/{surveys,reports}` (0700) |
| `CaptainIntegration__ArchitectRecipientsFile`, `__SharingPolicyVersion` | synthetic directory, `captain-sharing-policy-dev-1` |
| `Services__Python__*`, `Services__Rag__*`, `Regulations__Retrieval__Enabled` | loopback URLs + internal tokens (when the ports are given) |

Optional settings: `CaptainIntegration:ValidationFreshnessHours` (default 24) and `CaptainIntegration:CandidateLifetimeMinutes` (default 30).

## Tests

```bash
bash ~/bm-captain/src/tools/captain/unit-suites.sh            # all in-solution .NET suites + contract runner
bash ~/bm-captain/src/tools/captain/persistence-tests.sh      # live MySQL (VerifyFull); leftover-database check
bash ~/bm-captain/src/tools/captain/python-and-pdf-checks.sh  # PDF content check, Reports PDF-QA, pytest suites
```

## Database operations

| Script | Purpose |
|---|---|
| `tools/captain/migrate.sh` | Option B ranged script as `bm_migrator` |
| `tools/captain/add-migration.sh <Name>` | Generate a migration in WSL, then copy it into the worktree; register it in both `CaptainMigrations` lists |

Root is used only inside `mysql-setup.sh`. Never point these scripts at a shared or production server.

## rag-service

rag-service runs **without a corpus** in WSL (the real corpus is BLOCKED, see KNOWN-BLOCKERS). Search, sources and constraints answer 503, and the API fails closed.

## Survey extraction worker (Captain profile)

- Starts with the API only when `Services:Python:Enabled=true`. Disable it with `CaptainIntegration__SurveyWorker__Enabled=false`.
- Tuning keys (`CaptainIntegration__SurveyWorker__*`), with defaults:

  | Key | Default | Allowed range |
  |---|---|---|
  | `PollSeconds` | 5 | 1–300 |
  | `Concurrency` | 2 | 1–8 |
  | `LeaseSeconds` | 180 | 30–3600 |
  | `MaxAttempts` | 3 | 1–10 |
  | `ProcessingTimeoutSeconds` | 150 | 10–1800 |

  `run-api.sh` sets poll 2 s and lease 120 s.
- OCR needs `tesseract` (eng, sin, tam) and `pdftoppm` (poppler-utils) on the python-services host. Without them, OCR degrades to NEEDS_REVIEW with a manual-review diagnostic.
- Real-OCR local tests: `cd python-services && ~/bm-captain/venv-python-services/bin/python -m pytest -q tests_local`.
- Inspect a stuck survey (read-only): `SELECT status, state_version, processing_attempts, processing_lease_until_utc, failure_code FROM surveys WHERE id = ?;`. An expired lease is reclaimed automatically, up to `MaxAttempts`, then `EXTRACTION_ABANDONED`.
- OpenAPI export: `curl -s http://127.0.0.1:5180/swagger/v1/swagger.json` (Development only). The committed copy is `docs/captain-development/openapi.captain-dev.json`.

# Docker container verification

**Verdict: COMPLETE_AND_VERIFIED for a local containerized stack** (API + python-services + MySQL), built and run with
Docker Compose and exercised end to end. This is local verification only: nothing was pushed, published or deployed.
Production remains **NO-GO**.

## Tooling

| Item | Value |
|---|---|
| Docker Compose | plugin **v5.7.0** (released 2026-11-02), installed to `~/.docker/cli-plugins` in WSL; SHA-256 `7758048a…12a2` verified against the release digest |
| Compose file | `deploy/captain/compose.verify.yml` (project `bm-captain-verify`) |
| Driver script | `tools/captain/compose-verify.sh prepare\|build\|up\|down\|reset\|status` |
| Build context hygiene | `.dockerignore` excludes `.git`, bin/obj, test results, venvs, `*.pem`/`*.key`/`*.pfx`/`*.env`, `secrets/`, `tests_local`, the RH09 probe and the docs vault |
| Generated material | `~/bm-captain/compose-verify` (0700, outside the repository): MySQL server cert for hostname `mysql` (Captain CA, 30 days), fresh DB passwords, full migration SQL, least-privilege account init, mTLS material copy, API env |

`docker compose config --quiet` → **valid**.

## Images (built sequentially on the 3.9 GB host)

| Image | Id | Size | User | Base |
|---|---|---|---|---|
| `buildmind-api:verify` | `sha256:059ccfb60f7f…` | 107.5 MB (content) | `app` (uid 1654) | `mcr.microsoft.com/dotnet/sdk:10.0` → `aspnet:10.0` multi-stage |
| `buildmind-python:verify` | `sha256:6921d96506c6…` (rebuilt after the openings fix) | 139.4 MB (content) | `bmpy` | `python:3.13-slim` + tesseract (eng/sin/tam) |

## Runtime configuration (as inspected)

- **MySQL 8.4** (digest-pinned):
  - `--require-secure-transport=ON`;
  - the API connects with `SslMode=VerifyFull` + client certificate to hostname `mysql`;
  - runtime account DML-only; migrator account separate;
  - RH09 containment persisted (`max_execution_time=25000`, `innodb_lock_wait_timeout=20`).
- **python-services:**
  - uvicorn TLS with `--ssl-cert-reqs 2` (client certificate **required**);
  - token from a mounted file (`BUILDMIND_PY_INTERNAL_TOKENS_FILE`);
  - health check uses mTLS with hostname verification.
  - A TLS client **without** a certificate is rejected (curl exit, HTTP `000`).
- **API:**
  - `https://python-services:8091` with the Captain mTLS handler (server-CA-only trust, hostname check);
  - Development + Captain profile, because the profile refuses other environments by design;
  - only the API is published, on `127.0.0.1:8080`;
  - generation policy and survey samples are mounted read-only.
- **Hardening** (`docker inspect`):

  | Container | read-only root | cap_drop | no-new-privileges | memory limit |
  |---|---|---|---|---|
  | API | **true** | `[ALL]` | yes | 700 MiB |
  | python | **true** | — | — | — |

  - Both run as non-root.
  - Writing to `/app` fails with `Read-only file system`.
  - The artefact volume is owned by uid 1654 with mode 0700, set by a one-shot `artifacts-init` container with `network_mode: none`.
  - Networks: `internal` (internal: true) for MySQL/python; the API additionally joins `edge` for the loopback port.
- **Resource use at rest:** API 150 MiB, python 63 MiB, MySQL 456 MiB.

## Evidence

| Run | Scope | Result | Log (WSL) |
|---|---|---|---|
| A | `smoke_journey.py http://127.0.0.1:8080 --phases 2,3,4,6,8,10` (auth, land with CRS, generation with doors/windows, edits, confirmation, PDF report, AI rule-based, CRS frame) | **132 / 132** | `~/bm-captain/results/journey-containers.log` |
| Restart | `docker compose restart` (all services) | users/revisions/reports rows **6/11/1 before = after**; report artefact file persisted; ready = mysql + python Healthy | — |
| B | `--phases 3,4,6,9` after admin bootstrap (GRANTED in-container): architect directory, consent, withdrawal, notifications | **110 / 111** | `~/bm-captain/results/journey-containers-3469.log` |

Runs A and B overlap on phases 3, 4 and 6, so they must not be summed.

## Defects found by container verification (fixed)

1. **Idempotent EF migration script rejected by MySQL.** `migrations script --idempotent` emits `IF NOT EXISTS(...)` blocks outside stored procedures (ERROR 1064).
   - The init now applies the full ordered script, which runs only on an empty volume.
   - The local stack uses ranged scripts, which are unaffected.
2. **Edit validation regression.** A 10 % resize of a room left an existing window facing the interior circulation hall, and Python validation rejected the edit as `WINDOW_NOT_ON_EXTERIOR` (CRITICAL). The edit is valid; the window placement has become a design problem.
   - A window between one room and interior circulation or a sub-passage gap is now `WINDOW_FACES_INTERIOR_SPACE` (WARNING, so the customer must acknowledge it).
   - A window between two rooms stays CRITICAL.
   - A door opening into sub-passage space is `DOOR_OPENS_INTO_UNUSABLE_SPACE` (WARNING).
   - This was a real regression: phase 4 had not been rerun after the CRS frame commit changed the land ring. It is not container-specific.
3. Missing generation-policy env and CA path translation in the container env generator.

## Not verified / gaps (honest)

- **Email inside containers:** the one run B failure ("outbox delivered the notification email to the local mail capture").
  - The delivery policy only allows **loopback** SMTP without `AllowExternalDelivery` + STARTTLS, and inside a container the capture host is not loopback. Email is therefore not configured in the container stack; the policy was deliberately not weakened.
  - Email delivery is verified on the process stack (Mailpit).
- **RAG** is not part of the container stack (STEP 8B on hold). Journey phases 5 (RAG) and 7 (python outage, which controls local processes) were not run against containers.
- **Base images not digest-pinned:** `dotnet/sdk:10.0`, `aspnet:10.0` and `python:3.13-slim` are tag references. Production must pin digests and scan images; no image vulnerability scan was run (no scanner installed).
- `python.Dockerfile`'s built-in plain-HTTP `HEALTHCHECK` is overridden by the mTLS health check in compose. The API image has no `HEALTHCHECK`; compose orchestration uses `depends_on` health.
- `deploy/captain/compose.staging.yml` (secret-file layout for a staging host) was **not** run. Only `compose.verify.yml` was built and exercised.
- The LLM runtime is not containerized (it runs as a loopback process; see REAL-LLM-INTEGRATION-REPORT).

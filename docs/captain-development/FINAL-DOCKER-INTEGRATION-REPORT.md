# Final Docker integration report

**Verdict: complete local containerized backend stack, COMPLETE_AND_VERIFIED locally** (commit `9adfb97`). This is not a deployment; production stays NO-GO.

## Services (`deploy/captain/compose.verify.yml`, project `bm-captain-verify`)

| Service | Image | Security | Health / dependency |
|---|---|---|---|
| mysql | `mysql:8.4@sha256:a81d…` (digest-pinned) | TLS required; VerifyFull clients; least-privilege accounts; RH09 containment | healthcheck; init script applies the full migration script on an empty volume |
| python-services | `buildmind-python:verify` | non-root, read-only root, cap_drop ALL, no-new-privileges; mTLS required | mTLS healthcheck |
| rag-service | `buildmind-rag:verify` (pinned constraints) | runs as the corpus owner; read-only root; staged corpus mounted **read-only** and re-verified at start; mTLS required | mTLS healthcheck (start_period 30 s) |
| api | `buildmind-api:verify` (non-root uid 1654) | read-only root, cap_drop ALL; loopback-only port 8080; mTLS client for python and RAG; SMTP STARTTLS with platform validation | waits for healthy mysql, python and RAG, artefact init and mailpit |
| mailpit | `axllent/mailpit:v1.21.8` | STARTTLS **required**; Captain-CA certificate; loopback UI port 8026 | — |
| llm (profile `llm`) | `buildmind-llm:verify` (llama.cpp b12237) | runs as the model owner; read-only root; internal network only | restart policy |
| artifacts-init | API image, `network_mode: none` | one-shot volume ownership | — |

Networks: `internal` (internal-only) for mysql, python, RAG and LLM; `edge` for the API and mailpit's loopback publishing. Generated secrets and certificates live in `~/bm-captain/compose-verify` (0700), never in the repository; `.dockerignore` keeps keys and venvs out of build contexts.

## Commands (reproducible)

```bash
bash tools/captain/compose-verify.sh build          # LLM=1 also builds the llm image
bash tools/captain/compose-verify.sh prepare        # certs, passwords, env (LLM=1 adds AiEdit settings)
bash tools/captain/compose-verify.sh up             # full stack (without llm)
LLM=1 bash tools/captain/compose-verify.sh llm-up   # start llm + recreate api in LLM mode
bash tools/captain/compose-verify.sh status | down | reset   # reset = disposable volumes only
```

## Evidence

| Check | Result |
|---|---|
| `docker compose config` | valid |
| Images built | api, python, RAG (233 MB), llm (214 MB) |
| Readiness | **Healthy** for mysql, python and RAG (RAG over mTLS inside the network) |
| Journey phases 2,3,4,6,8,9,10,11 against `:8080` | **216/216**, including email through the TLS-required capture (**fixes the previous 110/111**), the completion routes, revert and sessions |
| RAG acceptance against the container API | **33/33**, 18/18 citations verified on the original PDFs; retrieval median 0.16 s |
| In-network mTLS | client without a certificate → refused (curl 56); with the Captain client certificate → 200 |
| Containerized LLM | `smoke_llm` **10/10**: proposal applied as revision 2; injection and unrelated refused; stale 409; model killed → 503; container auto-restarted |
| Crash recovery | a real WSL VM crash: after the daemon restarted, every service came back (restart policies), readiness was Healthy, and data persisted (users 23 / plan revisions 21 / reports 3) |
| Memory at rest (no LLM) | RAG 434 MB, MySQL 474 MB, API 83 MB, python 67 MB, mailpit 13 MB; LLM 1.36 GB when enabled |

## Not covered (honest)

- Journey phases 5 (needs a second API instance in `rag-constraints` mode) and 7 (controls host processes) were not run against containers; phase 5's retrieval checks are covered by the container RAG acceptance.
- The LLM container and the RAG container were not run **simultaneously** on this 3.9 GB host. Doing so twice caused WSL memory wedges during heavy host-side processing, so they were verified in sequence.
- Supply chain: the base images other than MySQL are tag-referenced (not digest-pinned); no image scan or signing (E-9).
- `compose.staging.yml` (secret-file layout for a staging host) is not exercised.

# Captain external input request (consolidated)

Raised 2026-11-25 under the final full-backend execution order. These are the **only** items the implementation cannot produce itself. All local development and testing continues without them.

**How to supply secrets:**
- Never paste a secret into chat, an issue or a Git-tracked file.
- Place each value in a file under `~/bm-captain/secrets/` (WSL) with mode `0600`, then reply only with "supplied: <item id>".

| # | Input | Why / feature that depends on it | Continues without it? | Secure configuration | Verification once supplied |
|---|---|---|---|---|---|
| E-1 | **External LLM provider** (name, model, base URL), an **API key** and an **approved monthly usage budget** | External-model NL editing quality and latency (the local model needs about 2 min per proposal on this host) | Yes: the local llama.cpp model is integrated and benchmarked | `~/bm-captain/secrets/llm.key`; `CaptainIntegration:AiEdit:Provider=openai-compatible`, `BaseUrl`, `Model`, `ApiKeyFile` | `tools/captain/llm_benchmark.py` against the provider, with acceptance/latency compared to the local model; no charges beyond the approved budget |
| E-2 | Data-transfer approval for E-1: send **minimised plan summaries** (room names, types, sizes and opening aliases only; no ids, names, addresses or survey files) | Required before any byte leaves the machine | Yes | written approval | the benchmark log shows the payload shape |
| E-3 | **SMTP provider**: host, port, STARTTLS support, username, password, and a **verified sender address/domain** (SPF/DKIM) | Real delivery of password recovery, report-ready, architect and withdrawal emails | Yes: Mailpit local capture, inside and outside containers | `~/bm-captain/secrets/smtp.password`; `CaptainIntegration:Email:*` with `AllowExternalDelivery=true` + `UseStartTls=true` | Delivery of each notification kind to E-4, with provider message ids recorded |
| E-4 | **One approved test recipient mailbox** | Proof of receipt | Yes | operator-provided address (not committed) | Reset code received and used successfully |
| E-5 | **Regulation reviewer sign-off**: a named reviewer and role for the reviewer registry, and reviewed constraint values (setbacks, coverage, etc.) taken from the 3 approved documents with page/clause citations | Regulation-**verified** constraints in generation (today: retrieval is real, but constraint values are unreviewed, so they are reported as unverified) | Yes: retrieval, citations and evidence binding work; generation uses labelled synthetic setbacks | sealed reviewer registry + versioned constraint table (pinned SHA-256), outside Git | `RagConstraintAcceptanceTests`; generation reports `REVIEWED` evidence |
| E-6 | Any **additional approved regulation PDFs** beyond the 3 approved sources (UDA Gazette 2235/54, Colombo DP, Maharagama DP) | Coverage of other local authorities | Yes | add to the approved source list through M7 approval (new pins) | stage → verbatim loader verification → retrieval tests |
| E-7 | **Representative survey plans** (English/Sinhala/Tamil) with ground-truth coordinates, authorised for local processing | OCR/extraction accuracy measurement | Yes: pipeline, sandbox and synthetic fixtures | private directory outside the repository | per-language accuracy report |
| E-8 | **Licence decision** for the MySQL provider (MySql.Data GPLv2 + FOSS exception) vs alternatives (G03) | Production dependency approval | Yes | decision record | — |
| E-9 | **Publication authorization**: remote push of the Captain branch, and the CI owner's approval to add a workflow | CI execution and independent review | Yes (local only) | explicit instruction | CI run reproduces the final regression |

Excluded scope (cost estimation, BOQ, 3D, payments) needs no inputs.

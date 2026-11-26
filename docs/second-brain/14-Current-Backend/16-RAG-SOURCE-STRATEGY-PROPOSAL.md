# RAG source version control and integration — PROPOSAL

Status: **PROPOSED, not approved** (BE-M7-D01, 2026-11-17). Resolves nothing in G10/G11 until the lead approves. Do not modify, move or rewrite the existing engine. Evidence: [2026-11-17 audit](../17-Progress/07-AUDIT-2026-11-17.md).

## Starting point (verified by static inspection)

The candidate canonical engine `C:\BuildMind-PDF-Processor\regulation_retrieval` is a standard-library BM25 retrieval package over **one** indexed Gazette, with a Python function API and no HTTP route. It sits inside a ≈12 GB extraction workspace (source PDFs, extraction output, indexes, backups, multiple venvs) with **no version control**. Older copies exist in a separate staging workspace.

## Step 1 — Confirm canonical source (lead)

1. Lead confirms `regulation_retrieval` (and whether `regulation_review_workflow`, `regulation_human_evaluation` are in scope).
2. Produce a read-only file inventory with SHA-256 for every candidate code file; diff against the newest staging copies; record differences. No file moves.

## Step 2 — Code-only version control (options)

| Option | Summary | Pros | Cons |
|---|---|---|---|
| **A (recommended)** | New **private** repository (e.g. `BuildMind-RAG-Engine`) created in a new, empty directory; code copied in (never moved) | Private by default; independent history; no PDF risk to the public backend repo | Second repo to coordinate |
| B | Code-only copy under public `BuildMind-Platform/rag-service/` | Single repo | Repo is public — requires licence/security review of code, manifest and fixtures first |
| C | Git submodule of option A inside `rag-service/` | Pinned version in backend repo | Submodule workflow overhead; still needs A |

Include: package `.py` files, `tests/`, READMEs, `config/` manifest **only after review** (contains document IDs, hashes and relative source paths). Exclude: `input_pdfs/`, `output/` (extracted text, indexes, evaluation results), `.venv*`, `__pycache__/`, `backup_*`/`*_backup.py`, step staging folders, evaluation datasets until licence review. `.gitignore` committed **before** any file is added. Copy verification: hashes of copied files equal source hashes; source workspace unchanged.

## Step 3 — Verify in isolation (requires approval to execute)

Re-run the existing tests from the new code-only checkout with `python -B`, outputs directed to a new temporary directory, recording count/result as **newly verified** evidence distinct from the historical 93-test log. Retrieval regression against the existing index is run from the canonical workspace only with explicit approval, writing to a new output directory (the CLI already refuses to overwrite).

## Step 4 — Integration design (M7 with M4/M6; contract not frozen)

```text
C# Application (M7 use case) --port--> Infrastructure typed HTTP client (M4-D05)
    --internal auth, timeout, correlation ID--> internal retrieval service (M7)
        --> RegulationRetrievalService.search(...) over a prebuilt, read-only index
M6 generation/feasibility consumes the same evidence contract; deterministic checks stay authoritative.
```

Hosting choice for the internal retrieval route (open, G11): (a) a separate private FastAPI service addressed by existing `RAG_SERVICE_URL`; or (b) a router in `python-services` registered via the M6 bootstrap custodian. Index building stays an offline, separately versioned step; request-time retrieval never rebuilds or writes.

Evidence contract draft fields (BE-M7-D03 to freeze), mapped from current output:

| Field | Available today | Note |
|---|---|---|
| document_id, source_pdf | Yes | Hash-based ID |
| page (one-based), char offsets, excerpt | Yes | Unicode offsets, not printed page labels |
| score | Yes (BM25) | Relevance, **not legal confidence** |
| jurisdiction, publication_date | Yes, from metadata sidecar (may be null) | Assertions, not verified |
| authority, version/effective date, amendment status | Partial / no | Needs reviewed metadata |
| applicability status | No | Must default to `unverified`; never inferred |
| evidence gaps / no-hit | No structured form | Required for honest no-answer |

Required tests before integration is DONE: known source page round-trip, no hit, ambiguous/unknown jurisdiction, obsolete/conflicting source, unreadable OCR page, adversarial embedded instructions, timeout/unavailable service, no fabricated constraints.

## Embeddings

Not implemented. Any embedding model, vector store, cost and privacy approach is a separate future decision; BM25 remains the verified baseline.

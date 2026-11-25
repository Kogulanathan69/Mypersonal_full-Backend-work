# Real regulation RAG report

**Verdict: BLOCKED_BY_EXPLICIT_PROTECTED_SCOPE** (STEP 8B ON HOLD; real-corpus activation is M7/STEP 8B work, P-11, input I-4).
- Real retrieval is **not** complete.
- A healthy rag-service and correct fail-closed 503 responses are verified, but per the order they do not count as completion.
- No protected file was modified: rag-service, `RagServiceClient`, the M7 registration guard, the approved-source pins and the manifest are all unchanged. No regulation text was invented.

## What is verified (fail-closed behaviour only)

- Journey phase 5 (process stack, `RAG=1`):
  - retrieval requires authentication;
  - without a verified corpus, retrieval returns 503 with **no passages**;
  - generation does not claim regulatory compliance and plans are labelled with synthetic setbacks.
- The rag-service dependency audit is clean: pip-audit found 0 known vulnerabilities in 14 packages (`~/bm-captain/results/audit/pip-audit-rag-service.json`).

## Root cause of the corpus blocker (read-only diagnosis, 2026-11-25)

1. `rag-service/regulation_retrieval/approved_sources_step4.py` pins each approved source's `source_pdf` as a **Windows-style relative path**, for example `Other_Documents\2235-54_E (1).pdf`.
2. `corpus_step4.validate_manifest` requires each manifest entry's `source_pdf` to **equal** the pin exactly. Any separator normalisation fails with `Approved identity/scope mismatch: source_pdf`, which is correct identity enforcement.
3. `corpus_step4.load_step4_corpus` builds the filesystem path as `root/'input_pdfs'/source_pdf`. On Linux (WSL, where the API must run because of G17 Smart App Control), the backslash is an ordinary filename character, so the path names a non-existent file `Other_Documents\2235-54_E (1).pdf` inside `input_pdfs`.
4. Corpus data integrity was checked read-only on Windows. All three approved PDFs exist under `C:\BuildMind-PDF-Processor\input_pdfs\Other_Documents\`, and each SHA-256 equals its pinned document id:

   | Source | SHA-256 = pinned id |
   |---|---|
   | Gazette 2235-54 | `14e82932…ef0f7` ✔ |
   | Maharagama Development Plan | `e480ad2f…a55f3` ✔ |
   | Colombo | `eee5bb47…d34c7` ✔ |

The blocker is therefore **only** path resolution on POSIX, not data integrity.

## Exact change request (requires lifting STEP 8B / M7 approval; not performed)

| Option | Change | Identity controls | Owner |
|---|---|---|---|
| **A (no code)** | Stage a POSIX corpus root whose `input_pdfs` directory contains the three approved PDFs under their **literal pinned names**, i.e. a filename containing the backslash character, as POSIX permits. The manifest, pins and records stay byte-identical. | Fully exercised: pin equality, PDF SHA-256, record SHA-256, page counts and metadata evidence | M7 approval of the execution layout, then the Captain runs it |
| **B (code, protected file)** | In `load_step4_corpus`, resolve the filesystem path from `PureWindowsPath(e['source_pdf']).parts` while still comparing the **unchanged** pinned string for identity | Unchanged semantics; adds platform-independent resolution | M7 (protected scope) |
| C | Run rag-service on Windows (Python) and reach it from WSL | Unchanged | Needs WSL networking change (NAT mode) and an approved Windows execution path |

After activation, these acceptance tests are required:
- `RagLiveSmokeTests` and `RagConstraintAcceptanceTests`;
- journey phase 5 real-retrieval checks: passages carry document id, page and record hash; refusal outside scope;
- regulatory review of the extracted constraints (G06). Setbacks stay synthetic until a reviewed constraint set exists.

Edge-role assignment in the land frame (all edges are currently `UNASSIGNED`) also depends on reviewed regulations: which edge is the road frontage drives setbacks.

## Honest limitations

- No real retrieval, ranking quality or citation correctness was measured in this execution.
- The approved sources carry `legal_status = not_verified`. Even after activation, outputs are preliminary guidance, not legal approval.

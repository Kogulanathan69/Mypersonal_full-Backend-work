# RAG service (M7): private regulation evidence retrieval

**MVP status (2026-11-19, `feature/m7-rag-retrieval-service`): implemented for development and CI.** Not production-approved.

**Internal authentication (G11 MVP):** every endpoint except `/health` requires `X-BuildMind-Internal-Token`, configured with `BUILDMIND_RAG_INTERNAL_TOKENS` (1–2 values of 32+ characters; fail closed). The development-only opt-out is `BUILDMIND_RAG_AUTH=disabled`. See `docs/second-brain/14-Current-Backend/31-INTERNAL-SERVICE-AUTH.md`. Transport and hosting are still open, so keep the service on loopback or a private network.

## What it does
- **Endpoint:** `POST /internal/v1/regulations/search`, evidence contract **v1** (lead-approved 2026-11-19).
- **Input:** a query, plus optionally an exact jurisdiction label and approved document IDs.
- **Output:** ranked source passages with **citations** (document, PDF page, character offsets, excerpt) and an explicit **evidence status**:

| Status | Meaning |
|---|---|
| `EVIDENCE_FOUND` | At least one passage meets the evidence checks |
| `AMBIGUOUS_EVIDENCE` | Passages exist but need review; they are returned, flagged |
| `NO_MATCHING_EVIDENCE` | Nothing supports the query, or requested sources are only partly covered |
| `INSUFFICIENT_JURISDICTION_EVIDENCE` | The jurisdiction isn't supported by an approved source |
| `INSUFFICIENT_SOURCE_EVIDENCE` | The requested source isn't in the approved corpus |

- **What it never does:** generate answers, claim legal currency or applicability, or approve compliance. `legal_status_verified` and `answer_generated` are always `false`.
- **Location filtering:** exact approved jurisdiction labels only. No alias or locality inference; an unknown location is reported, never guessed. `GET /internal/v1/regulations/sources` lists the labels.
- **Liveness:** `GET /health` returns `{"service":"buildmind-rag","status":"ok","contract_version":"1","corpus_loaded":bool}`.
- **Hardening:**
  - `/docs`, `/redoc` and `/openapi.json` are disabled;
  - request bodies are limited to 16 KB;
  - validation errors never echo input;
  - unexpected errors are a generic 500;
  - with no corpus configured, search and sources return `503 CORPUS_UNAVAILABLE`.

## Engine (reused, not rebuilt)
`regulation_retrieval/` is a **verbatim code-only copy** of the existing engine's runtime modules: STEP 4 BM25 retrieval with jurisdiction filtering, and the STEP 6 evidence assessor and grouping. The source and the SHA-256 of every file are in `regulation_retrieval/PROVENANCE.json`, and a test enforces that the copy stays byte-identical. The original workspace is unchanged.

**This repository is public. It must never contain:**
- PDFs, extracted text, indexes or embeddings;
- evaluation datasets or personal data;
- keys.

The corpus is located at runtime and verified by the reused loaders (original-PDF and record SHA-256 checks against the approved allowlist):

| Variable | Points to |
|---|---|
| `BUILDMIND_RAG_CORPUS_ROOT` | Corpus root containing `input_pdfs/` and `output/documents/` |
| `BUILDMIND_RAG_MANIFEST` | The approved STEP 4 manifest (`approved_corpus_manifest.json`) |
| `BUILDMIND_RAG_INDEX` | The prebuilt STEP 4 index (`multi_document_index.json`) |

## Run locally (loopback only)
```powershell
cd rag-service
python -m pip install -r requirements-dev.txt
$env:BUILDMIND_RAG_CORPUS_ROOT='C:\BuildMind-PDF-Processor'
$env:BUILDMIND_RAG_MANIFEST='C:\BuildMind-PDF-Processor\output\regulation_retrieval_step4\approved_corpus_manifest.json'
$env:BUILDMIND_RAG_INDEX='C:\BuildMind-PDF-Processor\output\regulation_retrieval_step4\multi_document_index.json'
python -B -m uvicorn app.main:app --host 127.0.0.1 --port 8100
```
C# configuration: `Services__Rag__Enabled=true`, `Services__Rag__BaseUrl=http://127.0.0.1:8100/`. The Application port is `IRegulationEvidenceSource`, implemented by `RagServiceClient`.

## Tests
- **CI (no corpus):** `python -B -m pytest -q`.
  - It runs the **real copied** retrieval, assessment and grouping code over a clearly **synthetic** two-council corpus.
  - It covers API hygiene and the shared contract example (`contract/search_response.v1.example.json`, also checked by the C# tests).
- **Local real corpus (read-only):** `python -B -m pytest -p no:cacheprovider tests_local` with the three variables set. It fails, never skips, if they're missing.

## Known limitations
- Lexical BM25 only. There are no embeddings; lexical false positives and negatives are possible.
- Evidence statuses are heuristic and are not semantic entailment.
- The corpus has **three** approved documents (UDA Gazette 2235/54, the Colombo and Maharagama development plans). Their legal currency and amendments are unverified.
- Maharagama naming ambiguity makes its passages `AMBIGUOUS_EVIDENCE` by design.
- Parcel-level applicability is never decided.

## Regulatory constraints (contract v0, synthetic only; pending M6 + lead approval)
- **Endpoint:** `POST /internal/v1/regulations/constraints` returns a human-reviewed constraint evidence pack bound to exact project, land and requirement revisions. See doc 29 §9.
- **Table:** `BUILDMIND_RAG_CONSTRAINTS` points to a versioned table JSON. Every citation must reconstruct from the loaded corpus, or the service refuses to start. **No real regulatory values are in this repository**; tests use `tests/fixtures/synthetic_constraints.v0.json`.
- **Strict request parsing:** every POST is validated as raw JSON first (16 KB streamed limit, `application/json`, no duplicate keys, no NaN, depth ≤ 32).

## Constraint integrity (2026-11-19; pending M6 + lead review)
- **Canonical digest:** BM-JCS-1 (`app/canonical_json.py`); shared vectors in `contract/canonical_json_vectors.v1.json`.
- **Reviewer authorization:** `BUILDMIND_RAG_REVIEWERS` points to a sealed reviewer registry, which is required whenever a table is configured. Each sign-off must come from a registered, active reviewer with the matching role, scoped to the jurisdiction and kind, within the authorization period.
- **Pins for real tables:** `BUILDMIND_RAG_CONSTRAINTS_VERSION`, `BUILDMIND_RAG_CONSTRAINTS_SHA256` and `BUILDMIND_RAG_REVIEWERS_SHA256`.
- **Synthetic tables:** only with `BUILDMIND_RAG_ALLOW_SYNTHETIC_CONSTRAINTS=1` (isolated development and testing).
- **Fixtures:** `python tools/build_contract_fixtures.py [--check]` regenerates or verifies every synthetic fixture, including the shared M6 setback scenarios.

## Cross-service acceptance (test-only)
- `tests/acceptance_app.py` serves each shared scenario through the real app (auth required) for the C# live acceptance tests. It is never part of the deployed service and refuses to start without `BUILDMIND_ACCEPTANCE_SYNTHETIC=1` and a token.
- The auth opt-out `BUILDMIND_RAG_AUTH=disabled` now also requires `BUILDMIND_RAG_ENVIRONMENT=development`.
- 422 responses are a fixed `{"code":"VALIDATION_FAILED"}`.

## Workload bounds (C10)
`BUILDMIND_RAG_MAX_CONCURRENT_WORK` (1–64, default 4) and `BUILDMIND_RAG_WORK_TIMEOUT_SECONDS` (1–60, default 10) bound engine work:
- over budget → 504 `WORK_TIMEOUT`;
- no free slot → immediate 503 `SERVICE_BUSY`;
- a slot is freed only when its work has actually finished.

Invalid values fail start-up.

## Version prefix parity
A synthetic reviewer registry must use a `synthetic-` registry version, and a real one must not. This matches the C# receipt policy.

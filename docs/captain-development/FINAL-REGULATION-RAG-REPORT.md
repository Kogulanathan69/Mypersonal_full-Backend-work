# Final regulation RAG report

**Verdict: real regulation retrieval is COMPLETE_AND_VERIFIED locally (process stack; container verification in FINAL-DOCKER-INTEGRATION-REPORT).**
- STEP 8B was lifted by the Captain for this workspace.
- Regulation-**verified constraint values** remain BLOCKED_BY_EXTERNAL_DEPENDENCY: they need a named human reviewer's sign-off (E-5).
- Nothing in this system is a statutory approval or professional certification.

## Source resolution (the WSL/Linux blocker, resolved without weakening identity)

`rag-service/tools/stage_approved_corpus.py` (outside the provenance-pinned verbatim engine) does the following:
1. Validates the approved manifest with the verbatim `validate_manifest` (pins, scope, approval metadata).
2. Locates each PDF by interpreting its Windows-style pin, and refuses unless its SHA-256 equals the pinned document id.
3. Writes the PDF at the exact path the unchanged loader computes on this OS.
4. Copies the extracted records, refusing unless each SHA-256 equals the manifest's `record_sha256`.
5. Copies the Gazette metadata sidecar, the manifest and the index; makes the files read-only; writes `STAGING-RECEIPT.json` with every SHA-256.
6. Re-runs the verbatim loader and evidence assessor.

Pins, manifest, records and engine are byte-identical to the approved sources.

| Approved source | Pages | SHA-256 (= pinned id) |
|---|---|---|
| UDA Gazette 2235/54 (2021) | 155 | `14e82932…ef0f7` |
| Maharagama Development Plan | 568 | `e480ad2f…a55f3` |
| City of Colombo Development Plan 2022–2031 | 462 | `eee5bb47…d34c7` |

## What runs

- **rag-service** (unchanged M7 engine):
  - STEP 4 BM25 retrieval with exact-label jurisdiction filtering;
  - STEP 6 evidence assessment (`EVIDENCE_FOUND`, `AMBIGUOUS_EVIDENCE`, `NO_MATCHING_EVIDENCE`, `INSUFFICIENT_JURISDICTION_EVIDENCE`, `INSUFFICIENT_SOURCE_EVIDENCE`);
  - citations: document, page, character offsets, excerpt;
  - `legalStatusVerified=false` and `answerGenerated=false` always.
- **Transport:** API → RAG over **mutual TLS** (Captain server CA only + hostname check + client certificate), plus the internal token. Served by uvicorn with `--ssl-cert-reqs 2`.
- **Index:** the approved prebuilt STEP 4 index (`multi_document_index.json`, about 50 MB), persisted on disk and reloaded and re-verified on every start.
- **Readiness:** `rag` Healthy only when the verified corpus is loaded; otherwise Degraded, and retrieval returns 503 (fail closed).

## Evidence (process stack)

| Check | Result | Log |
|---|---|---|
| `tests_local` (real corpus, read-only) | **7/7** | — |
| `tools/captain/rag_acceptance.py` | **33/33** | `~/bm-captain/results/rag/rag-acceptance.log` |
| Citations re-extracted from the **original PDFs** with pypdf (excerpt words on the cited page ≥60 % and above a distant page) | **18/18** | same |
| Jurisdiction filter (Colombo only) | pass | same |
| Unsupported query → `NO_MATCHING_EVIDENCE`; unknown jurisdiction → `INSUFFICIENT_JURISDICTION_EVIDENCE` | pass | journey phase 5 |
| Unapproved document id refused | pass | rag_acceptance |
| Tampered extracted record refused by the unchanged loader (identity mismatch) | pass | rag_acceptance |
| Instruction-like query retrieves source text only (`answerGenerated=false`) | pass | rag_acceptance |
| API→RAG mTLS negatives (`MTLS_TARGET=rag`) | **12/12** | `~/bm-captain/results/rag/mtls-rag.log` |
| Journey phase 5 (real retrieval) | **24/24**; full journey 253/253 | `~/bm-captain/results/final2/journey.log` |
| Retrieval latency through the API | median 0.33 s, max 0.85 s (n=6, warm) | rag_acceptance |
| Restart persistence | corpus re-verified and reloaded on every stack restart (several restarts this execution, including after the WSL VM crash) | readiness Healthy after each |

## Honest limitations

- **Lexical BM25, no embeddings.** The approved engine is lexical. "Semantic retrieval / embeddings / vector index" are **not** implemented. Adding an embedding model would change the approved M7 engine and needs a model choice, an evaluation dataset and M7 approval; it is recorded, not invented.
- **Conflicting evidence** between the Colombo and Maharagama plans is surfaced (sources are kept separate, with `UNRESOLVED_APPLICABILITY_OR_SOURCE_CONFLICT` warnings) and never resolved automatically.
- **Generation's "rag-constraints" evidence mode** still fails closed: no **reviewed** constraint table exists. Extracting setback numbers from retrieved text without a human reviewer would be inventing regulation values. Generation uses labelled synthetic setbacks. Input E-5.
- **Edge roles** (road frontage, side, rear) are not inferred from regulations (see FINAL-SURVEY-OCR-CRS-REPORT).
- **Corpus versioning, re-ingestion and admin management:** re-staging is idempotent and hash-checked (a changed source is refused, not overwritten). Approving a new source version requires new M7 pins (E-6). There is no admin upload endpoint, by design: unapproved documents must not enter the corpus.
- Legal currency and amendments of the three sources are unverified (`legal_status = not_verified`).

## STEP 8B: authorization basis and exact changes (for review)

**Authorization basis.** The Team Captain's "FINAL FULL BACKEND IMPLEMENTATION AND INTEGRATION EXECUTION ORDER" §3 states:
- "Regulation RAG STEP 8B development | GO";
- "The previous Regulation RAG STEP 8B development hold is explicitly lifted by the Team Captain for this Captain development workspace. This authorization supersedes the earlier STEP 8B development restriction."

The authorization is scoped to this Captain workspace. It does **not** authorize changes on shared branches; M7 review is still required before any merge.

**Exact changes made under it** (all in this branch):

| Change | Where | Nature |
|---|---|---|
| Approved-corpus staging tool | `rag-service/tools/stage_approved_corpus.py` (new; outside the verbatim engine) | copies approved PDFs/records after SHA-256 verification against the pins; runs the verbatim loader + assessor |
| rag-service runtime: staged corpus + TLS with required client certificate | `tools/captain/run-rag.sh`, `tools/captain/pki.sh` (rag-server leaf) | configuration only |
| API → RAG mutual TLS | `src/BuildMind.API/Composition/CaptainLocalIntegration.cs` (attaches the internal mTLS handler to `RagServiceClient` when the RAG URL is https) | composition only; **`RagServiceClient` code unchanged** |
| Container image / compose service for rag-service | `deploy/captain/rag.Dockerfile`, `rag.constraints.txt`, `compose.verify.yml` | packaging |
| Report evidence appendix | `src/BuildMind.Infrastructure/CaptainIntegration/EvidenceCitingReportRenderer.cs` (consumes `IRegulationEvidenceSource`) | consumer of the existing port |

**Unchanged:**
- `rag-service/app/*`;
- `rag-service/regulation_retrieval/*` (verbatim engine; the provenance test passes: rag-service suite 238 passed);
- approved-source pins, manifest, records and index;
- the M7 registration guard;
- the evidence contract v1.

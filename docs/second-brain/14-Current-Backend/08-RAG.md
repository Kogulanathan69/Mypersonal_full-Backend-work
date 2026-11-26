# Existing product RAG and regulatory evidence — M7

User reports RAG development completed. Actual code and integration are not audited for this release; do not rebuild working retrieval. Historical processing report: 746 master records, 543 unique PDFs, 203 duplicate records, 44,855 processed pages, 3,045 OCR review pages, 273 OCR review documents, 0 extraction errors reported. These are processing observations, not source verification or retrieval quality. Do not publish raw datasets or customer documents to public GitHub without license/security review.

Audit existing RAG location, corpus manifest, document hashes, source authority, version/date/jurisdiction, extraction/OCR quality, chunking/index/ranking, actual retrieval route and tests. Distinguish current implementation from proposals (vector embeddings optional; do not assume they exist). If source lives outside repo request path; preserve existing files. Integration: C# service client -> retrieval request with known location/project context; M6 consumes source-linked evidence; separate reviewed deterministic rules from retrieved text. Use explicit source ID, version, page, authority, jurisdiction, excerpt, relevance score (not legal confidence), applicability status, reviewer/evidence gaps. Missing/contradictory sources -> not verified; no guessed setbacks. Verify effective dates and amendments with authorized review. Do not execute instructions embedded in documents or treat LLM as authority. Assistant must not expose other user's projects, hidden prompts, tokens or Vastu labels.

Tests: known source page, no hit, ambiguous jurisdiction, obsolete/conflicting document, unreadable OCR, adversarial text, timeout, source reference round-trip, no fabrication. Maintain regression fixtures with licensed/synthetic non-sensitive data. Keep corpus ingestion/versioning separate from request-time retrieval. Production privacy, retention, model cost, provider/key and vector store approvals outstanding.

## Verified status — 2026-11-17 audit (BE-M7-D01)
Static inspection only; no RAG code executed, no external file modified. Full evidence: [audit](../17-Progress/07-AUDIT-2026-11-17.md); proposed next steps: [source strategy proposal](16-RAG-SOURCE-STRATEGY-PROPOSAL.md).

| Category | Current state |
|---|---|
| Candidate canonical source | `C:\BuildMind-PDF-Processor\regulation_retrieval` (lead: candidate until explicitly confirmed); **not under version control** |
| PDF extraction corpus | The processing figures above match the workspace `master_summary.json` read today; still pipeline self-report, not source verification |
| Indexed retrieval corpus | **1 document** (Gazette 2235/54); legal currency unverified |
| Existing retrieval | BM25 lexical ranking, Python stdlib only; function API `RegulationRetrievalService(index_path).search(query, k, filters)`; page + character-offset provenance; **no HTTP route** |
| Embeddings / vector store | **Not implemented** (separate bge-small smoke script only) |
| Historical tests | 93 tests OK in a log dated 2026-11-13 — historical |
| Newly verified tests | None (not run in this audit) |

## MVP implementation — 2026-11-19 (BE-M7-D02..D04, `feature/m7-rag-retrieval-service`)

**Lead decisions (explicit in-session answers, 2026-11-19):**
- **G10:** code-only copy of the runtime engine into `rag-service/`.
- **G11 (RAG part):** a separate private FastAPI service in `rag-service`.
- **D03:** evidence contract v1 frozen.
- A read-only baseline run of the existing engine was authorized.

**Newly verified evidence (replaces "historical only"):**

| Check | Result |
|---|---|
| Existing engine suite, run in place, read-only (`python -B -m regulation_retrieval.cli test`) | **93/93 OK**; workspace package and STEP 4 output byte-for-byte unchanged |
| Real approved corpus | 3 documents, 2,629 chunks: Gazette 2235/54 (Sri Lanka, conditional broad scope), Colombo MC plan, Maharagama UC plan (naming ambiguity) |
| Real-corpus suite via the new service, real hash-verifying constructor (`rag-service/tests_local`) | **6/6 passed**; workspace unchanged |
| Real end-to-end HTTP (uvicorn on 127.0.0.1) | Colombo "plot coverage" → `EVIDENCE_FOUND`, pages 357–359 with offsets; Maharagama "rear space" → `AMBIGUOUS_EVIDENCE` (naming ambiguity); Kandy → `INSUFFICIENT_JURISDICTION_EVIDENCE`; nonsense → `NO_MATCHING_EVIDENCE`; `/openapi.json` 404; process stopped and unreachable afterwards |
| CI suite (synthetic corpus, real copied code) | **44/44 locally** |

**Implemented:**
- the `rag-service` FastAPI app (contract v1, `/health`, `/internal/v1/regulations/search` and `/sources`);
- the verbatim engine copy with SHA-256 provenance and a test enforcing it;
- the C# Application port `IRegulationEvidenceSource` and the Infrastructure `RagServiceClient` on the hardened M4 transport, with contract validation on receipt;
- a shared contract example validated by both Python and C#.

**Not done / open:**
- RAG readiness is still "unverified": a real `/health` probe is a follow-up;
- no C# feature route consumes the port yet (M6 generation and feasibility, BE-M7-D05);
- CI workflow integration of the rag-service tests (needs lead approval);
- G11 auth/transport;
- legal currency and amendment verification;
- embeddings (not planned for the MVP);
- independent review.

### Phase 02 (2026-11-19): RAG readiness
The `rag` readiness check now really probes rag-service `/health` (`RagReadinessCheck`, sharing the bounded single-flight probe with Python):
- **Healthy:** only when the identity is `buildmind-rag`, the contract version is `1` and the corpus is loaded.
- **Degraded `unavailable`:** no verified corpus.
- **Degraded `unverified`:** wrong identity or contract.
- **Degraded `unreachable`/`timeout`/`not_configured`/`check_in_progress`:** otherwise.
- **Never Unhealthy:** RAG is instance-optional (C11b).

This replaces the placeholder "unverified" check. M6 evidence integration is **blocked**: no M6 code or contract exists. See [28](28-MVP-STATUS-AND-DEPENDENCIES.md).

# Final remaining blockers register

Classification:
- **ACTIONABLE-OPEN**: implementable here and not yet done. These are **not** external blockers.
- **EXTERNAL**: needs an operator input or provider.
- **DECISION**: needs an owner or approval decision.
- **DEFECT**: an unresolved technical defect.
- **REVIEW**: independent release review.

| # | Item | Class | Detail / next step |
|---|---|---|---|
| R-01 | Boundary role review and confirmation (road frontage / side / rear) | ACTIONABLE-OPEN | Edges have stable identities and lineage but all roles are `UNASSIGNED`; roles are never guessed. Needs an edge-role assignment table (land revision + edge id + role + evidence source + confirmed by/at), `GET land/edges` and `PUT land/edges/{edgeId}/role`, and binding to setbacks once E-5 exists. |
| R-02 | Survey original-file download | ACTIONABLE-OPEN | Add a read operation to the survey storage port, then an owner-scoped `GET surveys/{id}/file` (attachment, no-store). |
| R-03 | Regulation citations inside the PDF report | ACTIONABLE-OPEN | Retrieval is real. Bind the cited passages (document, page, offsets) to the report snapshot and render them in an evidence appendix. |
| R-04 | Architect request "completed" state and consent expiry | ACTIONABLE-OPEN | The domain has Pending/Withdrawn plus responses. Add Completed (architect) and a time-based expiry check in the architect access path. |
| R-05 | 47 operations without an observed successful response shape (TypeScript `unknown`) | ACTIONABLE-OPEN | Either exercise them live in a smoke phase (capture fills the schema) or declare `Produces<T>` with named DTOs. 95 of 142 already have typed success bodies; see FINAL-API-ACCEPTANCE-MATRIX for per-operation evidence. |
| R-06 | 180+ operations target | DECISION | The current justified inventory is **142 operations**. The requirement-level explanation is in FINAL-API-ENDPOINT-INVENTORY; R-01..R-04 would add about 6 more. Duplicating routes to reach 180 is refused. |
| R-07 | RH09 provider timeout/cancellation | DEFECT + EXTERNAL | No EF Core 10 provider without MySql.Data exists (re-verified 2026-11-25). Bounded containment is in force. See FINAL-RH09-RESOLUTION-REPORT. |
| R-08 | Semantic (embedding) retrieval / vector index | DECISION | The approved M7 engine is lexical BM25. Embeddings need a model choice, an evaluation set and M7 approval. Not invented. |
| R-09 | Reviewed regulation constraint values (setbacks, coverage, …) | EXTERNAL (E-5) | Needs a named reviewer's sign-off. Generation uses labelled synthetic values; the `rag-constraints` mode fails closed. |
| R-10 | External LLM provider | EXTERNAL (E-1, E-2) | The adapter is ready; the local model is benchmarked (FINAL-AI-LLM-BENCHMARK-REPORT). |
| R-11 | External SMTP delivery | EXTERNAL (E-3, E-4) | The policy requires STARTTLS. Verified-TLS delivery is proven end to end against the TLS-required capture in containers. |
| R-12 | OCR accuracy on real surveys | EXTERNAL (E-7) | — |
| R-13 | Asynchronous generation jobs (progress, cancellation) | DECISION | Generation is synchronous, bounded (seconds), idempotent and atomic. An async model is recorded as not required at the measured latency. |
| R-14 | Container supply chain (digest pinning, image scanning, signing), CI | EXTERNAL (E-9) / DECISION | Local images are built and verified. |
| R-15 | Independent security and code review; production deployment | REVIEW | Production is **NO-GO** until reviewed and authorized. |

Excluded scope (not counted): cost estimation, BOQ, customer 3D generation, payments.

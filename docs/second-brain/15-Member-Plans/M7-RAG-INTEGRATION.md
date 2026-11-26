# M7 — lead, RAG integration and QA (proposed)

RAG author reports development complete; avoid rebuilding. If taking M4 too, reconcile six slots with real roster; preserve existing progress. Day schedule subject to capacity.

| Task | Day | Deliverable | Acceptance evidence |
|---|---|---|---|
| BE-M7-D01 | 1 | Audit existing RAG and repos; approve scope/ownership/contracts | Branch/hash, file inventory, gaps |
| BE-M7-D02 | 2 | Verify corpus metadata, extraction and retrieval tests | Real source IDs/page trace; OCR gaps |
| BE-M7-D03 | 3 | Freeze evidence interface and jurisdiction status | Contract examples, no-hit/conflict tests |
| BE-M7-D04 | 4 | Integrate existing RAG with C# and Python through approved adapter | Auth, timeout, provenance contract tests |
| BE-M7-D05 | 5 | M6 feasibility/generation evidence integration | Known/unknown evidence distinct; no invented constraints |
| BE-M7-D06 | 6 | Cross-service QA, review and regression, docs sync | Actual integrated test evidence; unresolved gaps |
| BE-M7-D07 | 7 | E2E gate, independent review/merge, demo release readiness report | Explicit go/no-go, CI logs, PRs; no self-certification |

Shared RAG adapter infrastructure M4; retrieval implementation M7; M6 authoritative geometry and reviewed-rule validation. M7 reviews rather than replacing individual modules. No push/merge without approval. See [RAG](../14-Current-Backend/08-RAG.md).

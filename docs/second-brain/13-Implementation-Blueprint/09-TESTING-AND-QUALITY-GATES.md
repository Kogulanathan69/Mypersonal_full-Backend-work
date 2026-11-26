> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Testing and release gates

Everyone tests their own files; M7 coordinates evidence, not the sole QA implementer.

| Owner | Mandatory tests |
|---|---|
| M1-M3 | Angular component/service tests, error/loading, route/access tests, editor undo/redo. |
| M4 | dotnet build/test, auth/ownership/security, project status, admin/staff approval, migration smoke. |
| M5 | requirements persistence, revision save/confirm, source checks, reports, architect consent, C# ↔ Python integration. |
| M6 | pytest for survey parsing, geometry topology, invalid overlap/out-of-bounds, feasibility, exactly-3-distinct-or-infeasible. |
| M7 | RAG evidence/citations/relevance/jurisdiction/versions, NOT_VERIFIED handling, contract and E2E acceptance. |

Required high-risk acceptance: invalid geometry edit atomic rollback; concurrent edit protection; no plan confirmation on stale validation; staff never publishes without admin approval; RAG retrieval alone never leads to regulatory PASS; no fabricated plan 3 or government certification; security of survey files and report links.

`Done` ≠ accepted. Mark `QA_ACCEPTED` only after merged PR, test output, reviewed contract fixture, and the actual feature scenario succeeds. Store commit SHA / test evidence in [[EVIDENCE-REGISTER]].

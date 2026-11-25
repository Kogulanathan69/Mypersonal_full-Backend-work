# Final generation and validation report

Details: DOOR-WINDOW-GENERATION-REPORT.md. The generator is real geometry code (`python-services/app/internal/generation.py`, `openings.py`); no static example plans are returned.

| Capability | Status | Evidence |
|---|---|---|
| Requirements catalogue (versions, publish, retire), room counts per floor, spaces, relationships (adjacency), design method | IMPLEMENTED, LIVE_VERIFIED | journey; Requirements.Tests 456 |
| Requirement revisions + history (`GET requirements/revisions[/{id}]`) | IMPLEMENTED, LIVE_VERIFIED | journey 11 |
| Land + requirements revision binding (generation requires the expected current revisions; stale → 409) | IMPLEMENTED, LIVE_VERIFIED | journey 3 |
| Generation jobs (idempotency key; replay returns the same job), status, listing (`GET generation-jobs`) | IMPLEMENTED, LIVE_VERIFIED | journey 3/11 |
| **Three distinct** feasible alternatives or a truthful `INFEASIBLE` with a reason code; layout diversity by seed | IMPLEMENTED, LIVE_VERIFIED | journey 3/8/10 |
| Room placement, wall geometry, circulation (residual space ≥0.9 m), entrance, internal doors (BFS access tree), windows on exterior portions | IMPLEMENTED, LIVE_VERIFIED | test_openings 13; journeys |
| Geometry validation with severity: CRITICAL blocks; WARNING requires acknowledgement; unverified regulatory conditions are labelled (`SYNTHETIC` / not reviewed) | IMPLEMENTED, LIVE_VERIFIED | journey 4 (apply without acknowledgement → 422) |
| Alternative selection → plan + revision 1 | IMPLEMENTED, LIVE_VERIFIED | journey 3/8 |
| Regulation evidence binding | Retrieval is real (FINAL-REGULATION-RAG-REPORT). Constraint **values** in generation stay synthetic and labelled until a reviewed table exists (E-5). The `rag-constraints` mode fails closed (503). | journey 5 |
| Background / asynchronous generation with progress, cancellation, recovery | NOT IMPLEMENTED as async. Generation is synchronous within a bounded request (seconds), persisted atomically with its idempotency key, so a retried request replays and never duplicates. An async job model is unnecessary at the measured latency; recorded as a design decision. | — |
| Alternative ranking | Alternatives are returned in generator order with per-alternative diagnostics. No score-based ranking is claimed. | — |

## Severity policy review (order phase 09)

The interior-facing window rule (`WINDOW_FACES_INTERIOR_SPACE` = WARNING, `27e327d`) was reviewed and kept:
- An edit that leaves a window facing circulation is a ventilation/daylight **design issue the customer must acknowledge**. It is not invalid geometry.
- A window between two rooms, a door with the same space on both sides, overlapping openings, or an opening off its wall stay **CRITICAL**.
- Generation never produces a WARNING-level window placement: windows are placed only on exterior portions.
- No ventilation or access requirement was weakened. No regulation-based ventilation minimum exists to enforce (E-5).

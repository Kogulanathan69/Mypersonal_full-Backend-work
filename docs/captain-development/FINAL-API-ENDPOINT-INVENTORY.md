# Final API endpoint inventory

**142 operations / 120 paths** (exported from the running API at `9adfb97`, `openapi.captain-dev.json`). The exact method + path list, with per-operation **live runtime evidence** (positive / negative / 401), is FINAL-API-ACCEPTANCE-MATRIX.md; the typed contract is `contracts/buildmind-api.d.ts`.

| Area | Operations |
|---|---|
| Administrator | 36 |
| Customer: plans, editor, AI, confirmation | 17 |
| Account & sessions | 13 |
| Staff | 11 |
| Customer: requirements | 10 |
| Customer: land, CRS, revisions | 8 |
| Authentication | 7 |
| Architect | 6 |
| Customer: generation | 6 |
| Customer: surveys & extraction | 6 |
| Customer: architect requests & consent | 5 |
| Customer: projects & lifecycle | 5 |
| Notifications | 4 |
| Customer: reports | 4 |
| Architect directory | 1 |
| System | 1 |
| Customer dashboard | 1 |
| Regulation evidence | 1 |

## The 180+ target: requirement-level explanation

The order asks for 180+ operations "where justified" and forbids duplicates, aliases and inflation.

- This order added the operations the approved workflows lacked (+15).
- Admin list oversight already exists as `/admin/monitoring/{kind}`, so per-entity admin list routes would be duplicates.
- Liveness already exists as `/health`, so `/health/live` would be an alias.
- The remaining justified, not-yet-implemented operations are:

| Item | Operations |
|---|---|
| R-01 edge-role review | 2 |
| R-02 survey file download | 1 |
| R-04 architect request completion | 1 |

That gives about **146**. Excluded scope (cost, BOQ, 3D, payments) is not counted. **Reaching 180 would require duplicating existing operations; this is refused.**

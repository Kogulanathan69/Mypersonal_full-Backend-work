# Validation module

**Owner M6** — authoritative plan validation under [V3 ownership](../../../docs/second-brain/14-Current-Backend/10-OWNERSHIP.md) (reconciled 2026-11-17, G16): footprint, room overlap, verified minimum dimensions, connectivity/access, opening placement, requirement match and evidence status. Results bind the geometry hash, land/requirement revisions and rule/engine versions.

Proposed internal route (not frozen): `POST /plans/validate` — see [05-API-CONTRACTS](../../../docs/second-brain/14-Current-Backend/05-API-CONTRACTS.md). Consumers: M6 generation and M3 editor candidates. M2 land validation stays in `geometry/land`. Regulatory evidence comes from M7 and stays `unverified` unless reviewed; never invent rules. Vastu is an internal non-blocking heuristic only and never blocks or is shown.

M6 is also the proposed custodian of `app/main.py` and shared Python dependencies.

Interface and implementation pending.

<sub>Historical: this file previously asked to coordinate routes with M4/M5; superseded by V3.</sub>

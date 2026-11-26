# Plans module

Shared by two owners under [V3 ownership](../../../docs/second-brain/14-Current-Backend/10-OWNERSHIP.md) (reconciled 2026-11-17, G16):

| Sub-area (candidate path) | Owner | Responsibility |
| --- | --- | --- |
| `plans/editor` (V3 proposed path) — **actual M3 code lives in [`app/plan_editor`](../plan_editor/README.md)** (provisional, merged in `b03430e`) | **M3** | Pure, deterministic edit transformations (add/move/resize/remove rooms, walls, openings) applied to a copy; returns a candidate only |
| `plans/generation` | **M6** | AI-assisted plan generation: exactly three distinct feasible alternatives on supported success, or a truthful failure with diagnostics |

M6 is the proposed custodian of `app/main.py` and shared Python dependencies; register routers through that custodian. Proposed internal routes (not frozen): `POST /plans/editor/apply` (M3), `POST /plans/generate` (M6) — see [05-API-CONTRACTS](../../../docs/second-brain/14-Current-Backend/05-API-CONTRACTS.md). Authoritative validation of edited candidates is M6 (`validation/`); M3 must not duplicate it. C# owns persistence, versions and confirmation.

Interface and implementation pending.

<sub>Historical: this file previously read "Owner M6" for the whole module and asked to coordinate with M4/M5; superseded by V3.</sub>

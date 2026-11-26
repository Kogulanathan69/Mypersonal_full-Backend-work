# Geometry module

Shared by two owners under [V3 ownership](../../../docs/second-brain/14-Current-Backend/10-OWNERSHIP.md) (reconciled 2026-11-17, G16):

| Sub-area (candidate path) | Owner | Responsibility |
| --- | --- | --- |
| `geometry/land` | **M2** | Land boundary normalization and validation: CRS, closure, self-intersection, zero area, area/perimeter from metric polygon |
| `geometry/footprint` | **M6** | Buildable footprint and pre-design feasibility from known constraints |

M6 is the proposed custodian of `app/main.py` and shared Python dependencies; register routers through that custodian. Proposed internal routes (not frozen): `POST /geometry/land/validate` (M2), `POST /geometry/footprint/feasibility` (M6) — see [05-API-CONTRACTS](../../../docs/second-brain/14-Current-Backend/05-API-CONTRACTS.md). Canonical geometry schema and tolerances are open (G07); see [07-GEOMETRY](../../../docs/second-brain/14-Current-Backend/07-GEOMETRY.md).

Interface and implementation pending. Functions operate on explicit snapshots and never write customer state. Do not duplicate land validation in M6 or feasibility in M2.

<sub>Historical: this file previously read "Owner M6" for the whole module and asked to coordinate with M4/M5; superseded by V3.</sub>

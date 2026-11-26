# Application feature slices

> Reconciled 2026-11-17 (BE-M7-D01) with [V3 ownership](../../../docs/second-brain/14-Current-Backend/10-OWNERSHIP.md). Previously this file assigned all feature slices (including floor plans, revisions and confirmations) to M5; that V2-era assignment is superseded.

**Existing feature code (as of develop `b03430e`):** `PlanEditor/` (M3) — provisional contracts, versioning and confirmation behind ports, no endpoints/DI/persistence; see [`PlanEditor/README.md`](PlanEditor/README.md). All other folder names below are V3 candidates, not existing code; confirm exact names with the owner before creating them.

| Candidate feature folder | Owner slot |
| --- | --- |
| `Auth`, `Users`, `Projects` | M1 |
| `Survey`, `Land` | M2 |
| `PlanEditor` (exists, provisional; includes versioning and confirmation) | M3 |
| `Requirements`, `Dashboard`, `Reports`, `ArchitectRequests` | M5 |
| `Generation` (jobs, alternatives, selection) | M6 |
| Regulatory evidence use cases | M7 (shared client infrastructure from M4) |

Each slice owns its request/response DTOs, validation, handlers and tests. Shared abstractions in `../Abstractions/` and any persistence, DI or migration change are coordinated by M4. Do not duplicate another slot's business logic.

Existing code note: `../Abstractions/IProjectRepository.cs` is a starter interface for the M1 project domain; it has no implementation (verified 2026-11-17).

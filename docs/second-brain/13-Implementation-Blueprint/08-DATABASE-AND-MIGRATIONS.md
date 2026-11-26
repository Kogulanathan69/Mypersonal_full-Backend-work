> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Database and EF Core migration ownership

**Current proposed target: MySQL 8.x.** Older vault documents mention MSSQL; see [[15-OPEN-DECISIONS-AND-RECONCILIATION]]. No actual schema is claimed implemented.

| Area | Primary owner | Integration rule |
|---|---|---|
| Shared `BuildMindDbContext.cs` and `Migrations/**` | **M4** | M5 cannot independently generate/commit simultaneous migrations; send feature entity/config file and migration request to M4. |
| Identity, users, projects, staff proposal workflow | **M4** | Owner verified by authenticated session; admin approval required before staff changes apply. |
| Requirements, survey metadata, land revisions, floorplans, plan revisions, confirmation, reports, architect requests | **M5** | Own feature entities and EF configs, M4 serializes schema migrations. |
| Geometry computation, validation, alternatives | **M6** | No direct write to MySQL authoritative project tables. |
| RAG source registry/index + citations | **M7** | Not a substitute for transaction DB. Preserve source version and provenance. |

**Critical transaction invariant:** accepted plan edit commits new immutable revision, validation+hash and current pointer together; rejection changes none of them. Confirmation points to exact validated revision. Stale revision must conflict. Migration tests: upgrade, rollback plan, duplicate data, FK constraints, backups, test restore, unauthorized cross-project access.

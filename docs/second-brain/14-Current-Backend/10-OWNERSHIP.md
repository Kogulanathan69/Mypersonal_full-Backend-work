# Single-writer module ownership

These are six proposed backend work packages, not final physical roster. M7 is lead/RAG integration. If M7 also takes M4, one slot/person mapping remains unresolved: confirm reassignment or capacity before publishing. Do not infer votes or count M4/M7 twice as separate people. Existing frontend assignments remain unless lead approves changes.

| Slot | Business responsibility | Branch |
|---|---|---|
| M1 | Auth, users, projects | feature/m1-auth-projects |
| M2 | Survey, land, location, geometry normalization | feature/m2-survey-land |
| M3 | Editor transformations, versions, confirmation | feature/m3-plan-editor |
| M4 | Shared C# infrastructure, DB, security, service clients | feature/m4-infrastructure |
| M5 | Requirements, dashboard, PDF, architect requests | feature/m5-requirements-reports |
| M6 | Feasibility, generation lifecycle/selection, authoritative validation | feature/m6-ai-generation |
| M7 | Existing RAG, evidence contract, integration, QA, review | feature/m7-rag-integration (proposed) |

| Path family (proposed; inspect actual) | Primary writer | Review / boundary |
|---|---|---|
| src/BuildMind.API/Endpoints/Auth,Users,Projects; Application/Features equivalents; Identity business handlers | M1 | M4 security |
| src/.../Endpoints/Survey,Land; Application/Features equivalents; python-services/app/survey, geometry/land | M2 | M4/M6, no generation logic |
| src/.../Endpoints/PlanEditor,PlanVersions; Application/Features equivalents; python-services/app/plans/editor | M3 | M6 validation; M4 transactions |
| Program.cs, shared project files, DI, DbContext, migrations/snapshot, middleware, shared clients/security | M4 | M7 + affected feature owner |
| src/.../Endpoints/Requirements,Dashboard,Reports,ArchitectRequests; Application/Features equivalents | M5 | M1 owner, M3 confirmed version |
| src/.../Endpoints/Generation; Application/Features/Generation; python-services/app/plans/generation, geometry/footprint, validation | M6 | M3, M7, M4 |
| Python bootstrap/main.py, shared config and dependency lock | M6 proposed | M2/M3/M7 request changes |
| actual RAG source path (discover), evidence schema and QA contracts | M7 | M6/M4; no copy/rewrite |
| Feature entities, mapping classes and module tests | owning slot | M4 coordinates schema changes |
| CI and central docs | M4/M7 coordinated, designate one writer per file | affected owners |

Never create a second generation orchestrator in M4/M5, duplicate editor validation in M3, duplicate auth business logic in M4 or duplicate RAG retrieval in M6. M1 identity/business ownership; M4 shared auth infrastructure. M6 owns generation selection; M3 consumes selected reference. Exact class names and API routes are candidates until real code audit. Admin ownership gap remains.

Actual-path note (2026-11-17, develop `b03430e`): M3 code exists at `src/BuildMind.Application/Features/PlanEditor/`, `python-services/app/plan_editor/`, `tests/BuildMind.Application.ContractTests/` and `docs/proposals/m3-plan-editor/`. The table above lists proposed paths; reconciliation pending (G18).

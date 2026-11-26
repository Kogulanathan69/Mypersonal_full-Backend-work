> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Seven members × seven days: exact implementation assignments

Planning board only; estimates require team confirmation. All tasks must link to issue ID, owner, branch, PR, tests and daily deliverable. Every developer maintains their own unit tests.

| Day | M1 Angular Auth/Projects | M2 Angular Survey/Requirements | M3 Angular Plans/Editor | M4 C# Foundation/Identity | M5 C# Feature Workflows | M6 Python Survey/Geometry | M7 Lead/RAG |
|---|---|---|---|---|---|---|---|
| **1** Foundation | Angular routing, navbar, core theme | Reusable inputs/upload/stepper | Editor Konva canvas shell | Solution layers, DI, core entities | Feature entity schema, independent EF configs | FastAPI core, schemas, /health | RAG architecture, source taxonomy, branch rules, contracts |
| **2** Auth | Home, login, register + contract integration | Forgot/reset forms and validation | Dashboard cards + project progress | Auth/login/register/reset/JWT + tests | Dashboard summary, user profile, list read model | Survey parsing fixtures + safe upload schemas | RAG extraction, metadata and OCR-needed review; contract oversight |
| **3** Projects/Survey | New project create/list form | Survey upload and progress/error UI | Land preview map shell | Projects create/update/get + ownership | Secure survey storage, upload job state | Survey parser, boundaries/measurement confidence | RAG chunking and citations; review survey API error schema |
| **4** Land/Requirements | Ground room counters | Mapbox land confirmation and edit | Additional spaces + draft | Land ownership and project state checks | Land confirmation + ground requirements APIs | Polygon topology, area/perimeter; reject uncertain | Embed/index corpus; region/authority metadata; verify provenance |
| **5** Requirements/Generation | Relations/summary/design method | AI generation loading, 3 options | Alternative comparison/preview | Contracts: project permissions, job state, audit | Requirements/three-plan/select APIs | Generate exactly three valid distinct options or explain infeasible | RAG retrieval API, evidence schema, applicability NOT_VERIFIED |
| **6** Editor | Save/version UI | Editor select/move/resize/undo | AI edit preview, apply/reject, properties | Revision concurrency and auth review; integrate context patch | Edit/save/version API, Python adapter | Geometry edit validation, collision tests, atomic reject | RAG+Python integration; source accuracy tests, PR integration checks |
| **7** Confirmation | Final plan review and confirmation UI | PDF report/architect request UI | Request status, editor polish | Revision-specific confirm auth, integration and release gate | Reports, architect requests, PDF API | Regression geometry & validation; feasibility limits | End-to-end audit, RAG evaluation, team triage, release evidence |

## Daily mandatory synchronization
- 09:00 — 15-minute standup: completed yesterday, today tasks, blockers. Team Lead M7 updates delivery tracker.
- 09:15 — contract freeze for the day: API/geometry/RAG schema versions, fixtures, owner acknowledgement.
- 13:00 — integration checkpoint: FE talks to C# stub/real endpoints, C# to Python mock/real; update blockers.
- 17:00 — author PR with issue ID, changed paths, tests, screenshot/demo and API fixture evidence.
- 17:30 — reviewer approves; CI passes; single integration owner merges; M7 records accepted delivery.

## FE↔BE pairing (same-day dependency)
| Path | FE owner | C# owner | Python/RAG owner | Shared boundary |
|---|---|---|---|---|
| Login/register | M1 | M4 | — | auth JSON + error codes |
| Project dashboard | M1/M3 | M4/M5 | — | project statuses |
| Survey upload | M2 | M5 | M6 | upload job/status + uncertainty |
| Land/map | M2 | M4/M5 | M6 | GeoJSON geometry/units |
| Requirements | M1/M3 | M5 | M6 | room schema + constraints |
| 3 alternatives | M2/M3 | M5 | M6, M7 for evidence | generation result + validation |
| 2D edit | M2/M3 | M5 | M6 | edit command, optimistic concurrency |
| Regulations | M2/M3 | M5 | **M7 RAG; M6 applies deterministic checks** | rule evidence vs actual compliance |
| Confirmation | M1 | M4/M5 | M6 validation | revision hash / result |
| Report/architect | M2/M3 | M5 | M7 evidence if cited | report version, consent |

## Definition of ready
Before FE starts consuming an endpoint: endpoint owner supplies schema, examples (success, validation error, unauthorized, conflict, async pending), authorization rule, idempotency/retry expectations, evidence provenance when regulatory. If backend isn't ready, FE can use fixed contract-matching mock and label it as mock.

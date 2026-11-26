> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind Backend — Implementation Blueprint / Start Here

> **Version:** 2.0 draft, 2026-11-16. **Nature:** Proposed implementation and team coordination architecture, NOT a claim that these folders/classes/API endpoints already exist. The original Second Brain documents under folders 00–12 are preserved as historical source snapshots. The current decisions below supersede conflicting older technology/scope references for this proposed implementation; formal ADR approval and repository inspection still required.

## Read in order
1. [[01-SOLUTION-AND-DEPENDENCIES]] — code layers and architecture diagram.
2. [[02-COMPLETE-FOLDER-TREE]] — full proposed repository paths.
3. [[03-FILE-OWNERSHIP-REGISTER]] — single primary editor, reviewers and authority.
4. [[04-MEMBER-DAILY-WORK]] — seven-day work of all seven members.
5. [[05-API-AND-DATA-FLOWS]] — which file calls which file, request and result paths.
6. [[06-GIT-BRANCH-PR-CONFLICT-POLICY]] — GitHub workflow and no dual-edit policy.
7. [[07-INTERFACE-CONTRACTS]] — C# ↔ Python ↔ RAG ↔ Angular boundaries.
8. [[08-DATABASE-AND-MIGRATIONS]] — MySQL stewardship.
9. [[09-TESTING-AND-QUALITY-GATES]] — tests and acceptance.
10. [[10-CHANGE-CONTROL-AND-HANDOFFS]] — file locks, reassignment, incident escalation.
11. [[11-SECURITY-AND-MVP-SCOPE]] — security and latest business invariants.
12. [[12-REPO-BOOTSTRAP-AND-CI]] — step-by-step setup and CI.
13. [[13-EXAMPLE-VERTICAL-SLICES]] — working patterns and examples.
14. [[14-TEAM-CHECKLISTS-AND-TEMPLATES]] — ready-to-copy templates.
15. [[15-OPEN-DECISIONS-AND-RECONCILIATION]] — snapshot differences / approvals needed.

## Member roster (proposed assignment)
| Member | Scope | Code ownership |
|---|---|---|
| M1 | Angular: auth, dashboard, projects | frontend only — auth/projects components |
| M2 | Angular: survey, map, requirements | frontend only — land/requirement modules |
| M3 | Angular: plans, professional 2D editor, reports | frontend only — plan/editor modules |
| M4 | C# backend 1 / Core Lead | API host, auth, project, identity, shared architecture |
| M5 | C# backend 2 / Feature Lead | requirements, plan workflows, revision, confirmation, report, architect request |
| M6 | Python Backend Lead | survey, geometry, AI alternative generation, deterministic validation |
| M7 | Team Leader + RAG Engineer | RAG corpus, regulatory retrieval/provenance, integration review and coordination |

**Single-writer principle:** exactly one *primary owner* per shared source file at any given time. A reviewer can comment or request changes, but cannot push competing edits. Owners can delegate by explicit documented handoff; PR reviews are mandatory for all mainline merges.

## Current target decisions
- Angular + TypeScript + Konva frontend; C# ASP.NET Core main API with clean architecture; Python FastAPI private compute services; MySQL 8.x authoritative transactional DB.
- Residential preliminary 2D ONLY in MVP; exactly three *distinct valid* options when feasible, otherwise honest failure diagnostics. User can choose the manual design path.
- Edits validated against whole-plan constraints; invalid operations rejected atomically. Each accepted edit creates a revision. Client must explicitly confirm the **current** revision after validation.
- Backend-only Vastu heuristic, never asked or shown to customer; does not block valid manual edits.
- Main Admin approval mandatory before any Staff proposed add/edit/delete across assigned modules becomes live.
- RAG is evidence retrieval, NOT legal certification. `NOT_VERIFIED` for missing/ambiguous applicable regulations.
- No 3D, construction costing, or official permit-approval claims. Payment-gated export is an *older document proposal*, not assumed in current 7-day MVP without separate approval.

## Hard release gate
A document labeled READY, tests PASS, or feature DONE must carry actual repository commit hash + test evidence + reviewer + date. Design-only content stays PROPOSED.

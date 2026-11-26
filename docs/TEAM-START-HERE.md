# Team start — current working guide (Second Brain V3.0)

> **Reconciled 2026-11-17 (task BE-M7-D01).** The previous seven-member alignment in this file was V2-era and conflicted with the V3 ownership model. It is preserved unchanged under [Historical (superseded)](#historical-superseded--v2-era-alignment) for reference only. Current direction lives in the Second Brain; this page is a short entry point, not a separate authority.

## Read first

1. Root `CLAUDE.md` / `AGENTS.md` (identical agent instructions).
2. [Second Brain home](second-brain/00-HOME.md) → [V3 start here](second-brain/14-Current-Backend/00-START-HERE.md).
3. [Module ownership](second-brain/14-Current-Backend/10-OWNERSHIP.md) and your [member plan](second-brain/15-Member-Plans/00-INDEX.md).
4. [Open decisions](second-brain/14-Current-Backend/15-OPEN-DECISIONS.md) and the latest [session handover](second-brain/17-Progress/04-SESSION-HANDOVER.md).

Precedence (from V3): latest explicitly approved user decision > approved ADR/current contract > current V3 notes > historical V2. Source code and tests determine implementation status, not documentation.

## Current MVP scope (summary)

Preliminary AI-assisted **2D** residential planning for Sri Lanka. **3D and construction cost estimation are excluded** (D311). Vastu is an internal, non-blocking heuristic that is never shown to customers (D310). Reports are preliminary and never claim official approval (D312). Payment/subscription and admin-staff ownership are open (D314, G09). See [scope decisions](second-brain/14-Current-Backend/01-SCOPE-DECISIONS.md).

## Backend work packages (proposed — final roster open, G01)

| Slot | Backend responsibility | Branch |
| --- | --- | --- |
| M1 | Auth, users, projects | `feature/m1-auth-projects` |
| M2 | Survey, land, location, geometry normalization | `feature/m2-survey-land` |
| M3 | Editor transformations, versions, confirmation | `feature/m3-plan-editor` |
| M4 | Shared C# infrastructure, DB, security, service clients | `feature/m4-infrastructure` |
| M5 | Requirements, dashboard, PDF report, architect requests | `feature/m5-requirements-reports` |
| M6 | Feasibility, generation lifecycle/selection, authoritative validation | `feature/m6-ai-generation` |
| M7 | Existing RAG, evidence contract, integration, QA, review | `feature/m7-rag-integration` (proposed) |

Lead decision recorded 2026-11-17: the team lead currently holds **M4 and M7**. Allocation of the remaining members is **not confirmed** (G01). Do not infer a person-to-slot mapping from this table.

Frontend assignments: V3 states existing frontend assignments are retained unless the lead approves changes. They are tracked in the frontend repository, not here; see the historical table below for the last recorded V2 frontend column (unverified against the current frontend repo).

## Shared-file custodians (single writer)

| Path family | Custodian | Notes |
| --- | --- | --- |
| `src/BuildMind.API/Program.cs`, DI registration, C# project/solution files, DbContext, migrations/snapshot, middleware, shared clients/security | M4 | Feature owners request changes; M7 + affected owner review |
| `python-services/app/main.py`, shared Python config, dependency pins/lock | M6 (proposed) | M2/M3/M7 request router registration through the custodian |
| Actual RAG source (outside this repo, pending decision), evidence schema, QA contracts | M7 | No copy/rewrite of the existing engine |
| Feature entities, mappings, endpoints, handlers and module tests | Owning slot | M4 coordinates schema changes |
| CI workflow and central docs | M4/M7 coordinated, one writer per file | Affected owners review |

Full path-family table: [10-OWNERSHIP](second-brain/14-Current-Backend/10-OWNERSHIP.md).

## Architecture connection

Angular (separate frontend repo) → `BuildMind.API` → `BuildMind.Application` → `BuildMind.Domain`.
`BuildMind.Infrastructure` implements Application ports (persistence, external adapters); the API project is the composition root. Python FastAPI and RAG are private backend integrations reached through server-side adapters, never called directly by the public client. See [03-ARCHITECTURE](second-brain/14-Current-Backend/03-ARCHITECTURE.md).

## Merge policy

1. Start from latest `origin/develop`; reuse your feature branch if it exists.
2. Work only in your owned paths; shared-file changes go through the custodian. Contract/schema changes need consumer sign-off first.
3. Run the relevant build/tests, record evidence, update docs and the progress register in the same PR.
4. Open a PR into `develop`. The team lead merges after independent review, CI and QA gates. No automatic merge from a commit.
5. Never push directly to `main` or `develop`; never force-push shared branches.

Branch protection, owner-only merge enforcement and CI gate names are **not verified** (G13). As of the 2026-11-17 fetch, `main` no longer exists on the remote and a branch named `main-→-develop` points at the former `main` commit `d606f00` (earlier divergence recorded in the [2026-11-17 audit](second-brain/17-Progress/07-AUDIT-2026-11-17.md)). Default-branch and protection state are unverified; resolution is pending (G13).

## Definition of done

See [12-QA](second-brain/14-Current-Backend/12-QA.md): code and docs aligned, relevant tests pass, reviewer approved, QA verified, no critical security/data-integrity defect, evidence recorded. No leaked credentials, PDFs, personal data or compiled artifacts.

---

## Historical (superseded) — V2-era alignment

> **HISTORICAL — not current instructions.** Preserved verbatim from the pre-V3 starter (commit `763a9a7`). Conflicts with V3: M4 is no longer the identity/project CRUD owner (now M1); plans/versions/confirmation are M3, not M5; survey is M2 and `python-services/` is split by module (M6 is proposed bootstrap custodian only); M7 governs current docs (`14-`–`17-` folders), not `13-Implementation-Blueprint/`.

### Seven-member alignment (historical)

| Member | Frontend/Backend ownership | Protected folders |
| --- | --- | --- |
| M1 | Angular auth/dashboard integration | Frontend repository, not this backend repository |
| M2 | Angular survey/land/requirements | Frontend repository |
| M3 | Angular 2D editor/plan viewer | Frontend repository |
| M4 | C# API foundation, identity, project CRUD, DbContext/migrations integration | `src/BuildMind.API`, `src/BuildMind.Domain/Entities`, `src/BuildMind.Infrastructure/Persistence` |
| M5 | C# application use cases: requirements, plans, versions, report, architect requests | `src/BuildMind.Application/Features`, feature-specific contracts |
| M6 | Python FastAPI survey, geometry, plan generation, validation | `python-services/` |
| M7 | Team lead, RAG, docs governance, final PR/merge | `rag-service/`, `docs/second-brain/13-Implementation-Blueprint/` |

Historical text: "File owner is responsible for writing or integrating edits. Other members request changes through a PR comment/issue; avoid concurrent modification of shared files. M4 is integration owner for `Program.cs`, dependency injection, C# project files and DB migrations. M6 owns `python-services/app/main.py`. M7 owns RAG contracts; coordinate interface changes with M6. M1–M3 never commit their Angular source into this backend repo."

Historical branch examples: `feature/m4-auth-register`, `feature/m5-plan-versioning`, `feature/m6-survey-parser`, `feature/m7-rag-retrieval` (superseded by the V3 branch names above).

Historical Day-1 handoff: "M4: build/API skeleton, DI, project structure. M5: Application feature contracts and DTOs without editing M4 files. M6: Python health and module shells. M7: second-brain reference, scope reconciliation, RAG indexing design (no sensitive data in GitHub)."

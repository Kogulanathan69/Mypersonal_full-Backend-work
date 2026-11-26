# BuildMind-Platform (backend)

Preliminary AI-assisted **2D** residential planning for Sri Lanka — ASP.NET Core (C#, Clean Architecture) main API, private Python FastAPI services, MySQL 8.4 target, and regulatory evidence retrieval (RAG). The Angular client lives in a separate frontend repository.

**MVP scope:** land/survey input, home requirements, three distinct feasible 2D alternatives (or a truthful failure), validated editing, explicit confirmation and a preliminary report. **3D and construction cost estimation are out of scope** (D311). Outputs are preliminary and never an official approval (D312).

**Current state (2026-11-17, develop `b03430e` + M4 foundation branch):** four C# projects in `BuildMind.sln`; the API serves `GET /health` (liveness only). M3 provisional plan-editor contracts (C# Application) and Python `plan_editor` engine are merged without HTTP routes. No database integration, authentication or production feature endpoints yet. See the [audit](docs/second-brain/17-Progress/07-AUDIT-2026-11-17.md) and [progress](docs/second-brain/17-Progress/02-TASK-REGISTER.md).

Start here: [`docs/TEAM-START-HERE.md`](docs/TEAM-START-HERE.md) and the [Second Brain V3](docs/second-brain/00-HOME.md). AI agents: read `CLAUDE.md` / `AGENTS.md`.

<sub>Historical: the earlier one-line description mentioned "2D/3D Planning"; 3D is superseded by decision D311.</sub>

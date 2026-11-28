# BuildMind-Platform (backend)

## Team members: clone and run the **full Development backend** (Windows)

**Do not use the default `dotnet run` launch profile as the full stack:** it starts at port 5080 with database/authentication/feature gates disabled by default, so Swagger may list only `GET /health`.

Requirements: Windows WSL 2 with `Ubuntu-24.04`, Docker Desktop with Ubuntu WSL integration, internet for first build, and Git. WSL packages: `python3 openssl util-linux libacl1 acl ca-certificates`.

From PowerShell:

```powershell
git clone https://github.com/Kogulanathan69/Mypersonal_full-Backend-work.git
cd Mypersonal_full-Backend-work
powershell -NoProfile -ExecutionPolicy Bypass -File .\\START-BACKEND.ps1
```

This launcher runs the repository's portable script: it generates **new local** passwords, tokens and TLS certificates on that PC, builds services, initializes disposable local-development configuration, runs MySQL EF migrations, starts API/Python/RAG/Mailpit, and verifies important feature routes exist in Swagger. No shared passwords, production data or private keys need to be published. The script **does not** delete existing database volumes.

After a successful startup: **Swagger** <http://localhost:5182/swagger/index.html>; **Mailpit** <http://localhost:8027>. Swagger listing endpoints is not a substitute for authenticated workflow tests. For failures see [full team setup and diagnostics](docs/portable/INSTALLATION.md). For the exact per-machine setup scripts see `tools/portable/`.

---


Preliminary AI-assisted **2D** residential planning for Sri Lanka — ASP.NET Core (C#, Clean Architecture) main API, private Python FastAPI services, MySQL 8.4 target, and regulatory evidence retrieval (RAG). The Angular client lives in a separate frontend repository.

**MVP scope:** land/survey input, home requirements, three distinct feasible 2D alternatives (or a truthful failure), validated editing, explicit confirmation and a preliminary report. **3D and construction cost estimation are out of scope** (D311). Outputs are preliminary and never an official approval (D312).

**Current state (2026-11-17, develop `b03430e` + M4 foundation branch):** four C# projects in `BuildMind.sln`; the API serves `GET /health` (liveness only). M3 provisional plan-editor contracts (C# Application) and Python `plan_editor` engine are merged without HTTP routes. No database integration, authentication or production feature endpoints yet. See the [audit](docs/second-brain/17-Progress/07-AUDIT-2026-11-17.md) and [progress](docs/second-brain/17-Progress/02-TASK-REGISTER.md).

Start here: [`docs/TEAM-START-HERE.md`](docs/TEAM-START-HERE.md) and the [Second Brain V3](docs/second-brain/00-HOME.md). AI agents: read `CLAUDE.md` / `AGENTS.md`.

<sub>Historical: the earlier one-line description mentioned "2D/3D Planning"; 3D is superseded by decision D311.</sub>

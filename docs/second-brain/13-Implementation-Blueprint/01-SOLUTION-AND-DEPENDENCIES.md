> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Solution architecture and dependency direction

## Repo boundary (recommended two repos or mono-repo; decide at kickoff)
```
BuildMind/                   # logical workspace; existing repo may differ
  frontend/                  # Angular, M1-M3
  backend/
    BuildMind.sln            # C# ASP.NET Core Clean Architecture, M4/M5
    src/
      BuildMind.Domain/
      BuildMind.Application/
      BuildMind.Infrastructure/
      BuildMind.API/
    tests/
      BuildMind.Domain.Tests/
      BuildMind.Application.Tests/
      BuildMind.Infrastructure.Tests/
      BuildMind.API.IntegrationTests/
  python-services/           # FastAPI, M6 + M7 under separate folders
    app/
      api/
      schemas/
      services/
        survey/
        geometry/
        planning/
        validation/
        rag/
      core/
    tests/
  contracts/                 # owner M7 for review; M4 contract approver
  docs/
  .github/workflows/
```
This is a target tree, not an inventory of files already verified in GitHub.

## Rule for compile-time dependencies
```
BuildMind.Domain          → no project-layer references
BuildMind.Application     → BuildMind.Domain
BuildMind.Infrastructure  → BuildMind.Application + BuildMind.Domain
BuildMind.API             → BuildMind.Application + BuildMind.Infrastructure (composition root)
```
Domain has no ASP.NET Core, EF Core, SQL, Mapbox, HTTP, OpenAI or FastAPI dependencies. Application defines ports/interfaces and use cases. Infrastructure implements database, external adapters and document storage. API handles HTTP/auth/rate-limit/presentation and dependency wiring. Business decisions live in Application+Domain, NOT controllers.

## Runtime arrows
```
Angular (M1/M2/M3)
   ↓ HTTPS JSON /api/v1 + JWT
ASP.NET Core API (M4 outer API; M5 feature endpoints)
   ↓ use case command/query
Application (M4 auth/projects | M5 requirements/plans/reports)
   ↓ Domain entities/invariants
Infrastructure adapters (M4 identity/database foundation | M5 feature persistence)
   ├─ MySQL 8.x  [single authoritative state]
   ├─ Private FastAPI HTTP client (contract approved by M4 and M6)
   │    ├─ M6: survey geometry / three alternatives / validation
   │    └─ M7: regulatory RAG retrieval, provenance, applicability evidence
   └─ Private storage / export / email adapters (only as explicitly in-scope)
```
Python does NOT directly update authoritative C# project/plan/revision tables. FastAPI returns computations; C# validates identity/authorization, version preconditions and commits state. M7's RAG and M6's geometry services collaborate by validated response schemas, not shared mutable module edits.

## UI responsibility
M1 auth/project flows ↔ M4; M2 survey/map/requirements ↔ M4/M5 and M6 through C#; M3 alternatives/editor/reports ↔ M5 and M6; M7 coordinates RAG source evidence and cross-service contracts. No Angular-to-Python direct production calls, no secrets in frontend.

## Run modes
Local: localhost Angular → ASP.NET Core → internal FastAPI + local MySQL. Production: HTTPS API and private service-to-service networking, config via environment/secrets, no public Python endpoints unless separately protected and approved.

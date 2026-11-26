> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Backend technology status

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

| Technology | Status | Notes |
|---|---|---|
| C# / .NET | Confirmed | API implementation framework ASP.NET Core Web API recommended |
| MSSQL | Confirmed | Structured relational store |
| Mapbox | Confirmed | Location UI provider; backend receives confirmed coordinates |
| OpenAI API | Recommended, not approved | Chatbot and floor-plan assistance |
| Python FastAPI | Recommended, not approved | AI generation orchestration |
| PayHere | Recommended, not approved | Payment gateway |
| Azure Blob Storage | Recommended, not approved | Survey/plan file storage |
| Rules engine in C# | Proposed | Requires legal rule catalogue and design |

No real deployed services, code paths, or production environment were verified.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

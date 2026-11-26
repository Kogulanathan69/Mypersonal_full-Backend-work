> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Bootstrap, protected branches and CI

**Existing repositories were not audited for this package. Do not blindly overwrite existing source.**

M4 creates/owns `BuildMind.sln`, API/Application/Domain/Infrastructure C# projects, references, `Program.cs`, shared DI and EF Core MySQL migration setup. M6 owns FastAPI `main.py`, requirements and base route registration; M7 owns independent RAG modules. M1 owns Angular shared routing/theme. New packages and SDK versions must be approved and compatible.

Recommended required PR checks:
| Pipeline | Maintainer | Checks |
|---|---|---|
| `.github/workflows/backend-dotnet.yml` | M4 | dotnet restore, build, test, auth and EF smoke |
| `.github/workflows/python-tests.yml` | M6 | pytest, Ruff, geometry/schema golden fixtures |
| `.github/workflows/contracts-check.yml` | M7 | OpenAPI & JSON schema validation, contract compatibility |
| Angular CI | M1 | install, build, unit tests, lint, feature routing |
| E2E smoke | M7 coordinates | Angular → C# → Python → MySQL, mock where labeled |

`main` protected: no direct pushes / no force-push / CI green / required non-author review. Prefer narrow feature branches from protected `develop` (if the team uses one) or `main`. Add real GitHub user handles to CODEOWNERS after confirmation.

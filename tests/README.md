# Tests

C# tests are in the solution `BuildMind.sln` (added BE-M4-D01a, 2026-11-17):

- `BuildMind.Infrastructure.Tests` (M4): API liveness contract, truthful readiness, Clean Architecture layer-direction rules (csproj and compiled references), DI container validation; since BE-M4-D01c also shared error codes, RFC 9457 Problem Details (Development and Production), `/api/v1` versioning, typed configuration and start-up validation, `TimeProvider`, current-user safety and logging safety. xUnit v2 + `Microsoft.AspNetCore.Mvc.Testing` (in-memory test host; no MySQL, Python or network required). Test-only synthetic endpoints live in `Support/SyntheticTestEndpoints.cs` and are registered only by the test factory.

- `BuildMind.Requirements.Tests`, `BuildMind.Reports.Tests`, and `BuildMind.Workflow.Tests` (M5): requirements, report, and workflow contracts and domain behavior.

The M3 `BuildMind.Application.ContractTests` console project is built with the solution and executed by a separate CI step.

Run these commands from the repository root to build and verify the C# solution and the M3 contracts:

```sh
dotnet build BuildMind.sln --configuration Release
dotnet test BuildMind.sln --configuration Release --no-build
dotnet run --project tests/BuildMind.Application.ContractTests/BuildMind.Application.ContractTests.csproj --configuration Release --no-build --no-restore
```

Feature owners add test projects in coordination with M4, the solution file custodian.

Run the Python suite from `python-services` with `python -m pytest -q`.

Known local limitation: on a Windows machine with Smart App Control enabled, the test host may be blocked from loading the unsigned API assembly (see G17 in [open decisions](../docs/second-brain/14-Current-Backend/15-OPEN-DECISIONS.md)). Do not disable or work around the control; CI on Linux is the intended execution path pending a decision.

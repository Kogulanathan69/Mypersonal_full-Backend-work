> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# File ownership register — the one-writer contract

| Area / exact shared path | Primary writer | Mandatory reviewers | How others request edits |
|---|---|---|---|
| `backend/BuildMind.sln`, `Directory.Build.props`, `.editorconfig` | M4 | M5, M7 | Issue + PR proposal; M4 patches |
| `backend/src/BuildMind.API/Program.cs`, `appsettings.json` | M4 | M5, M7 | M5 requests DI/route registration through issue |
| `backend/src/BuildMind.API/Controllers/Auth*, Projects*, StaffProposals*` | M4 | M5 | API issue + feature PR |
| `backend/src/BuildMind.API/Controllers/Survey*, Requirements*, Plans*, Reports*, ArchitectRequests*` | M5 | M4 | Ask M5; M4 reviews auth boundary |
| `backend/src/BuildMind.Domain/Common/*`, `Entities/User.cs`, `Entities/Project.cs` | M4 | M5 | Additive contract request |
| `backend/src/BuildMind.Domain/Entities/{SurveyPlan,HomeRequirement,FloorPlan,PlanRevision,PlanConfirmation,ArchitectRequest}.cs` | M5 | M4 | Versioned migration review |
| `backend/src/BuildMind.Application/Features/{Auth,Projects,StaffProposals}/**` | M4 | M5 | Cross-team endpoint contract |
| `backend/src/BuildMind.Application/Features/{Survey,Land,Requirements,Plans,Reports,ArchitectRequests}/**` | M5 | M4/M6 | C# feature request |
| `backend/src/BuildMind.Application/Abstractions/IPlanComputeClient.cs`, `IRuleEvidenceClient.cs` | M5 | M6, M7, M4 | Contract proposal + acceptance fixtures |
| `backend/src/BuildMind.Infrastructure/Persistence/BuildMindDbContext.cs`, `Migrations/**` | M4 | M5, M7 | M5 adds configuration in own file; M4 merges migration and context changes |
| `backend/src/BuildMind.Infrastructure/Persistence/Configurations/{PlanRevision,Requirements}*` | M5 | M4 | Independent files preferred |
| `backend/src/BuildMind.Infrastructure/Identity/**` | M4 | M7 | Security review |
| `backend/src/BuildMind.Infrastructure/Integrations/{PythonPlanComputeClient,RagEvidenceClient}.cs` | M5 | M6, M7 | Contract tests included |
| `backend/src/BuildMind.Infrastructure/DependencyInjection.cs` | M4 | M5 | M5 describes needed registrations; M4 edits |
| `python-services/app/main.py`, `core/**` | M6 | M7 | M7 proposes route registration; M6 makes edit |
| `python-services/app/services/{survey,geometry,planning,validation}/**` | M6 | M5/M7 | Service contract issue |
| `python-services/app/services/rag/**`, `schemas/rag.py`, `api/rag_routes.py` | **M7** | M6/M5 | RAG PR owned by M7 |
| `python-services/app/integrations/rag_client.py` | M6 | M7 | Calls M7 RAG API, no copy of retrieval logic |
| `contracts/http/openapi-main.yaml`, `contracts/error-codes.md` | M4 | M1-M3, M5, M7 | Contract ticket; no unilateral changes |
| `contracts/http/openapi-python-internal.yaml` | M6 | M5, M7 | Review both endpoints and auth |
| `contracts/jsonschema/canonical-plan-v1.json` | M6 | M5, M3, M7 | ADR required for breaking changes |
| `contracts/jsonschema/rag-evidence-v1.json` | **M7** | M5, M6 | Source evidence required |
| `.github/workflows/backend-dotnet.yml` | M4 | M7 | CI change issue |
| `.github/workflows/python-tests.yml` | M6 | M7 | CI change issue |
| `.github/workflows/contracts-check.yml`, `docs/team/**` | **M7** | M4/M5/M6 as relevant | PR with review |
| `frontend/src/app/features/auth/**`, dashboard/projects | M1 | M4 | FE/BE contract handoff |
| `frontend/src/app/features/land/**`, survey/requirements | M2 | M5, M6 | FE/BE contract handoff |
| `frontend/src/app/features/plans/**`, editor/reports | M3 | M5, M6 | FE/BE contract handoff |
| `frontend/src/app/core/**`, `app.routes.ts`, shared theme | M1 custodian | M2/M3 | M1 integrates shared edits |

## Which two people must NEVER edit the same shared file simultaneously?
- **M4 vs M5:** `BuildMindDbContext.cs`, `Program.cs`, `DependencyInjection.cs`, `*.csproj`, the migration snapshot, common domain model. **M4 is designated integration owner**. M5 works in separate feature folders and supplies a requested patch. If M5 must change a shared file, M4 explicitly grants a temporary exclusive handoff in ownership log.
- **M6 vs M7:** `python-services/app/main.py`, `core/config.py`, `requirements.lock`, shared router registration. **M6 integrates bootstrap changes**; M7 exclusively edits `services/rag/**` and `schemas/rag.py`. M6 exclusively edits geometry/planning. RAG contract JSON schema is owned by M7.
- **M1 vs M2/M3:** `app.routes.ts`, theme and core interceptors. **M1 integrates global Angular changes**, others own their feature folders.
- **M5 vs M6:** canonical geometry schema shared contract owner **M6**; M5 writes C# adapter consumer, does not rewrite schema independently.

## Owner unavailable / critical urgent fix
1. Stop competing edits; open `OWNER-HANDOFF` issue with reason, scope, exact paths, deadline.
2. Original owner or M7 lead records temporary new writer; if M7 is requester and affected owner unavailable, M4 approves architecture/shared C# changes, M6 approves shared Python bootstrap changes.
3. Assign one person to the source file for the handoff window; others do not edit.
4. Replacement writes branch, CI green, reviewer checks; handoff closes with new commit hash.
5. On-call emergency hotfix uses `hotfix/<ticket>-<slug>` with review, never silently force-push main.

## CODEOWNERS recommendation
```
/backend/src/BuildMind.API/Program.cs                  @M4
/backend/src/BuildMind.Infrastructure/Persistence/    @M4
/backend/src/BuildMind.Application/Features/Plans/     @M5
/python-services/app/services/rag/                    @M7
/python-services/app/services/geometry/               @M6
/contracts/jsonschema/rag-evidence-v1.json           @M7
```
Replace `@M4` etc with real GitHub usernames before committing. Code owners are reviewers, not a substitute for repository permissions or branch protection.

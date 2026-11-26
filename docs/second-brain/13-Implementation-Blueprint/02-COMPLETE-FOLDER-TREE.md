> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Proposed complete backend source folder and file map

All files below are **planned examples**, not claims about the currently cloned repository. Feature namespaces can be adjusted via a reviewed ADR. Exactly one primary owner is assigned in [[03-FILE-OWNERSHIP-REGISTER]].

```text
backend/
├── BuildMind.sln                       [M4]
├── Directory.Build.props               [M4]
├── .editorconfig                        [M4]
├── README.md                            [M4]
├── src/
│   ├── BuildMind.Domain/                [M4 project skeleton; feature file owners vary]
│   │   ├── BuildMind.Domain.csproj      [M4]
│   │   ├── Common/Entity.cs             [M4]
│   │   ├── Common/DomainEvent.cs        [M4]
│   │   ├── Entities/User.cs             [M4]
│   │   ├── Entities/Project.cs          [M4]
│   │   ├── Entities/SurveyPlan.cs       [M5]
│   │   ├── Entities/HomeRequirement.cs  [M5]
│   │   ├── Entities/FloorPlan.cs        [M5]
│   │   ├── Entities/PlanRevision.cs     [M5]
│   │   ├── Entities/PlanConfirmation.cs [M5]
│   │   ├── Entities/ArchitectRequest.cs [M5]
│   │   ├── Entities/StaffProposal.cs    [M4]
│   │   ├── ValueObjects/GeometryRef.cs  [M5]
│   │   ├── ValueObjects/RuleEvidence.cs [M7 designs contract; M5 C# integration owner]
│   │   ├── Enums/PlanStatus.cs          [M5]
│   │   └── Exceptions/DomainException.cs [M4]
│   ├── BuildMind.Application/
│   │   ├── BuildMind.Application.csproj [M4]
│   │   ├── Common/Contracts/Result.cs   [M4]
│   │   ├── Common/Behaviors/ValidationBehavior.cs [M4]
│   │   ├── Abstractions/IClock.cs       [M4]
│   │   ├── Abstractions/IUnitOfWork.cs  [M4]
│   │   ├── Abstractions/IPlanComputeClient.cs [M5]
│   │   ├── Abstractions/IRuleEvidenceClient.cs [M5]
│   │   ├── Features/Auth/Register/      [M4]
│   │   │   ├── RegisterCommand.cs
│   │   │   ├── RegisterHandler.cs
│   │   │   └── RegisterValidator.cs
│   │   ├── Features/Auth/Login/         [M4]
│   │   ├── Features/Projects/Create/    [M4]
│   │   ├── Features/Projects/Get/       [M4]
│   │   ├── Features/Survey/Upload/      [M5]
│   │   ├── Features/Land/Confirm/       [M5]
│   │   ├── Features/Requirements/Save/  [M5]
│   │   ├── Features/Plans/Generate/     [M5]
│   │   ├── Features/Plans/Select/       [M5]
│   │   ├── Features/Plans/Edit/         [M5]
│   │   ├── Features/Plans/Validate/     [M5]
│   │   ├── Features/Plans/SaveRevision/ [M5]
│   │   ├── Features/Plans/Confirm/      [M5]
│   │   ├── Features/Reports/Generate/   [M5]
│   │   ├── Features/ArchitectRequests/  [M5]
│   │   └── Features/StaffProposals/     [M4]
│   ├── BuildMind.Infrastructure/
│   │   ├── BuildMind.Infrastructure.csproj [M4]
│   │   ├── Persistence/BuildMindDbContext.cs [M4]
│   │   ├── Persistence/Configurations/UserConfiguration.cs [M4]
│   │   ├── Persistence/Configurations/ProjectConfiguration.cs [M4]
│   │   ├── Persistence/Configurations/PlanRevisionConfiguration.cs [M5]
│   │   ├── Persistence/Configurations/RequirementsConfiguration.cs [M5]
│   │   ├── Persistence/Migrations/      [M4 exclusive owner/merger]
│   │   ├── Repositories/ProjectRepository.cs [M4]
│   │   ├── Repositories/PlanRepository.cs [M5]
│   │   ├── Identity/PasswordHasher.cs   [M4]
│   │   ├── Identity/TokenIssuer.cs      [M4]
│   │   ├── Integrations/PythonPlanComputeClient.cs [M5]
│   │   ├── Integrations/RagEvidenceClient.cs [M5]
│   │   ├── Storage/SurveyStorage.cs     [M5]
│   │   ├── Reports/PreliminaryPdfRenderer.cs [M5]
│   │   └── DependencyInjection.cs      [M4; M5 proposes patch via PR]
│   └── BuildMind.API/
│       ├── BuildMind.API.csproj         [M4]
│       ├── Program.cs                   [M4]
│       ├── appsettings.json            [M4; no secrets]
│       ├── Middleware/ErrorHandlingMiddleware.cs [M4]
│       ├── Controllers/AuthController.cs [M4]
│       ├── Controllers/ProjectsController.cs [M4]
│       ├── Controllers/SurveyController.cs [M5]
│       ├── Controllers/RequirementsController.cs [M5]
│       ├── Controllers/PlansController.cs [M5]
│       ├── Controllers/ReportsController.cs [M5]
│       ├── Controllers/ArchitectRequestsController.cs [M5]
│       └── Controllers/StaffProposalsController.cs [M4]
└── tests/
    ├── BuildMind.Domain.Tests/         [feature owner writes; M4 infra setup]
    ├── BuildMind.Application.Tests/    [feature owner writes; M4 infra setup]
    ├── BuildMind.Infrastructure.Tests/ [feature owner writes]
    └── BuildMind.API.IntegrationTests/ [M4 harness; module owner tests]

python-services/
├── pyproject.toml                       [M6]
├── requirements.lock / constraints.txt  [M6]
├── .env.example                         [M6; NEVER real secrets]
├── app/
│   ├── main.py                          [M6]
│   ├── core/config.py                   [M6]
│   ├── core/security.py                 [M6]
│   ├── api/health.py                    [M6]
│   ├── api/survey_routes.py             [M6]
│   ├── api/plan_routes.py               [M6]
│   ├── api/validation_routes.py         [M6]
│   ├── api/rag_routes.py                [M7, private/internal]
│   ├── schemas/survey.py                [M6]
│   ├── schemas/plan.py                  [M6]
│   ├── schemas/validation.py            [M6]
│   ├── schemas/rag.py                   [M7]
│   ├── services/survey/                [M6]
│   │   ├── ingest.py
│   │   ├── measurement_extractor.py
│   │   └── polygon_normalizer.py
│   ├── services/geometry/              [M6]
│   │   ├── topology.py
│   │   ├── room_operations.py
│   │   ├── setbacks.py
│   │   └── canonical_schema.py
│   ├── services/planning/              [M6]
│   │   ├── generate_three.py
│   │   └── feasibility.py
│   ├── services/validation/            [M6]
│   │   └── whole_plan_validator.py
│   ├── services/rag/                   [M7]
│   │   ├── extract.py
│   │   ├── chunk.py
│   │   ├── embed.py
│   │   ├── retrieve.py
│   │   ├── provenance.py
│   │   ├── applicability.py
│   │   └── evidence_service.py
│   └── integrations/rag_client.py      [M6; consumes M7 schema]
└── tests/
    ├── survey/                         [M6]
    ├── geometry/                       [M6]
    ├── planning/                       [M6]
    ├── validation/                     [M6]
    ├── rag/                            [M7]
    └── contracts/                      [M6 for geometry; M7 for RAG]

contracts/
├── http/openapi-main.yaml               [M4 owner; feature author submits change]
├── http/openapi-python-internal.yaml    [M6 owner; M7 reviews RAG subset]
├── jsonschema/canonical-plan-v1.json    [M6 owner; M5 approves consumer compatibility]
├── jsonschema/rag-evidence-v1.json      [M7 owner; M6/M5 approve consumer compatibility]
└── error-codes.md                       [M4 owner]

docs/team/
├── ownership.md                         [M7]
├── api-handoff-log.md                    [M7]
├── decisions/                            [M7 recorder; affected owner approves]
└── integration-evidence.md               [M7]
.github/workflows/
├── backend-dotnet.yml                    [M4]
├── python-tests.yml                      [M6]
└── contracts-check.yml                   [M7]
```

### Suggested C# per-feature files
For each Application `Features/X/Action/`, prefer `ActionCommand.cs` or `ActionQuery.cs`, `ActionHandler.cs`, `ActionValidator.cs`, `ActionResponse.cs` as needed. No automatic generic repository for every entity. Use naming consistently. DTOs remain at application/API boundary, not Domain. EF entity configurations live in Infrastructure.

### Shared file rule
A component may use another layer through public interface/contract. **Do not edit the other member's file just because your feature needs a new method.** Propose a contract change via PR, let owner make or approve it, then integrate after the contract merges.

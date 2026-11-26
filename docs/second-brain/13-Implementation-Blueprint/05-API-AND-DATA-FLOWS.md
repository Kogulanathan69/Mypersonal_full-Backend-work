> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# End-to-end file-level connection flows

All route names below are **proposed contract examples** until approved and implemented. Replace sample paths once real OpenAPI is merged.

## A. Register/login — M1 ↔ M4
```
frontend/features/auth/login.component.ts [M1]
 -> frontend/core/api/auth-api.service.ts [M1]
 -> POST /api/v1/auth/login (proposed)
 -> backend/API/Controllers/AuthController.cs [M4]
 -> Application/Features/Auth/Login/LoginHandler.cs [M4]
 -> Infrastructure/Identity/PasswordHasher.cs [M4]
 -> Infrastructure/Persistence/BuildMindDbContext.cs [M4] -> MySQL
 <- token/session response through auth-api.service -> Angular session
```
**Security:** use secure password hashing, short-lived access token/secure cookie approach decided by ADR; rate-limit, non-enumerating password-reset responses, role authorization. Do not publish JWT signing keys.

## B. Project creation — M1 ↔ M4
```
frontend/features/projects/new-project.component.ts [M1]
 -> projects-api.service.ts [M1]
 -> ProjectsController.cs [M4]
 -> CreateProjectHandler.cs [M4]
 -> Domain/Entities/Project.cs [M4]
 -> ProjectRepository.cs [M4] / UnitOfWork -> MySQL
```
Project owner ID must come from verified identity, not client-supplied user IDs.

## C. Survey and land — M2 ↔ M5 ↔ M6
```
frontend/features/survey/survey-upload.component.ts [M2]
 -> upload API (C# SurveyController.cs [M5])
 -> UploadSurveyHandler.cs [M5] -> SurveyStorage.cs [M5]
 -> PythonPlanComputeClient.cs [M5] -> Python app/api/survey_routes.py [M6]
 -> survey/ingest.py + polygon_normalizer.py [M6]
 <- extraction result {geometry, units, sourcePage, confidence, limitations}
 -> ConfirmLandHandler.cs [M5] -> transaction MySQL
 -> Angular map boundary confirmation [M2]
```
An uploaded survey is **not** proof of cadastral accuracy. If dimensions/boundaries ambiguous, show manual review state. Survey upload must not automatically imply regulation approval.

## D. Requirements — M1/M3 ↔ M5
```
Angular room form -> RequirementsController.cs [M5]
 -> SaveRequirementsHandler.cs [M5]
 -> HomeRequirement entity [M5] -> database [M4-owned context]
 -> version / draft status to UI
```
Collect ground floor first; optional spaces and room relationships; persist structured validated counts and preferences. Do not ask customers Vastu questions.

## E. Generate exactly three alternatives — M3 ↔ M5 ↔ M6 + M7
```
Angular 3-plan request [M3]
 -> PlansController.cs [M5]
 -> GeneratePlansHandler.cs [M5] verifies land + requirements + authorization
 -> IPlanComputeClient [M5] -> PythonPlanComputeClient.cs [M5]
 -> /internal/plans/generate [M6] -> planning/generate_three.py [M6]
    -> geometry validators [M6]
    -> IF rules evidence needed: integrations/rag_client.py [M6]
       -> rag_routes.py [M7] -> rag/evidence_service.py + provenance.py [M7]
    <- evidence (source, revision, jurisdiction, confidence, NOT_VERIFIED)
 <- 3 DISTINCT and valid plan alternatives or explicit infeasible/error result
 -> C# persists plan set + provenance -> Angular comparison cards [M3]
```
RAG retrieval does not grant pass. Geometry/rules engine applies deterministic validated checks where verifiable. Infeasible requests do NOT fabricate successful 3 plans.

## F. Edit/save/confirm — M3 ↔ M5 ↔ M6
```
Angular editor [M3] -> proposed room edit {projectId, baseRevisionId, operation, args}
 -> PlansController.cs [M5]
 -> EditPlanHandler.cs [M5] checks project authorization + baseRevision/version
 -> Python whole_plan_validator.py + room_operations.py [M6]
    -> atomic geometry result or structured rejection
 -> M5 saves NEW immutable revision (only if valid)
 -> Angular renders accepted geometry, or keeps previous version + error
 -> ConfirmPlanHandler.cs [M5]: verify saved revision has current valid check
 -> explicit homeowner acknowledgement; confirmation tied to exact revision hash
```
If plan changed, stale validation and confirmation must be invalidated. No Admin/Staff override. Use concurrency token and transaction guard; reject 409 stale revision, 422 bad geometry (example statuses to ratify).

## G. Reports/architect — M3 ↔ M5
```
Angular report page [M3] -> ReportsController.cs [M5]
 -> GenerateReportHandler.cs [M5] loads exact confirmed version + checks
 -> PreliminaryPdfRenderer.cs [M5] -> secured stored PDF
 -> preview/download endpoint with per-project access check
Angular assistance request [M2/M3] -> ArchitectRequestsController.cs [M5]
 -> explicit recipient/documents/consent -> persisted request and access grants
```
Never claim official approval, send emails, or share surveys without successful authorization and explicit consent.

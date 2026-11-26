> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# How files connect — 4 concrete examples

## Survey Upload (M2 ↔ M5 ↔ M6)
`Angular survey form [M2] → SurveyController [M5] → UploadSurveyHandler [M5] → secure file storage [M5] → Python survey_routes [M6] → ingest/polygon_normalizer [M6] → C# checks/land revision → Angular confirmation [M2]`. M4 integrates DbContext/migration if needed; M7 reviews boundaries/evidence. Uploaded PDF ≠ legal boundary verification.

## Generate 3 Plans (M3 ↔ M5 ↔ M6 ↔ M7)
`Angular plan comparison [M3] → PlansController [M5] → GeneratePlansHandler [M5] → PythonPlanComputeClient [M5] → generate_three [M6] → geometry validator [M6] → RAG evidence client [M6] → rag/evidence_service [M7]`. C# persists three verified alternatives or an infeasible error. M4 owns shared DI/authorization.

## Save & Confirm (M3 ↔ M5 ↔ M6)
`Angular Konva editor [M3] → PlansController [M5] → EditPlanHandler [M5] → Python whole_plan_validator [M6] → M5 writes immutable revision → ConfirmPlanHandler [M5] authenticates homeowner, exact revision, current validation → C# MySQL transaction`. Rejected edit does not modify previously saved geometry or confirmation. M4 handles shared context and authorization platform review.

## RAG Source Update (M7 ↔ M6 ↔ M5)
`regulations PDF metadata/extract/chunk/index/retrieve [M7] → source + page + authority + applicability evidence → M6 deterministic rule evaluator → M5 API response → M2/M3 UI`. Staff proposed changes must be approved before publication. No invented legal approvals.

> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — AI & System Architecture

- **Project:** BuildMind
- **Document ID:** BM-SB-006
- **Version:** 1.1
- **Status:** Draft; not approved or implementation-verified
- **Prepared:** 2026-11-14

## Confirmed technology
- **Angular** frontend
- **C#/.NET** backend
- **Microsoft SQL Server** database
- **Mapbox** location/mapping

## Recommended technology — pending final decision
- ASP.NET Core Web API as C# backend interface
- Mapbox GL JS in Angular
- OpenAI API for chatbot/planning assistance
- Python FastAPI for AI plan orchestration and geometry algorithms if needed
- Konva.js for interactive 2D canvas rendering (not a complete CAD engine)
- PayHere for Sri Lankan online payment
- Azure Blob Storage for survey and generated-file storage

## Conceptual system diagram
```
Browser / Angular
  ├── Mapbox map UI
  ├── Konva.js 2D editor [proposed]
  └── Chatbot / 3-plan comparison / payment UI
             |
             v
      C#/.NET API
  ├── Project + requirements management → MSSQL
  ├── Survey/file management → Blob storage [proposed]
  ├── Jurisdiction + deterministic rules validation → versioned rules in MSSQL
  ├── AI orchestration → OpenAI / FastAPI [proposed]
  ├── Subscription verification → PayHere [proposed]
  └── Authorized export/download
```

## End-to-end processing
1. Frontend uploads survey via backend; metadata saved and file stored safely.
2. Mapbox location chosen and saved, without running initial rules validation yet.
3. Requirements captured and saved.
4. Backend resolves regulatory jurisdiction from **verified** geographic/authority datasets, selects source-backed rules, runs initial checks.
5. AI path uses structured inputs to generate candidates, validates each, returns **three** suitable plans if possible. Manual path uses 2D editor geometry and validates on submit.
6. Both paths ideally share one structured floor plan geometry model (rooms, walls, boundaries, dimensions, doors etc.) so editing, validation and export reuse code.
7. Changed geometries or requirements invalidate affected compliance results.
8. Server verifies eligibility/payment and authorizes finalized file export.

## Boundaries and safety
- Model-generated images/text alone are not reliable dimensional geometry. AI is assistance; deterministic geometry and rule checks are necessary.
- Mapbox is **not** the source of legal zoning/setback decisions.
- No subscription activation solely from browser payment callback. Backend verifies payment with trusted gateway.
- Private upload files must have access controls; avoid making them public blobs.
- User-facing chatbot is separate from internal developer Second Brain Q&A.

## Logical data entities (proposed, not a SQL schema)
Users, Projects, SurveyPlans, ProjectLocations, HouseRequirements, RegulatorySources, BuildingRules, ValidationRuns, ValidationResults, FloorPlans, FloorPlanVersions, SubscriptionPlans, UserSubscriptions, Payments.

## APIs (proposed groups, not actual routes)
Auth, Projects, Survey, Location, Requirements, Rules, Chatbot, Generate, FloorPlan, Subscription, Payment, Download.

## Architecture open decisions
Exact versions, backend hosting, AI approach, geometric constraints, FastAPI necessity, image vs structured outputs, jurisdiction data, Konva support requirements, subscription models, export formats and operational limits.

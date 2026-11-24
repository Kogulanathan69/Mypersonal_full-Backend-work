# Backend implementation matrix

Baseline audit 2026-11-24 of `fec3254` (branch `feature/captain-full-backend-completion`). The state is taken from source plus executed tests, never from file existence alone.

Legend:
- IMPLEMENTED: code exists.
- PARTIAL: some required pieces are missing.
- MISSING: no code.
- VERIFIED: tests executed in this session and passed.
- BLOCKED: needs an external decision.
- DEFERRED: knowingly postponed.

"Normal host" means `Program.cs` with its configuration flags.

## Baseline (Phase 0)

| Feature | State | Evidence and gaps |
|---|---|---|
| API composition root, RFC 9457, security headers, body limit, rate limiting, CORS, readiness | IMPLEMENTED, unit-VERIFIED | Infrastructure.Tests 1140/1140 |
| Register / login / JWT | IMPLEMENTED, unit-VERIFIED | Development only, `Authentication:Enabled` + `Database:Enabled`; not live-verified |
| Refresh / logout-all | IMPLEMENTED | Behind `Authentication:RefreshTokens:Enabled` |
| Password recovery | PARTIAL | Provisional; Development only; sender never delivers (no email approved) |
| Profile | IMPLEMENTED | Registered with auth |
| Project CRUD + AuthorizationVersion | IMPLEMENTED | Atomic owner/version/revision CAS on update |
| Dashboard | IMPLEMENTED | Registered with auth |
| Survey upload/status | PARTIAL | Services, endpoint and private storage exist. **`ISurveyRepository` has no implementation and no table.** Module never registered. |
| Survey extraction (Python) | PARTIAL | `python-services/app/survey/*` library only; no HTTP route |
| Land geometry / CRS | IMPLEMENTED (library) | Python `geometry/land`; C# compatibility v1 (`fec3254`); not exposed over HTTP |
| Requirements | PARTIAL | Services and repository exist. **`IRequirementCatalogueProvider` has no implementation.** Registration is restricted to `Testing` hosts. Requirements.Tests 456/456 |
| Plan revision history (read) | IMPLEMENTED | MySQL query exists; not registered in the normal host |
| Plan save / candidate preview / apply / reject | PARTIAL | Application services exist. **Missing:** `ICandidatePreviewStore`, `ICandidatePreviewTransaction`, `IPlanEditorComputeClient`, `ICandidateValidationEvidenceSource`, `IPlanRevisionStore`, and plan revision writes. |
| Final confirmation | PARTIAL | **Missing:** `IFinalConfirmationTransaction`, `IConfirmedPlanSnapshotSink`, `IProjectLifecycleEligibilityPort` |
| AI-assisted edit | PARTIAL / BLOCKED | `IAiEditProposalPort` has no implementer (G20) and no approved AI provider (G11) |
| Generation workflow | PARTIAL | C# contracts; Python feasibility/placement/`layout_validation` libraries. No HTTP route, no C# orchestration, no job persistence. Uncommitted orchestration in the main workspace is of unverified origin and **not adopted**. |
| Regulation retrieval (RAG) | IMPLEMENTED, unit-VERIFIED | Registered behind `Regulations:Retrieval:Enabled`; rag-service pytest 238/238; constraints contract v0 uses synthetic fixtures |
| Reports | PARTIAL | Builder, PDF renderer, integrity and read verifier exist. **No `IReportRepository` implementation, no table, no endpoints.** Reports.Tests 163 pass + 1 skip. |
| Architect requests / consent | PARTIAL | Entity, DTOs, workflow candidate. **No repository, table or endpoints.** |
| Python services HTTP | PARTIAL | `/health` only; no internal auth |
| MySQL migrations | IMPLEMENTED | 4 migrations: InitialFeatureSchema, AddProjectAuthorizationVersion, AddSessionSecurity, AddPlanRevisionHistory. Live verification in Phase 2. |
| RH09 | DEFERRED | See KNOWN-BLOCKERS |

Phase updates are appended below as they are verified.

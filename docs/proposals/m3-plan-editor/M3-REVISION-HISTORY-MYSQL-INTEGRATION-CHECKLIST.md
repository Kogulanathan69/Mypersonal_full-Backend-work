# M3 Revision History — M4 MySQL integration checklist

Status: STEP 17-A handoff checklist. This document describes the evidence M3 needs from M4; it does not approve physical schema names, modify M4 persistence, or claim a real adapter exists.

## Verified current state

- `PlanRevisionHistoryService`, `IPlanRevisionHistoryQueryPort`, `IProjectAuthorizationPort`, and the two Revision History HTTP routes exist on `develop`.
- `BuildMindDbContext` currently maps users, projects, requirement revisions, project requirement heads, and session-security records. No plan or immutable plan-revision persistence record is present.
- The `projects.authorization_version` concurrency token exists, but no production `IProjectAuthorizationPort` registration exists for M3.
- No class implementing `IPlanRevisionHistoryQueryPort` and no production registration for the M3 endpoint module exists.
- M3's database-free host uses controlled ports. It is not a fake MySQL adapter and proves no SQL behavior.

## Adapter delivery required from M4

- [ ] Implement `IPlanRevisionHistoryQueryPort.ReadPageAsync` with exact `projectId` and `planId` predicates and bounded keyset retrieval.
- [ ] Implement `ReadRevisionAsync` with exact `projectId`, `planId`, and `revisionId` predicates.
- [ ] Return no protected row for a missing or wrong-scope identity.
- [ ] For the first page, establish `TraversalHead` and the bounded rows in one consistent database read.
- [ ] For a continuation, verify the cursor's original head, after-version, after-revision, and expected-parent identities before setting `CursorAnchorVerified=true`.
- [ ] Read at most `pageSize + 1` rows (or an equivalently bounded query) to determine `HasMore`; never load full history.
- [ ] Translate malformed or unverifiable lineage to `InvalidCursor`, with no rows or head metadata.
- [ ] Preserve cancellation and classify provider timeout/unavailability through the existing safe persistence error boundary.

## DI and authorization dependencies

- [ ] Register the M4 query adapter as scoped `IPlanRevisionHistoryQueryPort` only after its disposable-MySQL suite passes.
- [ ] Supply an approved scoped `IProjectAuthorizationPort` backed by the project owner and current positive `authorization_version`.
- [ ] Keep `ICurrentUser` as the only actor source; never accept an actor from route, query, body, or header data.
- [ ] Ensure the initial and final authorization calls use fresh database state. A denial or version change must suppress every history/revision payload.
- [ ] Register `IPlanRevisionHistoryService` and the M3 endpoint module only in the reviewed production composition change; STEP 17-A does not activate either.

## Schema and migration prerequisites

M4 remains the single writer for records, mappings, `BuildMindDbContext`, migrations, and physical names. The reviewed design must provide:

- [ ] A plan identity scoped to its project and an exact current immutable-revision head.
- [ ] Immutable revision rows containing revision ID, project/plan scope, version, exact parent, geometry JSON and SHA-256, land/requirements revision IDs, validation evidence, command ID, creator, UTC creation time, and change summary.
- [ ] Uniqueness preventing duplicate plan/version and revision identity, plus parent/head integrity suitable for compare-and-swap writers.
- [ ] An index supporting exact project/plan lookup and deterministic descending keyset traversal without offset scans.
- [ ] A reviewed M4 migration and snapshot update; no automatic migration at application startup.
- [ ] MySQL 8.4-compatible JSON and `datetime(6)` mappings consistent with existing conventions.

## Disposable test fixture

Use only an isolated `buildmind_test_<unique>` database and synthetic identities. Seed, through the M4-owned test utility:

1. Owner A and Owner B.
2. Project A owned by Owner A with authorization version 7.
3. A different Project B owned by Owner B.
4. Plan A with four immutable revisions in exact parent order and a current head at version 4.
5. A same-shaped foreign plan/revision under Project B for non-disclosure checks.
6. Canonical geometry from `docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json`, with its exact computed hash and valid synthetic validation evidence.

The fixture must expose IDs to tests without embedding credentials or customer data. Database creation, migration, seed, and cleanup belong to the disposable test harness, never application startup.

## Required real-MySQL cases

- Authorized owner list and exact lookup.
- Cross-owner, cross-project, cross-plan, and cross-revision non-disclosure.
- Authorization-version change before read, between read and final check, and immediately before response release.
- Page sizes 1 and 50, deterministic order, bounded SQL, and index/query-plan evidence.
- Tampered original head, anchor revision/version, expected parent, project, and plan cursor bindings.
- Concurrent append while traversing: continuation stays on its original head; a fresh query observes the new head.
- Corrupt JSON/hash/evidence/lineage rows fail closed without payload.
- Provider timeout, connection failure, cancellation, lock timeout, and disposal behavior.
- Migration from empty database, incremental upgrade, no pending model changes, reviewed ranged deployment script, and cleanup.

## Expected public error mapping

| Application status | HTTP result |
|---|---|
| `DeniedOrNotFound` | 404 `NOT_FOUND`, no protected payload |
| `InvalidRequest` | 400 validation problem |
| `InvalidCursor` | 409 `REVISION_CONFLICT`, no protected payload |
| `DependencyUnavailable` | 503 `DEPENDENCY_UNAVAILABLE` |
| `DependencyTimeout` | 504 `DEPENDENCY_TIMEOUT` |
| Invalid dependency response or unexpected failure | generic 500, no internal/provider details |

## Configuration and regression commands

Credentials must come from an approved secret store or process environment and must never be printed or committed. Existing configuration keys are `Database__Enabled`, `ConnectionStrings__BuildMind`, and test-harness variables documented in `tests/BuildMind.Persistence.IntegrationTests/README.md`.

After M4 adds the adapter and its live tests, run from the repository root:

```powershell
dotnet build BuildMind.sln -c Release --no-restore
dotnet run --project tests/BuildMind.Application.ContractTests/BuildMind.Application.ContractTests.csproj -c Release --no-build
dotnet test tests/BuildMind.Infrastructure.Tests/BuildMind.Infrastructure.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~RevisionHistoryIntegrationHostTests|FullyQualifiedName~PlanEditorEndpointContractTests"
dotnet test tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~PlanRevisionHistory"
```

The final command is pending: no `PlanRevisionHistory` real-MySQL tests or adapter exist yet. It must run only with the authorized disposable MySQL 8.4 variables configured. A mock-only green run is not MySQL integration evidence.

# MySQL logical schema and migration coordination

Design proposal; table names must reconcile with actual source and approved migrations. C# owns writes. IDs, lengths, nullability, provider, collation, index names, deletion/retention policy need explicit review. Use UTC timestamps, exact numeric or validated metric JSON as appropriate; never rely on latitude/longitude degrees for area.

| Entity | Key fields and relationship | Owner |
|---|---|---|
| Users | id, normalized unique email, password hash, role/status, createdUtc | M1 |
| PasswordResetTokens | userId FK, token hash, expiresUtc, usedUtc | M1 |
| RefreshTokens (if approved) | hashed token, userId, expiry, revokedUtc | M1 |
| Projects | id, ownerId FK, name, type, status, concurrency token | M1 |
| SurveyPlans | projectId, storage key, content hash, MIME, size, state | M2 |
| SurveyExtractionResults | surveyId, revision, extracted fields and confidence, source page | M2 |
| LandLocations / LandBoundaries | projectId, revision, CRS, source, polygon JSON, area/perimeter, validation and confirmed time | M2 |
| RequirementRevisions | projectId, revision, floor count, rooms, spaces, relationships, design method | M5 |
| GenerationJobs | projectId, exact land/requirements revisions, idempotency key, state/error, timestamps | M6 |
| PlanAlternatives | jobId, stable alternative ID, geometry/schema version, comparison metrics | M6 |
| SelectedAlternatives | project/job/alternative reference, selected time | M6 |
| FloorPlans | projectId, optional selected alternative, current revision pointer | M3 |
| PlanVersions | planId, version number, geometry hash + JSON, land and requirement revision, validation ref | M3 |
| PlanEditProposals | planId/baseVersion, commands, state, diagnostics | M3 |
| ValidationResults | geometry/input hash, engine/rule versions, errors, warnings, evidence status | M6 |
| PlanConfirmations | exact planVersion, userId, acknowledgment, timestamp | M3 |
| Reports | projectId, confirmed plan version, snapshot/artifact key, state | M5 |
| ArchitectRequests / ShareConsents | recipient ref, projectId, selected documents, consent and policy version, status | M5 |
| RegulatorySourceMetadata | document/version, authority, date, jurisdiction, source references, review state | M7 |
| DelegatedStaff / ModuleAssignments / ChangeProposals / ApprovalEvents | scoped staff account, proposed change, base version, admin decision, audit record | TBD |

Proposed indexes: Users normalized email unique; Projects(ownerId,status); child FKs; unique(projectId,revision); unique(planId,versionNumber); unique(jobId,alternativeId); idempotency unique per project/operation. Use application-managed concurrency token compatible with selected MySQL provider. Do not assume SQL Server rowversion or T-SQL. Version confirmation and revision updates transactional; no cascade deletion of historical reports without policy.

Migration workflow: feature owner supplies entity and mapping proposal, M4 integrates DbContext/model snapshot exclusively; inspect generated migration SQL; use disposable test database; verify empty apply, upgrade, constraints, rollback/forward recovery, provider compatibility. No destructive change on existing DB without explicit approval and backup/recovery plan. Do not store raw survey files or credentials in Git or DB blobs by default; store private artifact references.

## Implemented physical schema — BE-M4-D03 (2026-11-18, uncommitted, not yet executed against MySQL)

`users`, `projects`, `requirement_revisions`, `project_requirement_heads`. Details, keys and blocked areas: [22](22-M4-D03-SCHEMA-REGISTER.md). The logical tables above remain proposals until their owners' contracts and approvals exist.

## BE-M4-D06 persistence, transactions and concurrency (STEP 09-A/09-B; mapping lead-approved)
`TransactionRunner` was audited and left unchanged: it already provides atomicity, unknown-commit and unconfirmed-rollback handling, cancellation safety, ReadCommitted by default, quiet disposal and no retries. Optimistic concurrency uses `projects.revision` (concurrency token) and requirement-revision compare-and-set. New live tests cover lost-update prevention and parallel writers. No schema change and no migration change. See [24-M4-D06-PERSISTENCE-CONCURRENCY.md](24-M4-D06-PERSISTENCE-CONCURRENCY.md).

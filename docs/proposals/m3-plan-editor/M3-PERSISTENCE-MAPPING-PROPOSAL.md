# M3 persistence mapping proposal

**PROVISIONAL — OWNER APPROVAL REQUIRED**

Task: `BE-M3-INT-04`

Target: MySQL 8.4 through an M4-selected .NET 10 EF Core provider

Owners required: M4 database/infrastructure, M1 projects/lifecycle, M5 requirements/reports, M6 validation/hash

This document describes M3 application persistence requirements. It is not an approved physical schema, EF mapping, migration, or database implementation.

## Existing application contracts

| Contract | Current responsibility | Persistence implication |
|---|---|---|
| `IPlanRevisionStore` | Exact/current/history lookup and compare-and-append | Immutable revisions plus atomic current pointer |
| `ICandidatePreviewStore` | Candidate create/get | Immutable candidate snapshot and state |
| `ICandidatePreviewTransaction` | Apply, Reject and Expire transitions | Conditional state transition; Apply also appends revision atomically |
| `IFinalConfirmationTransaction` | Confirmation plus approved M1 lifecycle transition | Confirmation/lifecycle atomicity; outbox requirement must be added by M4 design |
| `IConfirmedPlanSnapshotSink` | Publish confirmed snapshot after confirmation | Must become post-commit/outbox driven for durable delivery |

No existing contract authorizes M3 to add EF entities, DbContext configuration or migrations.

## Logical relationships

```text
Projects (M1)
  └─ FloorPlans
       ├─ PlanRevisions (immutable chain)
       ├─ EditCandidates (immutable snapshot + terminal state)
       └─ PlanConfirmations (immutable exact-revision reference)
            └─ OutboxMessages (shared M4/M5 delivery mechanism)
```

All child rows must carry or validate project scope. Cross-project plan, revision, candidate, confirmation and input-revision references are invalid even if identifiers exist.

## FloorPlans

| Logical field | Required | Proposed physical options | Notes |
|---|---:|---|---|
| PlanId | yes | `BINARY(16)` preferred; `CHAR(36)` alternative | Primary key |
| ProjectId | yes | Same UUID representation | FK to M1 Projects; indexed |
| CurrentRevisionId | nullable until first revision | UUID | FK to PlanRevisions; circular insertion ordering requires review |
| Status | yes | bounded `VARCHAR` or approved code table | Do not use provider-specific enum before lifecycle approval |
| ConcurrencyToken | yes | application-managed `BIGINT UNSIGNED` or opaque token | MySQL has no SQL Server `rowversion` equivalent |
| CreatedAtUtc | yes | `DATETIME(6)` | UTC enforced by application |
| UpdatedAtUtc | yes | `DATETIME(6)` | Updated with pointer/status changes |

Proposed constraints/indexes:

- PK `(PlanId)`.
- FK `ProjectId -> Projects.Id`, restrictive deletion.
- Unique or ordinary `(ProjectId, PlanId)` for scoped joins.
- Index `(ProjectId, Status)`.
- `CurrentRevisionId` must reference a revision for the same plan; ordinary FK alone cannot enforce same-plan scope. Use a composite candidate key `(PlanId, RevisionId)` and composite FK where provider support is acceptable, or enforce atomically with reviewed SQL and tests.

## PlanRevisions

Maps `PlanRevisionSnapshot`. Existing rows are append-only.

| Logical field | Required | Proposed type | Integrity rule |
|---|---:|---|---|
| RevisionId | yes | UUID | PK |
| PlanId | yes | UUID | FK to FloorPlans |
| ProjectId | yes/recommended denormalization | UUID | Must match plan project; supports owner-scoped queries |
| ParentRevisionId | nullable for root | UUID | Same-plan revision FK; no cascade delete |
| VersionNumber | yes | `BIGINT UNSIGNED` | Positive, monotonically appended per plan |
| GeometrySchemaVersion | yes | bounded `VARCHAR` | M6 approval required |
| GeometryJson | yes | `JSON` or immutable text | Exact snapshot used for hash verification |
| GeometryHash | yes | `BINARY(32)` or 64-char hex | M6 canonicalization/algorithm approval required |
| LandRevisionId | yes | UUID | Exact M2 revision |
| RequirementsRevisionId | yes | UUID | Exact M5 revision |
| ValidationId | yes | UUID | Exact M6 validation evidence |
| ValidationSnapshot | review | `JSON` or normalized reference | Needed if evidence can change/disappear |
| EditCommandId | nullable | UUID | Idempotency key for editor-generated revision |
| CreatedBy | yes | UUID | Trusted authenticated user |
| CreatedAtUtc | yes | `DATETIME(6)` | Immutable UTC timestamp |
| ChangeSummary | nullable | bounded text | Safe length; not trusted for audit decisions |

Constraints/indexes:

- PK `(RevisionId)`.
- Unique `(PlanId, VersionNumber)`.
- Candidate/composite unique `(PlanId, RevisionId)` for scoped FKs.
- Scoped command idempotency: unique `(PlanId, EditCommandId)` when non-null. Confirm provider behavior for multiple nulls and filtered-index alternatives.
- Index `(PlanId, CreatedAtUtc)` for history.
- Index `(ProjectId, PlanId, RevisionId)` for owner-scoped exact lookup.
- Parent FK must not cascade. Application/transaction verifies parent belongs to same plan and is current for normal append.
- Updates to geometry, hash, validation, input revisions, creator and timestamps are forbidden after insertion. Database permissions/triggers are optional defense-in-depth subject to M4 review.

## EditCandidates

Maps `CandidatePreviewSnapshot` and its terminal outcome.

| Logical field | Required | Proposed type | Integrity rule |
|---|---:|---|---|
| CandidateId | yes | UUID | PK |
| ProjectId / PlanId | yes | UUID | Scoped plan reference |
| OwnerId | yes | UUID | Trusted M1 identity; never request-owned |
| BaseRevisionId | yes | UUID | Same plan/project |
| LandRevisionId | yes | UUID | Exact M2 input |
| RequirementsRevisionId | yes | UUID | Exact M5 input |
| CommandId | yes | UUID | Candidate command identity |
| GeometryJson | yes | `JSON` or immutable text | Exact server-produced snapshot |
| GeometryHash | yes | binary or hex SHA-256 candidate | Must match exact stored snapshot |
| GeometrySchemaVersion | yes | bounded `VARCHAR` | Must match snapshot |
| ValidationId / snapshot | yes | UUID plus optional JSON | Exact evidence binding |
| Status | yes | bounded code | Pending/Applied/Rejected/Expired, provisional |
| CreatedAtUtc / ExpiresAtUtc | yes | `DATETIME(6)` | Expiry uses database/application UTC consistently |
| AppliedRevisionId | nullable | UUID | Required only when Applied; unique candidate outcome |
| ConcurrencyToken | yes | `BIGINT UNSIGNED` or opaque token | Conditional consumption |

Constraints/indexes:

- PK `(CandidateId)`.
- Composite FK `(PlanId, BaseRevisionId)` to same-plan revision candidate key.
- Unique `(PlanId, CommandId, BaseRevisionId)` proposed for preview idempotency; confirm whether multiple intentionally regenerated previews are permitted.
- Unique nullable `AppliedRevisionId` and FK to PlanRevisions.
- Check/transaction invariant: Applied requires `AppliedRevisionId`; other statuses require null.
- Index `(PlanId, Status, ExpiresAtUtc)`.
- Index `(OwnerId, ProjectId, Status)` only if an owner-facing candidate list is approved.
- Conditional `UPDATE ... WHERE Status='Pending' AND ExpiresAtUtc > now AND ConcurrencyToken=expected` or row lock protects single consumption.

Candidates are retained for audit/reconciliation. Cleanup may archive payloads only after an approved retention period and must preserve identifiers, outcome, hash and references.

## PlanConfirmations

| Logical field | Required | Proposed type | Integrity rule |
|---|---:|---|---|
| ConfirmationId | yes | UUID | PK |
| ProjectId / PlanId | yes | UUID | Scoped plan reference |
| ConfirmedRevisionId | yes | UUID | Exact current immutable revision |
| GeometryHash | yes | binary or hex | Must equal revision hash |
| ValidationId | yes | UUID | Exact M6 evidence |
| LandRevisionId | yes | UUID | Copied from confirmed revision |
| RequirementsRevisionId | yes | UUID | Copied from confirmed revision |
| ConfirmedBy | yes | UUID | Trusted actor |
| ConfirmedAtUtc | yes | `DATETIME(6)` | UTC |
| WarningAcknowledgement | yes | `BOOLEAN`/`TINYINT(1)` | Does not override blocking failures |
| IdempotencyKey | review | UUID or bounded string | Trusted operation key if API retry is approved |

The current `PlanConfirmationSnapshot` lacks land and requirements revision IDs; `IFinalConfirmationTransaction` receives the exact revision and can persist them, but an explicit shared contract decision is required.

Uniqueness depends on M1 lifecycle policy:

- Minimum: unique `(PlanId, ConfirmedRevisionId)` prevents duplicate confirmation of one revision.
- If only one active confirmation per plan is allowed, model supersession explicitly rather than overwriting history.
- Unique scoped idempotency key prevents uncertain client retry from creating duplicates.
- No cascade deletion from project/plan/revision.

## OutboxMessages

Shared M4/M5 proposal, not an M3-owned production table.

| Field | Proposed type | Notes |
|---|---|---|
| MessageId | UUID | PK and delivery identifier |
| EventType | bounded `VARCHAR` | e.g. `plan.confirmed` after approval |
| EventSchemaVersion | bounded `VARCHAR` | Independent of geometry schema |
| ConfirmationId | UUID | Unique event source reference |
| Payload | `JSON` or immutable text | Exact immutable event envelope |
| CreatedAtUtc | `DATETIME(6)` | Same transaction as confirmation |
| DeliveryStatus | bounded code | Pending/Processing/Delivered/DeadLetter proposal |
| AttemptCount | unsigned integer | Incremented atomically |
| NextRetryAtUtc | nullable `DATETIME(6)` | Scheduled retry |
| ProcessedAtUtc | nullable `DATETIME(6)` | Successful delivery |
| SafeFailureCode | nullable bounded `VARCHAR` | Never raw secrets/provider messages |
| LeaseOwner / LeaseExpiresAtUtc | optional | Multi-worker claim recovery |

Indexes: unique `(EventType, ConfirmationId)`, pending worker index `(DeliveryStatus, NextRetryAtUtc, CreatedAtUtc)`, optional lease-expiry index.

## MySQL type decisions requiring M4 approval

### UUID

- `BINARY(16)`: compact indexes and comparisons; requires consistent byte-order conversion and diagnostic tooling.
- `CHAR(36)`: human-readable and simpler tooling; larger indexes and collation considerations.
- Recommendation for review: `BINARY(16)` with one tested application converter. Never mix encodings or use provider-specific ordered UUID conversion without cross-service fixtures.

### Geometry snapshot

- MySQL `JSON`: syntax validation and selective inspection; database may normalize textual representation. Do not compute a canonical hash from a database reserialization.
- `LONGTEXT`/bounded text: exact bytes preserved; no native JSON validity unless checked by application/database constraint.
- Requirement: store the exact application serialization used for the bound hash, or store canonical bytes separately. M6 owns canonical serialization approval.

### Hash

- `BINARY(32)`: compact, fixed size, no case issues.
- `CHAR(64)` ASCII hex: readable, larger.
- Current C# value includes `sha256:` prefix and lowercase hex. M4 mapping must parse/store/reconstruct consistently; M6 has not approved the final format.

### Time and status

- Use `DATETIME(6)` interpreted as UTC; reject unspecified/local `DateTime` at adapters. MySQL timestamp range/session conversions make `TIMESTAMP` less explicit for long-lived domain history.
- Prefer bounded strings or approved lookup codes for provisional states. Avoid physical MySQL ENUM until change/version policy is approved.

## Delete and retention rules

- No cascade from FloorPlans to revisions, confirmations, candidates or outbox audit records.
- Project deletion should be soft-delete/restricted until M1 privacy/retention policy defines archival/anonymization.
- Historical revision geometry and confirmation snapshots require an approved retention/privacy decision.
- Candidate payload cleanup must never make an Applied candidate impossible to reconcile with its revision.

## Open decisions

1. UUID representation and byte ordering.
2. Exact immutable JSON byte preservation versus MySQL JSON normalization.
3. M6 canonical geometry serialization/hash representation.
4. Validation reference versus immutable evidence snapshot retention.
5. One-active-confirmation lifecycle policy.
6. Candidate preview idempotency scope.
7. Application token versus locking strategy and isolation level.
8. Outbox leasing/dead-letter/retention policy.
9. Cross-module transaction feasibility for M1 lifecycle state and M3 confirmation.

No migration should be generated until M1/M4/M5/M6 approve these decisions and M4 reconciles names with the shared model.

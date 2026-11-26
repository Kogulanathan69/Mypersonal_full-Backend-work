# M3 transaction design

**PROVISIONAL — OWNER APPROVAL REQUIRED**

Task: `BE-M3-INT-04`

Primary implementation owner: M4

Required reviewers: M1, M3, M5, M6

This document defines required atomic outcomes. It does not select an EF provider, isolation level, SQL syntax or production implementation.

## General rules

- External geometry, validation, AI and RAG calls occur before a database transaction. Never hold database locks during network computation.
- Results from external work are immutable, hash-bound proposals. Every current-state invariant is rechecked inside the database transaction.
- Transaction retries are permitted only with a stable idempotency key and reconciliation after uncertain outcomes.
- Owner/project scoping is applied to every read and conditional write.
- Append new history; never overwrite revision geometry or delete prior revisions.

## Candidate Apply

### Outside the transaction

The Application service may authenticate the actor, load candidate/current inputs for early feedback, validate the stored candidate snapshot/hash, and verify authoritative evidence. These checks reduce contention but are not sufficient for correctness.

### Required atomic transaction

1. Begin transaction using the M4-approved isolation strategy.
2. Load/lock or conditionally target the candidate by CandidateId, OwnerId, ProjectId and PlanId.
3. Recheck status is Pending and `ExpiresAtUtc > database-consistent now`.
4. Recheck FloorPlan current revision equals BaseRevisionId.
5. Recheck applicable land and requirement revisions, using transactionally consistent authoritative state or approved version tokens.
6. Recheck candidate snapshot hash/evidence references stored in the row; do not call external services while locked.
7. Check `(PlanId, CommandId)` idempotency and candidate outcome.
8. Allocate the next version under compare-and-append protection.
9. Insert one immutable PlanRevision.
10. Conditionally update FloorPlan current pointer and concurrency token from BaseRevisionId to new RevisionId.
11. Conditionally update candidate Pending -> Applied and set AppliedRevisionId.
12. Commit.

Any failed insert/update/constraint rolls back revision, pointer and candidate state together. Affected-row counts must be exactly one for conditional pointer/candidate writes. Zero means conflict/replay/expiry, not success.

### Failure points

- Before revision insert: no state change.
- After revision insert but before pointer update: rollback removes staged revision.
- After pointer update but before candidate consumption: rollback pointer and staged revision.
- Candidate consumption conditional update loses race: rollback everything.
- Commit response lost: state is uncertain; reconcile candidate outcome and command/revision uniqueness before retry.

## Candidate Reject and Expire

Reject is a conditional terminal transition:

```text
UPDATE candidate
SET status=Rejected, concurrencyToken=next
WHERE id=? AND owner=? AND project=? AND plan=?
  AND status=Pending AND expiresAtUtc>now AND token=?
```

Expiry is `Pending -> Expired` only when `ExpiresAtUtc <= now`. Reject/Expire never inserts a revision.

Replay outcomes:

- Applied -> `AlreadyApplied`; preserve AppliedRevisionId.
- Rejected -> `AlreadyRejected`.
- Expired -> `Expired`.
- Concurrent Apply/Reject: one conditional transition wins; loser reads terminal state.
- Expiry during Apply: the transaction's consistent check decides. If expired at its authoritative check, Apply loses and creates no revision.

Retention: keep terminal metadata and hash/references. Payload archival/deletion requires policy approval and must preserve audit/reconciliation.

## Revision append and restore

Normal `IPlanRevisionStore.TryAppendAtomicallyAsync` requires:

1. Recheck expected current revision.
2. Check scoped command idempotency.
3. Insert immutable revision with next unique version.
4. Update current pointer conditionally.
5. Commit.

Restore:

1. Authenticate/authorize before transaction.
2. Load exact historical immutable revision without modifying it.
3. Revalidate its geometry against current land/requirements and M6 rules outside locks.
4. Begin transaction and recheck expected current revision plus applicable input versions.
5. Insert a new revision containing the historical geometry and fresh validation/input bindings.
6. Set ParentRevisionId to the revision current at restore time; record source restore ID in approved metadata/change summary.
7. Update current pointer conditionally and commit.

Restore never updates, deletes or reactivates a historical row. Concurrent stale restore returns conflict.

The current `IPlanRevisionStore.TryAppendAtomicallyAsync` can express compare-and-append but does not expose an explicit plan concurrency token or uncertain-outcome reconciliation query. M4 design approval is required.

## Final confirmation

### Outside the transaction

The existing service checks authenticated ownership, exact/current revision, geometry hash, validation freshness/blocking state, current inputs and warning acknowledgement. M1 lifecycle eligibility should also be checked before beginning, but must be revalidated atomically if lifecycle state is stored in the same database.

### Required atomic transaction

1. Begin transaction.
2. Recheck trusted owner/project access or an approved authorization/version assertion.
3. Load exact current revision and matching plan pointer.
4. Recheck land/requirement applicability.
5. Recheck persisted validation reference/freshness assertion; no external call under lock.
6. Recheck warning acknowledgement requirement.
7. Recheck M1 lifecycle state and eligible transition.
8. Insert immutable PlanConfirmation with exact revision/input/hash/evidence references.
9. Apply the approved M1 project lifecycle transition with its concurrency token.
10. Insert one confirmed-plan OutboxMessage with unique `(event type, confirmation)`.
11. Commit.

Confirmation, lifecycle transition and outbox insertion either all commit or all roll back when they share the agreed database boundary. If M1 lifecycle is a separate service/database, true atomicity is unavailable; the team must approve an orchestration/saga and compensation policy rather than claim a local transaction solves it.

The current `IFinalConfirmationTransaction` method receives both confirmation and exact revision, so an M4 adapter can populate input revision fields and an outbox payload. The interface documentation does not explicitly require outbox insertion; this must be approved and tested in the M4 implementation.

`IConfirmedPlanSnapshotSink` currently executes after confirmation. A direct external publish failure must not roll back a committed confirmation. The production adapter should enqueue in the transaction and let a worker deliver after commit; the sink can become an outbox-facing abstraction after owner approval.

## Concurrency approaches

| Approach | Strength | Trade-off |
|---|---|---|
| Optimistic pointer/token update | Short transactions, conflict is explicit | Requires affected-row checks and retries/reload |
| `SELECT ... FOR UPDATE` on plan/candidate | Simple serialized decision per row | Lock contention; strict lock ordering required |
| Unique constraints plus insert | Strong final idempotency defense | Constraint errors require safe semantic translation |
| Serializable isolation | Broad anomaly protection | Higher contention/deadlock risk; provider behavior must be measured |

Recommended review baseline: optimistic conditional writes plus unique constraints; use short row locks only where allocating a version or coupling multiple conditional rows cannot be expressed safely. M4 must validate generated SQL on MySQL 8.4.

Lock order proposal to reduce deadlocks: Project (only if lifecycle update) -> FloorPlan -> Candidate -> Confirmation/Outbox inserts. All paths must use one documented order.

## Idempotency and uncertain commit

- Command: unique `(PlanId, CommandId)` returns existing outcome only after identity/hash comparison.
- Candidate Apply: CandidateId has one terminal outcome and at most one AppliedRevisionId.
- Confirmation: unique exact revision and/or trusted idempotency key according to lifecycle policy.
- Outbox: unique `(EventType, ConfirmationId)`.
- Report consumer: records MessageId before/with report creation and returns prior outcome for duplicates.

If commit acknowledgement is lost, never immediately repeat the writes. Query by CandidateId/CommandId/ConfirmationId/idempotency key:

- committed matching outcome -> return reconciled success;
- no outcome -> retry under the same key if policy permits;
- conflicting outcome -> return conflict and require review;
- database still unavailable -> return an explicit uncertain/persistence failure, not fabricated success.

## Test limitation

The INT-04 in-memory conformance double stages data and commits under one lock to demonstrate required observable outcomes. It cannot prove MySQL isolation, deadlock handling, EF change tracking, provider conversions, affected-row behavior or crash recovery. Disposable MySQL integration tests owned/coordinated by M4 remain mandatory.

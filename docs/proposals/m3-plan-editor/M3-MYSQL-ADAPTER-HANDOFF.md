# M3 MySQL adapter handoff

Status: STEP 15-B test-preparation handoff. No schema, migration, DbContext, production DI, credential, or database change is included.

## Boundary and ownership

M3 owns the application contracts and their conformance expectations. M4 remains the single writer for EF Core records, mappings, `BuildMindDbContext`, migrations, MySQL adapters, transaction implementation, and production persistence registration. M1 owns project authorization and lifecycle semantics. M5 owns report publication behavior; M6 owns authoritative validation integration.

The repository currently exposes two mutation shapes. `IPlanRevisionStore`, `ICandidatePreviewStore`, `ICandidatePreviewTransaction`, and `IFinalConfirmationTransaction` are older split boundaries. `IAtomicPlanMutationPort` carries authorization version, typed mutation identity, V1 fingerprint, matching-duplicate, conflict, unknown-outcome, and reconciliation semantics across six mutations. Selection of the canonical persistence path remains a joint M1/M3/M4/M5 decision. Nothing in STEP 15-B approves either path or authorizes duplicate adapters.

## Approved application contracts

- `IProjectAuthorizationPort`: exact project plus trusted actor returns either denial without a snapshot or an authorization snapshot containing the current positive version.
- `IPlanRevisionHistoryQueryPort`: bounded keyset page and exact revision reads scoped by project and plan. Continuations bind the original head, current anchor, and expected parent.
- `IPlanRevisionStore`: exact/current revision reads, legacy unbounded history, and atomic append result. It does not carry actor, authorization version, mutation fingerprint, or reconciliation authority.
- `ICandidatePreviewStore`: candidate create and candidate-ID lookup. Its lookup signature alone cannot prove project, plan, or owner predicates.
- `ICandidatePreviewTransaction`: Apply/Reject/Expire transition requests over an expected snapshot. It does not carry authorization version, durable mutation identity, fingerprint, or reconciliation.
- `IAtomicPlanMutationPort`: Candidate Create/Apply/Reject/Expire, Revision Append, Final Confirmation, and reconciliation. This is a database-free contract only; it provides no locking, uniqueness, transaction, or durability implementation.

## Required persistence concepts

The following are storage responsibilities, not approved table or column names:

1. A plan identity and current immutable-revision head.
2. Immutable revisions with exact parent, monotonic version, strict geometry JSON, geometry hash, land and requirement revision identities, validation evidence, command identity, creator, and UTC creation time.
3. Candidate snapshots with exact project/plan/owner/base revision, geometry and evidence binding, expiry, and lifecycle state.
4. Final confirmations bound to the exact current revision, geometry hash, validation evidence, confirming actor, warning acknowledgement, and authoritative time.
5. Durable mutation identity containing operation ID, operation category, fingerprint profile/version and exact fingerprint.
6. A durable outcome/reconciliation record sufficient to distinguish matching duplicate, fingerprint conflict, definite failure, and transaction outcome unknown without exposing another owner's payload.
7. Project/plan lifecycle state and compare-and-swap evidence.
8. Transactional report-outbox state for post-confirmation publication.

Physical names, SQL types, indexes, retention, and migration layout are proposals until M4 review and joint approval.

## Authorization-version semantics

- Authentication comes only from the trusted host current-user boundary.
- The project row must bind the exact project, owner and current positive authorization version.
- Preflight authorization is defense in depth. Every mutation adapter must recheck owner and authorization version inside the same database transaction as its writes.
- Revision History performs authorization before and after the read. A version change or revocation withholds all protected payload.
- Historical authorization evidence cannot authorize a new mutation after the version advances. Reconciliation may inspect an old mutation only when fresh disclosure authority is also valid.
- Denial and not-found must remain indistinguishable at customer boundaries.

## Revision and candidate invariants

- Revisions are immutable. A plan/version identity and an edit-command identity require reviewed uniqueness rules.
- Revision append compares the exact current head and advances it once; stale and concurrent writers cannot both succeed.
- History queries are bounded and keyset-based. Offset pagination or loading all history does not conform.
- A continuation must verify the original traversal head, anchor version/revision, and expected parent before releasing a page.
- Candidate creation persists only an exact validated Pending snapshot.
- Apply, Reject, and Expire are compare-and-swap transitions from Pending. Only one terminal transition wins.
- Apply must transition the candidate, append the immutable revision, and advance the plan head atomically.
- Reject and Expire must never append a revision.
- Expiry uses an authoritative database-side transaction time or an approved equivalent, not caller time.

## Idempotency, unknown outcomes, and lifecycle

- The exact typed V1 fingerprint must be recomputed or verified from frozen request material; arbitrary caller digests are not authoritative.
- A repeated operation ID with the exact fingerprint returns the original bound outcome as a matching duplicate.
- The same operation ID with a different fingerprint returns a conflict without protected stored identity or payload.
- Commit failure after the commit request produces a non-retryable unknown outcome with only the exact reconciliation reference.
- Reconciliation re-reads durable state by mutation identity and validates project, plan, actor, authorization evidence, operation category, profile and fingerprint before disclosure.
- No adapter or caller may blindly retry an unknown outcome.
- Final Confirmation must atomically persist the confirmation, perform the approved M1 lifecycle compare-and-swap, record mutation outcome, and enqueue the report event.

## Transactional report outbox

`IConfirmedPlanSnapshotSink` does not itself prove transactional publication. The MySQL design needs a durable outbox row written in the same transaction as confirmation and lifecycle change. Dispatch occurs after commit, is idempotent, and records delivery/retry state without changing confirmation truth. M5 must approve the event payload and consumption contract. No outbox schema exists today.

## Disposable MySQL regression requirements

M4's real adapter must be tested against an isolated MySQL 8.4 database with non-production credentials. Required cases:

1. Empty migration apply, incremental upgrade, ranged deployment script, rollback/forward recovery, and no pending model changes.
2. Exact owner/project/plan/revision predicates and cross-owner non-disclosure.
3. Authorization revocation/version change before a read, during a read, and immediately before mutation commit.
4. Bounded history, deterministic descending lineage, exact lookup, cursor-anchor tampering, concurrent append while traversing, and query-plan/index evidence.
5. Strict geometry JSON round-trip, exact SHA-256 binding, validation evidence binding, maximum payload limits, UTC microsecond behavior, and corrupted-row fail-closed behavior.
6. Candidate Pending-to-terminal CAS, Apply-versus-Reject/Expire races, stale base/head rejection, and rollback after each staged write.
7. Revision append uniqueness and exactly one winner for parallel writers.
8. Matching mutation duplicate, fingerprint conflict, duplicate command, and protected-payload suppression.
9. Commit-unknown fault injection followed by reconciliation; prove no automatic replay.
10. Final Confirmation uniqueness, exact current revision, lifecycle CAS, confirmation/outbox atomicity, and idempotent outbox consumption.
11. Cancellation before transaction, during reads, and before commit; lock timeout, deadlock, connection failure, rollback uncertainty, and disposal behavior.
12. Least-privilege runtime grants, mandatory TLS, readiness without writes, and sanitized logs/results.

Database-free M3 tests prove only application validation, orchestration and response integrity. They do not certify SQL predicates, indexes, constraints, isolation level, row locks, durability, rollback, commit outcome, migrations, provider behavior, or outbox delivery.

## Recommended integration order

1. Jointly select the canonical mutation boundary and approve authorization/lifecycle semantics.
2. M4 implements the read-only authorization plus Revision History vertical slice and runs the disposable-MySQL cases.
3. M4 implements the chosen candidate/revision mutation adapter with mutation ledger and reconciliation.
4. M1/M3/M4/M5 approve and implement Final Confirmation lifecycle plus transactional outbox.
5. Register production services and endpoints only after the full real-MySQL regression passes.

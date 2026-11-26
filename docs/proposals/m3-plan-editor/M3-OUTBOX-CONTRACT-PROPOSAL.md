# M3 confirmed-plan outbox contract proposal

**PROVISIONAL — OWNER APPROVAL REQUIRED**

Task: `BE-M3-INT-04`

Producer transaction: M3 confirmation through M4 infrastructure

Consumer: M5 report generation

Required reviewers: M1, M4, M5, M6

The purpose is reliable delivery of the exact confirmed snapshot reference. This document does not implement an outbox table, worker, broker or report service.

## Event identity

Proposed event type: `plan.confirmed`

Proposed event schema version: `1-draft`

Delivery identifier: `eventId`/outbox `messageId`

The event schema version is independent from the plan geometry schema version.

## Proposed immutable event

```json
{
  "eventId": "uuid",
  "eventType": "plan.confirmed",
  "eventSchemaVersion": "1-draft",
  "projectId": "uuid",
  "planId": "uuid",
  "revisionId": "uuid",
  "confirmationId": "uuid",
  "geometryHash": "provisional-hash-format",
  "geometrySchemaVersion": "provisional-geometry-version",
  "landRevisionId": "uuid",
  "requirementsRevisionId": "uuid",
  "validationId": "uuid",
  "confirmedBy": "uuid",
  "confirmedAtUtc": "ISO-8601 UTC",
  "warningAcknowledgement": true
}
```

M5 must load the exact immutable revision by RevisionId and verify project/plan/hash bindings, or receive an approved immutable snapshot in the event. It must never read an unqualified “current plan” because a later edit may exist.

Including full geometry increases message size and privacy exposure. The baseline proposal uses an immutable reference plus hash. Full snapshot inclusion requires M4/M5 storage, size, encryption and retention approval.

## Producer requirements

- Insert confirmation and outbox row in the same local transaction.
- Unique `(EventType, ConfirmationId)` prevents duplicate event creation.
- Payload is generated from the exact revision/confirmation rows, not client input.
- Commit before any external publication.
- A worker claims only committed Pending messages.
- Failure to contact M5 after commit changes outbox delivery state; it does not delete/roll back confirmation.

## Delivery guarantee

Delivery is at least once. Exactly once across process/database/network boundaries is not claimed.

M5 must be idempotent by EventId, with a durable inbox/processed-event record in the same transaction as report job/snapshot creation where practical. Duplicate messages return the existing report outcome and do not create a second report.

Ordering is not globally guaranteed. If per-plan ordering matters, include confirmation time/version and reject impossible stale transitions explicitly.

## Worker lifecycle

1. Select due Pending or recoverable messages ordered by creation time.
2. Claim a bounded batch with lease owner/expiry using provider-safe skip-lock/conditional-update behavior approved by M4.
3. Deliver outside the transaction used to claim.
4. On success, mark Delivered and ProcessedAtUtc.
5. On transient failure, increment AttemptCount, record a safe code, compute NextRetryAtUtc and release/expire lease.
6. On permanent failure or attempt threshold, mark DeadLetter and alert operators.

Worker restart recovers expired leases. A crash after M5 processes but before Delivered is recorded causes a duplicate delivery, which M5 handles idempotently.

## Retry policy

- Exponential backoff with jitter and configured caps is proposed; values require operations approval.
- Retry network timeout, temporary 5xx/unavailability and explicitly transient failures.
- Do not blindly retry schema rejection, authorization/configuration failure, invalid reference, or payload integrity mismatch.
- Store only safe failure codes/limited sanitized summaries. Detailed provider errors belong in secured operational telemetry, not customer payloads.

## Dead-letter and recovery

- Dead-letter records remain queryable with event identity, safe reason, attempt count and timestamps.
- Redrive requires authorization, audit, schema compatibility and idempotent consumer behavior.
- Correcting a bad event must create a new versioned event or approved repair record; do not mutate delivered history silently.
- Operations need pending age, retry count, dead-letter count and delivery latency monitoring.

## Consumer acceptance checks

M5 should reject safely when:

- Event schema version is unsupported.
- Required IDs are empty/malformed.
- Revision does not belong to project/plan.
- Revision hash/schema/input references differ from the event.
- Confirmation does not reference the exact revision.
- Validation reference cannot be resolved under the approved evidence-retention policy.

The consumer must not interpret the event as regulatory approval. It represents an exact user-confirmed preliminary plan.

## Contract gaps requiring approval

1. Final event name and schema registry/versioning process.
2. Reference-only versus embedded immutable geometry.
3. M5 inbox/idempotency storage and report uniqueness key.
4. M4 worker lease, batch, retry and dead-letter implementation.
5. Retention/privacy/encryption policy for geometry-related payloads.
6. Behavior when a later plan revision exists after confirmation.
7. Whether M1 lifecycle transition shares the same MySQL transaction.
8. Validation evidence retention and lookup owned by M6.

No production worker or table should be created until these owners approve the contract.

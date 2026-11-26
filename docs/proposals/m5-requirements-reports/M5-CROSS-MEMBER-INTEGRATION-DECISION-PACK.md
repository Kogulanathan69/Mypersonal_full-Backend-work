# BE-M5 — Cross-member integration decision pack (proposal; NOT approved)

**Baseline reviewed:** `develop` at `8c2223e1a703c3d3335d6e56f4839b1cd4ee443c`.  
**Prepared:** 2026-11-21. **Owners requested:** M1, M3, M4, M5, M6, M7 and Team Captain/Product for the policy decisions.  
**Status:** PROPOSED / implementation NO-GO for unresolved shared, storage, authorization and delivery contracts. This document changes no public API, dependency injection, schema, migrations or runtime routes. Separate review and explicit owner signoff are required.

## Why this pack exists

M5 has isolated dashboard, requirements and readiness services, a PDF renderer and integrity/read candidates, and architect consent/withdrawal candidates. Their fake-boundary tests do **not** establish a production actor-bound transaction, persisted plan confirmation, private storage durability, eligible architect recipient or actual artifact delivery.

Existing evidence: `DAY-06-FASTTRACK-HANDOVER.md`, `DAY-07-CANDIDATE-HANDOVER.md`, `RequirementRevisionRepository`, `RequirementsApplicationService`, `RequirementReadinessService`, `ReportGenerationCandidate`, `ReportReadCandidate`, and `ArchitectWorkflowCandidate`. Use the current implementations as the source of truth; this document proposes acceptance criteria, not implementation facts.

## Decisions requiring agreement

| ID | Joint owners | Required explicit decision | Acceptance proof / failure handling |
|---|---|---|---|
| M5-INT-01 | M1 + M4 + M5 | Trusted principal and authenticated actor identity; whether staff/admin may access customer projects and under what scoped permission | Actor derived from server session only; synthetic client-supplied actor ignored; wrong owner, revoked actor, staff-without-grant are not authorized |
| M5-INT-02 | M1 + M4 + M5 | One transaction/linearization point for requirements read/write and project ownership or authorization version; exact lock order and transfer/revocation ordering | Concurrent ownership transfer vs save/read produces a single justified ordering; no unauthorized persisted write; stale revision conflicts; transaction rollback verified |
| M5-INT-03 | M4 + M5 | Write operation identity, duplicate-request fingerprint, unknown commit/rollback reconciliation, error codes and retry policy | No automatic write retry after unknown outcome; double submit never creates multiple revisions; recovery observes committed outcome without leaking another owner's data |
| M5-INT-04 | M5 + M6 + M2/M4 as relevant | Consistent readiness context: current eligible land revision, requirements head, catalogue, design method, project eligibility, and atomic freshness recheck before generation | Null/unavailable context is not Ready; revision/land change or policy mismatch blocks stale generation; owner-scoped provider backed by authoritative data |
| M5-INT-05 | M3 + M4 + M5 | Persisted current eligible confirmation lookup, binding project/plan/plan revision/requirements/land/validation/hash; authority for invalidation after edits | A caller-supplied confirmation flag alone cannot generate reports; stale confirmation and hash mismatches fail closed |
| M5-INT-06 | M4 + M5 | Private PDF store interface and ownership; opaque artifact key, write-once bytes, bounded read-before-allocation, digest/readback verification, lifecycle and access policy | No public path/URL; incomplete/malformed/corrupt/oversize/missing bytes never yield Ready or download; metadata-only reads never expose bytes |
| M5-INT-07 | M1 + M4 + M5 | Report accept, publish, status/download authorization and actor-version/revocation linearization; private-storage write vs DB CAS recovery | At most one accepted pending execution per approved operation ID; CAS Pending→Ready or Failed; cancellation/timeout after dispatch is Unknown, not a rollback claim |
| M5-INT-08 | M4 + M5 + Product/G12 | Architect directory authority, recipient eligibility, explicit consent scope and policy-version acceptance, allowed documents/reports, expiry/retention, sharing/delivery channels, withdrawal cutoff | Wrong recipient, revoked consent, stale source or report block new access; withdrawal stops future grants under an agreed ordering; no claim to erase previously delivered copies |
| M5-INT-09 | M4 + M5 + M7 | Durable architect request/consent/event persistence, idempotency and unknown outcome reconciliation, owner/recipient audit boundaries | Atomic create/withdraw, no duplicate durable event on retry, cross-account hiding, race tested against a disposable database |
| M5-INT-10 | M1 + M3 + M4 + M5 + M6 + M7 | Production endpoint activation, error-envelope/HTTP mapping, test environment, minimum evidence and deployment gate | Isolated tests and real auth/TLS database-backed end-to-end checks pass; separate approval for schema migration, production DI/routes, protected branch merges and external delivery |

## Suggested technical boundaries for owner review — not agreed interfaces

1. **Requirements read/write:** the trusted boundary receives actor identity from M1 and checks project access at the same authoritative database observation as the revision head or commit. `GetByIdAndOwnerAsync` followed by an independent `TrySaveAtomicallyAsync` is **not** sufficient to prove revocation safety. M4 decides the transaction and row/version ownership predicates; M5 retains validation and safe response mapping.
2. **Readiness:** an M5/M6 context read must observe current eligible land, current requirements, catalogue and project state consistently. Revalidate the same identities at the generation commit; the current provisional `IRequirementReadinessContextProvider` cannot itself certify authorization.
3. **PDF generate/read:** accept the exact persisted confirmation and immutable source identities atomically. Render outside the DB transaction; M4 privately stores immutable bytes, verifies bounded read-back and digest, then CAS publishes Ready. Storage failure, unknown commit, cancellation or ambiguous publication must enter a durable reconciliation path rather than an unbounded retry or assumed cleanup. Status returns safe metadata only. Download verifies current authorization, exact artifact identity and bytes.
4. **Architect:** keep structural selection checks separate from eligibility and release authority. Bind consent to recipient, policy version and exact selected source revisions; persist request and consent atomically. Withdrawal must reauthorize and atomically revoke future grants according to the G12 cutoff. No recipient dispatch is permitted based only on an isolated Accepted receipt.
5. **Error semantics:** missing/unauthorized project/report/request remain externally indistinguishable where policy demands; internal timeout, dependency unavailable, stale revision and **uncertain write** outcomes must not be reported as successful empty results or proof of rollback.

## Minimum joint QA matrix to sign off

| Scenario | Required test boundary |
|---|---|
| Owner loses access between check and write | Real M1/M4 transaction, two actors, concurrent transfer/revocation |
| Two simultaneous requirements saves on same revision head | Real MySQL CAS; exactly one justified winner, no partial commit |
| Unknown commit / timed-out write | Persistence error injection, durable operation reconciliation, no blind retry |
| Stale requirements, land, validation or confirmation during readiness/report acceptance | Persisted M3/M4/M6 inputs; stale generation blocked |
| Duplicate report request after uncertain acceptance | Durable operation identity, at most one execution and safe status retrieval |
| Corrupt, oversized, missing or wrong-owner private PDF bytes | Bounded M4 storage read and M5 integrity/retrieval checks |
| Withdrawal concurrent with release/download | Durable event and G12-approved linearization/cutoff; no future unauthorized release |
| Wrong recipient, policy-version mismatch, duplicate consent operation | Real recipient directory and durable consent persistence |
| Failed auth and dependencies on public HTTP routes | M1-authenticated isolated HTTP plus M7 privacy/security assertions |

## Signoff and sequencing

1. **M1/M4/M5**: finalize INT-01–03; implement and test the actor-bound requirements transaction before enabling Requirements API or Dashboard production adapters.
2. **M5/M6**: finalize INT-04 and read/generation freshness semantics; then wire readiness behind the approved boundary.
3. **M3/M4/M5**: finalize INT-05–07; deliver an approved private storage/confirmation adapter, durable recovery and verified download; only then consider report routes.
4. **Product/G12 + M4/M5/M7**: finalize INT-08–09 before any recipient grant or release; implement the architect request backend using its approved persistence and eligibility policy.
5. **M7 + Captain**: approve and run INT-10 end-to-end evidence. Production route registration, migrations, publication to protected branches and external sharing remain separately gated.

**Requested response from each owner:** mark every relevant ID `AGREED`, `REVISE`, or `BLOCKED`, cite the exact source contract or proposed replacement, assign implementation owner and test owner, and list the evidence required to close the item. Silence is not approval.

**Caution on gate labels:** historical `G02` descriptions in repository planning notes refer to a source/CI audit, while later M5 communications use G02 for the atomic ownership/revocation gate. The Captain must confirm the canonical gate ID before either usage is marked resolved.

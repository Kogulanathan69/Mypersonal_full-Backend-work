# BE-M5-D07 — Architect request candidate

Stacked on Day 06 f0d5b0e44ce2212800bb5239b9cfba83a867fad8. Reuses immutable ShareConsent and ArchitectRequest models and existing DTOs. No production registration, database schema, directory, artifact dispatch, recipient endpoint or fabricated acceptance/decline.

## Scope
Create explicit grant for exact selected report/documents and recipient; owner-scoped get/list; version-bound withdrawal; strict receipt checks; safe unknown write outcomes. Selection helper is structural only, not authorization. Candidate maximum 100 documents/100 list entries and policy version 128 characters are provisional fail-closed limits, not approved product limits. Owner-only reads conservatively require original requester and current trusted authorization; ownership transfer policy pending. AttemptId is correlation only, NOT durable idempotency. No automatic retry after unknown write; reconciliation required.

## Proposed HTTP contract (not mapped)
POST /api/v1/projects/{id}/architect-requests: recipientId, reportId (optional), explicit documentIds array, explicitConsent, acknowledgedPolicyVersion; no actor or timestamps from body. GET same path (list), GET /{requestId}, POST /{requestId}/withdraw with expectedStateVersion. Authentication required, no-store, uniform missing/unauthorized, 400 invalid, 404 hidden, 409 conflict, 422 ineligible, 503 unavailable/unknown (do not retry unknown automatically); final status codes/operation identity subject to lead agreement. No recipient delivery endpoint until approved directory, consent and release policy.

## Production gates
M1/M4 atomic actor and owner checks; M3 exact current persisted confirmation; M4 private artifact integrity/read-back, document revision identity and eligible source checks, atomic consent/request/event persistence, concurrency and revocation linearization; G12 approved directory, recipient authorization, policy version, dispatch and retention; M7 real cross-account, stale revision, missing PDF, wrong recipient, concurrent withdrawal and end-to-end QA. Withdrawal blocks future access under agreed policy; does not erase previously delivered copies. Fake boundaries test orchestration only, not real persistence or policy. No real requirements-to-confirmation/report-to-architect end-to-end evidence yet.

## Verification
Run Workflow suite and full regression on local Windows SDK; record actual counts separately. Prior Day 06 1873 tests passed. Day 06 limitations (NU1900, Tamil/Sinhala shaping, exact independent PDF extraction, production report adapters) remain open. No new dependencies.

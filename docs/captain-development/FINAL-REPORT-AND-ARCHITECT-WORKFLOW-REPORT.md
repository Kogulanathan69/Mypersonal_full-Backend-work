# Final report and architect workflow report

## Preliminary PDF reports

| Capability | Status | Evidence |
|---|---|---|
| Report request bound to the exact confirmed revision + geometry hash + confirmation (mismatch → 404) | IMPLEMENTED, LIVE_VERIFIED | journey 6 |
| Real PDF generation (M5 renderer: plan geometry with openings, requirements, disclaimer, sources); artifact SHA-256, private storage | IMPLEMENTED, LIVE_VERIFIED | journey 6; Reports.Tests 164 incl. **PDF QA executed** (pypdf) |
| Atomic publication: Pending → Ready via CAS in a protected transaction. An incomplete artifact is never exposed as Ready; failures are recorded with a code. | IMPLEMENTED, LIVE_VERIFIED | journey 6; persistence suite |
| Duplicate protection: the same report id replays as `existing` (200) | IMPLEMENTED, LIVE_VERIFIED | journey 6 |
| Status, history (`GET reports`), authorized download (attachment, no-store, `application/pdf`), cross-user 404 | IMPLEMENTED, LIVE_VERIFIED | journey 6 |
| REPORT_READY notification (same transaction as publication) | IMPLEMENTED, LIVE_VERIFIED | journey 9 |
| Regulation citations inside the PDF | **NOT YET**: the report states that regulatory evidence is unreviewed/synthetic. Embedding the retrieval citations is actionable (R-03). | — |
| Regeneration | a new report id for the same confirmed revision (idempotent by id); no in-place overwrite | by design |

## Architect workflow

| Capability | Status | Evidence |
|---|---|---|
| Architect directory (admin-verified listings), request creation with explicit consent + policy version, atomic request + consent persistence | IMPLEMENTED, LIVE_VERIFIED | journey 6/9 |
| Exact shared revision/report; architect portal: dashboard, requests, request detail, report access, respond (accept/decline), feedback; customer conversation view | IMPLEMENTED, LIVE_VERIFIED | journey 9 |
| Consent withdrawal: access revoked immediately (404), ARCHITECT_REQUEST_WITHDRAWN notification | IMPLEMENTED, LIVE_VERIFIED | journey 9 |
| Notifications + transactional outbox email | IMPLEMENTED, LIVE_VERIFIED (local capture) | journey 9; FINAL-EMAIL-INTEGRATION-REPORT |
| Request **completion** state and consent **expiry** | **MISSING (actionable)**: the domain has Pending/Withdrawn plus architect responses; an explicit Completed state and time-based expiry are not modelled (R-04) | — |

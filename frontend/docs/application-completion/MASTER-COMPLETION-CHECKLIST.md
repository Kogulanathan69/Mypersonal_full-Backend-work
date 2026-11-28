# BuildMind master completion checklist

Updated: 2026-11-28  
Frontend starting HEAD: `5a1ed22daa085dc165c9336e5e4cfae58fd96fb4`  
Backend audited HEAD: `25309cd82417fb1e821a09f4e9fbc6084dfc0b2f` (`feature/fasttrack-final-backend`)

Status vocabulary: `NOT STARTED`, `IN PROGRESS`, `UI IMPLEMENTED`, `CONTRACT VERIFIED`,
`LIVE INTEGRATED`, `VERIFIED COMPLETE`, `BLOCKED`, `NOT SUPPORTED BY CURRENT BACKEND`.

| Module | Discovery | UI | Contract | Live integration | Tests/evidence | Status / next action |
| --- | --- | --- | --- | --- | --- | --- |
| Public homepage | complete | accepted | n/a | n/a | 222 unit baseline; responsive evidence | VERIFIED COMPLETE — protected checkpoint |
| Runtime configuration | complete | n/a | base URL and transport documented | API live at loopback through ignored same-origin proxy; committed `config.json` remains environment-neutral | live browser evidence | VERIFIED COMPLETE for local development |
| Authentication/session | complete | screens aligned to contract | 7 auth/session endpoints verified in contract | live registration/login and guarded routing passed | 245 tests include single-flight concurrent refresh and no-loop handling | LIVE INTEGRATED |
| Password recovery | complete | screens aligned to email + token contract | forgot/reset contracts verified | API and Mailpit live; UI round trip not yet run | screen fixture tests | UI IMPLEMENTED — complete disposable Mailpit reset round trip |
| Profile/security | complete | profile, password, sessions and security-event screens implemented | profile, password, status, sessions, revocation and events verified | profile/session inventory and revocation live browser verified | live screenshot and session-state defect correction | LIVE INTEGRATED except password-change round trip |
| Customer shell/dashboard | complete | guarded shell and project-backed dashboard implemented | notifications verified | disposable account dashboard live browser verified | screenshot + route/auth tests | LIVE INTEGRATED |
| Projects | complete | searchable list, create, detail, conflict-safe edit and lifecycle screens implemented | list/create/detail/update/lifecycle verified | create/list/update/search/lifecycle live browser passed | API fixtures and screenshots | LIVE INTEGRATED |
| Survey upload/extraction | complete | authenticated PDF/image preview, bounded zoom, extraction evidence and immutable measurement review implemented | list, upload, status, private file, extraction and review verified | upload live verified; extraction/review requires successful synthetic OCR evidence | browser screenshots; API/component fixtures | UI IMPLEMENTED — live review awaits a successfully extracted synthetic document |
| Land workspace | complete | authenticated architectural boundary drawing, point inspection, edge measurements, validation and revision-aware confirmation implemented | land, validation, confirmation and edges verified | live geometry workflow pending project with land revision | API fixture; type/lint/build regression | UI IMPLEMENTED — add live geometry evidence and edge-role correction |
| Requirements | complete | authenticated server-owned room programme and conflict-safe revision persistence implemented | snapshot and full revision write verified; nested additional-space fields remain untyped | live workflow pending seeded requirements revision | API fixture; type/lint regression | UI IMPLEMENTED PARTIAL — typed catalogue/bootstrap contract needed for first revision and richer fields |
| Regulation evidence | complete | project-scoped truthful unavailable boundary with professional-review guidance implemented | retrieve contract verified; approved corpus state not configured | deliberately not invoked without an approved corpus | backend negative evidence exists | BLOCKED for live evidence — no citations or legal conclusions fabricated |
| Generation/layouts | complete | prerequisite summary, idempotent generation, actual candidate floor-plan rendering, diagnostics and selection implemented | create/get/alternatives/select verified | live workflow requires confirmed land and requirements | API fixtures; full regression | UI IMPLEMENTED — live prerequisite workflow pending |
| Plan editor/revisions | complete | persisted plan inspection, selection, zoom, supported room-resize preview, before/after validation and apply/reject, immutable history, revert and confirmation implemented | plan GET, candidates/apply/reject, versions/confirm/revert verified | live selected-plan workflow pending prerequisites | component/API fixtures; full regression | UI IMPLEMENTED — live workflow pending prerequisites |
| Instruction refinement | complete | instruction entry, server proposal provenance, validated candidate apply/reject and no-auto-apply boundary implemented in plan workspace | rule-based/LLM proposal contract verified | live workflow pending selected plan prerequisites | API fixture; regression pending | UI IMPLEMENTED — actual `proposalSource` is displayed |
| Architect collaboration | complete | customer directory and existing consented request tracking implemented | directory and request list verified; creation blocked by missing policy-discovery contract | live workflow pending ready report/policy version | full regression | UI IMPLEMENTED PARTIAL — no policy version is guessed |
| Architect portal | complete | guarded shell, verified-profile dashboard, consent-scoped request queue, exact report download, response, feedback and completion implemented | dashboard/request/report/respond/feedback/complete verified | live role workflow pending an admin-provisioned architect | API fixture; regression pending | UI IMPLEMENTED — server remains the authorization authority |
| Staff portal | complete | authenticated module-aware dashboard, assigned-task transitions and proposal withdrawal implemented | dashboard/tasks/proposals verified; monitoring and policy reads remain | live role workflow pending provisioned staff identity | regression pending | UI IMPLEMENTED PARTIAL — add monitoring and policy views |
| Admin portal | complete | authenticated shell, dashboard, account activation/suspension, task inventory, monitoring and audit-chain verification implemented | dashboard/accounts/tasks/monitoring/audit verified | live role workflow pending provisioned admin identity | layout regression pending | UI IMPLEMENTED PARTIAL — catalogue, architect and staff administration remain |
| Reports/revisions | complete | immutable plan history, conflict-safe revert, exact revision confirmation, report list and authenticated PDF download implemented | report create/list/download and plan versions/confirm/revert verified | live workflow pending confirmed plan | API fixtures; full regression | UI IMPLEMENTED — report creation becomes available from confirmed-plan workflow |
| Global states/accessibility | complete | shared primitives and corrected public semantics/contrast | RFC 7807/error codes verified | n/a | 0 raw colours; 56/56 contrast pairs; axe 10/10 desktop/mobile routes | VERIFIED COMPLETE for tested routes |
| Final handover | complete | n/a | reconciled matrix and route inventory | local services verified | final handover report | VERIFIED COMPLETE with evidence-based limitations |

## Runtime evidence

- `http://localhost:5182/health`: HTTP 200 on 2026-11-28.
- `http://localhost:5182/health/ready`: HTTP 200, MySQL/Python healthy and RAG degraded as expected without an approved corpus.
- Swagger at port 5182: HTTP 200.
- Mailpit at port 8027: HTTP 200.
- Frontend `http://127.0.0.1:4300/`: HTTP 200.
- Portable startup registered 148 routes and completed without warnings on 2026-11-28.
- Live customer Playwright suite passed 2/2 sequentially with disposable local data.

## Exact resume point

Mission handover complete. Remaining items require an approved regulation corpus, provisioned privileged identities, a policy-version discovery contract, richer requirements bootstrap contracts, successful OCR evidence, or approved external providers. Do not commit an operator-specific origin.

# Independent review package

The author has **not** reviewed their own work as "independent". This package is for an assigned reviewer (G19).

## Scope

Branch `feature/captain-full-backend-completion` (local), commits `fec3254..HEAD`. Key review targets, in risk order:

| # | Area | Files | What to check |
|---|---|---|---|
| 1 | Admin RBAC and audit | `CaptainIntegration/AdministrationStore.cs`, `Endpoints/Administration/*` | Fresh privilege reads; lock order (users rows ascending → admin role rows → audit head last); last-admin protection; proposal apply atomicity and stale handling; audit hash canonical form |
| 2 | Protected writes | `ProtectedPlanningWriter`, all `*Store` appends | A1–A8 lock + authorization version on every Captain write |
| 3 | Survey worker | `SurveyExtractionWorker.cs`, `SurveyStorageReconciliation.cs` | Claim/lease/heartbeat CAS; publication only with the current lease token; deferred delete + reconciler safety (never deletes referenced blobs) |
| 4 | Architect portal | `ArchitectPortal.cs`, `DeliverableAdapters.cs` (eligibility hook) | Recipient scoping; withdrawal revocation; consent-bound report access; transactional outbox |
| 5 | OCR sandbox | `python-services/app/survey/ocr.py` | rlimits, env scrubbing, header limits, temp cleanup, network isolation option |
| 6 | Account security | `AccountSecurityService.cs`, `AccountSecurityStore.cs`, `SmtpEmailSender.cs` | Code hashing, single use, session revocation, loopback-only default |
| 7 | AI rule-based adapter | `RuleBasedAiEditProposalPort.cs` | Bounded numbers only; never an LLM claim; orchestration validates and stores, never applies |
| 8 | Migrations | `Persistence/Migrations/2026112418*`, `…190404*` | Additive only; FK RESTRICT; naming; pin lists |

## How to reproduce

LOCAL-RUNBOOK → `restart-stack.sh` (RAG=1 for phase 5) → `smoke_journey.py` (188) / `smoke_admin.py` (65) / `persistence-tests.sh` / `unit-suites.sh` / python `pytest`.

## Known issues to confirm

- RH09;
- the intermittent M4 `RequestBodyLimitTests` broken-pipe race;
- M3 orchestration collapses AI rejection reasons into `AI_PROPOSAL_INVALID`;
- the containers are not built.

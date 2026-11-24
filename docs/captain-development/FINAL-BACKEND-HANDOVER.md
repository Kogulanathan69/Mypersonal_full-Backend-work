# Final backend handover — Captain full-backend completion

| | |
|---|---|
| Branch | `feature/captain-full-backend-completion` (local only; **not pushed, not merged**) |
| Worktree | `C:\Users\kogul\Documents\BuildMind-Worktrees\captain-full-backend-completion` |
| Starting SHA | `fec32547e7b2508f9d7af71206dc651cfebe5794` |
| Final code tree | `86b9593` (the following commit adds only these documents) |
| Main workspace | `BuildMind-Platform` on `feature/bm-final-backend-integration`, untouched. Its pre-existing uncommitted files were not imported. |
| Other worktrees / member branches | untouched; nothing imported, cherry-picked or merged |

## Status by acceptance category

### A. Backend implementation completion: **COMPLETE for the approved 2D MVP register, with documented external limits**

V3 register: **46 product routes**. At the end, **43 are IMPLEMENTED**:
- 2 PARTIAL: password recovery, which has no approved email provider;
- 1 BLOCKED but answering truthfully: AI edit proposals (G20);
- admin/staff OUT_OF_SCOPE (G09).

See MASTER-API-ENDPOINT-MATRIX for the per-route detail.

Built in this execution's final round (`86b9593`):
- durable survey extraction: worker, local PDF-text/OCR, persistence and result API;
- `/users/me`;
- land validate/confirm;
- alternatives/select;
- `POST /plans`;
- edits/validate;
- `POST /versions`;
- the confirm alias;
- the truthful AI-proposal route;
- report listing;
- OpenAPI export and frontend guide.

Still not implemented, by decision:
- admin/staff (G09);
- project lifecycle transitions (no approved M1 writer);
- real reviewed regulatory constraints (G06, EDGE-ROLE);
- email delivery;
- architect dispatch (G12);
- cost/3D (outside the MVP).

### B. Local functional verification: **VERIFIED**

Full customer journey live: **136/136** checks on the real stack:
- API .NET 10;
- MySQL 8.4.13 VerifyFull;
- python-services with token auth;
- rag-service fail-closed, no corpus.

Journey coverage, in order: register → login → JWT → project → requirements → survey upload → **extraction → review evidence** → land validate → **confirm land** → generation → alternatives → select → edit/validate → save version → history → confirm → PDF report → download → architect request (consent) → withdraw → dashboard → refresh/logout → dependency failure → rate limit.

| Suite | Result |
|---|---|
| Application unit | 252/252 |
| Infrastructure | 1160/1160 |
| Reports (with PDF QA) | 164/164 |
| Requirements | 456/456 |
| Workflow | 197/197 |
| ContractTests | exit 0 |
| Live MySQL | 103/103 |
| python-services | 981/981 |
| Local OCR | 3/3 |
| rag-service | 238/238 |

### C. Full integration verification: **PARTIAL**

| Area | Status |
|---|---|
| API ↔ MySQL / python-services / extraction worker | Verified live |
| API ↔ rag-service | Verified on the fail-closed paths only |
| Real regulation corpus | BLOCKED in WSL |
| Angular client | Not in this repository; contract handed over (OpenAPI + guide) |
| CI | Not run: nothing pushed |
| Independent review | Not obtained (G19) |
| Members' parallel implementations | Not reconciled |

### D. Production readiness: **NO-GO**

- RH09 is DEFERRED/UNRESOLVED.
- R-1/R-3, G03 licensing, G11 hosting/TLS and G19 review are open.
- OCR sandboxing (SR-5) is open.
- Every feature is Development-only.
- STEP 8B is ON HOLD.
- Synthetic policy data must be replaced by approved sources.

### E. Out of scope / blocked by decision

Admin/staff (G09), AI provider (G20), email provider, architect delivery (G12), cost/3D.

## Gates preserved (all 20 non-negotiable restrictions)

| Restriction | Outcome |
|---|---|
| STEP 8B | Untouched: no rag-service or `RagServiceClient` change |
| Windows security | Smart App Control, PowerShell execution policy and application control unchanged |
| Git | No push, merge or destructive Git command |
| Production | No production access |
| Security controls | JWT, ownership, sessions, TLS and internal auth not weakened |
| Regulation and corpus | No invented regulation; corpus identity not bypassed |
| Ratified contracts | Not silently changed: the M1 inventory was restored when the pin caught it, and the V3 routes are additive in the Captain module |
| Secrets and private data | No secrets in the repository. Private survey files never leave the loopback python-services. No coordinates are inferred from lengths. NEEDS_REVIEW is never mapped to verified. |
| Synthetic data | Labelled as synthetic |
| RH09 | Deferred |
| Captain DB | Never reset or dropped |
| Long-running work | No transaction held during Python/OCR; retries are bounded |

## Checkpoints

| SHA | Content |
|---|---|
| `b98a396` … `d2d74bc` | Earlier rounds: phases 0–7 (profile, MySQL, Python API, generation, editing, regulatory evidence, reports, verification tools) |
| `ed7945d` | Previous round's documents |
| `f4880f6` | Master-order phase 0: requirements audit + master matrix |
| `86b9593` | Survey extraction worker + V3 register route completion + tests + OpenAPI |
| (next) | Final documents for this round |

## Migrations

All migrations are additive. See DATABASE-MIGRATION-REGISTER. This round added `20261124170241_AddSurveyExtraction`, applied to the Captain DB via `migrate.sh`. M4 must review all Captain migrations before any shared branch.

## Changes to existing (non-Captain) code, for owner review

| Change | Owner |
|---|---|
| Earlier rounds: session store lock order | M1/M4 |
| Earlier rounds: `python-services/app/main.py` bootstrap | M6 |
| Earlier rounds: M4 test pins | M4 |
| Earlier rounds: `InternalsVisibleTo`, one `Program.cs` call | — |
| This round: `python-services/app/main.py` adds the `/internal/v1/survey/extract` route with a per-path body limit | M6 |
| This round: `ProfileEndpointModule` and `PlanningEndpointModule` / `PlanLifecycleEndpointModule` helper methods changed from `private` to `internal`, so the Captain module reuses the same handlers. No behaviour change. | M1, M3 |
| This round: migration pin lists, 1 entry each | M4 |

## Decisions needed before any further step

1. Contract approval of the Captain internal routes (`captain-*` schemas, including `captain-survey-extract/1`) and the V3 route aliases by M2/M3/M6.
2. Ownership of the survey worker (TD-07/08/10) and of the evidence ledger (M2/M3/M6).
3. Real requirement catalogue, generation policy, freshness window and sharing policy (product).
4. Edge-role mapping and a reviewed constraint table (M6/M7/G06).
5. AI provider and data policy (G20); email provider; architect delivery and retention (G12).
6. Approved frontend origin (C6) and an M1 lifecycle writer.
7. Production prerequisites: RH09 resolution, R-1/R-3, G03 licence, G11 transport/mTLS, OCR sandboxing.

## How to run

See LOCAL-RUNBOOK. Related documents: FRONTEND-INTEGRATION-GUIDE, SECURITY-REVIEW, TECHNICAL-DECISION-REGISTER, FEATURE-COMPLETION-MATRIX, INTEGRATION-COMPLETION-MATRIX, DATABASE-SETUP, DATABASE-MIGRATION-REGISTER, API-ENDPOINT-INVENTORY, TEST-EVIDENCE, KNOWN-BLOCKERS, DEVELOPMENT-PROGRESS.

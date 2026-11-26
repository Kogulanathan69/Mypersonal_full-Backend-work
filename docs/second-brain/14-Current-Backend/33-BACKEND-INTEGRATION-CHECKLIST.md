# Backend integration checklist: all members (MVP, 2026-11-19)

- **Purpose:** a single checklist for integrating member work on the shared M4 infrastructure and the M7 retrieval service. Run it before every PR to `develop`.
- **Status meanings:** COMPLETED = merged or CI-verified; PENDING = work exists but isn't merged or verified; BLOCKED = waiting on a decision or another member.

## A. Rules every member follows
- [ ] **Branch from the latest `develop`**, open PRs as drafts, and **never push to `develop`/`main`**. Merging needs an independent review (G19).
- [ ] **Single writer for shared files.** M4 owns `BuildMindDbContext`, migrations, `Program.cs` and `ApiServiceCollectionExtensions`, `Directory.*`, `.github/workflows`. Ask M4 for any change; don't edit them yourself.
- [ ] **Schema changes** go through an M4-coordinated **new** migration. Never edit `20261118133137_InitialFeatureSchema`.
- [ ] **Errors** use RFC 9457 Problem Details with C5 codes. Don't hand-roll error bodies; dependency failures surface as `DependencyFailureException` (503/504).
- [ ] **No secrets or PII** in code, logs, tests or docs. Configuration comes from environment variables or user secrets only (names in doc 30).
- [ ] **Internal calls** go through `InternalServiceClient`-based typed clients only (allowlisted relative paths, deadline, bulkhead, no retries). Never `new HttpClient()` to a service.
- [ ] **Database writes** use `TransactionRunner` / repositories. Never call external services inside a transaction, and never retry writes automatically.
- [ ] **Tests:** add tests for your feature and keep every existing objective. CI must be green; C# tests run only in Linux CI (G17).
- [ ] **Python services** bind to loopback or a private network only. No public exposure.

## B. Per-member integration status
| Member | Integrates with | Status | Next action |
|---|---|---|---|
| **M1** auth/projects | `ICurrentUser`, JWT (A1/A2), `User`/`Project` repositories | **BLOCKED**: `feature/m1-auth-projects` unmerged; rehydration factories missing; A1/A2 undecided | M1 delivers factories and the token service; lead decides A1/A2; then M4 adds JWT validation and auth policies |
| **M2** survey/land | Storage adapter, upload limits, land entities | **BLOCKED**: no M2 storage or upload contract or entities | M2 proposes the contract; then M4 builds the storage adapter, request limits and migration |
| **M3** plan editor | `IAiEditProposalPort`, `IValidationFreshnessPolicy` (G20), job/version persistence | **COMPLETED (#25, #31 merged)** for authorization adapters; **PENDING** job/plan persistence contract | M3 + M4 agree job/version keys; G20 implementers assigned |
| **M5** requirements/reports | Requirement-revision repository (M4), report snapshot | **COMPLETED (#28, #33 merged)**; **PENDING**: `feature/m5-requirements-reports` has 1 unmerged commit (no M4-owned files) | M5 opens a PR; report snapshot contract still needed for M4-D06 |
| **M6** generation/validation | python-services routes, `IPlanValidationEvidenceVerifier`, M7 constraints (doc 29), shared internal auth (§C) | **BLOCKED**: no M6 code or branch | Reply to doc 29 §8; agree python-services auth adoption (§C) |
| **M7** RAG | rag-service (contract v1), `IRegulationEvidenceSource`, RAG readiness, G11 token | **PENDING merge**: PR #32, CI green (runs #64, #67, #68, #69 by metadata) | Independent review; lead artifact verification |
| **M4** infrastructure | Everything above | **COMPLETED** D01–D06 (CI-verified); **OPEN** D07 (R-1/R-3, review) | Doc 27 decision; reviews |

## C. Shared internal authentication: M6 coordination (python-services)
**What already exists (no change needed on the C# side):** `InternalServiceClient` sends `X-BuildMind-Internal-Token` for **any** internal service that has `AuthToken` configured, so `Services__Python__AuthToken` already works.

**Proposed adoption in python-services** (needs the **M6 bootstrap custodian's agreement**, because the code is shared by M2, M3 and M6):
1. Reuse the reviewed pattern in `rag-service/app/auth.py`:
   - default-deny except `/health`;
   - `hmac.compare_digest`;
   - two-token rotation;
   - fail closed with 503 when required but unconfigured;
   - explicit development opt-out only.
   The variables would be `BUILDMIND_PYTHON_INTERNAL_TOKENS` / `BUILDMIND_PYTHON_AUTH`.
2. C# start-up validation: require `Services:Python:AuthToken` outside Development (the same rule as RAG; one-line change in `ServicesOptions.Validate`, owned by M4).
3. CI: generate a random per-run token for the live Python smoke tests (a `database-compatibility` workflow change, which needs lead approval).
4. Rollout order: (1) python-services accepts tokens with the opt-out set in Development only; (2) the API sends the token; (3) enforce everywhere.

**Message for M6 (lead to send):**
> python-services currently accepts any caller on the network path. We propose the same internal token check rag-service now uses (PR #32, `rag-service/app/auth.py`, doc 31). Can the bootstrap custodian approve adding it to python-services (default-deny except `/health`)? The C# side already sends the header when configured.

## D. Final integration gate (before any release candidate)
- [ ] All member PRs merged with **independent review** (G19).
- [ ] CI green on `develop`, with **artifacts read** and counts recorded (not just job status).
- [ ] MySQL R-1/R-3 decision taken (doc 27); 5 verified green database runs.
- [ ] End-user auth and CORS (A1/A2, C6) implemented and tested.
- [ ] Internal auth on **both** Python services; G11 transport and hosting decided.
- [ ] M6 generation/feasibility delivered; doc 29 contract approved or explicitly deferred.
- [ ] Regulation sources' legal currency reviewed (G06), or the product clearly labels evidence as unverified (already enforced in the contract).
- [ ] Runbook (doc 30) executed end to end on a machine without Smart App Control restrictions.

## E. Deliverable summary: M4 + M7 (see doc 32 for evidence)
| Completed (verified) | Pending (done, awaiting review/CI evidence) | Blocked |
|---|---|---|
| M4 D01–D06 core (CI #52/#66); M7 D02, D03 | M7 D04 (PR #32: retrieval service, C# port, readiness, G11 token); D07 docs (runbook, acceptance matrix); run #69 counts | M4: JWT/auth policies (M1, A1/A2), CORS (C6), storage/limits (M2), job dedup (M3/M6), R-1/R-3 decision. M7: D05 (M6), D06 end-to-end (M6). python-services auth (M6 custodian). Independent reviews (G19) |

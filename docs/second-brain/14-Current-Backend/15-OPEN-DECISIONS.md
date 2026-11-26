# Open decisions and release blockers

| ID | Question | Owner / required evidence |
|---|---|---|
| G01 | Actual member-to-slot mapping (lead M4+M7?) and frontend capacity | Lead signoff |
| G02 | Current source, branches, migrations, CI status and uncommitted work | Read-only repository audit |
| G03 | MySQL provider compatibility with EF Core/.NET and exact schema | M4 dependency/test proof |
| G04 | Exact API prefix, names, JSON casing, error envelope, auth/session/reset/email | M4 + M1 + consumers |
| G05 | Survey supported formats, reliable OCR, limits, storage, CRS and tolerances | M2 verified sample coverage |
| G06 | Verified regional rules/authority applicability and source license | M7 expert review |
| G07 | Geometry canonical schema, supported plot/floor/room cases, thresholds and timeout | M6 with M2/M3 |
| G08 | Manual path and upper-floor scope/screen ordering | Product/lead approval |
| G09 | Admin/staff module owner, customer/subscription/payment and architect company portal scope | Product/lead decision |
| G10 | Existing RAG code path, current retrieval API, real evidence tests | M7 source audit |
| G11 | Service transport/auth, hosting, queue/job persistence, retention, AI provider and budget | M4/M6/M7 |
| G12 | Report renderer, sharing channels, retention, recipient authority | M5 approval |
| G13 | Actual CI gates/main branch protection/names | Lead GitHub review |
| G14 | Google Sheets automation alternative setup | Optional; do not block code |
| G15 | Performance targets, release support envelope, deployment and recovery | QA/product signoff |
| G16 | `python-services/app/geometry`, `plans`, `validation` READMEs still state whole-module M6 ownership; V3 splits geometry/land (M2), plans/editor (M3), generation/validation (M6) | **RESOLVED 2026-11-17** — lead approved; geometry/plans/validation READMEs reconciled (BE-M7-D01) |
| G17 | Smart App Control on the lead's workstation blocks loading unsigned freshly built C# assemblies (test host and API run); local C# test execution and API smoke check blocked | Device owner/lead: choose a policy-compliant execution path (e.g. CI on Linux, separate dev environment, approved code-signing); no bypass |
| G18 | M3 merged code uses paths differing from V3 proposals (`python-services/app/plan_editor` vs `plans/editor`; `docs/proposals/m3-plan-editor`), and its C# console contract runner is not in `BuildMind.sln` or CI | Lead + M3 (+ M4 for solution/CI): accept actual paths in 10-OWNERSHIP; decide solution/CI inclusion |
| G19 | PR #8 (BE-M4-D01a) was squash-merged as `183ecf9` with **no recorded review**, although ruleset `BuildMind-Develop-Protection` requires 1 approving + code-owner review | Lead: arrange a retrospective independent review; record outcome (no approval or waiver is assumed) |
| G20 | Implementers not yet assigned for M3 ports `IAiEditProposalPort`, `IValidationFreshnessPolicy` and the composite `IApplicableInputRevisionSource` | Lead + M2/M3/M5/M6/M7 (see 17-SHARED-CONVENTIONS-PROPOSAL §13) |

Until resolution, mark relevant work BLOCKED or PROPOSED; test fixtures must be explicitly labeled. No unsupported compliance, delivery readiness, or live integration claims.

## Status notes — 2026-11-17 (nothing below resolves a decision)
- G01: lead currently holds M4 + M7 (lead decision 2026-11-17); remaining allocation **open**.
- G02: audited 2026-11-17 — see [audit](../17-Progress/07-AUDIT-2026-11-17.md).
- G03, G04, G11, G13: **open**; lead asked to keep EF Core MySQL provider, disposable DB/test account, error format/API prefix, internal service auth and main/develop divergence open until separately reviewed.
- G09: admin module ownership **open**.
- G10: candidate canonical RAG path identified (`C:\BuildMind-PDF-Processor\regulation_retrieval`), not yet explicitly confirmed; RAG repository structure **open** — see [proposal](16-RAG-SOURCE-STRATEGY-PROPOSAL.md).

- 2026-11-17 (D01a): G16 resolved. G17 opened. G03/G04/G09/G10/G11/G13 and final roster remain open.

- 2026-11-17 (develop sync): G13 changed — `main` deleted on the remote; branch `main-→-develop` at former main `d606f00`; `origin/HEAD` dangling locally. Still open. G18 opened.

- 2026-11-17 (lead): default branch intentionally changed to `develop`; the former `main` was accidentally renamed to `main-→-develop`. G13 stays open until the production branch name and protection rules are corrected. G17 and G18 stay open after D01a CI verification.

- 2026-11-17 (BE-M4-D01b, proposals only — nothing resolved):
  - G03: [provider study](18-MYSQL-PROVIDER-STUDY.md) — only stable EF Core 10 MySQL provider found is Oracle `MySql.EntityFrameworkCore 10.0.12+MySQL26.10.0` (GPL-2.0 + FOSS exception, licence review needed; MySQL 8.4 and feature behaviour to be proven in a D02 spike). Pomelo has no EF Core 10 release; EF Core 9 is out of support since 2026-11-10. **Open.**
  - G04: conventions C1–C12 proposed in [17-SHARED-CONVENTIONS-PROPOSAL](17-SHARED-CONVENTIONS-PROPOSAL.md). **Open.** New sub-items A1 (token storage) and A2 (password hasher) listed there.
  - G11: client/timeout/cancellation patterns proposed (I6/I7); internal service authentication, hosting and AI provider still **open**.
  - G13: ruleset on `develop` requires PRs with 1 approving + code-owner review, last-push approval and resolved threads, blocks deletion/force-push/direct updates, but has **no required status checks**; bypass actors not visible without admin access; production branch name pending. **Open.**
  - G17: unchanged, **open** (Smart App Control stays enabled).
  - G18: M3 runner default fixture path confirmed broken by static analysis (resolves to `tests/docs/...`); unmerged M3 `FixtureLocator` would fix it; integration options in §14 of the conventions proposal. **Open** pending M3 agreement.

- 2026-11-18 (BE-M4-D01b STEP 03-A…F, documentation only — **no decision resolved**):
  - G01 final member allocation: **open** (lead holds M4 + M7).
  - G03: re-verified 2026-11-18 — Oracle `10.0.12+MySQL26.10.0` still latest stable, no NuGet advisories; EF Core 11 is `net11.0`-only; Pomelo still 9.0.0. Oracle = research candidate, **BLOCKED** for adoption pending licence review and authorized spike ([study](18-MYSQL-PROVIDER-STUDY.md)). **Open.**
  - G04: decision matrix with states in [17-SHARED-CONVENTIONS-PROPOSAL.md §15](17-SHARED-CONVENTIONS-PROPOSAL.md); C1–C12 direction provisionally accepted for review (lead 2026-11-17), **not approved**. C6 dev origin unverifiable: frontend repository has no Angular project on any branch (2026-11-18). **Open.**
  - G09 admin ownership, G10 RAG source management: **open**.
  - G11: client, timeout and failure contracts specified (§16–§17); internal service auth/transport **open**.
  - G13: unchanged — **open** (required checks, bypass, production branch).
  - G14 Google Sheets automation: **deferred**; must not block development.
  - G17: **open** — Smart App Control stays enabled.
  - G18: revalidated on develop `183ecf9`; PR #9 (`FixtureLocator`) still open and unreviewed. Option B preferred, A fallback, C long-term (§18). **Open** pending M3 agreement.
  - G19: PR #8 retrospective review **outstanding**.
  - G20: ownership recommendations restated (§17); PR #9 would add `ICandidateValidationEvidenceSource` (M6). **Open** — no assignment made.
  - C11b readiness policy: PENDING LEAD APPROVAL (§16).

- 2026-11-18 **lead decision** (STEP 03-G; record in [17-SHARED-CONVENTIONS-PROPOSAL.md §20](17-SHARED-CONVENTIONS-PROPOSAL.md#20-lead-decision-record--2026-11-18)):
  - G04: **partially decided** — C1, C3, C4, C5, C8, C9, C10 APPROVED; C11b APPROVED AS ARCHITECTURAL DESIGN. **G04 stays OPEN** for C2, C5b, C6, C7, C12, `/internal/v1`, feature diagnostics, A1, A2.
  - G03: **CONDITIONAL TESTING APPROVAL** (Oracle candidate for the future D02 spike only). **Open** — adoption requires licence review, verified versions, vulnerability audit, MySQL 8.4/migration/transaction/concurrency/least-privilege tests and explicit lead acceptance.
  - G18: Option B recorded as preferred (A fallback, C long-term). **Open** — no M3 approval claimed.
  - G20: recommendations recorded only. **Open**.
  - Unchanged and **open**: G01, G09, G10, G11, G13, G17, G19. G14 deferred.

- 2026-11-18 (BE-M4-D01c, nothing resolved):
  - **New governance finding (recorded under G19's theme, not a new decision):** PR #10 (BE-M4-D01b docs) was squash-merged as `3f8f439a44b6dd43226e44d94daa94c94c31019a` by the repository owner at 19:58:38 UTC, 37 s after creation and 2 s after its CI run completed ([run 44164246514](https://github.com/Kogulanathan69/BuildMind-Platform/actions/runs/44164246514)), with **no recorded review**. Same pattern as PR #8. Independent review of PR #8 and PR #10 remains outstanding; no approval or waiver recorded.
  - G17: local C# runs blocked different assemblies across rebuilds (Infrastructure, Domain, then the test assembly). **Open**; Smart App Control remains enabled.
  - C8 dependency note: Infrastructure-level options binding would require `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.12 — not installed; binding kept in the API composition root.
  - G01, G03, G04 (C2, C5b, C6, C7, C12, `/internal/v1`, feature diagnostics, A1, A2), G09, G10, G11, G13, G18, G19, G20: **open**. G14 deferred.

- 2026-11-18 (STEP 04-K / 05-A, nothing resolved):
  - G19 theme: PR #11 (BE-M4-D01c) merged as `0a4d70a` with **0 recorded reviews**; the same is true for PR #9 (M3), #13 (M1) and #14 (M5) per GitHub API. Independent review of PR #8, #10, #11 outstanding; no approval or waiver recorded.
  - G13: no required status checks; CI nevertheless passed on all recent `develop` pushes (EV-20261118-21). Exact test counts require admin log access.
  - G03: provider revalidated (EV-20261118-24): no change from the study; **licence acceptance for commercial use is the blocking decision**, then the disposable-database spike. G03 open.
  - G17: still open (Smart App Control enabled); C# execution stays on Linux CI.
  - New dependency for D02 (proposal only): domain entities `Report`, `ArchitectRequest`, `RequirementRevision` are immutable without parameterless constructors and with nested value objects; persistence mapping strategy (separate persistence models vs. EF mapping of domain types) must be agreed with M1/M3/M5 before feature schemas are frozen.

- 2026-11-18 (STEP 05-B, BE-M4-D02a):
  - **G03 lead decision:** **CONDITIONAL INTERNAL DEVELOPMENT AND COMPATIBILITY TESTING AUTHORIZATION — PRODUCTION AND COMMERCIAL DISTRIBUTION LICENSING REVIEW PENDING**. Approved for internal development and compatibility testing only: `MySql.EntityFrameworkCore` 10.0.12 (+MySQL26.10.0), `Microsoft.EntityFrameworkCore` / `.Relational` / `.Design` 10.0.12, repository-local `dotnet-ef` 10.0.12. This is **not** legal clearance and **not** approval for commercial distribution. No deliverable containing the provider may be published or distributed until the licensing obligations are reviewed and accepted. **G03 remains OPEN** (licence review, the 22 spike tests on MySQL 8.4, final acceptance).
  - Database CI job: proposal only ([19](19-DATABASE-CI-PROPOSAL.md)); the workflow is unchanged.
  - Persistence mapping strategy for immutable domain types and per-feature schemas: still need M1/M3/M5 agreement.

- 2026-11-18 (STEP 05-C): the repository is **public**. The D02a push publishes source and package references only (no provider binaries); the lead should confirm this is consistent with the G03 conditional internal-testing authorization. G03 open. The database CI job is still a proposal ([19](19-DATABASE-CI-PROPOSAL.md), revised: TLS, `partial_revokes` pre-check, `dotnet-ef` tooling smoke check).

- 2026-11-18 (STEP 05-D/E): lead authorized the isolated MySQL CI job for internal compatibility testing only; implemented on `feature/m4-mysql-compatibility`. G03 **open** (commercial licence review; whether a public repository is acceptable is a separate decision). PR #18 merged with 0 reviews (G19 theme).

- 2026-11-18 (STEP 05-E9) **Decision needed (G03 / D02b): T06 acceptance criteria.** `MySql.EntityFrameworkCore` 10.0.12 cannot produce MySQL-executable idempotent migration scripts (it emits SQL Server `IF NOT EXISTS … BEGIN … END`). Options: (A) keep T06 failing as a recorded provider limitation (CI job stays red); (B) change the requirement to ranged scripts with history tracking (proven by T25) and turn T06 into a test that documents the limitation; (C) reassess the provider (the fallback in the study). No change made without approval. G03 remains open.

- 2026-11-18 (STEP 05-E10): **resolved for internal development: migration deployment Option B** (ranged scripts + history + `bm_migrator` + backup/verification; raw idempotent SQL is a documented provider limitation; T06 acceptance changed accordingly; reassess on provider upgrade). Production migration process and G03 commercial licensing remain **open**.

- 2026-11-18 (STEP 06-A, D03): lead-reported team approval for D03 (no per-member record). Open: (1) **M1:** add `User`/`Project` rehydration factories (and decide `IUserRepository.UpdateAsync` concurrency) to unblock M1 repositories; (2) **migration generation path** (CI job or approved machine; local dotnet-ef blocked by G17); (3) Report/ArchitectRequest source tables and G12; (4) G20 for the M3 store; (5) version/status column limits (M4 defaults: 64/32). G03 open.

- 2026-11-18 (STEP 06-C/D): migration-generation path implemented as a CI review artifact (G17 still open for local execution). Open: M1 rehydration factories; Report/ArchitectRequest sources and G12; G20; G03; review and commit of the generated initial migration.

- 2026-11-18 (STEP 06-E3): PR #21 closed as superseded by PR #22 (squash, 0 reviews; G19). New D03 decisions: `normalized_email` collation (binary versus inherited `ai_ci`); optional database constraint for `current_revision_number`. Still open: G03, G17 (local execution), G19 (reviews), G20, M1 rehydration factories, Report/ArchitectRequest sources (G12).

- 2026-11-18 (STEP 06-F): PR #23 merged (0 reviews, G19). **Decisions requested from M1:** `User`/`Project` rehydration factories (signatures in 22); revision semantics of `IProjectRepository.UpdateAsync`; concurrency for `IUserRepository.UpdateAsync` (lost-update risk). Still open: `normalized_email` collation, head-number constraint, G03, G17, G19, G20, G12.

- 2026-11-18 (STEP 07-A, D04): readiness implemented per C11b. Still blocking the rest of D04: **C6** (frontend origin), **C7** (OpenAPI package), **A1/A2** (token storage, password hashing; M1 + frontend), the JwtBearer package approval, and M2 upload limits. Unchanged: G03, G12, G17, G19 (PR #22/#23 merged with 0 reviews), G20, M1 rehydration factories (D03).

- 2026-11-18 (STEP 08-A, D05): PR #24 merged (0 reviews; G19). D05 shared client implemented without feature routes. Still open: **G11** (internal transport/authentication, hosting), `/internal/v1` (C1 internal convention), M2/M3/M6 Python route and DTO contracts, M2 private storage contract, RAG service contract, safe-retry idempotency contract (no retries until then). Unchanged: C6, C7, A1, A2, G03, G12, G17, G20, D03 M1 dependencies.

- 2026-11-18 (STEP 08-B): approval needed for the **Linux CI C#-to-Python smoke test** ([23](../14-Current-Backend/23-M4-D05-INTERNAL-INTEGRATION.md)). New finding for M6/G11: disable FastAPI `/docs` and `/openapi.json` outside development. G11 remains OPEN.

- 2026-11-18 (STEP 08-C): **lead approved** (explicit in-session answer) the live C#-to-Python CI smoke test and the TRX/JUnit result artifacts; implemented. G11 (internal authentication, transport, hosting) remains **OPEN**; the smoke test uses loopback only and proves no authentication model. FastAPI `/docs`/`/openapi.json` exposure finding for M6 still open.

- 2026-11-18 (STEP 09-A): the D06 persistence fallback mapping applies C11b and C5 codes and **needs source review** before commit. Still open: idempotency for duplicate requests (C2/C5b), the production database account policy (D06 checklist 04 beyond CI), the M1 rehydration factories, the M2/M3/M6 job and revision persistence contracts, and the M5 report snapshot.

- 2026-11-18 (STEP 09-B): **lead approved** the D06 database-failure fallback mapping (409 `REVISION_CONFLICT` / 409 `CONFLICT` / 503 for unreachable, deadlock and lock-wait / 504 command timeout / 500 for unknown commit, unconfirmed rollback and unexpected) with no automatic write retries. Still open: idempotency (C2/C5b), the production database account policy, the M1 rehydration factories, the M2/M3/M6 job and revision contracts, the M5 report snapshot, G11, G17 and G19 (independent human review still required).

- 2026-11-19 (STEP 10-A): approvals still needed before M4 can be called production-ready: A1/A2, C6, C7, C2/C5b, G11, the hosting posture (AllowedHosts, HTTPS/HSTS), request size and rate limits, GitHub Actions SHA pinning, the production database account policy, and independent review of PRs #8–#27 (G19). See [25-M4-FINAL-QA-REPORT.md](25-M4-FINAL-QA-REPORT.md).

- 2026-11-19 (STEP 10-E): **decisions needed:** (1) accept the process-wide connection-open serialization as the MySql.Data TLS-race workaround (performance trade-off; TLS unchanged); (2) the verification method for an intermittent race: ≥5 consecutive green `database-compatibility` runs via manual re-runs (no workflow change), or an approved stress step; (3) optionally, an approved isolated CI diagnostic that reproduces the UNGUARDED provider race in its own process (never in the main test process, since corruption is process-wide); (4) reporting the defect upstream (an external action requiring approval). Provider upgrade: none available (26.10.0 latest).

- 2026-11-19 (STEP 10-F): **lead approved** (explicit in-session answer) process-wide serialized MySQL connection opening, acceptance after 5 consecutive green database CI runs on the same commit, and unchanged TLS policies and provider versions. **Deferred** (no action without new approval): a stress workflow, an ungated reproduction, an upstream bug report. Residual risk R-1 (provider `KILL QUERY` connection ungated) is accepted until an upstream fix.

- 2026-11-19 (STEP 10-H): **lead decision "Stop; revisit design"**: the open-gate semantics are NOT changed (strict hold until the provider open ends). **OPEN, blocking production readiness: R-3.** MySql.Data doesn't bound or cancel its handshake after TCP connect, so one stalled handshake holds the process-wide gate and every other open fails with 503 after `ConnectionTimeout`. Options for the decision: (a) bounded lease (release after the connect timeout; residual rare provider race R-2), (b) strict hold, (c) another design. Stress workflow, ungated reproduction and upstream report remain deferred.

- 2026-11-19 (STEP 10-I): the lead authorized the 10-H/10-I corrections for **development and CI only** (explicit instruction); **production NOT approved**; no bounded lease; R-1/R-3 unresolved. **New decision pending:** doc 27 (provider spike MySqlConnector + Pomelo, stall detection + recovery, circuit breaker, upstream report).

- 2026-11-19 (M7 MVP): **lead approved** (explicit in-session answers):
  - G10: code-only copy into `rag-service/`; the canonical engine is confirmed as the source of the copy, and its workspace stays the corpus location;
  - G11 (RAG hosting only): a separate private FastAPI service;
  - BE-M7-D03: evidence contract v1 frozen;
  - a read-only baseline run.

  **Still open:** G11 internal authentication and transport; CI workflow integration of the rag-service tests; a real RAG readiness probe; the M6 evidence consumer (D05); legal currency of the 3 sources (G06).

- 2026-11-19 (M7 phase 02): **M7-D05 blocked**. No M6 code or contract exists on any branch; M6's plan requires "M7 sourced rules/constraints". A structured regulatory-constraint contract (reviewed constraints with provenance, consumed by M6's deterministic validator) needs **M6 + M7 + lead approval**; it was not invented. Consolidated dependency list: [28](28-MVP-STATUS-AND-DEPENDENCIES.md).

- 2026-11-19 (phase 03): **record:** PR #30 merged to `develop` (`d5db6f8`) with 0 reviews and **without** the approved 5-consecutive-verified-run evidence (only public-metadata green runs). R-1/R-3 open on `develop` (G19 applies). **New proposal for decision:** M6–M7 regulatory constraint contract, doc 29 (needs M6 + M7 + lead).

- 2026-11-19 (phase 04): **G11 partially decided (lead, explicit in-session answer):** a per-service shared-secret token (`X-BuildMind-Internal-Token`), implemented for rag-service and the C# client; fail closed; default-deny (doc 31). **Still open:** python-services adoption (M6 custodian), transport encryption and hosting, mTLS/JWT as future options.

- 2026-11-19 (phase 05): **coordination pending, M6 bootstrap custodian:** adopt the rag-service internal-token pattern in python-services (doc 33 §C). It needs the custodian's agreement plus lead approval for the CI token change. The C# side already supports `Services:Python:AuthToken`.

- 2026-11-19 (final integration 01): **lead approved** (explicit in-session answer) the CI full-stack smoke: rag-service started live in `database-compatibility` (loopback, random per-run token, no corpus), live C#→RAG tests, and a built API + MySQL + Python + RAG `/health/ready` smoke (`tools/smoke/full-stack-smoke.sh`). R-1/R-3 re-evaluated (doc 27 §6): Pomelo has no EF Core 10 release, so D isn't viable now; the recommendation is A + operational F.

- 2026-11-19 (M6-review corrections): constraints contract **v0 implemented with synthetic fixtures** (doc 29 §9); **approval needed from M6 + lead:** kind set/units, condition-field sources, domain reviewer and table location for real values (G06), Gazette/council precedence. **Security decisions to confirm:** strict unknown-member rejection on internal responses; transport enforcement outside Development with the explicit `AllowPlaintextPrivateNetwork` exception (G11).

- 2026-11-19 (constraint integrity, doc 29 §11): **for M6 + lead:**
  - reviewer identity type (GUID vs pseudonymous string);
  - parcel-edge mapping owned by M6;
  - add `regulatoryEvidencePackId` to M6 feasibility results;
  - mandatory kinds for the initial feasibility check;
  - adopt BM-JCS-1 as the shared digest format.
  **For the lead:**
  - the real reviewer registry owner and process (G06);
  - conservative revocation (all of a revoked reviewer's sign-offs become invalid);
  - Production allows no plaintext exception (Staging keeps it until G11);
  - python-services authentication with its maintainer.

- 2026-11-19 (cross-member integration, doc 29 §12): M4/M7-owned H4-R1 items are closed or proposed for closure: revision width, strict typing, no-reflection errors, two-setting auth opt-out, live synthetic acceptance. **Still needed:** M6 + lead answers to §11 items 1–4; M6 runtime consumer; M2/M5 condition-value sources; evidence pack id in M6 jobs (M4 + M6); G06 real registry/tables; python-services auth; G11; G19.

- 2026-11-19 (final acceptance, doc 29 §14): **new decision for the python-services maintainer, M6 and the lead:** `shapely` is in no python-services requirements file, so M6 D02 geometry/feasibility tests are skipped (locally confirmed; CI very likely). Either pin `shapely` or make the D02 tests fail rather than skip in CI. Still open: §11 items 1–4, M6 pack-id propagation, G06, python-services auth, G11, G19, R-1/R-3.
- 2026-11-19 **RESOLVED (lead, for M6 + python-services maintainer):** pin `shapely==2.1.2` in `python-services/requirements.txt` (runtime import by M6 D02; exact M6-verified environment, GEOS 3.13.1). Approved together with a CI guard that fails on any skipped python-services test or on any `test_d02_*` module without a passing test. **Follow-up for the maintainer:** `numpy` arrives transitively via Shapely and is unpinned (resolved to 2.5.5 locally); decide whether to constrain it.

- 2026-11-19 (doc 34 §4): **New review request for M1:** `feature/m1-day02-day06-checkpoint` (no PR) adds EF migration `AddProjectAuthorizationVersion` + model snapshot (M4 single-writer review) and `JwtBearer 10.0.15` / `System.IdentityModel.Tokens.Jwt 8.23.0` (dependency approval and vulnerability review). Other items are unchanged, see doc 34 §4.

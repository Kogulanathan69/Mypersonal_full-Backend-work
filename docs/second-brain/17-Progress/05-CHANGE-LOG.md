# V3 change log

2026-11-17 — V3 documentation proposal. Preserved 48 original Markdown notes with historical warnings (three historical navigation files renamed; current navigation regenerated). Added current decisions, architecture, logical MySQL data model, proposed API register, workflow/geometry, admin gaps, RAG integration, ownership, security/testing, environment, Git workflow, six detailed member plans and M7 proposal, agent instructions and progress registers. No code/DB/remote changes. Need team approval and actual repository verification. Legacy content is intentionally not silently rewritten; V3 decisions supersede inconsistent historical notes.

2026-11-17 — BE-M7-D01 documentation reconciliation (lead-approved slice, uncommitted). Reconciled to V3: `docs/TEAM-START-HERE.md` (V3 slot table, custodians, merge policy; V2 alignment preserved as historical), `docs/API-CONTRACT-REGISTER.md` (V3 owners, implementation status, verified `/health`; initial register preserved as historical), `src/BuildMind.Application/Features/README.md` (V3 feature owners; previously all M5), `python-services/app/survey/README.md` (M2; previously M6), root `README.md` (2D-only, removes 3D per D311, current state), `rag-service/README.md` (verified vs planned RAG, no embeddings, external candidate source). Added `17-Progress/07-AUDIT-2026-11-17.md` and `14-Current-Backend/16-RAG-SOURCE-STRATEGY-PROPOSAL.md` (proposal). Updated 08-RAG verified status, 15-OPEN-DECISIONS status notes and G16, progress registers, 00-HOME and tree index. No decision resolved; no code/DB/dependency/external change.

2026-11-17 — G16 + BE-M4-D01a (uncommitted). Reconciled `python-services/app/geometry|plans|validation/README.md` to V3 ownership (G16 resolved). Added classic `BuildMind.sln`, Application/Infrastructure DI registration entry points, API composition root wiring and `public partial class Program`, test project `tests/BuildMind.Infrastructure.Tests`, CI C# restore/build/test steps. Updated tests README, 03-ARCHITECTURE, 13-ENVIRONMENT, open decisions (G16 resolved, G17 added), task/evidence registers, handover. No database, migration, auth logic, feature logic or external RAG change. C# test execution blocked locally by Smart App Control (G17).

2026-11-17 — BE-M4-D01a CI evidence recorded (docs only, lead-approved commit). Actions run 44070194073 on `c37029e` (PR #8): C# 22/22 passed, Python 42 passed (lead-retrieved logs). Task register set to CI VERIFIED — INDEPENDENT CODE REVIEW PENDING; G13 note updated with lead's explanation; G17 and G18 remain open.

2026-11-17 — BE-M4-D01b (docs and read-only research, uncommitted, branch `feature/m4-shared-contracts`). Added shared conventions/infrastructure-boundary proposal (17) and MySQL provider study (18), both PROPOSED. Recorded PR #8 squash merge `183ecf9`, post-merge CI (C# 22/22, Python 42), missing review (G19) and ruleset findings (G13). Added G20 (unassigned M3 port implementers). Updated API register, 05-API-CONTRACTS pointer, task/evidence registers, handover, indexes. No decisions resolved; no source, dependency, database, CI or settings change.

2026-11-18 — BE-M4-D01b STEP 03-A…F (uncommitted docs on `feature/m4-shared-contracts`). Extended the shared conventions proposal (§15–§19: decision states, health/readiness, interface ownership and transactions, M3 runner, D01c plan) and the MySQL provider study (re-verification, identities, spike plan, D02 acceptance). Updated API register, 05-API-CONTRACTS, open decisions, task register (D01c row added, NOT STARTED), evidence register (EV-20261118-01..06), handover. No decisions approved; no source, configuration, dependency, database, CI or RAG changes.

2026-11-18 — STEP 03-G: recorded lead decisions (C1, C3, C4, C5, C8, C9, C10 approved; C11b approved as architectural design; G03 conditional testing approval; G18 preference; G20 recommendations) in the shared conventions proposal §20, provider study, 05-API-CONTRACTS, API register, open decisions, task/evidence registers and handover; index labels updated. Documentation only; committed on `feature/m4-shared-contracts` for PR review.

2026-11-18 — BE-M4-D01c (uncommitted, `feature/m4-shared-infrastructure`): shared infrastructure implementation per lead decisions C1, C3, C4, C5, C8, C9, C10; 117 C# test cases authored (local execution blocked, G17); docs updated (17 §21, 03, 05, 11, 13, 15, API register, registers, tests README). Recorded PR #10 merge evidence and governance finding (no review).

2026-11-18 — STEP 04-K/05-A (docs only, branch `feature/m4-database-infrastructure`). Recorded PR #11 merge (`0a4d70a`), both earlier CI failures and corrective commits, final CI status, missing reviews (PR #9/#11/#13/#14), D02 environment audit and provider revalidation (EV-20261118-17..24); added D02 implementation readiness plan to 18-MYSQL-PROVIDER-STUDY. Task register: D01c MERGED (review outstanding), D02 PREPARATION (stopped at G03 gate). No source, dependency, database, CI or RAG change.

2026-11-18 — BE-M4-D02a (uncommitted). EF Core 10.0.12 + Oracle MySQL provider (G03 conditional internal-testing authorization), database options and validation, `BuildMindDbContext`, design-time factory, transaction helper, error classifier, log hardening; 63 new database-free test cases; spike project (22 tests, outside the solution); account SQL proposal; database CI proposal (19). Docs: 03, 11, 15, 18, task/evidence registers, handover. Workflow, solution file, feature code and RAG unchanged.

2026-11-18 — STEP 05-C: D02a review corrections (TLS mandatory, change-tracker discard, safe failure log, retry semantics, spike probe, `partial_revokes` check), implementation commit `2ee8324`, `develop` merge `3ff94e0`, push; database CI proposal revised. Evidence EV-20261118-29..31.

2026-11-18 — BE-M4-D02b (`947d8f8`): persistence safety corrections, isolated MySQL 8.4 CI job (digest-pinned, TLS-only least-privilege accounts, masked random credentials, cleanup checks), CLI-generated migration verification on a synthetic model, 4 new live tests (26 total) and 14 new database-free cases (210). Evidence EV-20261118-32..35. Live results pending.

2026-11-18 — STEP 05-E8: `fix(ci)` for the PR #21 `database-compatibility` job (restore before EF tooling, .NET 10 SDK selection, grant preflight). Evidence EV-20261118-36..37.

2026-11-18 — STEP 05-E9: `MySqlModelConventions` (timestamps `datetime(6)`), spike migration and T21 updated, T25 ranged-deployment test, deployment docs switched to ranged scripts, CI exports idempotent + ranged scripts. Evidence EV-20261118-38..39.

2026-11-18 — STEP 05-E10: UTC `DateTimeOffset`/`DateTime` converters in `MySqlModelConventions`, T19D diagnostics, T06 acceptance change (lead-approved Option B), migration safety procedure. Evidence EV-20261118-40..42.

2026-11-18 — STEP 05-E11 (docs only): recorded the final 28/28 MySQL evidence and the T19D seven-stage root cause (read path), corrected the precision-loss wording, noted the T06 version-guard limitation, added the D02 reviewer checklist (20) and the D03 requirements (21). Evidence EV-20261118-43..44.

2026-11-18 — STEP 06-A (uncommitted): BE-M4-D03 schema records/configurations, requirement-revision mapper and repository, database-free and live tests, D03 register (22). PR #21: T06 provider-version fix `b5208bc` (CI red, under investigation). Evidence EV-20261118-47..48 (renumbered from 45..46 because the D02b branch already uses those IDs).

2026-11-18 — STEP 06-B: T06 distinguishes the provider's NuGet package version (10.0.12) from its assembly version (26.10.0.0); evidence EV-20261118-45..46 (on the D02b branch).

2026-11-18 — STEP 06-C/06-D: D03 audit fixes (truthful conflict translation, identity-reuse detection, project-FK-only `ProjectNotFound`, version length guard, constraint-table extraction, explicit composite-FK index), CI migration generation/verification step and migration tests, merge of D02b `59c4d08`. Evidence EV-20261118-49.

2026-11-18 — STEP 06-E3: committed the reviewed initial migration `20261118133137_InitialFeatureSchema`; tests expect it; EnsureCreated fallback removed; migration deployment safety (authorization, destructive rollback) documented. Evidence EV-20261118-50..52.

2026-11-18 — STEP 06-F (docs only, uncommitted): PR #23 evidence; reassessment that no repository is unblocked; proposed M1 contract change and decisions. Evidence EV-20261118-53..54.

2026-11-18 — STEP 07-A (uncommitted): BE-M4-D04 readiness (`/health/ready`, C11b) and safe response headers, security regression tests, security and API docs. Evidence EV-20261118-55.

2026-11-18 — STEP 07-B: D04 audit fixes (EF ConnectionError suppression, readiness deadline + single-flight gate + deferred scope disposal, explicit status mapping), readiness behaviour and live tests. Evidence EV-20261118-56.

2026-11-18 — STEP 08-A (uncommitted): BE-M4-D05 internal service client, dependency-failure Problem Details, Python readiness; docs. Evidence EV-20261118-57..58.

2026-11-18 — STEP 08-B: D05 security audit fixes and regression tests; live Python contract probe; D05 integration document (23) with CI smoke proposal. Evidence EV-20261118-59.

2026-11-18 — STEP 08-C: live C#-to-Python CI smoke test and machine-readable test-result artifacts (lead-approved); `InternalServiceBulkhead`; readiness gate release fix. Evidence EV-20261118-60.

2026-11-18 — STEP 09-A: verified run #50 evidence; D06 persistence fallback mapping and concurrency tests (uncommitted). Evidence EV-20261118-61..62.

2026-11-18 — STEP 09-B: D06 mapping lead-approved; uncertainty precedence, cancellation boundary, Development-log leak and guard wording corrected. Evidence EV-20261118-63.

2026-11-19 — STEP 10-A: run #52 evidence verified; M4 final QA report (doc 25); readiness abandoned-probe cancellation fix. Evidence EV-20261119-01..02.

2026-11-19 — STEP 10-B: readiness cancellation no longer runs callbacks inline (`ProbeDeadline`); M4 final handover (doc 26). Evidence EV-20261119-03.

2026-11-19 — STEP 10-E: run #55 failure investigated; MySql.Data 26.10.0 TLS static-state race confirmed from IL; process-wide open gate implemented (uncommitted). Evidence EV-20261119-04..05.

2026-11-19 — STEP 10-F: open-gate audit against the EF Core 10.0.12 IL; G1/G2 fixed; TLS policy wording corrected; lead approvals recorded. Evidence EV-20261119-06.

2026-11-19 — STEP 10-H: PR #30 CI failure analysed (stalled handshake held the open gate); per-connection gate ownership; SSL03 isolated; SSL04 added; R-3 recorded as an open lead decision. Evidence EV-20261119-07..08.

2026-11-19 — STEP 10-I: gate ownership per open operation; SSL03 bounded cleanup; ADR proposal for MySQL connection reliability (doc 27). Evidence EV-20261119-09.

2026-11-19 — M7 MVP: rag-service FastAPI (evidence contract v1) reusing the verified STEP 4/6 engine (code-only copy); C# `IRegulationEvidenceSource`/`RagServiceClient`; shared contract example; real-corpus and E2E evidence. Evidence EV-20261119-10..11.

2026-11-19 â€” M7 phase 02: real RAG readiness (`RagReadinessCheck`, shared `BoundedServiceProbe`); M6 integration audit (blocked); consolidated MVP status/dependencies (doc 28). Evidence EV-20261119-12.

2026-11-19 — Phase 03: PR #32 CI fix verified green, `develop` merged into the M7 branch; M6–M7 regulatory constraint contract proposal (doc 29); local runbook (doc 30); final MVP status (doc 28 §5). Evidence EV-20261119-13.

2026-11-19 — Phase 04: G11 MVP internal token authentication (rag-service + C# client, fail closed, default-deny); MVP acceptance evidence (doc 32); M6 coordination message. Evidence EV-20261119-14.

2026-11-19 — Phase 05: integration audit (no conflicts), backend integration checklist with M6 shared-auth plan (doc 33), full local regression. Evidence EV-20261119-15.

2026-11-19 — Final integration 01: CI full-stack smoke + live C#→RAG tests; R-1/R-3 fact-based re-evaluation (doc 27 §6). Evidence EV-20261119-16.

2026-11-19 — Final integration 02: run #72 step-level evidence (full-stack smoke, live RAG); final M4/M7 delivery status (doc 28 §7). Evidence EV-20261119-17.

2026-11-19 — M6-review corrections: human-reviewed constraint provider (contract v0, synthetic), strict raw-JSON validation (C# + Python), token transport enforcement, constraint C# port/client. Evidence EV-20261119-19.

2026-11-19 — Post-merge verification of PR #35 (CI #77/#78, live 12/12, fabrication guard); revised M6 evidence (doc 29 §10). Evidence EV-20261119-20.

2026-11-19 — Constraint integrity: BM-JCS-1 shared digest with independent C# recomputation; reviewer authorization registry; sealed and pinned tables; overlapping-applicability conflicts without precedence; synthetic isolation; Production transport without plaintext exceptions; `ConstraintEvidenceGate`; shared M6 setback scenarios. Evidence EV-20261119-21.

2026-11-19 — Cross-member integration: Int64 bounded revisions, strict C# internal typing, fixed 422 bodies, two-setting RAG auth opt-out, test-only acceptance service + C# live acceptance (AC01–AC04) in CI. Evidence EV-20261119-22.

2026-11-19 — PR #41 CI corrections: structured problem-details leakage check, distinct CI loopback ports with guards and a regression test, rag-service workload bounds (C10). Evidence EV-20261119-23.

2026-11-19 — Final cross-member acceptance: synthetic registry-version prefix parity, CI live token rotation and cancellation acceptance, C10 real-engine evidence, M4/M7 acceptance matrix (doc 29 §14), `shapely` skip finding. Evidence EV-20261119-24.

2026-11-19 — Pinned `shapely==2.1.2` for M6 D02 (lead-approved) and added a CI guard so mandatory python-services tests cannot silently skip. Evidence EV-20261119-25.

2026-11-19 — Final M4/M7 MVP delivery verdict (doc 34) after PR #45 (`2b277f9`). Evidence EV-20261119-26.

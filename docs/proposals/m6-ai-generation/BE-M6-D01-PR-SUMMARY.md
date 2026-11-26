# Draft PR summary — BE-M6-D01

**Proposed title:** feat(m6): add provisional generation and validation contracts  
**Proposed base:** `develop`  
**Proposed head:** `feature/m6-ai-generation`  
**Tested backend base (H4-R1):** `bf72343ce95655447b431c076dcf62d02c32c6de`  
**Delivery state:** Local draft. H4 authorization is required before creating a commit, pushing, or opening the actual PR.

## Summary

Add the provisional M6 Python and C# contracts for metric land, generation input and results, feasibility, candidate validation, and generation job snapshots. Reuse the existing M3 geometry types. Validate both languages against a shared synthetic fixture corpus. Require explicit reasons and zero alternatives for unsuccessful generation. A structural success fixture exercises three distinct alternatives and matching validation metadata.

## Proposed changes

| Area | Paths | Files |
|---|---|---:|
| Python implementation | `python-services/app/geometry/footprint/`, `python-services/app/plans/generation/` and M6 utilities under `python-services/app/validation/` | 7 |
| C# implementation | `src/BuildMind.Application/Features/Generation/Contracts/` | 7 |
| Python tests and JSON fixtures | `python-services/tests/m6_contract/` | 11 |
| C# representation and semantic scenarios | M6 scenario files under `tests/BuildMind.Application.ContractTests/Integration/` | 2 |
| M6 audit and review documentation | `docs/proposals/m6-ai-generation/` | 3 |
| **Total proposed overlay** | | **30** |

The [final development report](BE-M6-D01-FINAL-DEVELOPMENT-REPORT.md) lists all 27 implementation, test and fixture paths, contract responsibilities and readiness gates.

## Testing and security evidence

- Ubuntu WSL .NET **10.0.115** restored and built **10/10** Release projects with **0 warnings and 0 errors**.
- H4-R1 fast-forwarded to the exact tested PR #35 backend after isolated regression and zero collisions across all 30 M6 paths. Isolated and active runs passed **1,234** database-free C# tests, **276** shared console checks, **38** isolated M6 C# representation checks, **82** isolated M6 C# semantic/boundary checks, **628** Python tests and **139** RAG synthetic-corpus tests. That is **2,397** checks without repeated runs or subsets. The **574** M6 Python cases are within 628, for **694** M6-specific checks across both languages. The historical H2 total was **2,288**, with 1,196 solution and 68 RAG cases.
- Closed all **22** recorded semantic gaps in the provisional validation path. Cases cover identifiers, schema, numeric bounds, revisions, geometry, validation gating, alternative uniqueness and job invariants.
- Added duplicate JSON property detection, safe public error formatting, defensive validation of constructed and mutated snapshots, curated job diagnostics, and checks for immutable job identity and history. No candidate or approval is generated from retrieved text.
- Source regression and RAG byte provenance reproduced in isolated and synchronized checkouts. The complete Python and RAG suites each emitted one known Starlette test-client deprecation warning. Real-corpus RAG and database-dependent integration remain unexecuted in H2.

See the [test evidence report](BE-M6-D01-TEST-EVIDENCE-REPORT.md) for run scope, timing and closure cases.

## Pending decisions and exclusions

**Regulatory integration remains CORRECTIONS REQUIRED**. H4-R1 moved the prior 2 PASS, 6 PARTIAL and 4 FAIL to **3 PASS, 9 PARTIAL and 0 FAIL**. PR #35 supplies the structured synthetic provider, raw JSON preflight, and transport hardening. Remaining gates include digest verification, approved real tables and reviewer identity, condition and parcel applicability mapping, strict receipt semantics, secure Python service deployment and independent end-to-end testing. M6 deterministic validation remains authoritative, and M7 retrieval is advisory.

Other pending dependencies include M1/M4 atomic ownership and exact revision access, M2 confirmed metric parcel and edge identity, M3 collection/timestamp compatibility and validation freshness, M5 room-slot conversion, and M4 atomic publication and replay enforcement.

This PR draft excludes source corrections found during H3, endpoint and DI activation, shared harness registration, algorithms, worker/queue implementation, database mappings or migrations, production configuration, and legal or feasibility certification. The broad integer-range differences discovered statically in H3 are recorded for contract-owner review.

## Requested review

**Captain and independent QA review requested for the eventual PR.** Review the contract-only scope and the evidence separately from production integration approval.

- [ ] Confirm the proposed overlay contains only the 27 M6 implementation/test/fixture files and the three M6 documents.
- [ ] Confirm the tested base, version literals, local units, revision binding and explicit unavailable behavior.
- [ ] Verify that the shared harness remains unchanged by M6 and that the isolated runner exercises the 38 and 82 check groups.
- [ ] Inspect duplicate-key rejection, safe errors, state transitions and diagnostic publication without exposing submitted values.
- [ ] Review the M3 omission, timestamp and numeric-domain differences. Confirm approval owners and do not infer wire signoff from expected-difference tests.
- [ ] Reproduce the database-free suite and review complete artifacts for any later authorized PR.
- [ ] Confirm reviewed evidence, transport, authentication, persistence, replay and unavailable integration tests remain subject to explicit owner approvals.

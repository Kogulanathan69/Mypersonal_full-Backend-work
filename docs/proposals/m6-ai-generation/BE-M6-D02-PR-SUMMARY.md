# BE-M6-D02 — Buildable Footprint and Feasibility Engine

- **Owner** Member 6
- **Proposed base** `develop`
- **Proposed head** `feature/m6-footprint-feasibility`
- **Tested base** `542a137e960ae05ee906bb83128709db6640a0b4`, PR #40

## Summary

Adds the internal M6 callable for confirmed metric parcel validation, deterministic finite-edge setbacks, and preliminary area feasibility. The four implementation modules and four test modules use the accepted D01 wire envelope. The three M6 reports document the implementation, evidence, and this publication summary.

The engine supports one metric polygon, either exterior winding, strictly contained disjoint holes, concave and irregular parcels, asymmetric distances, explicit zero and computed empty results. Setbacks use supplied exterior edge IDs. Disconnected or unrepresentable geometry returns an explicit outcome. The numeric operating range is bounded. Chord-approximated setback buffers produce separate conservative and optimistic bounds. Results inside the approximation band require review. Read the [development report](BE-M6-D02-FINAL-DEVELOPMENT-REPORT.md) for the full geometry, precision, and status limits.

## Compatibility and verification

PR #40 was the only incoming commit. No exact, case-only or file-directory collisions affect the 11 paths, and all eight implementation and test hashes were preserved. D02 consumes an explicitly synthetic, revision-bound D01 snapshot and does not ingest a raw M7 pack. The existing D01 Python and C# wire boundary passed five response probes.

Both the isolated and active checkouts passed **2,724** non-duplicated local suite checks. The breakdown is Python **717** (including **663** M6 and **89** D02), database-free C# **1,400**, shared console **286**, isolated D01 C# **120**, and RAG synthetic corpus **201**. Each checkout also passed the independent **120/120** geometry audit and **5/5** wire probes. WSL .NET 10.0.115 restored and built **10/10** Release projects with zero warnings and errors. Both dependency checks passed. The local verified environment includes Shapely 2.1.2. The shared CI requirements do not declare Shapely, so a hosted Python status needs its test and skip counts checked before it is used as evidence for geometry. Database and real-corpus verification were outside the local run. See the [test evidence report](BE-M6-D02-TEST-EVIDENCE-REPORT.md).

## Outstanding integration conditions

The C01–C12 reassessment is **4 PASS, 8 PARTIAL, 0 FAIL**. PR #40 adds BM-JCS-1 recomputation, sealing and reviewer authorization, overlap conflict detection, synthetic isolation and stricter Production transport. The D02 development report records the outstanding real-table and reviewer approvals, registry-version parity, revision-width and pack traceability, M6 reviewer UUID and edge mapping, mandatory kinds, M2 and M5 data authority, and deployment authentication. Production regulatory integration remains **CORRECTIONS REQUIRED**. This delivery covers synthetic preliminary area assessment. It leaves production activation, database operations, real regulation values, compliance certification, and D03 placement out of scope.

## Review requested

**Captain and independent QA, please review this draft.**

- [ ] Verify geometry bounds, exact revision and edge binding, and diagnostic behavior for missing, unsupported, zero, and inconclusive outcomes.
- [ ] Review the independent containment rerun and verify all 89 D02 cases in an environment with Shapely.
- [ ] Review C01–C12 with the responsible contract owners, including the M2, M5, M7 and M3/M4 handoffs before D03.
- [ ] Confirm the 11-path M6 scope and the absence of production activation.

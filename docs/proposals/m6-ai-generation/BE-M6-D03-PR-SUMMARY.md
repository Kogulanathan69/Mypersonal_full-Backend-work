# BE-M6-D03 draft PR summary

## Scope

Member 6 local development for deterministic ground-floor room placement and adjacency. Baseline `5ec2d139d1a9c175832ef1cdccb025ae10d92701`.

- Add revision-bound synthetic placement inputs with explicit room counts, area and rotation.
- Search supported conservative D02 footprints for axis-aligned rooms. Validate exact dimensions, geometry coverage, holes, disjoint room interiors and mandatory shared-wall relationships independently before returning a complete placement.
- Distinguish proven conservative area shortfall from bounded search exhaustion and unsatisfied mandatory adjacency. Retain D02 unsupported outcomes and curated diagnostics.
- R01 requires the original regulatory evidence to match before reusing any D02 outcome. The original control flow let an obsolete empty-region decision survive a setback change. The local assessment now captures the evidence for all outcomes, including review and unsupported results that omit applied rules. A mismatch requires a fresh D02 calculation and returns `NEEDS_REVIEW / INPUT_REVISION_MISMATCH`.

## Verification

R01 reproduced the stale negative decision before the correction and passed the same regression after it. D03 71 passed, D02 89 passed, M6 Python 734 passed, full Python 788 passed, RAG 238 passed, WSL Release build passed with zero warnings and errors, database-free C# 1,425 passed, shared console 348 passed and isolated D01 C# 120 passed. Both dependency checks passed. The regression total is 2,919 with Python subsets counted once. A separate 40-case geometry and freshness audit passed.

## Handoffs

The synthetic fixture scope and R01 correction are ready for review. Publication approval and independent QA are pending. Bounded search can miss feasible layouts, and the D03 area proof covers only the conservative footprint. Multipart, multi-storey and nonrectangular layouts are outside the supported scope. Production approval remains pending the M2 parcel and edge handoff, M5 requirements and relationships mapping, M3 geometry and evidence handoff, and M4/M7/lead regulatory and runtime gates, including authentication of the supplied evidence. D04 requires agreed candidate diversity, geometry handoff and authoritative validation criteria.

The [final development report](BE-M6-D03-FINAL-DEVELOPMENT-REPORT.md) covers the five official subtasks and technical audit. The [test evidence report](BE-M6-D03-TEST-EVIDENCE-REPORT.md) includes the representative candidate and backup hashes.

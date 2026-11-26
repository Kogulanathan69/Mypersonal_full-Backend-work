> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — Sri Lankan Building Rules Knowledge

- **Project:** BuildMind
- **Document ID:** BM-SB-005
- **Version:** 1.0
- **Status:** Draft; not approved or implementation-verified
- **Prepared:** 2026-11-14

## Purpose and caveat
BuildMind must evaluate **supported, source-verified** Sri Lankan residential planning rules. **No numeric setbacks, building lines, road-width classes, coverage ratios or height thresholds are verified or encoded in this starter document.** This draft is not legal guidance or official approval.

## Trigger order (confirmed)
Survey upload → Mapbox location confirmation → house requirements → **initial rule analysis**. A second, geometry-dependent validation occurs on generated/manual plans and again after edits.

## Suggested regulatory categories (candidates, require source check)
- Land/plot extent and land boundary geometry
- Local planning or zoning classification
- Street lines / building lines / road reservations
- Boundary setbacks and other clearances
- Site coverage, floor area and height where relevant
- Room ventilation/light, access and parking if covered

## Source selection process
1. Establish a reliable property location and relevant land geometry. Mapbox coordinates alone do **not** prove legal jurisdiction or plot boundaries.
2. Determine applicable development area, zoning, relevant local authority and other authority restrictions using authoritative sources.
3. Identify applicable gazetted regulation, plan, clause, effective date, exceptions and amendments.
4. Obtain verified measurements/parameters and encode into a versioned machine-readable rule.
5. Execute checks only when all required inputs exist. Record Pass / Fail / Requires review / Insufficient information / Not applicable.
6. Return source citations, measurement units, scope, and implementation coverage with each result.

## Distinguish pre-plan checks from plan checks
**After requirements:** verify jurisdiction, feasible constraints, and requirement-level rules where inputs suffice.
**After plan drawing/generation:** validate building footprint, setbacks and geometry-dependent checks.
**After edits:** rerun affected checks; old pass must not be reused blindly.

## Implementation proposal
A deterministic C# rules engine with jurisdiction resolver, versioned rule register, evaluation results and source references. AI may explain results but must not invent rule values or be the legal authority.

## Regulatory research starting points (not verified per-clause in these files)
- https://www.uda.gov.lk/applicable_regulations.html
- https://www.uda.gov.lk/applicable_dev_plans.html
- https://www.uda.gov.lk/myzoneuda.html
- Relevant local authority and road authority documents; check originals, applicability and amendments.

## Open gaps
Initial geographic coverage, actual law/gazette texts, legally effective versions, zoning data layers, road classifications and names, computation rules, map/jurisdiction resolution, reviewer approval.

See [source register](REGULATORY-SOURCES.md).

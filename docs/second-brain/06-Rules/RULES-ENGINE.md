> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Sri Lankan building rules engine

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Separation of checks
- **Initial requirements validation:** after survey + Mapbox location + house requirements, check what can be assessed without full geometry.
- **Plan geometry validation:** assess each AI/manual drawing and after relevant edits.

## Suggested result model
Passed, Failed, Requires Review, Insufficient Information, Not Applicable. Include rule ID, source/clause, parameters, project/plan version, checked inputs and limitation.

## Authoritative source discipline
Do not hardcode setback distances, building lines, zone restrictions, coverage or road reservation values without verified current applicable authority source. Keep effective date and locality. An automated check is **not government approval**.

## Engine recommendation
Deterministic checks in C# with versioned rules; AI can assist explanations but should not be the final compliance authority.

## Open dependencies
Verified rule catalogue, jurisdiction mapping, rule precedence, unit standard, geometric representation, legal review.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

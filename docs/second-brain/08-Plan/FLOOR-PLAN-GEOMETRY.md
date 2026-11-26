> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Shared floor plan geometry contract

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Critical design decision pending
Both AI-generated and manually drawn plans should use a common structured geometry representation.

## Candidate fields (illustrative only)
Project/plan/floor/version ID; coordinate system and units; site polygon; walls; rooms; apertures; dimension references; metadata; validation version.

## Risks
Pixels vs meters confusion, transforms/zoom changing stored geometry, invalid polygons, overlaps, adjacency, wall thickness, stair geometry, multi-floor representation.

## Acceptance tests required
Geometry round-trip API→Angular→save; dimensions invariant across zoom; deterministic rule checks; export geometric fidelity.

This is **not** a finalized JSON schema.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

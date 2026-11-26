> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Three-plan AI generation backend

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Confirmed outcome
Create three alternatives based on land + requirements, validate each candidate, let user choose one and edit.

## Proposed service pipeline
Validate inputs → construct constraints → generate structured candidate geometry → normalize geometry → independent deterministic validation → store selected candidates/results → return status/alternatives.

## Technologies pending
OpenAI API + Python FastAPI recommended, not approved. A text/image model cannot guarantee dimensionally valid CAD/geometry or legal compliance without structural checks.

## Failure states
No candidates, fewer than three valid candidates, invalid geometry, provider error, timeouts, duplicated alternatives.

## Open acceptance criteria
Room minimums, exact coordinates/dimensions, number of usable alternatives, geometry schema, regeneration/quotas, performance limits.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

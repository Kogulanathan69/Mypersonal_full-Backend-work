> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Logical MSSQL data model

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Proposed entities (not existing database tables)
| Entity | Key relationships |
|---|---|
| User | owns Projects |
| Project | owns Survey, Location, Requirements, Plans |
| SurveyDocument | refers to secure file storage |
| ProjectLocation | stores confirmed coordinates plus jurisdiction resolution state |
| HouseRequirements | versioned under Project |
| RegulatorySource | authority, clause, effective date, geography |
| BuildingRule | versioned implementation bound to source |
| ValidationRun | project/plan version, rule set version |
| ValidationFinding | individual rule result and evidence |
| FloorPlan | AI/manual origin, project |
| FloorPlanVersion | structured geometry + edit revision |
| UserSubscription | entitlement and validity |
| Payment | trusted gateway transaction reference |

## Unresolved choices
Physical table naming, foreign keys, geometry storage, migration strategy, delete/retention policy, audit fields and concurrency semantics.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

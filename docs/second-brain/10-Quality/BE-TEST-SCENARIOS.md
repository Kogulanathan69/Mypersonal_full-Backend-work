> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Backend test scenarios

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

| ID | Test | Expected |
|---|---|---|
| BE-T01 | Survey upload request | Stores survey; no regulatory validation triggered |
| BE-T02 | Initial rule check before house requirements | Reject/mark incomplete without false pass |
| BE-T03 | Coordinates outside covered jurisdiction | Requires Review/unsupported; never fabricated rules |
| BE-T04 | Rule source expired/unverified | No compliant assertion |
| BE-T05 | AI returns invalid geometry | Reject candidate, report failure |
| BE-T06 | Exactly three valid plans generated | Three independent validation records |
| BE-T07 | Edit after validated plan | Previous validation invalidated/re-run |
| BE-T08 | Manual drawing violating setback | Failure when applicable verified rule supported |
| BE-T09 | Payment callback forged | Entitlement not granted |
| BE-T10 | Same payment callback repeated | Idempotent processing |
| BE-T11 | Unpaid plan export | Authorization denied |
| BE-T12 | Another user's plan requested | Access denied |

These are proposed scenarios and **have not been executed**.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

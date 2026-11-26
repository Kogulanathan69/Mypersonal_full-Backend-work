> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Project lifecycle and state transitions

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Proposed project states (not approved enums)
Created → SurveyUploaded → LocationConfirmed → RequirementsSubmitted → InitialRulesChecked → Designing → PlanValidated → UserConfirmed → SubscriptionEligible → Exported.

Failures/unknown outcomes must not be collapsed into successful states. Users may revisit and revise earlier steps, which can invalidate downstream rule or plan states.

Do not create these as database enum values until business review.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

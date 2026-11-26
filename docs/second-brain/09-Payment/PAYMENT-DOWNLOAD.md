> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Subscription, payment and export

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Confirmed business rule
A finalized 2D floor plan can only be downloaded by a user with an eligible paid subscription.

## Proposed server-side sequence
Verify owner and finalized plan → check entitlement → if missing, initiate provider payment → validate signed/verified payment notification server-side → update entitlement idempotently → allow authorized export.

## Recommended technology
PayHere payment gateway, not approved; Azure Blob Storage file hosting, not approved.

## Safeguards
Never trust browser success callbacks alone; idempotency/replay defense, secure tokens/file URLs, audit trail; no raw card data in MSSQL.

## Gaps
Pricing, renewal, gateway setup, entitlement duration/quotas, export format, refund handling.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

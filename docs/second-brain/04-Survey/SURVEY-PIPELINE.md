> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Survey processing backend

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Confirmed step
User uploads survey plan before confirming location. Upload **must not immediately trigger Sri Lankan building rules analysis**.

## Proposed pipeline
Validate file → store privately → create document metadata → optional extraction → show extraction uncertainties for user confirmation → proceed to Mapbox location selection.

## Pending
PDF/image formats, OCR/vector parsing, plot boundary tracing, measurements and units, trust confidence, malicious upload scanning, size quotas, asynchronous processing.

## Evidence required
Survey sample types and extraction acceptance tests; no implementation code has been inspected.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

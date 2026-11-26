> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — Functional Requirements

- **Project:** BuildMind
- **Document ID:** BM-SB-003
- **Version:** 1.0
- **Status:** Draft; not approved or implementation-verified
- **Prepared:** 2026-11-14

## Requirement IDs and expected behaviour
| ID | Feature | Requirement | Status |
|---|---|---|---|
| FR-01 | Account | Register/login and manage user/project access | Proposed detail |
| FR-02 | Survey | Upload plan and review/confirm available survey data; **no automatic rules analysis at upload** | Upload/order confirmed; parsing TBD |
| FR-03 | Location | Use Mapbox to identify and confirm property location | Confirmed |
| FR-04 | Requirements | Capture and save house design requirements after location | Confirmed; fields TBD |
| FR-05 | Rules | After requirements, select applicable rules and evaluate supported preliminary conditions; show violations/unknowns | Confirmed timing; rules TBD |
| FR-06 | Chatbot | Answer user questions about workflow/rules with uncertainty indicated | Confirmed concept; provider TBD |
| FR-07 | Method | Let user select AI-generated or manual design | Confirmed |
| FR-08 | AI plan | Create 3 candidate 2D plans; validate them; allow one to be selected and edited | Confirmed |
| FR-09 | Manual | Let user draw a 2D floor plan, submit for validation, correct | Confirmed |
| FR-10 | Edit/validate | Revalidate plans on relevant changes, report failures and incomplete checks | Confirmed concept; triggers proposed |
| FR-11 | Dashboard | Access saved projects and designs | Proposed detail |
| FR-12 | Subscription | Require eligible paid subscription before downloads; verify payments server-side | Paid access confirmed; payment details TBD |
| FR-13 | Download | Download confirmed 2D plan in supported format | Confirmed; format TBD |

## Core acceptance checks
- User can upload survey then identify location then enter requirements; analysis **does not run earlier**.
- AI method returns three candidates when generation succeeds; status of each is shown; a candidate can be selected and edited.
- Manual method allows drawing, validation, correction, and resubmission.
- Changed plan geometry does not inherit stale compliant status.
- User without a verified qualifying subscription cannot download.
- Unsupported regulatory checks are presented as unknown/incomplete, not pass.

## Error/exception requirements (proposed)
Unsupported/corrupt upload, location not confirmed, missing house inputs, ambiguous jurisdiction, AI service failure, invalid geometry, incomplete checks, payment pending/failure, unauthorized download: display explicit failure without silently proceeding.

## Requirement data still to be defined
Survey formats and dimensions, site boundary data, exact house fields, initial authority coverage, measurable rule thresholds, room/wall geometry schema, plan regeneration policy, export formats, subscription pricing and eligibility.

## Traceability
[User journey](../02-User-Experience/USER-FLOW.md) · [Rules](../03-Regulatory-Knowledge/SRI-LANKAN-BUILDING-RULES.md) · [Architecture](../04-Technical-Architecture/SYSTEM-ARCHITECTURE.md)

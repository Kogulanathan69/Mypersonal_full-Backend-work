> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — User Flow

- **Project:** BuildMind
- **Document ID:** BM-SB-004
- **Version:** 1.0
- **Status:** Draft; not approved or implementation-verified
- **Prepared:** 2026-11-14

## Main happy path
1. Register/login and create or resume project (**account UI details proposed**).
2. Upload survey plan. Preview and confirm uploaded content; warn if unreadable.
3. Select/confirm land location using Mapbox. Show correction controls.
4. Enter house requirements. Validate required fields; save.
5. Perform first Sri Lankan regulatory/feasibility analysis based on survey + location + requirements. Explain passes, fails, incomplete/unknowns. Where needed, revise inputs.
6. Show **AI generation** versus **manual drawing** choice. Chatbot may help during journey.
7. **AI path:** request three alternatives → validate each → compare → select one → edit optional → revalidate.
8. **Manual path:** open 2D editor → draw → save → validate → correct → revalidate.
9. Confirm final design when supported checks are met or appropriate review is recorded.
10. Check subscription; if not eligible, show plan/payment process; backend verifies payment.
11. Download the finalized plan in a supported format (TBD).

## Decisions and branches
| Point | Choice | Expected handling |
|---|---|---|
| Survey | Missing/corrupt document | Ask for replacement or correction |
| Mapbox | Incorrect location | Allow user to revise and reconfirm |
| Requirements | Missing inputs | Show field-level feedback |
| Rules | Violations/unknowns | Explain source/condition; allow correction or flag review |
| Design method | AI | Generate exactly 3 alternatives if possible; report partial/failure accurately |
| Design method | Manual | Open editable floor plan canvas |
| After edit | Geometry or requirements changed | Revalidate affected rules |
| Download | No verified paid access | Route to subscription/payment |

## Proposed screen inventory
Auth; dashboard; create project; survey upload/preview; Mapbox location; house requirements; validation results; method selection; AI three-plan comparison; 2D editor; final plan review; subscription/payment; download; chatbot panel.

## Open flow questions
Account timing; save drafts/autosave; exact chatbot placement; AI retry/re-generation caps; blocking vs warning compliance checks; plan versioning; subscription rules and export types.

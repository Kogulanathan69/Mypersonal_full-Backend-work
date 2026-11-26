> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — Project Scope

- **Project:** BuildMind
- **Document ID:** BM-SB-002
- **Version:** 1.1
- **Status:** Draft; not approved or implementation-verified
- **Prepared:** 2026-11-14

## In scope (confirmed concept)
1. Survey plan upload and available land data capture.
2. Mapbox land location selection/confirmation **before** house requirements.
3. House requirements input (specific fields TBD).
4. Sri Lankan building-rule analysis **after** requirements capture.
5. AI chatbot for questions/clarification.
6. Generate **three** AI floor plan options; rule-check each; select preferred option and edit.
7. Manual 2D drawing; rule validation and correction.
8. Confirm final plan; paid subscription required for download.

## Supporting features (proposed detail)
Registration/login, saved project/dashboard, upload preview, map confirmation, user-visible validation results, save and revalidate edits, payment reconciliation and guarded export. Exact UI screens and boundaries are not fully approved.

## Out of scope for MVP
3D building design/rendering, interior visualization, structural engineering, plumbing/electrical designs, full architect replacement, automated official permit approval, construction management or cost estimation.

## Scope boundaries and constraints
- Survey upload **does not itself trigger rules analysis**.
- Mapbox assists location discovery but does not itself provide legally authoritative zoning or building lines.
- Pre-design checks are limited by available details; geometry checks require a plan.
- A successful check is not a government building-plan approval.
- Time constraint requires narrow, explicitly verified rule and drawing capabilities.

## Deliverables sought
Web UI, backend APIs, project database, survey/map/requirements flow, rule-checking, AI three-plan path, manual editor, edits and revalidation, chatbot help, paid export and project documentation.

## Dependencies and unresolved details
Authority coverage, source gazettes, reliable site geometry, survey parser, AI output geometry, editor library, payment configuration, file storage/export and security controls.

## Related
[Overview](PROJECT-OVERVIEW.md) · [Requirements](FUNCTIONAL-REQUIREMENTS.md)

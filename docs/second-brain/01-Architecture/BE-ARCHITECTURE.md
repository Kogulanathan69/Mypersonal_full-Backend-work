> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# C#/.NET backend architecture

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

## Proposed responsibilities
Authorization and workflow state, survey file and metadata, location/jurisdiction resolution, requirements persistence, deterministic rule selection/validation, AI orchestration, floor-plan persistence/versioning, payment verification and export authorization.

## Confirmed business sequence
1. **Survey upload** (FR-02 / UF-03): User uploads a land survey plan. Upload itself does **not** trigger building rules validation.
2. **Mapbox location** (FR-03 / UF-04): User identifies/confirms property location after upload.
3. **House requirements** (FR-04 / UF-05): User supplies residential design needs.
4. **Initial regulatory analysis** (FR-05 / UF-06): Only after the above information has been collected, run supported Sri Lankan rules analysis.
5. **Design method** (FR-07 / UF-08): User chooses AI generation of three alternatives or manual drawing.
6. **Design and check** (FR-08–10 / UF-09–11): AI path: 3 plans → validate → choose one → optional edit/revalidate. Manual path: user draws → validate → correct/revalidate.
7. **Paid export** (FR-12–13 / UF-13–14): Final confirmed 2D floor plan requires eligible paid subscription to download.

## Proposed approach
A modular C#/.NET backend (ASP.NET Core Web API recommended) with isolated service boundaries. A separate Python FastAPI generation service is a recommendation only.

## Key invariants
- Survey upload must not itself trigger regulatory analysis.
- Initial rules assessment requires collected house requirements and confirmed site location.
- Rule results cannot be marked passed when missing source data or unsupported checks.
- Plan edit invalidates affected compliance results.
- Backend, not Angular, authorizes downloads.

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

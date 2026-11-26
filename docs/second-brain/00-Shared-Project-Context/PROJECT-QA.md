> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — Knowledge Questions and Answers

**Version:** 1.1
**Updated:** 2026-11-14
**Entries:** 40
**Scope:** Project documentation statements; not repository-verified implementation.

## Rules for using this register

- Answers describe documented requirements, proposals, or known gaps, **not proof that code exists**.
- Keep Confirmed, Proposed, Recommended and Unknown distinctions.
- For regulatory numeric values, verify the specific authoritative clause and jurisdiction first.
- If an answer changes, update its source document, this register, and the change log.

## Quick index

| ID | Question | Knowledge status |
|---|---|---|
| BM-Q-001 | What is BuildMind? | Confirmed Requirement |
| BM-Q-002 | What is the MVP scope? | Confirmed Requirement |
| BM-Q-003 | Are 3D designs included? | Confirmed Requirement |
| BM-Q-004 | Does the system officially approve building plans? | Confirmed Scope Limitation |
| BM-Q-005 | What is the main user journey? | Confirmed Requirement |
| BM-Q-006 | Is login required before starting? | Unknown/Gap |
| BM-Q-007 | When is the first rules analysis executed? | Confirmed Requirement |
| BM-Q-008 | What happens immediately after uploading survey? | Confirmed Requirement |
| BM-Q-009 | What survey formats are supported? | Unknown/Gap |
| BM-Q-010 | Does survey upload automatically extract boundaries? | Proposed Design |
| BM-Q-011 | What is Mapbox used for? | Confirmed Requirement |
| BM-Q-012 | Does Mapbox determine legally applicable planning rules by itself? | Confirmed Design Limitation |
| BM-Q-013 | Which house information does the user provide? | Confirmed Requirement + Proposed Details |
| BM-Q-014 | Can users revise house requirements? | Proposed Design |
| BM-Q-015 | How are Sri Lankan rules selected? | Proposed Design |
| BM-Q-016 | What building setback distances apply to a specific location? | Unknown/Gap |
| BM-Q-017 | Is the first supported Sri Lankan locality finalized? | Unknown/Gap |
| BM-Q-018 | What validation stages exist? | Proposed Design |
| BM-Q-019 | What if a rule cannot be checked? | Proposed Safety Requirement |
| BM-Q-020 | Which design methods can the user select? | Confirmed Requirement |
| BM-Q-021 | How many plans does AI generate? | Confirmed Requirement |
| BM-Q-022 | Can the user choose and edit an AI-generated plan? | Confirmed Requirement |
| BM-Q-023 | Can users draw floor plans themselves? | Confirmed Requirement |
| BM-Q-024 | Which 2D drawing library is approved? | Recommended — Pending Approval |
| BM-Q-025 | Will AI-generated and manually drawn plans share geometry? | Proposed Design |
| BM-Q-026 | Can OpenAI API alone guarantee exact floor plan geometry? | Proposed Design Limitation |
| BM-Q-027 | What happens when an edited plan violates rules? | Proposed Design |
| BM-Q-028 | Is AI chatbot included? | Confirmed Requirement |
| BM-Q-029 | Is the chatbot model finalized? | Recommended — Pending Approval |
| BM-Q-030 | Is the internal Second Brain assistant the same as customer chatbot? | Confirmed Knowledge Design Principle |
| BM-Q-031 | What frontend technology is confirmed? | Confirmed Decision |
| BM-Q-032 | What backend technology is confirmed? | Confirmed + Recommended |
| BM-Q-033 | Which database is confirmed? | Confirmed Decision |
| BM-Q-034 | Is Python FastAPI confirmed? | Recommended — Pending Approval |
| BM-Q-035 | Is file storage finalized? | Recommended — Pending Approval |
| BM-Q-036 | Must users pay to download? | Confirmed Requirement |
| BM-Q-037 | What payment gateway is selected? | Recommended — Pending Approval |
| BM-Q-038 | Which download file formats are available? | Unknown/Gap |
| BM-Q-039 | Has any BuildMind feature been verified in source code? | Not Verified Implementation |
| BM-Q-040 | How should the Second Brain answer unsupported questions? | Confirmed Knowledge Design Principle |

## Detailed answers

### BM-Q-001 — What is BuildMind?

**Answer:** BuildMind is an AI-assisted residential 2D floor-planning web application; the wider architect-automation vision is outside the current MVP.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/PROJECT-OVERVIEW.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-002 — What is the MVP scope?

**Answer:** Survey upload, Mapbox location, house requirements, supported rules checks, AI/manual plan creation, editing, subscription-gated download.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/PROJECT-SCOPE.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-003 — Are 3D designs included?

**Answer:** No. 3D architectural modelling is out of scope for the current MVP.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/PROJECT-SCOPE.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-004 — Does the system officially approve building plans?

**Answer:** No. Automated checks cannot replace official local authority approval.

**Knowledge status:** Confirmed Scope Limitation

**Source:** `03-Regulatory-Knowledge/SRI-LANKAN-BUILDING-RULES.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-005 — What is the main user journey?

**Answer:** Survey upload → Mapbox location → house requirements → initial building-rules analysis → AI/manual choice → plan creation/validation → final confirmation → paid subscription → download.

**Knowledge status:** Confirmed Requirement

**Source:** `02-User-Experience/USER-FLOW.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-006 — Is login required before starting?

**Answer:** Authentication UI is proposed, but the requirement for login specifically before survey upload is not finalized.

**Knowledge status:** Unknown/Gap

**Source:** `02-User-Experience/USER-FLOW.md`

**Implementation evidence:** Not verified against a live repository.

**Next verification:** Confirm login timing.

### BM-Q-007 — When is the first rules analysis executed?

**Answer:** Only after survey upload, location identification and house requirements collection; not on survey upload.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-05); 08-Decisions/DECISION-REGISTER.md (BM-DEC-005)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-008 — What happens immediately after uploading survey?

**Answer:** The next confirmed user workflow step is Mapbox-based land location identification.

**Knowledge status:** Confirmed Requirement

**Source:** `02-User-Experience/USER-FLOW.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-009 — What survey formats are supported?

**Answer:** Not selected yet. Supported extensions, sizes, extraction methods remain TBD.

**Knowledge status:** Unknown/Gap

**Source:** `07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-003)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-010 — Does survey upload automatically extract boundaries?

**Answer:** Survey extraction is proposed where technically supported; implementation and reliability remain unverified.

**Knowledge status:** Proposed Design

**Source:** `05-Features/SURVEY-UPLOAD.md; 01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-02)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-011 — What is Mapbox used for?

**Answer:** User selection/identification and confirmation of the land location.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-03)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-012 — Does Mapbox determine legally applicable planning rules by itself?

**Answer:** No. Authoritative zoning/jurisdiction data must be separately verified and mapped.

**Knowledge status:** Confirmed Design Limitation

**Source:** `03-Regulatory-Knowledge/SRI-LANKAN-BUILDING-RULES.md; 07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-012)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-013 — Which house information does the user provide?

**Answer:** Residential requirements; examples include floors, bedrooms, bathrooms, kitchen/living spaces, parking. Final mandatory fields are TBD.

**Knowledge status:** Confirmed Requirement + Proposed Details

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-04)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-014 — Can users revise house requirements?

**Answer:** Requirement editing is proposed. Relevant rules checks should be rerun if changed.

**Knowledge status:** Proposed Design

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-04, FR-05)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-015 — How are Sri Lankan rules selected?

**Answer:** Proposed: use location, applicable jurisdiction, available survey and requirements; only verified regulations may become executable checks.

**Knowledge status:** Proposed Design

**Source:** `03-Regulatory-Knowledge/SRI-LANKAN-BUILDING-RULES.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-016 — What building setback distances apply to a specific location?

**Answer:** Unknown without verifying the applicable authority, development plan, road restrictions and source clauses. Do not invent fixed numbers.

**Knowledge status:** Unknown/Gap

**Source:** `07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-001, BM-GAP-002)`

**Implementation evidence:** Not verified against a live repository.

**Next verification:** Choose area and obtain applicable official clauses.

### BM-Q-017 — Is the first supported Sri Lankan locality finalized?

**Answer:** No. Initial jurisdiction coverage is an open decision.

**Knowledge status:** Unknown/Gap

**Source:** `07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-001)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-018 — What validation stages exist?

**Answer:** Proposed stages: initial requirements check, each AI-generated plan, manually drawn plan, and edited-plan revalidation.

**Knowledge status:** Proposed Design

**Source:** `03-Regulatory-Knowledge/SRI-LANKAN-BUILDING-RULES.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-019 — What if a rule cannot be checked?

**Answer:** Return incomplete/review-required feedback; do not treat an unsupported check as passed.

**Knowledge status:** Proposed Safety Requirement

**Source:** `03-Regulatory-Knowledge/SRI-LANKAN-BUILDING-RULES.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-020 — Which design methods can the user select?

**Answer:** AI generation or manual 2D drawing.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-07)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-021 — How many plans does AI generate?

**Answer:** Three alternative 2D floor plans are intended; failed generation must not be represented as three valid completed plans.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-08)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-022 — Can the user choose and edit an AI-generated plan?

**Answer:** Yes. User selects one of the generated alternatives and can edit it.

**Knowledge status:** Confirmed Requirement

**Source:** `02-User-Experience/USER-FLOW.md (Main happy path, step 7)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-023 — Can users draw floor plans themselves?

**Answer:** Yes. Manual 2D drawing is a confirmed path, followed by validation and correction.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-09)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-024 — Which 2D drawing library is approved?

**Answer:** None approved yet. Konva.js with Angular is recommended, subject to evaluation.

**Knowledge status:** Recommended — Pending Approval

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-011); 07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-006)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-025 — Will AI-generated and manually drawn plans share geometry?

**Answer:** A shared structured plan format is recommended, not finalized. Schema is still open.

**Knowledge status:** Proposed Design

**Source:** `04-Technical-Architecture/SYSTEM-ARCHITECTURE.md; 07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-005)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-026 — Can OpenAI API alone guarantee exact floor plan geometry?

**Answer:** No guarantee is established. Proposed architecture requires structured geometry and independent deterministic validation.

**Knowledge status:** Proposed Design Limitation

**Source:** `04-Technical-Architecture/SYSTEM-ARCHITECTURE.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-027 — What happens when an edited plan violates rules?

**Answer:** Proposed: show violations, allow corrections and revalidate affected checks before confirmation.

**Knowledge status:** Proposed Design

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-10)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-028 — Is AI chatbot included?

**Answer:** Yes, conversational assistance for planning and application questions is part of the proposed user experience.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-06)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-029 — Is the chatbot model finalized?

**Answer:** No. OpenAI API is recommended but still pending approval.

**Knowledge status:** Recommended — Pending Approval

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-010); 07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-011)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-030 — Is the internal Second Brain assistant the same as customer chatbot?

**Answer:** No. Second Brain serves the project team; customer chatbot should expose only approved end-user knowledge.

**Knowledge status:** Confirmed Knowledge Design Principle

**Source:** `07-Knowledge-QA/SECOND-BRAIN-KNOWLEDGE-BASE.md`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-031 — What frontend technology is confirmed?

**Answer:** Angular.

**Knowledge status:** Confirmed Decision

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-001)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-032 — What backend technology is confirmed?

**Answer:** C#/.NET. ASP.NET Core Web API is recommended; exact version and API style are not approved.

**Knowledge status:** Confirmed + Recommended

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-002, BM-DEC-009)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-033 — Which database is confirmed?

**Answer:** Microsoft SQL Server (MSSQL).

**Knowledge status:** Confirmed Decision

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-003)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-034 — Is Python FastAPI confirmed?

**Answer:** No. It is recommended for coordinating AI floor-plan generation, subject to feasibility.

**Knowledge status:** Recommended — Pending Approval

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-010)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-035 — Is file storage finalized?

**Answer:** No. Azure Blob Storage is recommended but not approved.

**Knowledge status:** Recommended — Pending Approval

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-013)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-036 — Must users pay to download?

**Answer:** Yes. Finalized floor plan download requires an eligible paid subscription.

**Knowledge status:** Confirmed Requirement

**Source:** `01-Project-Foundation/FUNCTIONAL-REQUIREMENTS.md (FR-12, FR-13); 08-Decisions/DECISION-REGISTER.md (BM-DEC-008)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-037 — What payment gateway is selected?

**Answer:** No final choice. PayHere is recommended pending confirmation.

**Knowledge status:** Recommended — Pending Approval

**Source:** `08-Decisions/DECISION-REGISTER.md (BM-DEC-012)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-038 — Which download file formats are available?

**Answer:** Not decided; options are discussed as possibilities only.

**Knowledge status:** Unknown/Gap

**Source:** `07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-010)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-039 — Has any BuildMind feature been verified in source code?

**Answer:** No source-code verification has been recorded in this knowledge pack.

**Knowledge status:** Not Verified Implementation

**Source:** `06-Implementation-Evidence/EVIDENCE-REGISTER.md; 07-Knowledge-QA/KNOWLEDGE-GAPS.md (BM-GAP-009)`

**Implementation evidence:** Not verified against a live repository.

### BM-Q-040 — How should the Second Brain answer unsupported questions?

**Answer:** State Unknown/Gap, identify missing information and point to evidence or a verification action; never invent code, rules or approvals.

**Knowledge status:** Confirmed Knowledge Design Principle

**Source:** `07-Knowledge-QA/SECOND-BRAIN-KNOWLEDGE-BASE.md; SECOND-BRAIN-TREE.md`

**Implementation evidence:** Not verified against a live repository.

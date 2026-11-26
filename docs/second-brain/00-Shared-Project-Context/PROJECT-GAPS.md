> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# BuildMind — Prioritized Knowledge Gap Register

**Version:** 1.1
**Updated:** 2026-11-14
**Status:** Planning assessment; all 12 gaps remain **Open**.
**Ownership:** TBD — no team members have been assigned.

## 1. Priority definitions

- **Critical:** Blocks reliable core 2D-plan creation or building-rule validation.
- **High:** Necessary for delivering MVP workflows securely or documenting actual behaviour.
- **Medium:** Required for operational readiness, but can be sequenced after core feasibility work.

Priority is a proposed planning judgment, **not** a confirmed delivery commitment.

## 2. Summary

| Priority | Open gaps | IDs |
|---|---:|---|
| Critical | 6 | BM-GAP-001, BM-GAP-002, BM-GAP-003, BM-GAP-004, BM-GAP-005, BM-GAP-012 |
| High | 4 | BM-GAP-006, BM-GAP-007, BM-GAP-009, BM-GAP-010 |
| Medium | 2 | BM-GAP-008, BM-GAP-011 |

## 3. Prioritized gap register

| ID | Gap | Priority | Status | Owner | Key dependency |
|---|---|---|---|---|---|
| BM-GAP-001 | First supported jurisdiction | Critical | Open | TBD | BM-GAP-012, BM-GAP-002 |
| BM-GAP-002 | Verified regulatory clauses and amendments | Critical | Open | TBD | BM-GAP-001 |
| BM-GAP-003 | Survey formats, extraction and verification | Critical | Open | TBD | BM-GAP-005 |
| BM-GAP-004 | Three feasible, distinct AI plans | Critical | Open | TBD | BM-GAP-005, BM-GAP-002 |
| BM-GAP-005 | Shared floor-plan geometry and units | Critical | Open | TBD | BM-GAP-003 |
| BM-GAP-006 | Konva.js approval and editor MVP | High | Open | TBD | BM-GAP-005 |
| BM-GAP-007 | PayHere approval and payment lifecycle | High | Open | TBD | BM-GAP-010 |
| BM-GAP-008 | Azure storage and hosting approval | Medium | Open | TBD | BM-GAP-003 |
| BM-GAP-009 | Repository and test evidence | High | Open | TBD | All implemented features |
| BM-GAP-010 | Export formats and subscription entitlements | High | Open | TBD | BM-GAP-007 |
| BM-GAP-011 | Chatbot provider and approved knowledge | Medium | Open | TBD | BM-GAP-002 |
| BM-GAP-012 | Mapbox-to-legal-jurisdiction matching | Critical | Open | TBD | BM-GAP-001 |

## 4. Gap resolution cards

### BM-GAP-001 — First supported jurisdiction
- **Priority:** Critical; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Wrong regional rules may invalidate designs
- **Suggested next action:** Select an initial Sri Lankan local-authority area; record UDA/development-plan applicability
- **Evidence needed to close:** Signed-off authority/area and mapped boundaries
- **Dependencies:** BM-GAP-012, BM-GAP-002

### BM-GAP-002 — Verified regulatory clauses and amendments
- **Priority:** Critical; **Status:** Open; **Owner:** TBD
- **Risk / impact:** False compliance claims or illegal design advice
- **Suggested next action:** Collect authoritative provisions; record dates, exceptions, thresholds, interpretation review
- **Evidence needed to close:** Rule catalogue with clause, source URL, effective date, verifier, tests
- **Dependencies:** BM-GAP-001

### BM-GAP-003 — Survey formats, extraction and verification
- **Priority:** Critical; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Incorrect land geometry contaminates all later stages
- **Suggested next action:** Select initial file formats; test sample surveys; define manual corrections and confidence checks
- **Evidence needed to close:** Prototype results on representative files and agreed accuracy criteria
- **Dependencies:** BM-GAP-005

### BM-GAP-004 — Three feasible, distinct AI plans
- **Priority:** Critical; **Status:** Open; **Owner:** TBD
- **Risk / impact:** AI may output noneditable or invalid layouts
- **Suggested next action:** Prototype constraint-based generation with validation and failure handling
- **Evidence needed to close:** Three distinct dimensionally coherent editable alternatives on agreed test sites
- **Dependencies:** BM-GAP-005, BM-GAP-002

### BM-GAP-005 — Shared floor-plan geometry and units
- **Priority:** Critical; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Editor, AI and validation cannot interoperate
- **Suggested next action:** Define coordinate system, units, walls, rooms, openings, footprint, serialization
- **Evidence needed to close:** Versioned schema plus round-trip and geometry validation tests
- **Dependencies:** BM-GAP-003

### BM-GAP-006 — Konva.js approval and editor MVP
- **Priority:** High; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Manual drawing and editing effort could exceed timeline
- **Suggested next action:** Prototype drag/draw/resize/snap/dimensions/save/load with Angular
- **Evidence needed to close:** Team approval and tested basic editing workflow
- **Dependencies:** BM-GAP-005

### BM-GAP-007 — PayHere approval and payment lifecycle
- **Priority:** High; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Download eligibility/payment fraud risks
- **Suggested next action:** Confirm gateway, server callbacks, subscription model and test payments
- **Evidence needed to close:** Approved payment flow and backend-verification test cases
- **Dependencies:** BM-GAP-010

### BM-GAP-008 — Azure storage and hosting approval
- **Priority:** Medium; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Survey privacy, delivery and deployment uncertainty
- **Suggested next action:** Decide storage/hosting, access model, retention and costs
- **Evidence needed to close:** Approved deployment/storage decision and access-control design
- **Dependencies:** BM-GAP-003

### BM-GAP-009 — Repository and test evidence
- **Priority:** High; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Second Brain cannot substantiate implemented behaviour
- **Suggested next action:** Collect implementation repositories and link methods/APIs/tests as they are built
- **Evidence needed to close:** Evidence records include real paths, commits and test references
- **Dependencies:** All implemented features

### BM-GAP-010 — Export formats and subscription entitlements
- **Priority:** High; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Cannot finalize download and billing flows
- **Suggested next action:** Decide PDF/PNG etc, plan limits, watermark/quality rules, entitlement duration
- **Evidence needed to close:** Approved export and entitlement specification with acceptance tests
- **Dependencies:** BM-GAP-007

### BM-GAP-011 — Chatbot provider and approved knowledge
- **Priority:** Medium; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Unverified rules advice and prompt injection risks
- **Suggested next action:** Approve provider; restrict regulatory responses to verified knowledge; define refusals
- **Evidence needed to close:** Chatbot scope, source policy, evaluation questions and test outcomes
- **Dependencies:** BM-GAP-002

### BM-GAP-012 — Mapbox-to-legal-jurisdiction matching
- **Priority:** Critical; **Status:** Open; **Owner:** TBD
- **Risk / impact:** Wrong authority/zoning chosen for a parcel
- **Suggested next action:** Obtain authoritative legal boundaries; evaluate address/point/polygon ambiguity
- **Evidence needed to close:** Jurisdiction resolution tests and manual-review fallback
- **Dependencies:** BM-GAP-001

## 5. Recommended order of investigation

1. **Regulatory feasibility:** Define first jurisdiction (001), obtain authoritative boundary mapping (012), verify rule catalogue (002).
2. **Plan-data feasibility:** Agree structured geometry (005) and survey extraction/confirmation method (003).
3. **Plan creation feasibility:** Prototype 3 valid alternatives (004) and Konva manual editor (006).
4. **Delivery controls:** Confirm export/subscription rights (010), payment lifecycle (007), storage/hosting (008).
5. **Knowledge reliability:** Add implementation evidence (009) continuously; approve chatbot knowledge protections (011).

The tracks above can overlap; no deadlines or effort estimates have been approved.

## 6. Suggested first MVP decision gates

| Gate | Must be decided / demonstrated | Related gaps |
|---|---|---|
| G1 – Regulatory | One supported local authority + authoritative clause catalogue + jurisdiction mapping strategy | 001, 002, 012 |
| G2 – Geometry | Consistent units/coordinates and reliable land geometry captured from a survey | 003, 005 |
| G3 – Design | 3 editable AI plans and manual drawing proof of concept, with validation limitations clear | 004, 006 |
| G4 – Download | Approved paid entitlements, export format, payment verification | 007, 010 |
| G5 – Evidence | Actual test/repository references logged for implemented paths | 009 |

## 7. Source and verification boundaries

- This register prioritizes **previously identified gaps** and does not assert that legal values or technologies have been verified.
- Regulatory numerical values must come from current authoritative sources and applicable clauses, not AI guesses.
- Angular, C#/.NET, MSSQL, and Mapbox are confirmed. OpenAI/FastAPI/Konva/PayHere/Azure remain recommended until approved.
- Source-code paths, production API details, test results and delivery dates are currently **not supplied**.
- The completed-document count describes **drafts**, not verified implementation.

## 8. Update procedure

When a decision is made: assign an owner, capture the decision/source link, update dependent documents and `08-Decisions/DECISION-REGISTER.md`, and change a gap to **Closed** only after the listed closure evidence has been reviewed. Add a record to `08-Decisions/CHANGE-LOG.md`.

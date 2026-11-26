# BuildMind - M3 Team Contract Handoff

## Document Information

- Task ID: BE-M3-D01
- Member: M3
- Branch: feature/m3-plan-editor
- Status: PENDING TEAM REVIEW

## 1. Related Documents

- GEOMETRY-CONTRACT-PROPOSAL.md
- PLAN-GEOMETRY-SAMPLE.json

These documents are provisional proposals.
They are not approved canonical contracts.

## 2. M1 - Authentication and Projects

Requested agreements:

- Authenticated project ownership interface.
- Project identifier convention.
- Confirmation-driven project lifecycle.
- Unauthorized and cross-project error behavior.

Status: PENDING

## 3. M2 - Survey and Land

Requested agreements:

- Confirmed land revision identifier.
- Metric boundary geometry.
- Local coordinate origin and CRS.
- Axis directions and numeric precision.
- Land boundary fixture for editor tests.

Status: PENDING

## 4. M4 - Shared Infrastructure

Requested agreements:

- Plan, version and confirmation persistence.
- Shared DbContext integration.
- Migration coordination.
- Atomic transaction interface.
- Optimistic concurrency convention.
- Internal Python HTTP client.
- Public API error and route conventions.

M3 will not independently modify shared migrations,
DbContext or application bootstrap files.

Status: PENDING

## 5. M5 - Requirements and Reports

Requested agreements:

- Requirement revision identifier.
- Supported room types and references.
- Requirement snapshot access.
- Exact confirmed plan snapshot for PDF reporting.

Status: PENDING

## 6. M6 - Generation and Validation

Requested agreements:

- Approved canonical geometry JSON schema.
- Shared room, wall and opening identifiers.
- Polygon and shared-wall representation.
- Wall thickness and room area semantics.
- Opening offset and direction convention.
- Geometry precision and tolerance.
- Selected alternative reference.
- Authoritative validation request and response.
- Validator error codes and severity.
- Geometry hash and validation evidence conventions.

M3 owns editor transformations.
M6 owns authoritative geometry validation.

Status: PENDING

## 7. M7 - Integration, AI and QA

Requested agreements:

- Revised backend ownership reconciliation.
- Canonical contract review coordination.
- Approved AI edit proposal adapter.
- Regulatory evidence and warning policy.
- Final confirmation acceptance criteria.
- Independent QA and PR review.

Status: PENDING

## 8. Development Until Approval

Allowed:

- Independent M3 feature scaffolding.
- Clearly labeled provisional fixtures.
- Isolated experimental DTOs and unit tests.

Not allowed:

- Claiming provisional geometry as canonical.
- Duplicate authoritative validation.
- Unapproved changes to shared files.
- Fabricated validation or integration results.

## 9. Approval Register

| Member | Status | Evidence |
|---|---|---|
| M1 | Pending | Not supplied |
| M2 | Pending | Not supplied |
| M4 | Pending | Not supplied |
| M5 | Pending | Not supplied |
| M6 | Pending | Not supplied |
| M7 | Pending | Not supplied |

No team approval or integration success is claimed.

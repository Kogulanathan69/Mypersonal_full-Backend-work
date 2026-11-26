# BuildMind - M3 Geometry Contract Proposal

## Document Information

- Task ID: BE-M3-D01
- Feature: 2D Editor Engine
- Author Role: Member 3
- Version: 0.1
- Status: PROPOSED - NOT APPROVED
- Branch: feature/m3-plan-editor

## 1. Purpose

Define a proposed shared geometry representation
for generated and manually edited 2D floor plans.

## 2. Ownership

- M3: Editor transformations, versions, confirmation.
- M6: Generation and authoritative validation.
- M2: Confirmed land and coordinate conventions.
- M4: Database and shared infrastructure.
- M5: Requirement revisions and report consumption.
- M7: AI/RAG integration and acceptance QA.

## 3. Geometry Principles

- Store actual geometry, not rendered images.
- Use metric measurements in metres.
- Preserve stable geometry identifiers.
- Support polygon-based room geometry.
- Keep wall thickness explicit.
- Reference doors and windows to parent walls.
- Separate geometry from frontend zoom and pixels.

## 4. Proposed Models

- FloorPlan
- Floor
- CoordinatePoint
- Room
- Wall
- Opening
- EditCommand
- PlanVersion
- PlanConfirmation
- ValidationResultReference

## 5. Proposed Identifier Convention

- ProjectId: UUID
- PlanId: UUID
- FloorId: UUID
- RoomId: UUID
- WallId: UUID
- OpeningId: UUID
- RevisionId: UUID

Identifier format requires shared contract approval.

## 6. Coordinate Convention

- Plan dimensions are expressed in metres.
- Proposed local Cartesian X/Y coordinates.
- Proposed positive X right and positive Y up.
- Frontend screen transformations remain separate.
- Origin, CRS and precision require M2/M6 approval.

## 7. Proposed Edit Commands

- AddRoom
- RemoveRoom
- MoveRoom
- ResizeRoom
- UpdateWall
- AddOpening
- MoveOpening
- RemoveOpening

Exact payload fields require contract approval.

## 8. Edit Safety

- Operate on a candidate copy.
- Never silently mutate the source geometry.
- Preserve stable references.
- Reject unsupported topology.
- Return changed geometry identifiers.
- Delegate authoritative validation to M6.
- Reject critical invalid candidates.

## 9. Versioning

- Require an expected base revision.
- Preserve immutable saved versions.
- Store geometry and validation references.
- Reject stale revisions.
- Persist accepted changes atomically.

## 10. Final Confirmation

- Confirm an exact saved revision.
- Verify authenticated project ownership.
- Require explicit user acknowledgement.
- Revalidate before confirmation.
- Reject critical or stale validation.
- Provide exact confirmed snapshot to M5.

## 11. Pending Decisions

- Canonical JSON field naming.
- Coordinate reference and origin.
- Polygon ring representation.
- Wall topology and shared-wall references.
- Opening offset convention.
- Numeric precision and tolerances.
- Geometry hash convention.
- Authoritative validator contract.
- Diagnostic codes and severity.
- Database mapping and concurrency token.

## 12. Required Reviews

- M2: Coordinate convention.
- M6: Canonical geometry and validator.
- M4: Persistence and API infrastructure.
- M5: Requirement and confirmed snapshot.
- M7: Contract and integration QA.

This document is a proposal only.
It does not replace the canonical schema.
No implementation or test success is claimed.

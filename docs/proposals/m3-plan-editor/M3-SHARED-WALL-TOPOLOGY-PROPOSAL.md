# M3 shared-wall topology proposal

**PROVISIONAL — M6 CANONICAL CONTRACT APPROVAL REQUIRED**

Task: `BE-M3-INT-05`

## Existing representation

The draft model stores:

- Room polygon coordinates.
- Room `wallIds`.
- Independent Wall records with stable ID, start/end and thickness.
- Opening records with wall ID, offset and width.

A wall becomes “shared” only when multiple rooms contain the same wall ID. This is sufficient for M3 to reject unsafe move/resize. It is insufficient for deterministic neighboring transformations because the model does not state:

- Which polygon edge corresponds to each wall ID.
- Room-side/orientation for a wall.
- Explicit adjacency and ownership.
- Stable vertex/junction identity.
- Whether reversed endpoints represent the same edge.
- Wall centerline versus face/volume semantics.
- T-junction or non-manifold connectivity.
- How opening offsets behave when orientation/length changes.

## Representation options

### A — Shared wall record referenced by rooms

One Wall ID and segment, with room references to the same ID.

Advantages: close to the current model, single opening parent, no duplicate wall drift. Missing: ordered room-edge mapping, side/orientation and junction identity.

### B — Half-edge/topological graph

Each directed room boundary edge has a stable ID and optional twin; vertices/junctions are explicit. A physical wall links one or more half-edges.

Advantages: explicit orientation, adjacency, traversal and topology-preserving edits. Costs: substantially more complex schema, serialization and migration; requires M6/frontend agreement.

### C — Per-room wall segments with shared-group ID

Each room owns a boundary segment; matching segments share a group/physical-wall ID.

Advantages: room orientation is natural. Risks: duplicated geometry may drift; reconciliation and opening ownership become harder.

M3 does not select an option. Option A plus explicit room-edge and orientation references may be the smallest extension, while advanced junction editing may ultimately require Option B.

## Proposed minimum topology information

- Stable WallId and stable endpoint/vertex identities or deterministic edge references.
- Ordered mapping from each room polygon edge to WallId.
- Explicit adjacent RoomIds and the side/orientation for each room.
- Junction connectivity and permitted degree.
- Wall thickness interpretation and alignment.
- Opening parameterization: normalized fraction or metric offset from an approved oriented endpoint.
- Affected entity IDs returned by every transformation.
- Canonical rules for reversed segments and near-coincident endpoints.

## Shared-wall resize preparation

For a requested room-edge displacement:

1. Resolve target room edge and physical wall.
2. Resolve adjacent rooms and junction endpoints.
3. Confirm the operation type is supported (for example, simple two-room manifold edge only).
4. Reject T-junctions, multiple neighbors, holes, angled/curved edges or ambiguous endpoint snapping until explicitly supported.
5. Copy the complete plan.
6. Move the physical wall and update both rooms' edge vertices using the topology graph.
7. Update connected exclusive walls under approved junction rules.
8. Reparameterize or reject openings; never silently detach them.
9. Return all changed IDs and request whole-plan M6 validation.
10. Keep the original unchanged until explicit Apply.

## Orientation and openings

An offset is ambiguous without a canonical wall direction. Reversing endpoints changes metric offset meaning unless offset is transformed to `newLength - oldOffset - width`. M6 must define:

- Canonical direction or per-room orientation.
- Offset reference point and units.
- Behavior when wall length changes.
- Door swing/room-side metadata if later required.
- Collision and minimum-clearance authority.

Until then, endpoint-changing referenced walls and shared-wall resizing remain safely unsupported.

## Required acceptance fixtures

- Two rooms sharing one oriented wall.
- Same wall with reversed room-edge orientation.
- Three-room chain.
- Endpoint junction with degree two/three.
- T-junction.
- Shared wall with opening.
- Resize longer/shorter with opening.
- Neighbor becomes invalid or below approved dimension.
- Near-coincident endpoints around approved tolerance.

Each fixture needs canonical expected geometry, affected IDs, hash and authoritative diagnostics supplied or approved by M6. The INT-05 fixtures are proposals only.

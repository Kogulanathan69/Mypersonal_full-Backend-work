# M3 advanced geometry preparation

**PROVISIONAL — M2/M4/M6 OWNER APPROVAL REQUIRED**

Task: `BE-M3-INT-05`

This proposal inventories executable M3 structural behavior and prepares deterministic fixtures for future authoritative spatial validation. It does not change the production transformation engine, canonical geometry contract, requirements, or dependencies.

## Verified current behavior

The current Python editor supports eight candidate operations:

- Add, remove, move and resize room.
- Update wall.
- Add, move and remove opening.

Locally verified structural protections include:

- Deep-copy candidate creation; original source remains unchanged.
- Stable IDs for existing entities.
- Unique entity IDs across a plan.
- Existing floor/room/wall/opening references.
- Closed room rings with at least four points.
- Finite coordinates and positive wall/opening dimensions.
- Non-degenerate walls.
- Opening offset/width fitting its parent wall.
- Shared-wall room move/resize rejection using `UNSUPPORTED_TOPOLOGY`.
- Referenced-wall endpoint update rejection.

These checks do not establish architectural, regulatory or canonical spatial validity.

## Fixture inventory

### Executable structural fixture

`python-services/tests/m3_plan_contract/fixtures/advanced_geometry_cases.json`

Base geometry contains two adjacent rectangular rooms, one stable shared wall and a door on the shared wall. Executable mutations cover:

1. Structurally accepted adjacent rooms with shared-wall door.
2. Duplicate room/entity ID.
3. Missing room-to-wall reference.
4. Zero-length wall.
5. Opening exceeding its parent wall.
6. Invalid room-ring closure.
7. Negative opening offset.
8. Non-finite coordinate injected by the test runner.

The same pack records, but does not execute as accepted spatial behavior:

- Valid rectangular, independent, adjacent and concave rooms.
- Distinct multiple floors.
- Three connected rooms, endpoint junction and T-junction.
- Reversed shared-wall orientation and neighboring resize.
- Overlap, self-intersection, zero area and degenerate vertices.
- Invalid thickness and advanced opening relationships.

### Land fixture

`python-services/tests/m3_plan_contract/fixtures/land_boundary_cases.json`

Contains proposed local-metric polygons for:

- Rectangular parcel.
- Irregular parcel.
- Fully contained footprint.
- Partial boundary crossing.
- Entirely outside footprint.
- Boundary-touching footprint.
- Self-intersecting parcel.

There is no land-boundary field in the current M3 plan schema and no approved predicate/tolerance contract. These are pending M2/M6 conformance fixtures and are not passing geometry tests.

## Expected spatial relationships

Fixture labels such as `inside`, `crosses`, `outside`, `touches`, `validPolygon`, `disjointInteriors`, `junction` and `sameUndirectedEdge` are proposed expectations. Their exact meanings depend on:

- Ring/edge canonicalization.
- Boundary-inclusive versus strict containment.
- Precision and tolerance.
- Wall centerline versus wall-volume representation.
- CRS and normalization.
- M6 diagnostic ownership.

No current M3 result should return these as authoritative conclusions.

## Dynamic neighboring-room resize proposal

If M6 approves a topology contract, a candidate-only workflow could:

1. Validate command and identify the target room.
2. Resolve target boundary edges to stable wall IDs.
3. Resolve every adjacent room referencing each affected shared wall.
4. Reject ambiguous junctions, non-manifold edges or unsupported holes.
5. Copy the entire plan.
6. Move the requested boundary under an approved anchor/direction policy.
7. Update the shared wall once.
8. Update all affected room rings using stable vertex/edge references.
9. Re-anchor or reject dependent openings according to the approved wall parameterization.
10. Return all affected room, wall and opening IDs.
11. Send the whole candidate, exact land revision and requirements revision to M6.
12. Present preview only after authoritative validation; wait for Apply/Reject.

Unsafe fallback behavior is prohibited. If adjacency cannot be resolved uniquely or opening/topology preservation is ambiguous, return `UNSUPPORTED_TOPOLOGY` and leave the source unchanged.

## Numeric and coordinate decisions

Current draft behavior is `localCartesian`, metres, origin `(x,y)`, X right, Y up, finite double values and exact ring closure. These are provisional serialization checks, not approved numeric policy.

M2/M6 must decide:

- Local CRS derivation and transformation from EPSG:4326/survey coordinates.
- Origin provenance and axis convention.
- Ring closure representation and winding.
- Precision model and persisted rounding.
- Comparison and snapping tolerance.
- Minimum segment/area thresholds.
- Near-touching versus touching versus overlap.
- Boundary-inclusive containment.
- Canonical ordering and hash serialization.

M3 must not hardcode an architectural tolerance before these decisions.

## Shapely assessment

No spatial dependency is currently installed. Reliable implementation of the following generally requires an approved library such as Shapely 2.x/GEOS:

- Polygon validity and self-intersection.
- Zero/near-zero area detection.
- Interior overlap and intersection.
- Parcel containment/crossing/touching predicates.
- Shared-boundary discovery.
- Wall/room/opening spatial relationships.
- Tolerance-aware geometry analysis.

M3 may own deterministic transformations after the topology contract is approved. M6 remains authoritative for accepting geometry and mapping spatial diagnostics. M2 remains authoritative for normalized land boundaries and coordinate provenance.

Before approval, M4/M6 should verify Python 3.13 wheels, Windows/CI compatibility, GEOS behavior, license, payload/complexity limits, denial-of-service controls, deterministic precision and parity fixtures. Shapely was not installed and `requirements.txt` was not changed.

## Actionable owner requests

### M2

- Approve the land fixture representation and confirmed revision binding.
- Publish coordinate normalization, origin, axes, units and CRS metadata.
- Decide boundary-touching semantics jointly with M6.

### M6

- Approve canonical schema, shared-wall/adjacency representation and stable topology IDs.
- Approve precision/tolerance, ring rules and canonical hash.
- Publish authoritative validator request/response and spatial diagnostics.
- Decide the Shapely request and validate fixture outcomes.

### M4

- Define approved dependency integration/review process.
- Set internal geometry payload/entity/vertex limits.
- Define compute timeout, cancellation and response-size behavior.
- Ensure unavailable/timeout results never create a saved revision.

## Remaining unsupported behavior

Dynamic neighbor adjustment, arbitrary polygon validity, room/wall/opening intersection, land containment and tolerance-sensitive relationships remain unsupported or blocked. The fixture pack prepares integration evidence; it does not convert these into implemented features.

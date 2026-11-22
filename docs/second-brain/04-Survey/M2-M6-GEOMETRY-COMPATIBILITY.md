# BuildMind — M2/M6 D04 Geometry Compatibility

**Status:** ISOLATED DEVELOPMENT — GO
**Shared contract:** PROVISIONAL
**Production integration:** NO-GO
**Last updated:** 2026-11-22
**Owner:** Backend Member 2
**Coordination:** Backend Member 6 / Backend Member 4

## 1. Purpose

This document records the M2 land-geometry handoff proposal and the current compatibility position for M6 feasibility and generation work.

It distinguishes Captain-approved technical conventions, existing M2 implementation evidence, provisional proposals and unresolved shared decisions.

This document does not publish or authorize a shared API contract, persistence migration or production integration.

## 2. Approved Technical Baseline

The following conventions are approved by the Team Captain for isolated development:

- Canonical interchange CRS: EPSG:4326 (WGS84).
- GeoJSON coordinate order: longitude, latitude.
- Metric calculations must use a suitable projected CRS.
- Distance and perimeter units: metres.
- Area unit: square metres.
- Preserve original source CRS and source coordinate values.
- Use explicit PyProj transformations with `Transformer.from_crs(..., always_xy=True)`.
- Exterior rings: counterclockwise.
- Interior rings: clockwise.
- Validate geometry after transformation.
- Do not silently assign a CRS to unlabelled coordinates.
- Do not hardcode one UTM zone globally.

These conventions do not constitute acceptance of the current implementation.

## 3. Existing M2 Implementation

Current local implementation references:

- `python-services/app/geometry/land/validation.py`
- `python-services/app/geometry/land/__init__.py`
- `python-services/tests/test_land_geometry.py`

Current validator characteristics:

- Accepts coordinates already expressed in a confirmed local metric frame.
- Checks coordinate shape, numeric values and finiteness.
- Rejects unclosed rings and duplicate consecutive vertices.
- Checks polygon topology and invalid geometry.
- Calculates area in square metres and perimeter in metres, assuming the input frame is metric.

Current CRS implementation status:

- Explicit source and calculation CRS inputs are required.
- PyProj CRS parsing and transformation code is implemented in `crs.py`.
- `always_xy=True`, `allow_ballpark=False` and `only_best=True` are configured.
- Projected calculation CRS must use metres.
- Source coordinates and source CRS metadata are retained in the result.
- Metric geometry validation and exterior/interior orientation normalization are implemented.
- WGS84 GeoJSON output code is implemented in longitude/latitude order.
- Actual PyProj runtime transformations remain unverified because Windows Application Control blocks a native dependency DLL.
- ProjectId/LandRevisionId binding, parcel-edge identity and exclusion semantics remain unresolved.

Current local evidence: 14 CRS tests passed, including one mocked transformation-path test; the full Python service suite reported 890 passed and 1 warning; Python compile check passed. Real PyProj runtime transformation remains unverified. These results do not prove real PyProj transformation behavior or shared-contract acceptance.

## 4. Proposed M2-to-M6 Geometry DTO

**Status: PROVISIONAL — for isolated compatibility analysis only.**

Illustrative structure:

    {
      "contract_version": "0.1-provisional",
      "project_id": "project-id",
      "land_revision_id": "land-revision-id",
      "source_crs": "EPSG:4326",
      "calculation_crs": "UNRESOLVED",
      "geometry_crs": "EPSG:4326",
      "geometry": {
        "type": "Polygon",
        "coordinates": []
      },
      "area_m2": null,
      "perimeter_m": null,
      "validation_status": "PENDING",
      "validation_diagnostics": []
    }

Field intent:

- `contract_version`: identifies the proposed DTO version.
- `project_id`: project identity; persistence binding is pending M4.
- `land_revision_id`: immutable land revision identity; binding is pending M4.
- `source_crs`: CRS of the original input coordinates.
- `calculation_crs`: actual projected CRS used for metric calculations.
- `geometry_crs`: CRS of the output geometry; proposed EPSG:4326.
- `geometry`: GeoJSON geometry in longitude/latitude order.
- `area_m2`: area calculated in the projected metric frame.
- `perimeter_m`: perimeter calculated in the projected metric frame.
- `validation_status`: proposed validation outcome; final enum is unresolved.
- `validation_diagnostics`: structured validation findings; final schema is unresolved.

Polygon versus MultiPolygon support, nullability, numeric precision, diagnostic codes and final field names require joint contract verification.

## 5. Provisional and Unresolved Decisions

The following must not be silently defaulted:

| Decision | Status | Coordination |
|---|---|---|
| Exact calculation CRS | Unresolved | M2/M6 |
| Coordinate origin and local frame | Unresolved | M2/M6 |
| Coordinate precision | Unresolved | M2/M6 |
| Boundary comparison tolerance | Unresolved | M2/M6 |
| Geometry validity tolerance | Unresolved | M2/M6 |
| Area/perimeter comparison tolerance | Unresolved | M2/M6 |
| Confirmed parcel binding | Unresolved | M2/M4 |
| LandRevisionId persistence binding | Unresolved | M4 |
| Parcel-edge identity | Unresolved | M2/M6 |
| Exclusion semantics | Unresolved | M2/M6 |
| Final geometry DTO and version | Provisional | M2/M6/M4 |
| Candidate/editor geometry acceptance | M3-owned | M3 |

## 6. Synthetic Acceptance Fixture Plan

### Positive fixtures

1. EPSG:4326-labelled known rectangle transforms into an explicitly selected projected CRS.
2. Source CRS and original coordinate values remain preserved.
3. A projected geometry transforms back to WGS84 in longitude/latitude order.
4. Exterior ring is normalized counterclockwise.
5. Interior ring is normalized clockwise.
6. Polygon remains closed and valid after normalization.
7. Metric area and perimeter are consistent with an independently calculated expected result within an explicitly agreed tolerance.
8. Same geometry and same land revision retain consistent identity in a synthetic handoff fixture.

### Negative fixtures

1. Missing source CRS is rejected as ambiguous.
2. Unsupported CRS identifier is rejected explicitly.
3. Invalid coordinate values are rejected.
4. Latitude/longitude values outside EPSG:4326 valid ranges are rejected.
5. Self-intersecting geometry is rejected.
6. Empty geometry is rejected.
7. Invalid geometry after transformation is rejected.
8. A stale or mismatched LandRevisionId is rejected or marked inconclusive according to the future agreed contract.
9. Missing required parcel-edge identity is not silently inferred.
10. Unknown exclusion semantics must not be interpreted as confirmed buildable area.

These are planned fixtures, not completed test results.

## 7. PyProj Environment Blocker

Selected candidate dependency: `pyproj==3.8.0`.

Environment evidence:

- Python 3.13.17.
- Environment: `python-services\.venv313`.
- CPython 3.13 Windows wheel dry-run succeeded.
- Package installation succeeded.
- Runtime import is blocked by Windows Application Control.

Code Integrity evidence:

- Event IDs: 3077 and 3033.
- Timestamp: 2026-11-22 4:45:29 PM.
- Policy ID: `0283ac0f-fff1-49ae-ada1-8a933130cad6`.
- Blocked dependency DLL: `pyproj.libs\tiff-69dcdf91c822b00d23e8dbf49655fe78.dll`.
- DLL SHA256: `AD27D079A0CA8D75FB72093EC4E01B21F9105CC71A16B2277C6511F87DC94AAA`.

PyProj runtime transformation tests remain blocked pending an IT-approved environment. No security policy bypass or modification is authorized.

## 8. Ownership Boundaries

- M2: survey and land input, CRS normalization, land geometry validation and proposed handoff.
- M6: feasibility/generation compatibility and its own validation interface.
- M4: persistence, project/land revision identity, infrastructure and shared schema coordination.
- M3: candidate/editor geometry integration acceptance.

No M2 change to M6-owned files, M3 authoritative contracts, shared bootstrap, dependency lock or protected CI configuration is authorized by this document.

## 9. Current Acceptance Status

| Area | Status |
|---|---|
| Approved CRS conventions documented | Complete |
| Existing M2 validator documented | Complete |
| Proposed DTO documented | Provisional |
| CRS transformation code | Implemented; runtime verification blocked by IT environment |
| Ring orientation code | Implemented; runtime verification pending |
| WGS84 GeoJSON code | Implemented; runtime verification pending |
| Precision/tolerance decisions | Unresolved |
| Parcel and revision binding | Unresolved |
| M6 compatibility verification | Pending |
| Production integration | NO-GO |
| M7 STEP 8B | STRICT ON HOLD |

## 10. Next Actions

1. M2 completes mocked transformation-path tests while the approved PyProj runtime environment is pending.
2. IT provides an approved environment for real PyProj runtime verification.
3. M2 runs focused real transformation tests and the full Python regression suite in that environment.
4. M6 verifies compatibility with its D02/D03/D04 coordinate assumptions.
5. M4 coordinates persistence and immutable revision identity decisions.
6. M2/M6/M4 resolve the provisional DTO, calculation CRS, coordinate frame and tolerances.
7. Shared contract publication, D05 authorization and production activation remain subject to the required decisions and approvals.

## 11. M6 Source-Level Compatibility Findings and V2 Proposal

**Status:** M2 proposal only — shared contract approval pending
**Review basis:** M6 read-only source-level review, 2026-11-22

### 11.1 Confirmed Compatibility Findings

1. M2 currently preserves source coordinates and CRS, calculates geometry in an explicitly selected projected CRS, and produces EPSG:4326 GeoJSON.
2. M6 `MetricLand` expects a `localCartesian` coordinate system with metre units, an explicit origin, X direction `right`, and Y direction `up`.
3. M6 `parcel.py` reads `coordinateSystem.origin` and checks its magnitude against a coordinate limit. The inspected footprint code does not show an origin-offset transformation.
4. M6 setback evidence uses positional `parcelEdgeIndex` values. `validate_confirmed_parcel` preserves the supplied exterior edge order when constructing parcel edges.
5. M2 ring orientation normalization can change the relationship between input vertex positions and edge indices. Therefore, direct positional edge-index handoff is not yet proven safe.
6. M6 binds confirmed parcel geometry to `projectId` and `landRevisionId`. M2's current CRS result does not yet provide the complete M6 handoff contract.

### 11.2 Coordinate Frame Proposal

- Retain EPSG:4326 GeoJSON as the geographic interchange representation.
- Retain the original source CRS and source coordinates.
- Continue metric geometry validation and calculations in an explicitly selected projected CRS with metre units.
- Define a separately approved conversion from projected coordinates into M6 `localCartesian`.
- The conversion must specify the origin, axis directions, units, and exact coordinate relationship. Do not assume that origin is subtracted or that projected coordinates can be passed directly.
- Record the source CRS, calculation CRS, local frame definition, and transformation provenance.
- Do not publish or implement a shared conversion formula until M6 confirms the intended origin semantics.

### 11.3 Parcel Edge Identity Proposal

- Do not assume positional edge indices remain stable after ring orientation normalization or start-vertex changes.
- Prefer stable parcel-edge identifiers linked to the physical boundary segments.
- If stable identifiers are not supported, define and test an explicit verified remapping between the original parcel edges and normalized edges.
- Preserve the original edge order and source geometry as evidence.
- M6 must confirm how setback evidence will resolve to the same physical parcel edge after normalization.

### 11.4 Revision and Provenance Proposal

The eventual handoff should bind geometry and validation evidence to:

- `ProjectId`
- `LandRevisionId`
- A versioned geometry contract
- Original source CRS and source geometry
- Calculation CRS and metric geometry
- Approved local Cartesian frame metadata
- Transformation provenance and validation diagnostics
- Stable parcel-edge identity or verified edge remapping

M6 candidate/layout provenance and M3 Mutation Fingerprint V1 must remain governed by their respective owners. This proposal does not redefine either contract.

### 11.5 Joint Compatibility Fixture Proposal

M2 proposes that M2/M6 agree on one synthetic fixture before shared integration:

- An asymmetric geographic polygon with one interior hole.
- An explicit projected calculation CRS suitable for the fixture.
- A non-zero local origin with an explicitly agreed coordinate convention.
- Source, projected, and local-frame representations retained together.
- Independently verified expected transformed coordinates once an approved PyProj runtime is available.
- Stable identities for exterior parcel edges.
- Unequal setback values assigned to named physical edges.
- Expected area and perimeter with an agreed precision policy.
- Matching and mismatched project/land revision examples.
- Tests covering ring orientation changes and start-vertex changes without changing physical edge identity.

No numeric transformation result or universal tolerance is asserted by this proposal.

### 11.6 Decisions Still Required

| Decision | Required owner(s) | Status |
|---|---|---|
| Meaning and application of local origin | M6, M2 | Pending |
| Projected-to-local coordinate mapping | M6, M2 | Pending |
| Coordinate precision and transformation tolerance | M6, M2 | Pending |
| Stable parcel-edge identity or remapping | M6, M2, M3 | Pending |
| Confirmed land revision binding and persistence | M4, M6 | Pending |
| Final versioned handoff DTO and diagnostics | M2, M6, M4 | Pending |
| Joint synthetic fixture acceptance | M2, M6 | Pending |
| Real PyProj runtime verification | M2, approved environment | Blocked |

### 11.7 Integration Gate

The findings above establish conditional compatibility at the geometry level, not direct DTO compatibility.

M2's current projected metric geometry must not be passed directly as M6 `localCartesian` geometry without an agreed and verified conversion. Positional parcel-edge mapping must not be assumed safe without stable identity or verified remapping.

This V2 proposal authorizes no shared code changes, DTO publication, dependency changes, production activation, protected-branch merge, or D05 implementation.

**Current decision:** Isolated D04 development may continue. Shared integration remains NO-GO pending joint contract and fixture approval, real PyProj runtime evidence, and the required owner acceptance.

# BE-M6-D03 final development report

**R01 verdict: PASS for the supported local Member 6 scope.** Publication approval and production activation remain gated.

The BuildMind-Platform worktree is on `feature/m6-room-placement-adjacency`, created at verified `origin/develop` commit `5ec2d139d1a9c175832ef1cdccb025ae10d92701`. The WSL HTTPS fetch confirmed the exact tip and D02 merge ancestry at `9bbed47222104615d1fbdd46ec0a9d566f92e6f6`. The clean pre-D03 checkout was `a5c3d4a70a861abe3d75d49a38fd022e4f3de1f1`. The D01 and D02 branches remain at `dc8acdd` and `a5c3d4a`. The incoming review covered the D02 merge and Shapely CI pin, M3 F02, M5 room relationships, M4/M7 integrity and acceptance changes, and the latest delivery gates. The implementation stays within the existing draft contracts and M6 ownership.

## Official completion matrix

| D03 subtask | Outcome and evidence |
| --- | --- |
| 01. Normalize saved counts/types with stable IDs | PASS for synthetic scope. Exact room-type counts match the pre-expanded D01 room slots. Each slot retains its UUID, type and dimensions and requires an explicit area and rotation policy. Metric ground-floor scope, refs, and project/land/requirements revisions are validated. The production M5-to-D01 adapter remains a handoff dependency. |
| 02. Design bounded placement and assign dimensions | PASS. Deterministic rectangle search consumes the D02 conservative polygon. Dimension variants are derived from explicit bounds and area with opt-in 90-degree rotation. Fixtures cover rectangular, irregular, concave and holed footprints, differing dimensions and backtracking. |
| 03. Represent polygons, walls and shared boundaries and score adjacency | PASS. Rooms expose closed rings and wall segments. Shared segments and lengths are computed independently from final geometry. Tests verify required adjacency, required separation, optional scores, tolerance, gaps and corner contacts. Circulation and openings require their own approved contracts and validation. |
| 04. Reject overlap and out-of-footprint candidates and bound runtime | PASS. Independent validation checks exact dimensions and a polygon containment/interior disjointness predicate. Fixed diagnostics and deterministic caps cover invalid input, stale revisions, unsupported topology, no solution found, adjacency rejection, dependency and geometry failures. |
| 05. Create acceptance fixtures for counts, preferences and failure | PASS. 71 D03 tests pass, including the R01 setback-evidence cases. The linked evidence report includes the candidate example and full regression counts. |

## Entry point and algorithm

`app.geometry.placement.engine.place_rooms` takes a D01 `GenerationInput`, a D03 `PlacementAssessment`, and internal `PlacementRequirements`. The trusted caller pairs the local D02 `FeasibilityAssessment` with the request evidence used for that calculation. The entry point checks revision equality, the D02 policy version, and evidence freshness before reusing any outcome. The engine checks the coordinate frame and parcel coverage before placement.

### R01 setback-evidence freshness

The former non-feasible return preceded the evidence checks. Reusing an empty-region decision after changing setbacks from 5 m to 0 m returned `INFEASIBLE / EMPTY_BUILDABLE_REGION` with identical project, land and requirements revisions. R01 checks the original evidence before either propagating a D02 result or entering the placement path. Obsolete evidence now returns `NEEDS_REVIEW / INPUT_REVISION_MISMATCH`. A D02 result freshly calculated with the new evidence allowed a room to be placed.

The D03-only assessment pairs the trusted result with an immutable serialized copy of the validated evidence. Comparison reuses the D02 structural equality rule for the entire evidence model, including status, rule set, constraints, identity, provenance and gaps. D03 also checks the reported rule-set version and applicable constraints where present, and requires applied rules for decisive D02 outcomes. D02 can omit applied rules for review or unsupported results. The captured input lets D03 validate those results and preserve their diagnostics when the evidence is current.

This check establishes consistency with the evidence supplied for the original calculation. Production evidence authenticity, permissions and independent currency checks remain integration gates. The caller owns capture of the original evidence and scheduling a fresh D02 calculation on mismatch.

The engine tries axis-aligned rectangles at sizes derived from minimum width and depth, area-driven width and depth variants, and supplied maxima. It considers a 90-degree turn only with explicit permission. It orders rooms by descending mandatory relationship count, descending explicit area, then UUID. Tile positions align either rectangle edge to coordinates from exterior or hole vertices and previously placed rooms. Candidate ranks use satisfied mandatory rules against placed rooms, optional matches, bounds and rotation. Depth-first backtracking returns the first complete candidate that satisfies the mandatory rules. The final optional score counts satisfied optional pairs. It is a local ordering heuristic, with no optimality guarantee.

The default budget is 20,000 candidate probes (validated range 1 to 20,000). Each search visit probes up to 1,024 candidate positions and explores up to 32 children. The local shape limits are 16 rooms, 128 exterior edges, 16 holes, and 512 total vertices for the parcel and footprint. D02 precision limits apply, including a 1,000,000 m coordinate magnitude limit and 1 micrometre minimum clearance. Candidate coordinates remain in the input frame without snapping. The dimension and area checks compare exact rationals derived from the supplied and emitted floating-point coordinates. GEOS handles polygon topology. For supported cases, final validation reconstructs all room polygons, checks IDs, types, dimensions, rotation, validity, positive area and footprint coverage, then verifies no interior intersection and recomputes wall contact.

## Adjacency and failure semantics

The D01 relationships are undirected. D03 treats them as mandatory unless the synthetic placement mapping explicitly marks the pair optional. Shared-wall adjacency requires collinear boundary contact with positive length above 1 nanometre and a length within 1 nanometre of the configured minimum (default 0.10 m). Separation allows point contact and boundary segments at most 1 nanometre long. A positive gap never produces shared-wall contact. A returned shared segment records the actual length, including segments below the adjacency threshold. These thresholds are local geometry policy with no regulatory status.

| Outcome | Meaning |
| --- | --- |
| SUCCESS | One complete independently checked local placement. |
| FAILED | Invalid input, invalid footprint, or safe calculation/validation failure. |
| INFEASIBLE | Proven D02 decision, or exact conservative-polygon area below the sum of per-room lower bounds. The latter proof applies to the conservative footprint used by D03. |
| NEEDS_REVIEW | Missing or stale data, unavailable dependency, bounded search exhaustion, or full room geometry found with mandatory adjacency unsatisfied. Search outcomes carry no mathematical impossibility proof. |
| UNSUPPORTED | D02 unsupported outcome or D03 room and geometry/precision limits. |

The discrete size and position proposals can miss continuously feasible placements, especially geometries needing oblique alignment, interior offsets, additional aspect ratios or more backtracking. The implementation covers rectangular rooms on polygon footprints within its bounds. Multipart footprints, multi-storey and nonrectangular rooms, circulation and door connectivity, placement of additional spaces outside the supported D01 slot catalogue, and complete three-alternative generation remain outside the local callable.

## Final technical audit

A separate post-implementation review by the implementing agent inspected the sole placement entry point and every return that propagates a D02 outcome. It also checked revision binding, candidate ordering, bounds, floating-point effects, diagnostic safety, D01/D02 compatibility and ownership. Independent assertions rebuilt output polygons with GEOS and checked containment, dimensions, area, shared length and zero interior overlap. Forty deterministic tilings passed, including stale evidence in both directions and fresh negative results. External reviewer sign-off remains pending.

- Validation uses exact dimensions from the emitted coordinates, and the size search considers width growth with constrained depth. R01 adds the evidence check for every D02 outcome.
- Medium, M6. Candidate enumeration and preference scoring are bounded heuristics. D04 diversity work must retain truthful search outcomes and confirm distinct layouts and authoritative validation.
- Release blocker, M2/M5. Confirm the metric parcel and edge-identity handoff and the production room-slot, dimensions, areas and relationship mapping. The current corpus is synthetic.
- Release blocker, M4/M7/M6/lead. Close the reviewed-evidence handoff, including reviewer identity, edge mapping, evidence pack IDs and mandatory kinds. Real reviewer registry custody, reviewed tables and C09/C10 infrastructure policies remain on their existing approval path.
- Release blocker, M3/M6. Approve plan geometry handoff and evidence binding, then confirm D04 candidate diversity and validation requirements. D04 implementation is outside this delivery.

The existing Starlette TestClient deprecation warning was observed in both Python suites. No unresolved critical D03-local defects were found. The [test evidence report](BE-M6-D03-TEST-EVIDENCE-REPORT.md) records the backup and executed checks.

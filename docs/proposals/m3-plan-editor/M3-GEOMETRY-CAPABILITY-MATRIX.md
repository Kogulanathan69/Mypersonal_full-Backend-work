# M3 geometry capability matrix

**PROVISIONAL — OWNER APPROVAL REQUIRED**

Statuses: `LOCALLY_TESTED`, `STRUCTURAL_CHECK_ONLY`, `UNSUPPORTED`, `BLOCKED_CANONICAL_CONTRACT`, `BLOCKED_SPATIAL_DEPENDENCY`, `BLOCKED_AUTHORITATIVE_VALIDATION`.

| Operation/capability | Supported input | Unsupported input | Status and evidence | M2 | M6 | Shapely | Remaining approval/gap |
|---|---|---|---|---|---|---|---|
| Add room | Closed ring, unique IDs, supplied exclusive walls | Spatial overlap, outside parcel, arbitrary invalid polygon | `STRUCTURAL_CHECK_ONLY`; existing Python tests | Land containment | Authoritative validity | Likely | Canonical ring/topology and spatial validation |
| Remove room | Existing room; removable exclusive walls/openings | Ambiguous shared topology policy | `LOCALLY_TESTED`; existing tests | No direct | Topology policy | No for current structural path | Shared-wall removal semantics |
| Move independent room | Existing room with exclusively referenced walls | Shared wall, overlap, parcel crossing | `LOCALLY_TESTED`; existing + INT-05 source immutability | Boundary | Full candidate | Likely | Neighbor policy and authoritative result |
| Resize independent room | Positive finite scale, explicit/default anchor, exclusive walls | Shared wall, invalid polygon/overlap | `LOCALLY_TESTED`; existing tests | Boundary | Full candidate | Likely | Anchor, precision and polygon validity |
| Move/resize shared-wall room | None beyond fail-safe detection | Any wall referenced by multiple rooms | `UNSUPPORTED`; INT-05 verifies `UNSUPPORTED_TOPOLOGY` | Boundary | Adjacency/topology | Likely | Canonical adjacency and neighbor algorithm |
| Update wall thickness | Existing wall, positive finite thickness | Regulatory/minimum thickness conclusions | `STRUCTURAL_CHECK_ONLY`; existing tests | No direct | Architectural validation | No | Approved dimensions/rules |
| Update wall endpoints | Only unreferenced wall | Wall referenced by a room | `UNSUPPORTED`; fail-safe existing test | Boundary | Ring/wall synchronization | Likely | Stable edge/vertex topology |
| Add opening | Unique ID, existing wall, nonnegative offset, positive width, fits length | Opening overlap, connectivity/regulatory validity | `STRUCTURAL_CHECK_ONLY`; existing tests | No direct | Opening authority | Likely | Overlap/clearance/topology policy |
| Move opening | Existing opening, valid structural offset/fit | Opening overlap and architectural result | `LOCALLY_TESTED`; INT-05 stable-ID/source tests | No direct | Authoritative acceptance | Likely | Opening collision rules |
| Remove opening | Existing opening | None structurally | `LOCALLY_TESTED`; existing tests | No | Full candidate validity | No | M6 acceptance after edit |
| Shared-wall door | Opening references the single shared wall ID | Dual/duplicated wall ownership ambiguity | `STRUCTURAL_CHECK_ONLY`; cross-language INT-05 fixture | No | Shared-wall semantics | Likely | Wall ownership/orientation contract |
| Adjacent rooms | Shared wall reference may represent adjacency | Geometric adjacency without shared ID | `STRUCTURAL_CHECK_ONLY`; fixture accepted | No | Canonical adjacency | Likely | Explicit adjacency semantics |
| Concave room | Schema can carry closed vertices | Validity, winding, self-intersection, area | `BLOCKED_SPATIAL_DEPENDENCY`; pending fixture | No | Polygon authority | Yes | M6 precision/validity |
| Multiple floors | Distinct IDs and levels | Inter-floor alignment/vertical constraints | `STRUCTURAL_CHECK_ONLY`; current DTO validation | No | Multi-floor policy | Possibly | Level uniqueness/alignment contract |
| Duplicate IDs | None | Duplicate floor/room/wall/opening IDs | `LOCALLY_TESTED`; Python/C# INT-05 | No | No | No | Diagnostic contract approval |
| Missing references | None | Missing room/opening wall IDs | `LOCALLY_TESTED`; Python/C# INT-05 | No | No | No | Diagnostic contract approval |
| Zero-length wall | None | Coincident endpoints | `LOCALLY_TESTED`; Python INT-05, C# existing | No | Tolerance near-zero | For near-zero | Exact vs tolerance threshold |
| Invalid wall/opening dimensions | Positive finite only | Zero/negative/non-finite | `LOCALLY_TESTED`; schemas/validators | No | Minimum rules | No | Canonical numeric policy |
| Room overlap | Not detected | All overlap/touch ambiguity | `BLOCKED_SPATIAL_DEPENDENCY`; pending fixture | Boundary context | Authority | Yes | Predicate/tolerance/diagnostic |
| Polygon self-intersection/zero area | Not detected beyond closure/length | Bow-tie, collinear, repeated vertices | `BLOCKED_SPATIAL_DEPENDENCY`; pending fixtures | Land version for parcel | Authority | Yes | Validity/tolerance |
| Land containment | No land polygon in M3 schema | Inside/cross/outside/touch cases | `BLOCKED_AUTHORITATIVE_VALIDATION`; pending land fixtures | Boundary/CRS | Predicate authority | Yes | M2 port plus M6 validation |
| Wall/T-junction analysis | Not represented explicitly | Non-manifold and T-junction transforms | `BLOCKED_CANONICAL_CONTRACT`; pending fixtures | No direct | Topology schema | Likely | Junction/edge identity |
| Canonical geometry hash | Provisional exact C# JSON serialization | Cross-language canonical equivalence | `BLOCKED_CANONICAL_CONTRACT` | Input revision | Owner | No | M6 canonicalization |

Pending fixtures are not counted as executed support. A fixture status is not an authoritative validation result.

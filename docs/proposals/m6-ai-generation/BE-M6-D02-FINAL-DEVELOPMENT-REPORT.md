# BE-M6-D02 — Buildable footprint and preliminary feasibility

- **Owner** Member 6
- **Development branch** `feature/m6-footprint-feasibility`
- **Verified base** `9ee4a07e71a200155a022c224cae8df1d5085847`
- **Scope** Internal local callable using confirmed-parcel equivalents and explicitly synthetic evidence. Production activation remains pending.

## Repository baseline

PR #38 merged at `4e8ac449dea9e43c2ee6b950d382ca7841e8e306`. WSL Git fetched `origin/develop` at the verified base above. All 30 accepted D01 blobs match the published D01 branch. The two commits after the merge added M5 requirements sections and M7 evidence documentation. The D01 input and feasibility payloads and the M5 readiness interface retained their existing shape. The D01 branch was preserved at `dc8acdd624ee78240a4ff920766217262d2b6c9b`. The fresh backup contains 30 D01 files verified by SHA-256 and a complete Git bundle verified by independent import and Git object inspection.

## Five D02 development gates

| Gate | Implementation | Result |
|---|---|---|
| D02.1 Confirmed metric parcel | Revalidate D01 metric input and exact project/land UUIDs. Check ordered rings, finite local metres, area, edge length, repeated vertices, topology, holes, and numerical budgets. | **PASS** with synthetic confirmed snapshots. M2 provider authority pending. |
| D02.2 Buildable footprint | Subtract independently supplied finite-edge setbacks with two geometric bounds. Verify one representable valid output polygon, original parcel containment, and setback clearance. | **PASS** for documented scope. Multi-component result and uncertain topology are explicit outcomes. |
| D02.3 Area | Measure original and conservative buildable area and compute removed area. Sum required areas from explicit revision-bound assumptions. Report a proven minimum shortfall when available. | **PASS** for supplied local assumptions. M5 policy handoff pending. |
| D02.4 Result and diagnostics | Reuse the D01 `FeasibilityResult` and existing boundary conventions. Distinguish invalid, unsupported, unverified, conflicting, unavailable, and inconclusive cases. | **PASS**. The validated synthetic area result carries a preliminary-area warning. |
| D02.5 Acceptance and independent audit | Analytic rectangles and concave notch, irregular and narrow parcels, holes, multipart rejection, numeric and status boundaries, and failure handling. Independent audit exercised mixed zero/positive setbacks at the origin and at a large translation. | **PASS**. The audit correction is covered by two added regression cases. |

See the [test evidence](BE-M6-D02-TEST-EVIDENCE-REPORT.md) for execution results and numeric expectations.

## Architecture and contract boundary

`assess_feasibility` is a Python callable in the existing footprint package. It accepts D01 `GenerationInput`, a caller-selected `ConfirmedMetricParcel`, an optional exact `ReviewedSetbackSnapshot` marked synthetic, and optional `AreaAssumptions`. The request and trusted snapshots must agree on the relevant revisions and land/room/evidence content. The raw request path retains `parse_contract_json` and its duplicate-property and type checks. The callable revalidates programmatically supplied models before using them.

The result embeds the unchanged D01 `FeasibilityResult`. Internal fields report removed area, optimistic area, the minimum proven shortfall, and applied synthetic rule records with source and reviewer metadata. The local wrapper is not a new shared wire contract. Invalid generation input raises a fixed `FeasibilityInputError` because it may lack valid identities for a D01 result. For valid envelopes, `NEEDS_REVIEW` and `UNSUPPORTED` carry fixed diagnostic codes and curated messages. No endpoint, DI, worker, database, or production regulatory provider was added.

## Supported geometry and numerical policy

A parcel is one valid local-Cartesian polygon with a closed exterior and optional strictly contained, mutually disjoint holes. A ring needs at least three distinct vertices and the repeated closing point. Both exterior orientations are accepted without changing the supplied order. Exterior edge `i` is the segment between vertices `i` and `i+1`. Original holes remain exclusions. D01 accepts only exterior edge references, so no hole-boundary setback is inferred.

The local computation is bounded to 128 exterior edges, 512 ring vertices in total, and 16 holes. Coordinate and origin component magnitudes must be at most 1,000,000 m. Input edge length and polygon clearance must be at least 1 µm. Topologically valid geometry outside these bounds is unsupported. Positive setbacks must be between 1 µm and 1,000,000 m. Explicit zero is accepted for any exterior edge. These are numerical development limits.

For a positive supplied setback `d`, the ideal forbidden region is all points within Euclidean distance `d` of the **finite** edge segment. This is a strip with round caps at the endpoints. Subtract the union of those neighborhoods from the parcel. The round caps matter at corners, including concave notches. An infinite supporting line is never used to exclude distant concave lobes. Front, rear and side setbacks use the supplied exterior edge IDs. The engine does not infer frontage. Every exterior edge must have at least one applicable reviewed synthetic value. Distinct supplied values for the same edge are treated as conflicting. Distinct rules with equal distances retain their provenance.

GEOS approximates segment buffers with chords. With 32 segments per quadrant, inflating the radius to `(d + m) / cos(π/128)` encloses the ideal forbidden neighborhood, where `m = 10⁻⁸ m` is the roundoff margin. Buffering at `d − m` produces an inscribed exclusion and an optimistic area bound. The output candidate comes from the enclosing exclusion and is independently checked with polygon validity, strict parcel coverage and distance to every positive-setback edge. If rounding puts a valid, computed single polygon just outside a sloped boundary, the candidate is inset by `m` and rechecked. This step does not change source parcel coordinates or the optimistic bound.

A disconnected candidate or optimistic result is rejected explicitly because the D01 result can represent one polygon. The engine never extracts a largest component. Unsupported result geometry type or D01 vertex/holes limits also produce an explicit result. The ideal geometry within the approximation band is not exhaustively classified. If the numerical bounds disagree on the area decision, the result is `NEEDS_REVIEW`.

## Area and diagnostic semantics

Original and candidate areas are measured in m². Removed area is original area minus the conservative candidate, including the approximation margin. The required area is the sum of the supplied room minimum width-by-depth products and explicitly supplied additional space areas. An empty additional-area array is an explicit assumption. No circulation allowance, dimension catalogue or legal threshold is inferred.

`FEASIBLE` means that a verified, positive-area preliminary candidate meets the supplied total-area assumption. Its warning scopes the result to synthetic evidence and excludes placement and compliance conclusions. `INFEASIBLE` requires either a zero optimistic area or an optimistic shortfall greater than the area guard, the maximum of `10⁻¹⁰ m²`, 32 ULPs of parcel area and parcel perimeter times `m`. The shortfall field reports the minimum proven difference against the optimistic bound. Missing requirements evidence keeps `requiredAreaM2` null. Missing, conflicting or unverified setbacks keep the buildable area and geometry unknown. A computed empty candidate has buildable area zero and null geometry. GEOS and dependency failures use fixed non-success diagnostics. They cannot become a proof of infeasibility.

## Source inventory

| Path relative to repository root | Purpose |
|---|---|
| `python-services/app/geometry/footprint/parcel.py` | Confirmed metric snapshot, precision and topology checks, exterior edge references |
| `python-services/app/geometry/footprint/setbacks.py` | Finite-segment Boolean geometry, optimistic and conservative bounds |
| `python-services/app/geometry/footprint/area.py` | Revision-bound required-area assumptions and finite area arithmetic |
| `python-services/app/geometry/footprint/feasibility.py` | Internal orchestration, safe diagnostics, synthetic evidence and D01 adaptation |
| `python-services/tests/m6_contract/test_d02_parcel.py` | D02.1 topology, exact identity, units and precision |
| `python-services/tests/m6_contract/test_d02_footprint.py` | D02.2 analytic area, finite-edge distance, concavity and component handling |
| `python-services/tests/m6_contract/test_d02_area.py` | D02.3 explicit area policy and numeric limits |
| `python-services/tests/m6_contract/test_d02_feasibility.py` | D02.4 and D02.5 composed decisions, evidence, security and response tests |

D01 contracts, shared Python dependency manifests, shared .NET code, GitHub CI and FastAPI bootstrap were preserved. Shapely 2.1.2 was already available in the approved local environment. The D02 geometry tests explicitly report unavailability in an environment without that dependency.

## Independent audit and disposition

| Severity | Finding | Disposition |
|---|---|---|
| Medium | Native overlay could round a newly constructed point just outside an unbuffered sloped parcel edge. The initial generated audit rejected 89 of 120 valid mixed-setback cases on strict containment. | Corrected with the guarded 10 nm inset of a valid computed candidate and a full recheck. Added two regression cases at ordinary and large translated coordinates. The 120-case audit passed after correction. |
| Low | The first irregular-polygon test assigned 69 m² to a polygon whose independent shoelace area is 67 m². | Corrected the fixture expectation before the gate passed. |
| High, production gate | M2 snapshot authenticity and atomic ownership have no production handoff in this slice. M7 digest verification, parcel applicability, reviewer authority, overlap/precedence and legal currency remain open. | Synthetic trusted input only. No retrieved passage is promoted to a rule. Production regulatory integration remains CORRECTIONS REQUIRED (D01 disposition: 3 PASS, 9 PARTIAL, 0 FAIL). |
| Medium, contract gate | D01 represents one buildable polygon and contains known M3 collection, integer and UTC compatibility decisions. | Explicit multipart rejection and preserved D01 wire shape. Shared changes need owner agreement. |

No unresolved critical defect was identified in the tested local scope. The bounded approximation and the independent assertions validate this operating range. Broader numeric ranges and exact topology between the two bounds remain outside that scope.

## D03 prerequisites and readiness

**D02 local implementation is ready for review.** Before consuming the footprint for D03 placement, agree the supported M2 boundary and edge-identity handoff, the M5 room-slot and required-area policy, and the M7 reviewed constraint representation and exact evidence-pack/revision binding. Confirm the candidate geometry handoff and unsupported multipolygon policy with M3 and M4. D03 must place and validate actual geometry inside the conservative footprint. An area comparison is only a prerequisite. Production DI, real regulatory data, database-backed workflow and D03 implementation remain outside this delivery.

## PR #40 compatibility and publication gate

The initial D02 regression used `9ee4a07e71a200155a022c224cae8df1d5085847`. The publication baseline is `542a137e960ae05ee906bb83128709db6640a0b4`, the squash merge of [PR #40](https://github.com/Kogulanathan69/BuildMind-Platform/pull/40). It was the only incoming commit and changed 34 paths. The incoming tree had no exact, case-only, or file-directory collision with the 11 D02 paths. The D01 generation contracts, D02 dependencies in the shared Python manifests, solution definition, and CI workflow were unaffected by the incoming commit. The 11-file overlay was tested on a detached copy of the exact merge, then the active branch was fast-forwarded to that commit and retested. All eight D02 implementation and test files retain the original SHA-256 hashes.

### C01–C12 regulatory reassessment

**4 PASS, 8 PARTIAL, 0 FAIL. Production regulatory integration remains CORRECTIONS REQUIRED.** The updated PASS for C05 covers the synthetic status and semantic gate. The partial conditions remain subject to their owners' integration approval.

| Condition | Verdict | Evidence and remaining approval conditions |
|---|---|---|
| C01 Versioned constraint contract | **PASS** | The `constraints-0` endpoint, typed C# port, and shared fixtures remain present. PR #40 adds required integrity fields. Provider and client releases need coordination when rolling out the expanded wire shape. |
| C02 Source provenance | **PARTIAL** | Source excerpts reconstruct against the verified corpus and table and registry digests are carried. Formal clause identity, legal currency and domain approval remain open. |
| C03 Applicability and precedence | **PARTIAL** | Both implementations evaluate intersecting condition ranges, including open and closed bounds. A conflict stays a conflict without inferred precedence. Actual parcel facts, front/rear/side edge assignment, and authority precedence remain open. |
| C04 Reviewed numeric constraints | **PARTIAL** | Seals, pins, active reviewer role, jurisdiction, kind, authorization dates, and revocation checks protect the provider. Real reviewer appointments, table custody, and real values still need signoff. |
| C05 Fail-closed evidence outcomes | **PASS** in synthetic contract scope | The C# gate applies conflicting, missing, unverified, then reviewed precedence over explicitly mandatory kinds. Shared scenarios run through M6 semantic validation. Final mandatory kinds and actual parcel mapping remain integration decisions. |
| C06 No fabricated regulations | **PASS** | The structured provider stays separate from retrieval. Synthetic tables and reviewer identifications are segregated, and synthetic packs require isolated opt-in. D02 consumes explicit synthetic snapshots only. |
| C07 M6 validation authority | **PASS** | The gate interprets evidence status. M6 keeps deterministic geometry and feasibility authority. |
| C08 Exact evidence binding | **PARTIAL** | C# validates the echoed revision and request binding and independently recomputes BM-JCS-1. M7's project revision is Int32 on C# receipt while M1's is Int64. D02 results and jobs still need verified pack ID and revision propagation. |
| C09 Transport and auth | **PARTIAL** | Production options enforce HTTPS or loopback with no plaintext exception. RAG synthetic opt-in is gated by C# environment validation and provider configuration. Python service auth, RAG deployment enforcement, and Staging policy still need coordinated rollout evidence. |
| C10 Operational failure handling | **PARTIAL** | Existing timeout, cancellation and safe-unavailable regression passed. Production resource and cancellation behavior and credential operations remain unverified. |
| C11 Python and C# mapping | **PARTIAL** | Shared canonical and scenario vectors passed. M7 reviewer strings versus M6 UUIDs and kind-to-edge mapping remain open. An isolated synthetic registry with a non-prefixed version was accepted by Python, while the C# receipt policy requires the `synthetic-` prefix. |
| C12 End-to-end evidence | **PARTIAL** | Synthetic receipt, gate, validation, and D02 response probes passed. Reviewed real rules, approved corpus, production transport, and persisted jobs remain outside the executed local scope. |

PR #40 is compatible with the local D02 callable because that callable consumes the D01 synthetic snapshot and the D01 result wire contract. It does not call the M7 provider or accept its pack directly. The production mapping gaps above need M2, M4, M5, M6 and M7 review, including trust in the provider's pinned table and registry. A recomputable pack digest establishes content integrity within that trust boundary.

Local setup uses Python 3.13.17, Shapely 2.1.2 and GEOS 3.13.1. The shared CI requirements do not declare Shapely. Geometry-specific pytest modules can skip when it is unavailable, so verify the hosted Python test and skip counts before using CI as D02 geometry evidence. The complete local publication regression executed all 89 cases in both checkouts.

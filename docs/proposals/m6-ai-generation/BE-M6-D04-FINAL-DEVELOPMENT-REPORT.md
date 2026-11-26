# BE-M6-D04 final development report

**Verdict: PARTIAL.** The supported local synthetic validator and search are implemented. Acceptance of the production evidence, geometry handoffs and M3 contract remains pending.

## Baseline and scope

BuildMind-Platform origin is `https://github.com/Kogulanathan69/BuildMind-Platform.git`. The clean starting branch was `feature/m6-room-placement-adjacency` at `cbf7675cea59cfa21864afdfec3d7b0904059b5e`. The verified HTTPS fetch advanced `origin/develop` to `3d72f3767759f6f451457e193bebffd3cb7dddc0`. First-parent history contains D03 PR #48 at `adbd681`. The new local branch `feature/m6-layout-diversity-validation` starts at the fetched tip and carries the six uncommitted files below. Prior M6 branch tips were preserved. The pre-D04 worktree and fetched baseline have identical D01-D03 Python code and tests. Incoming changes cover M3 atomic plan mutation contracts (#49) and M5 design method, reports, readiness and architect workflow (#47 and #51).

## Official D04 task matrix

| Member-plan item | Local disposition and acceptance evidence |
| --- | --- |
| 1. Bounded reproducible search and geometric comparison | Complete for synthetic rectangles. The numerical fixture yields three pairwise-distinct alternatives. IDs, order and insignificant perturbations cannot create diversity. |
| 2. Positive area, collisions, containment, topology and supported openings and connectivity | Complete for rectangle geometry and shared-wall relationships. Overlap, outside fit, holes, omissions and invalid geometry are tested. Openings and circulation/connectivity need approved geometry and validation semantics. |
| 3. Approved soft Vastu ranking | Deferred. There is no approved heuristic in the consumed snapshots. No Vastu condition can block a candidate or edit. |
| 4. Sourced M7 constraints and unknown-rule handling | Partial. Explicit synthetic D02 setbacks are checked locally. Missing, stale, conflicting, unverified or unknown evidence cannot establish compliance. Production M7 pack mapping and mandatory-kind policy need approval. |
| 5. M3 validator contract and positive and negative tests | Partial. The internal callable and automated suites are available locally. Cross-member contract publication and M3 acceptance require separate authorization. |

The official acceptance criteria come from the Member 6 Day 4 plan. The Second Brain geometry and ownership registers govern M2/M3/M4/M5/M7 handoffs. Previous D01-D03 reports define the local feasibility, geometry and evidence limits. [Member plan](../../second-brain/15-Member-Plans/M6-AI-GENERATION.md) · [Geometry](../../second-brain/14-Current-Backend/07-GEOMETRY.md) · [Ownership](../../second-brain/14-Current-Backend/10-OWNERSHIP.md)

## Callable interfaces and policy

`generate_alternatives` and `validate_candidate` in `app.geometry.layout_validation` take the existing D01 input, D03 evidence-paired D02 assessment, D03 room policy, an explicit confirmed parcel and reviewed setback snapshot, and explicit mandatory constraint kinds. The only supported scope is synthetic `("setback",)`. A scope omission is inconclusive and any additional mandatory kind is unsupported. The caller supplies current trusted snapshots. D04 tests consistency by exact project/land/requirements identities, matching parcel content and coordinate frame, the complete original evidence snapshot, D02 policy and rule-set versions, and exact applicable rule records. It checks full edge coverage and conflicting distances. It reconstructs the D02 conservative geometry and requires topological equality to the supplied footprint. Every result in a successfully prepared context binds the supplied project, land and requirements revision and immutable evidence JSON including source version/locator, jurisdiction, review reference and applicability basis.

Candidate geometry is reconstructed from four finite bounds. D04 checks slot identities and counts, room types, rotation permissions, minimum and optional maximum spans, explicit minimum areas, positive rectangle geometry, parcel and hole coverage, interior disjointness, mandatory relationships, exact setbacks and conservative footprint coverage. The candidate revision is compared with the current context before room validation. Shared walls and lengths are recomputed from polygon boundary intersections. A corner has zero shared length. Interior overlap or a positive gap receives no contact tolerance. D03 separation is shared-wall separation, allowing point contact and contact up to 1 nm. D03 adjacency uses positive contact above 1 nm and a default 0.10 m minimum with 1 nm comparison tolerance. Minimum dimensions and area use exact rationals from the supplied coordinates. The setback violation test uses an absolute 10 nm safety margin. A room in the conservative margin can fail footprint coverage without a proven setback violation. Geometry is never repaired or snapped.

The search uses the D03 placement callable once with its bounded default 20,000 probes, independently validates the seed, then tries the unchanged geometry, reflections across the footprint extent axes, whole-layout translations aligned to the footprint extent or centered within it, and single-room translations. The variations use the same input snapshots throughout. With at most 16 rooms, D04 enumerates at most 260 raw variants: four base or reflected layouts, 16 whole-layout translations and 15 moves per room. Duplicate variants are skipped before charging the budget of at most 96 unique candidate validations, including the seed. Other limits are 128 parcel/footprint exterior edges, 16 holes, 512 total boundary vertices, and coordinates of magnitude at most 1,000,000 m. Parcel, footprint and room spans use the existing 1 micrometre lower precision bound. The algorithm compares layouts within the bounded pool and returns the first triple in deterministic enumeration order with three qualifying edges. Invalid and near-duplicate layouts cannot fill a triple. Failure returns zero candidates and an explicit inconclusive diagnostic. This search explores a single seed and bounded transformations, so a feasible arrangement can remain undiscovered.

## Equivalence, thresholds and numerical fixture

Canonical order is the required slot UUID order, with type and local metric bounds retained and signed zero normalized. Comparing layouts ignores external candidate IDs, serialization field order and room list order. Repeated room types retain their approved slot identity. Candidate digests are SHA-256 over a fixed JSON array containing exact revision IDs and canonical room slot/type/bounds. Digests provide stable identity. The pairwise metric separately enforces tolerance without relying on hash equality or quantization buckets. Rotations and reflections are inspected in the parcel frame and can qualify when geometry changes sufficiently.

Let T be 1% of the shorter footprint extent, clamped to 0.05–0.50 m. A pair requires maximum absolute displacement of a matching room bound of at least T. The score sums displacement/T, maximum differential displacement of a pair of room centroids/T, and the Jaccard distance of the shared-wall graphs. Mandatory adjacency and diversity topology use the same predicate: the shared wall must exceed 1 nm and reach the configured minimum within the 1 nm comparison tolerance. The score is rounded to nine decimals after calculation. A topology change alone cannot make a tiny perturbation distinct. Circulation has no component.

A 10 m × 8 m parcel with two 2 m × 2 m bedrooms and required adjacency gives T = 0.08 m. Room bounds in metres are listed in stable slot order.

| Alternative | Room 1 | Room 2 | Shared wall |
| --- | --- | --- | --- |
| A | (0,0,2,2) | (0,2,2,4) | 2 m |
| B | (8,0,10,2) | (8,2,10,4) | 2 m |
| C | (0,6,2,8) | (0,4,2,6) | 2 m |

Pair scores A/B, A/C and B/C are 100, 125 and 150. Each interior is 4 m². The third layout reverses the relative vertical arrangement. At T = 0.05 m, a 1 nm movement and a 0.04999 m movement fail the diversity gate, while 0.05 m qualifies. GEOS topology near floating-point boundaries can vary by version or environment. Reproducibility is scoped to the supported tested runtime.

## Outcomes and precedence

Validation reports the first decisive gate. Import failures take precedence at entry. The next gates check structurally valid bounded inputs, confirmed parcel and evidence scope/freshness, usable D02 status and bindings, room policy, and footprint geometry. Candidate checks then proceed through revision/representation, slot identity, per-slot geometry and sizing in UUID order, overlap and relationships, proven setback violation, and conservative coverage. Candidate success is last. The standalone callable revalidates actual geometry against current snapshots and does not trust cached candidate claims.

| Outcome | Meaning |
| --- | --- |
| VALID | All applicable checks in the explicit synthetic scope passed. Generation returns exactly three independently valid, pairwise-distinct layouts. |
| INVALID_GEOMETRY | Invalid or inconsistent footprint/room geometry, parcel or hole exclusion, interior overlap, or failure of conservative fit. |
| REQUIREMENTS_VIOLATION | The approved synthetic room identities, counts, types, rotation, dimensions, area or mandatory relationship policy failed. |
| REGULATORY_NON_COMPLIANT | A candidate violates an explicitly supplied, applicable, reviewed synthetic setback by more than the safety margin. |
| UNSUPPORTED | Geometry, policy, mandatory kind or complexity exceeds the implemented envelope. |
| INCONCLUSIVE | Missing, stale, conflicting, unknown or unverified evidence, missing input, an unsafe calculation, unavailable placement, or an insufficient bounded search. |
| DEPENDENCY_UNAVAILABLE | The local geometry dependency is unavailable or reported unavailable by D02/D03. |

D04 has no provider call. An absent upstream regulatory snapshot fails closed. Provider transport, reviewer authority, authentication, legal currency, precedence, actual jurisdiction applicability, and broader mandatory coverage/FAR/height/parking checks belong to approved upstream contracts. D01 lacks exact representations for all seven states and the full D04 validation evidence. A shared mapping or wire change requires separate approval. D04 states remain within the internal callable.

## Separate technical review by the implementing agent

Review covered geometry predicates, D03 output independence, exact-three selection, ordering, identity, repeated-room behavior, numerical tolerance, evidence and negative D02 freshness, safe failures, and runtime/memory bounds. Corrections included verifying the separately supplied confirmed parcel, independently rebuilding the conservative footprint, rejecting oversized parcel geometry before serializing the request, ordering per-room checks by UUID, and recomputing wall metadata even when an injected D03 response omits it. The fixture originally used the wrong separation literal. It now exercises the existing D01 `separated` contract. The audit is internal. External QA and production acceptance remain pending.

| Severity and owner | Outstanding condition |
| --- | --- |
| High, M2/M4/M7/lead | Accept authoritative parcel/edge mapping, reviewed rule pack identity, freshness and jurisdiction policy, provider-scoped failures, production evidence trust, and mandatory constraint coverage. |
| High, M5/M6 | Approve the production room-count, exact slot identity, area, rotation and relationship mapping. D03 currently accepts the explicit synthetic catalogue. |
| High, M3/M6 | Accept the geometry-edit and validator handoff, including openings/connectivity and result reason mapping. |
| Medium, M6 | Search recall depends on available D03 seeds, transformations and the geometry engine. Tune the D04 heuristic only with approved fixtures and constraints. |
| Medium, M7/M6 | Approve any Vastu soft-ranking inputs and policy before use. |

D05 prerequisites include the accepted snapshot handoffs, confirmed validation and outcome mapping, reviewed production regulatory coverage, and separate authorization for job orchestration, durable revisions, persistence and lifecycle verification.

## Original file inventory

All files below are new and M6-owned.

- `python-services/app/geometry/layout_diversity.py`
- `python-services/app/geometry/layout_validation.py`
- `python-services/tests/m6_contract/test_d04_layout.py`
- `docs/proposals/m6-ai-generation/BE-M6-D04-FINAL-DEVELOPMENT-REPORT.md`
- `docs/proposals/m6-ai-generation/BE-M6-D04-TEST-EVIDENCE-REPORT.md`
- `docs/proposals/m6-ai-generation/BE-M6-D04-PR-SUMMARY.md`


## R01 controlled correction

The Captain issued partial acceptance and authorized local M6 corrections. R01 adds the internal `physical_access_required` gate to both callable entry points. The synthetic fixture caller explicitly supplies false for geometry and setback evaluation. Omission or None returns `INCONCLUSIVE / PHYSICAL_ACCESS_SCOPE_UNKNOWN`. True returns `INCONCLUSIVE / PHYSICAL_ACCESS_EVIDENCE_UNAVAILABLE`. A value outside the accepted internal type returns `UNSUPPORTED / PHYSICAL_ACCESS_SCOPE_UNSUPPORTED`. The new gate runs after existing validation and before `VALID`. Generation checks its D03 seed through this gate before enumeration, so unresolved access yields zero alternatives.

| Connectivity or opening acceptance item | R01 disposition |
| --- | --- |
| Stable room identities and mandatory adjacency/separation | Validated using existing room program and measured wall relationships. Contradictory adjacency is rejected at ingress. |
| Shared wall, corner contact and detached rooms | No proof of physical access. Tests exercise all three with required access and return inconclusive. |
| Complete versus incomplete access evidence and absent required path | Current D04 inputs cannot establish completeness. Unknown or mandatory access fails closed. Proof of connection or disconnection awaits an approved policy and binding. |
| Door/window identity, parent wall, finite offset and width, wall span | Existing M3 draft representation and structural regressions reviewed. D04 has no approved candidate-bound opening input. |
| Geometric consistency with the actual shared wall and graph | Pending M3/M5 agreement on exact candidate and room associations, temporal binding, traversability and completeness. |
| Three alternatives | Explicit geometry-only fixture still generates scores 100, 125 and 150. Required or unresolved access yields zero alternatives. |

The separate internal audit checked precedence, malformed scope values, deterministic outcomes, false-access inference, the triple gate, existing diversity metrics, stale positive and negative D02 results, synthetic setback limitations, geometry precision and workload bounds. The correction closes the prior implicit scope assumption. All seven statuses remain internal and are exercised in the regression suite. Soft Vastu is deferred by Captain decision and has no acceptance penalty. Independent QA and the M3 handoff review remain pending. Publication readiness is **PARTIAL**.

### R01 file inventory

Of the original six deliverables, R01 updated `layout_validation.py`, `test_d04_layout.py`, and the final development, test evidence and PR summary Markdown files. The original `layout_diversity.py` is preserved byte for byte. R01 added the M3 validator handoff and dependency register. All eight are uncommitted. [Handoff](BE-M6-D04-M3-VALIDATOR-HANDOFF.md) · [Dependency register](BE-M6-D04-DEPENDENCY-REGISTER.md) · [Regression evidence](BE-M6-D04-TEST-EVIDENCE-REPORT.md)

## Final release readiness audit

The wall tolerance and duplicate-budget regressions passed. The audit confirmed identical outcomes, counters and candidate IDs with `PYTHONHASHSEED=1` and `PYTHONHASHSEED=777`. Existing fail-closed physical-access behavior is preserved. Missing scope or required access produces zero alternatives. Shared contracts and M3 Mutation Fingerprint V1 remain unchanged.

The audit verified 76 focused D04 passes and 864 full Python service passes, including D04. `pip check` reported no broken requirements. The .NET Release build succeeded for 10 projects with zero warnings or errors against a temporary tracked-HEAD archive. Release tests passed 1,559 and the Application contract runner passed 405 checks. The earlier audit recorded 238 RAG passes. The final audit did not rerun RAG. [Executed test evidence](BE-M6-D04-TEST-EVIDENCE-REPORT.md)

**Gate 2 OPEN. JC-01–JC-08 joint approval BLOCKED.** M2 mandatory review and M5 official confirmation remain pending. M4 and M7 technical drafts have been received and await owner verification. The 25 joint QA scenarios remain **UNEXECUTED** and the 47 deferred M3 scenarios remain **OUTSTANDING**. Production validator integration is **NOT AUTHORIZED**. D05 remains **ON HOLD** pending its specific authorization.

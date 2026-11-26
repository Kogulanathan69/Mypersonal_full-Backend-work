# BE-M6-D04-R01 M3 validator handoff

**Local technical review package.** Captain granted partial acceptance and authorized controlled M6 corrections. M3 integration and independent QA acceptance are pending.

## Internal callable

`app.geometry.layout_validation.validate_candidate` takes a D01 `GenerationInput`, a D03 `PlacementAssessment` with its original D02 evidence, the D03 synthetic `PlacementRequirements`, a tuple of D03 `PlacedRoom` records, and the exact project, land, and requirement revision tuple. Keyword inputs are a confirmed parcel, reviewed synthetic setback snapshot, explicit mandatory kinds, optional D03 search limits, and the R01 scope flag `physical_access_required`. The flag is M6-internal, outside all shared contracts. False is appropriate for an explicitly geometry-and-setback-only synthetic evaluation. True declares mandatory physical access, and None, including omission, leaves its scope unknown. Neither True nor None can produce a valid candidate.

`Validation` returns status, a fixed diagnostic code, and, after successful context preparation, revision and serialized evidence bindings. A valid candidate adds recomputed walls and a stable geometry digest. The digest describes room geometry and revisions. It does not bind future M3 opening geometry. `generate_alternatives` uses the same checks and returns three candidates and three pairwise scores only when every mandatory gate passes. Otherwise it returns no candidates.

| Classification | Example code | Meaning |
| --- | --- | --- |
| VALID | SUPPORTED_SYNTHETIC_CHECKS_PASSED | Explicit synthetic scope passed |
| INVALID_GEOMETRY | ROOM_INTERIORS_OVERLAP | Invalid supported geometry |
| REQUIREMENTS_VIOLATION | MANDATORY_ADJACENCY_VIOLATION | Approved room program or relationship failed |
| REGULATORY_NON_COMPLIANT | VERIFIED_SETBACK_VIOLATION | Verified synthetic setback breached beyond tolerance |
| UNSUPPORTED | PHYSICAL_ACCESS_SCOPE_UNSUPPORTED | Input is outside the accepted representation or scope |
| INCONCLUSIVE | PHYSICAL_ACCESS_SCOPE_UNKNOWN, PHYSICAL_ACCESS_EVIDENCE_UNAVAILABLE | Scope missing or required access has no approved candidate-bound evidence |
| DEPENDENCY_UNAVAILABLE | GEOMETRY_DEPENDENCY_UNAVAILABLE | Required local geometry dependency unavailable |

For a valid generation budget, the callable first resolves dependencies and binds the confirmed parcel, D02 assessment, room policy and reviewed evidence. It then checks revision and representation, room identities and geometry in UUID order, overlap and ordered adjacency/separation, verified setbacks, conservative containment, physical access scope, and finally success. A candidate with both overlap and unresolved access reports overlap. Unknown or unsupported access scope reports a fixed code without echoing the value. A malformed generation budget is rejected at entry.

## Existing and proposed boundaries

D01 has stable slot IDs and the undirected `adjacent` and `separated` relationships. D03 exposes rectangle bounds, walls and shared segments. A corner has zero shared-wall length. A shared segment can satisfy adjacency if its measured length meets the configured threshold. Neither feature implies physical access.

M3 implements a draft `0.1-draft` plan geometry with floor rooms, room wall IDs, wall IDs and metric endpoints, and opening ID, type, wall ID, offset and width. The existing M3 validators and tests cover representation-level checks, including a missing parent wall and opening span. D04 has no approved binding from the M3 draft to the D03 candidate slots or footprint, no evidence of an exact opening snapshot bound to the candidate digest, and no approved completeness or accessibility semantics. M5 records catalogue relationships by room type. The current D04 interface has no approved exact-slot mandatory access policy. No D04 opening validation or access graph can establish a positive or negative connectivity proof in this envelope.

Supported geometry is a single ground floor with axis-aligned metric rectangles in a single conservative polygon with optional holes. Local coordinates are bounded by 1,000,000 m. D02/D03 geometry floors use 1 micrometre clearance and D03 shared-wall tolerance is 1 nanometre against the configured minimum, 0.10 m by default. There is no coordinate snapping. Candidate diversity uses a 5–50 cm clamped, footprint-scaled displacement threshold. Multipart footprints, oblique room polygons, and opening/access payloads are outside this callable. Synthetic setback evidence provides local checks only. Actual jurisdiction, rule applicability, provider authorization, precedence and broader mandatory rules need M7 and M4 approval.

## Synthetic example

The following is an internal call sketch with fixture objects, not a wire payload. The room bounds share a 2 m wall.

```python
rooms = (
    PlacedRoom(slot_a, "bedroom", (0, 0, 2, 2)),
    PlacedRoom(slot_b, "bedroom", (2, 0, 4, 2)),
)
result = validate_candidate(
    request, assessment, room_policy, rooms, revision,
    confirmed_parcel=parcel, reviewed_setbacks=synthetic_snapshot,
    mandatory_kinds=("setback",), physical_access_required=True,
)
assert result.code == "PHYSICAL_ACCESS_EVIDENCE_UNAVAILABLE"
```

With the same geometry and scope None, the code is `PHYSICAL_ACCESS_SCOPE_UNKNOWN`. Corner-only contact or fully detached room rectangles produce the same inconclusive result when the approved adjacency checks otherwise pass. With false and passing synthetic scope checks, the two layouts obtained by reversing room serialization order have identical validation evidence. The 10 m × 8 m generation fixture returns three candidates with pair scores 100, 125 and 150. See the focused and full regression results. [Test evidence](BE-M6-D04-TEST-EVIDENCE-REPORT.md)

## Decisions for M3 review

- Define the exact candidate or edit revision, floor, coordinate frame and room-slot identity binding.
- Confirm how a wall shared by two rooms is represented, including split segments, wall thickness and opening offset direction.
- Determine which existing opening types and geometries can prove traversability, whether a direct edge or path is required, and how exterior, corridor or stair endpoints are identified.
- Agree with M5 on mandatory-policy ownership and how a trustworthy complete graph is distinguished from missing or partial evidence.
- Agree on the result, diagnostics and evidence mapping with M6, and the transport or persistence boundary with M4.

These decisions, draft opening adoption, production activation, shared contract publication, and D05 remain unapproved. Soft Vastu is deferred by Captain decision and has no validation effect.

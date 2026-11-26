# BE-M6-D03 test evidence report

R01 regression at baseline `5ec2d139d1a9c175832ef1cdccb025ae10d92701`, branch `feature/m6-room-placement-adjacency`. Results below were executed locally. Python used Windows 3.13.17 with Shapely 2.1.2 and GEOS 3.13.1. The RAG suite used the same existing virtual environment. C# used WSL .NET SDK 10.0.115.

| Executed gate | Result |
| --- | --- |
| D03 focused placement suite | 71 passed, including 17 additional R01 cases |
| D02 regression | 89 passed within the M6 and full Python runs. Modules contributed 11 area, 30 feasibility, 19 footprint and 29 parcel tests. |
| M6 Python contract regression | 734 passed, including the D02 and D03 cases |
| Full Python service regression | 788 passed, zero failed or skipped |
| RAG synthetic-corpus regression | 238 passed, zero failed or skipped |
| Python and RAG environment dependency checks | Both `pip check` invocations reported no broken requirements |
| WSL .NET Release build | Succeeded with zero warnings and errors |
| Database-free C# solution tests | 1,425 passed, zero failed or skipped. Application 128, Infrastructure 722, Reports 54, Requirements 398, Workflow 123. |
| Shared C# console harness | 348 passed, zero failed |
| Existing M6 C# representation and semantics | 38 representation and 82 semantics passed, zero failed, via an ignored runner linked to the existing scenario sources |
| Separate geometry and freshness audit | 40 generated rectangular tilings passed independent containment, dimension, interior-disjointness, contact-length and repeatability assertions, with stale evidence rejected in both directions and fresh negative decisions preserved |

Unique regression count across the five suites is **2,919**. Focused subsets, audit samples, build and dependency checks are outside that total. The two Python suites reported the existing Starlette TestClient deprecation warning. This run exercised synthetic data and database-free tests.

## R01 reproduction and coverage

The isolated `test_obsolete_empty_buildable_region_requires_fresh_assessment` failed before the correction. D03 returned the old `INFEASIBLE / EMPTY_BUILDABLE_REGION` result after setback distances changed from 5 m to 0 m, with the same project, land and requirements revisions. The same test passed after the change. D03 returned `NEEDS_REVIEW / INPUT_REVISION_MISMATCH` for the obsolete result, and a fresh D02 calculation allowed placement.

The focused suite covers current and obsolete feasible results, empty and non-empty D02 proofs of infeasibility, review outcomes with and without applied rules, and an unsupported multipart outcome. Tests also check changes to gap details, rule identity, reviewer identity and source provenance, including a rule outside the applied set. Missing and malformed inputs, mutation after capture and inconsistent D02 applied evidence are checked. Fresh results preserve valid negative decisions and allow placement when feasible.

## Geometry and boundary evidence

- Two rooms of 2 m by 2 m filled a 4 m by 2 m footprint in 24 candidate probes. Each has 4 m2 area, the intersection area is 0 m2 and shared wall length is exactly 2 m.
- An L-shaped fixture required changing the first room from the initial placement. The verified rooms were `[2,0,5,2]` and `[0,0,2,3]` in metres.
- A 3 m by 2 m room fit a 2 m by 4 m footprint with rotation permission and returned search exhaustion with rotation disabled.
- A 5 m by 2 m conservative footprint with a combined 11 m2 explicit room minimum returned the proven-area diagnostic. Two 2 m by 2 m rooms in a 3 m by 3 m footprint returned search exhaustion despite 9 m2 of area.
- Tests covered invalid types, counts, duplicate IDs, contradictory and unknown relationships, stale revisions and setback evidence, missing dimensions and slot policies, nonfinite input, footprints with holes, concavity and multipart rejection, tiny positive gaps, tolerance boundaries, and a single-probe cap. Fault injection tested import and GEOS failure without reflecting exception details.

The following excerpt is from the executed two-room candidate. Bounds are `[minX,minY,maxX,maxY]` in metres.

```json
{
  "outcome": "SUCCESS",
  "rooms": [
    {"slot_id": "00000000-0000-0000-0000-000000000064", "room_type": "bedroom", "bounds": [0, 0, 2, 2], "rotated": false},
    {"slot_id": "00000000-0000-0000-0000-000000000065", "room_type": "bedroom", "bounds": [2, 0, 4, 2], "rotated": false}
  ],
  "candidate_checks": 24,
  "optional_matches": 0,
  "shared_walls": [
    {"slots": ["00000000-0000-0000-0000-000000000064", "00000000-0000-0000-0000-000000000065"], "segment": [[2, 0], [2, 2]], "length_m": 2}
  ]
}
```

## Backup integrity

The R01 snapshot of all six deliverables is in `BE-M6-D03-R01-281e64b8bf68c87d3da5e5b7bf58` under the external Desktop backup directory. Its `SHA256SUMS.txt` records per-file digests verified against the working copies. The original baseline artifacts are listed below.

External directory `C:/Users/CTMS/Desktop/BuildMind-Backend-Backup/BE-M6-D03-c833c0aa85b04cbdae033cb7bb53a1c6`.

| Artifact | Verification | SHA-256 |
| --- | --- | --- |
| `repository.bundle` | Git bundle verify passed. Complete history and 50 refs including D01, D02 and verified develop. | `8e6daf1f48f6e8f7e5bc1ae54ad8361ed6c0afe13bf1f05cf33f7a4659f06cfd` |
| `pre-d03-tracked.zip` | 522 readable file entries, matching the tracked-file count at the clean pre-D03 HEAD. | `1bb0f5a1f3f41878a2a0c21756fa87abf4c8c2b9db9ed4c2271a05d40a563bee` |

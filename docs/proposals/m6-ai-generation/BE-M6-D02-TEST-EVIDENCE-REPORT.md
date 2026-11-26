# BE-M6-D02 — Test evidence

- **Verified backend** `9ee4a07e71a200155a022c224cae8df1d5085847`
- **Scope** Final local verification of the D02 overlay on `feature/m6-footprint-feasibility`

## Environment and regression

Python ran in the existing Windows virtual environment with Python 3.13.17, Shapely 2.1.2 and GEOS 3.13.1. The database-free C# run used Ubuntu WSL and .NET SDK 10.0.115, with separate ignored `obj/d02-linux` outputs. RAG ran in the previously verified separate Python environment. The logs are external to the repository.

| Check | Result | Wall time | Evidence log |
|---|---|---:|---|
| WSL SDK | 10.0.115, exit 0 | 1.66 s | `sdk.log` |
| Restore | 10/10 projects, exit 0 | 125.97 s | `restore.log` |
| Release build | 10/10 projects, zero warnings or errors, exit 0 | 81.71 s | `build.log` |
| Database-free C# solution | 1,317 passed, zero failed or skipped, exit 0 | 60.93 s | `solution.log` |
| Shared console | 286 passed, zero failed, exit 0 | 16.65 s | `shared-harness.log` |
| Isolated D01 C# | 120 passed, zero failed, exit 0 | 125.93 s | `d01-harness.log` |
| Full Python regression after audit correction | 717 passed, one warning, exit 0 | 12.50 s | `final-python.log` |
| M6 Python subset | 663 passed, exit 0 | 9.53 s | `final-m6-python.log` |
| D02 Python subset | 89 passed, exit 0 | 4.96 s | `final-d02-python.log` |
| Python `pip check` | No broken requirements, exit 0 | 2.11 s | `final-python-pip.log` |
| RAG synthetic corpus | 139 passed, one warning, exit 0 | 4.64 s | `rag.log` |
| RAG `pip check` | No broken requirements, exit 0 | 1.69 s | `rag-pip.log` |
| Python D02 result through actual D01 C# boundary | Five response probes passed, exit 0 | 14.32 s | `d02-wire.log` |

The C# solution projects passed: Application Unit Tests **128**, Infrastructure **614**, Reports **54**, Requirements **398**, Workflow **123**. The 120 isolated D01 cases consist of 38 representation checks and 82 semantic/boundary checks. All 22 existing gap cases passed again. The five supplemental C# probes read actual Python results for a positive footprint, proven positive-area shortfall, zero/empty footprint, missing evidence and disconnected geometry. They verified the D01 status, area presence and footprint nullability.

The Python and RAG full runs each reported the existing Starlette `TestClient` deprecation involving `httpx`. No geometry test was skipped in the verified Shapely environment. The approved real corpus and database-dependent integration were not exercised locally. The reported PR #38 post-merge results (2,352/2,352, 65/65 MySQL, 3/3 CI) remain separate historical evidence supplied for D02 authorization.

### Before and after

| Check | Previous locally verified D01 H4-R1 | D02 final local result |
|---|---:|---:|
| C# solution | 1,234 | 1,317 |
| Shared console | 276 | 286 |
| D01 C# representation / semantic | 38 / 82 | 38 / 82 |
| Complete Python | 628 | 717 |
| M6 Python subset | 574 | 663 |
| D02 Python subset | — | 89 |
| RAG synthetic corpus | 139 | 139 |
| Non-repeated regular suite checks | 2,397 | **2,579** |

The C# and shared console increases reflect the newer team backend baseline. The D02 addition is 89 Python cases. The final total is 1,317 + 286 + 120 + 717 + 139. The M6 Python and D02 Python counts are subsets of 717. Focused reruns, dependency checks, probe executions and independent audit samples are outside the total.

## D02 test modules and acceptance coverage

| Test module | Final cases | Coverage |
|---|---:|---|
| `test_d02_parcel.py` | 29 | Minimum ring size, winding and exact edge identities, malformed or unrelated references, finite numbers, units, positive area, duplicate vertices and edges, self-crossing, hole containment and precision/resource caps |
| `test_d02_footprint.py` | 19 | Rectangle and asymmetric calculations, explicit zero, clockwise edge mapping, irregular and concave shapes, narrow and consumed parcels, retained hole, strict containment and distance, multipart rejection, reproducible bytes and translated sloped-edge cases |
| `test_d02_area.py` | 11 | Explicit area assumptions, revision and room-program mismatch, zero extras, nonfinite and negative values, multiplication and sum overflow, underflow |
| `test_d02_feasibility.py` | 30 | Preliminary sufficiency, proven area shortfall, the precision band, unknown versus zero, missing/conflicting/unverified evidence, synthetic trust, mismatched snapshots, missing assumptions, invalid/unsupported geometry, safe exceptions and D01 wire results |

The D02.5 security coverage exercises NaN and infinity, bool or string coordinates and assumptions, excessive coordinates, inadequate ring clearance, complexity caps, malformed and duplicate JSON, negative setbacks, out-of-range edge indices, and invalid or mismatched IDs. It tests missing, conflicting and non-applicable evidence, malformed synthetic markers, and geometry/dependency errors with an injected sensitive marker. Those failures preserve unknown measurements and fixed non-success diagnostics.

## Numerical evidence

Values below are synthetic. The first three cases use the 10 × 8 m parcel. The three requested room/space components of the asymmetric example total 15 m² (9 + 2 + 4).

| Fixture | Independent reference | Conservative output and disposition |
|---|---|---|
| 1 m on each rectangle edge | 8 × 6 = 48 m² | 47.99156447 m², optimistic 48.00000028 m², 9 m² required, preliminary `FEASIBLE` |
| Edges (1, 2, 3, 0.5) m in original order | (10 − 2 − 0.5) × (8 − 1 − 3) = 30 m² | 29.98794980 m², removed 50.01205020 m², preliminary `FEASIBLE` |
| Explicit zero on every edge | Original area 80 m² | 80 m² retained, zero removed |
| Concave L-shaped parcel, 0.5 m on all edges | Parcel 39 m². The two arms and rounded notch give 24 + 0.25 − π/16 ≈ 24.05365046 m². | Conservative 24.04954369 m², optimistic 24.05372958 m². Both distant arms retained, notch excluded. |
| Valid irregular polygon | Shoelace parcel area 67 m² | Strict containment, validity and all four independent edge-clearance assertions pass. |
| 0.3 m narrow lot, opposing setbacks 0.14 m | 10 × (0.3 − 0.28) = 0.2 m² | Conservative area within 0.003 m² of the reference. |
| 0.3 m narrow lot, opposing setbacks 0.16 m | No positive-area region | Both bounds zero. |
| Rectangle with 60 m² required, 1 m setbacks | Ideal area shortfall 60 − 48 = 12 m² | `INFEASIBLE`, minimum proven shortfall 11.99999972 m² |
| Rectangle with 48 m² required, 1 m setbacks | Threshold lies between bounds | `NEEDS_REVIEW`, no asserted shortfall |
| Rectangle with two opposing 5 m setbacks | Entire 8 m width consumed | Zero buildable area and null geometry, `INFEASIBLE` when required area is known |
| Disconnected dumbbell footprint | Two surviving components | `UNSUPPORTED`, buildable area and geometry unknown |

## Independent geometry audit

A deterministic 120-case generated sample used ordered simple polygons with five to ten edges, mixed zero and positive setbacks, and coordinates near both zero and 800,000 m. Before correction, 89 cases failed strict parcel containment because GEOS overlay points rounded just outside sloped, unbuffered edges. The D02 guard now insets only a valid computed polygon by 10 nm and rechecks it. The original parcel is preserved. Two representative cases were added to the 89-case regression.

After correction, **120/120** generated cases passed validity, parcel coverage, deterministic output bytes, area-bound ordering and sampled distance to the finite source segments using a separate projection calculation. Sampling covered vertices and segment midpoints. The implementation also checks whole-geometry minimum distances in GEOS. The 120-case audit is independent evidence outside the regular suite count.

## Publication compatibility regression

Both runs below used the PR #40 merge `542a137e960ae05ee906bb83128709db6640a0b4`. The first ran on an isolated checkout with the 11-file overlay. The second ran after fast-forwarding the active branch to the exact same baseline. All final runs exited successfully.

| Verification | Isolated checkout | Active checkout |
|---|---:|---:|
| Full Python | 717 passed | 717 passed |
| M6 Python subset | 663 passed | 663 passed |
| D02 Python subset | 89 passed, 0 skipped | 89 passed, 0 skipped |
| WSL .NET 10.0.115 restore and Release build | 10/10, 0 warnings/errors | 10/10, 0 warnings/errors |
| Database-free C# solution | 1,400 passed | 1,400 passed |
| Shared contract console | 286 passed | 286 passed |
| Isolated D01 C# representation and semantics | 38 + 82 passed | 38 + 82 passed |
| RAG synthetic corpus | 201 passed | 201 passed |
| Python and RAG `pip check` | Both clean | Both clean |
| Independent generated geometry | 120/120 | 120/120 |
| Python-to-C# response probes | 5/5 | 5/5 |
| Non-duplicated regression total | **2,724** | **2,724** |

C# project totals were Application 128, Infrastructure 697, Reports 54, Requirements 398, and Workflow 123. Each run contributes the same 2,724 unique suite checks counted once. The 783 M6 checks are 663 Python plus 120 standalone C# checks already represented above. The 89 D02 cases are part of the Python counts. Geometry audits, wire probes, builds, dependency checks and the repeated checkout run are outside the total. Python and RAG each retained the existing Starlette deprecation warning. No database-dependent or real-corpus suite ran locally.

The first isolated C# attempt placed artifacts outside the checkout. Existing fixture lookup depends on the ancestor directory containing the solution, so that setup caused fixture lookup failures. The successful rerun and active run used ignored `obj/m6-d02-pub` output inside the respective checkout. Retained logs distinguish the unsuccessful runner setup from the completed suites.

The independent publication audit generated 120 deterministic five- to ten-edge parcels with mixed zero and positive setbacks at ordinary and 800,000 m translated coordinates. Both runs checked validity, containment, byte-repeatability, ordered area bounds, and independently projected finite-edge distances at sampled vertices and midpoints. Both five-response probe runs parsed actual D02 outputs through the D01 C# boundary.

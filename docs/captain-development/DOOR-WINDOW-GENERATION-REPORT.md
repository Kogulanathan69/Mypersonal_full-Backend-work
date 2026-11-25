# Door and window generation report

**Verdict: COMPLETE_AND_VERIFIED** (local), **INDEPENDENT_REVIEW_REQUIRED** before merge (M6 generation owner).
Opening dimensions are **design preferences**, not code-compliance values; every alternative carries the diagnostic `OPENING_DIMENSIONS_DESIGN_PREFERENCE`.

## Model (`captain-openings-v1`, `python-services/app/internal/openings.py`; commits `834b2ff`, `27e327d`)

1. **Spaces.**
   - The envelope is the bounding box of all walls.
   - Residual space (envelope minus rooms) becomes **circulation** where it is at least 0.9 m passable; narrower residue is a sliver.
   - Generated exterior walls close the circulation.
2. **Classification.** Each wall interval is classified by probes 0.3 m either side: room / `HALL:n` / `OUTSIDE` / sliver.
3. **Doors.**
   - One entrance (1.0 m) from outside into the hub (circulation or living space).
   - A BFS access tree connects every room through 0.9 m doors (0.8 m for bathrooms).
   - Bathrooms are never pass-through. Passing through a bedroom is a fallback and raises `ACCESS_THROUGH_BEDROOM` (WARNING).
4. **Windows.** Windows are placed only on outside-facing portions of habitable rooms, with per-type widths and 0.15 m corner clearance. A room without one gets `ROOM_WITHOUT_EXTERNAL_WINDOW` (WARNING).
5. **Infeasibility.** An unreachable room gives `PHYSICAL_ACCESS_UNSATISFIABLE` (CRITICAL). Generation then fails truthfully (`INFEASIBLE`) instead of returning fewer than three alternatives.

## Validation of edited plans (`validate_openings`)

| Code | Severity | Meaning |
|---|---|---|
| OPENING_WALL_NOT_FOUND, OPENING_OUTSIDE_WALL, OPENINGS_OVERLAP | CRITICAL | structurally invalid |
| WINDOW_NOT_ON_EXTERIOR | CRITICAL | window between two rooms, or with no room |
| DOOR_CONNECTS_NO_SPACES | CRITICAL | same space on both sides |
| WINDOW_FACES_INTERIOR_SPACE | WARNING | window now faces circulation or a gap, e.g. after a room shrink (added in `27e327d`) |
| DOOR_OPENS_INTO_UNUSABLE_SPACE | WARNING | door opens into a sub-passage gap (added in `27e327d`) |
| OPENING_CORNER_CLEARANCE, NO_ENTRANCE, ROOM_NOT_REACHABLE, PHYSICAL_ACCESS_NOT_EVALUATED | WARNING | design or access issues |

WARNINGs require explicit customer acknowledgement when the candidate is applied (existing M3 flow). CRITICAL rejects the candidate.

## Evidence

| Evidence | Result |
|---|---|
| `tests/captain/test_openings.py` | **13/13**: generation, access tree, bathroom rule, windows, infeasible access, interior-space warnings, window-between-rooms CRITICAL |
| python-services full suite | **1021 passed** |
| Journey (process stack) phases 2,3,4,6,8,9 | 155/155 (at `834b2ff`) |
| Container journey runs A/B | 132/132 and 110/111. Generated plans contain an entrance, internal doors and windows; the phase 4 edit is accepted as a candidate |
| Reproduction on the stored container plan | before the edit: no findings; after the 10 % resize: `WINDOW_FACES_INTERIOR_SPACE` (WARNING), previously `WINDOW_NOT_ON_EXTERIOR` (CRITICAL). The journey does not assert the code itself. |
| PDF report | the M5 renderer draws openings; PDF QA run (see FINAL-LIVE-INTEGRATION-EVIDENCE) |

## Regression found and fixed

At `2745372` (CRS frame) the canonical ring changed and so did generated layouts. A 10 % room resize then left an existing window facing interior circulation, and the valid edit was rejected as `WINDOW_NOT_ON_EXTERIOR`. Phase 4 had not been rerun after the CRS change; the container run exposed it. Fixed in `27e327d`. The edit engine still does not move openings when rooms change, so they are re-validated and surfaced as warnings.

## Limitations

- Axis-aligned rectangular rooms only. Door swing, furniture clearance, staircase and accessibility (ramp) rules are not modelled.
- No regulation-based minimum opening or ventilation sizes: these await reviewed regulations.

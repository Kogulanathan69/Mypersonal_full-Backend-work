# CRS and geometry integration report

**Verdict: COMPLETE_AND_VERIFIED** for the coordinate frame pipeline: CRS normalisation → local frame → canonical digest → persisted frame → generation on the transformed geometry.
**INDEPENDENT_REVIEW_REQUIRED** before merge (M2/M6 owners).
Edge **role** assignment (road frontage etc.) is **not** implemented and depends on reviewed regulations. It is not claimed.

## Design (`captain-frame-v1`, commit `2745372`)

| Step | Implementation |
|---|---|
| Normalisation | Reuses the M2 `normalize_land_crs` (PROJ via pyproj 3.8.0). No "ballpark" transformations are allowed, so an unknown or ambiguous CRS → 422 with a `reasonCode`. |
| Calculation CRS | LOCAL metres/feet: anchor (0,0), feet converted to metres. For a geographic source inside Sri Lanka, **EPSG:5235 (SLD99 / Sri Lanka Grid 1999)** is selected automatically. Geographic outside Sri Lanka → `CALCULATION_CRS_REQUIRED`. A projected source requires an explicit calculation CRS or keeps its own. |
| Local frame | Anchor = floored minimum corner of the projected ring. Axes are grid east/north in metres. Provenance (source CRS, PROJ pipeline description, library versions) is stored with the frame. |
| Canonical ring | Open ring, counter-clockwise, starting at the smallest vertex, 6-decimal fixed text |
| Digest | SHA-256 over `captain-frame-v1\|crs\|anchor\|ring`. The .NET `LandFrameDigest` recomputes it, and the API **rejects** a Python response whose digest does not match (`DependencyFailureException(MalformedResponse)`). |
| Edges | UUIDv5(frame id + sorted endpoints), with length and grid bearing. `parentEdgeId` gives split lineage across land revisions. Role is `UNASSIGNED`. |
| Persistence | `land_revisions.frame_json` + `geometry_digest` (migration #15). The land contract keeps its closed ring. For LOCAL input, the established validation runs first, so `POLYGON_INVALID` is unchanged. |
| API | `PUT /land` accepts `georeference{sourceCrs, sourceUnit, calculationCrs, sourceExterior}`, exclusive with `exterior` (`EXTERIOR_AND_GEOREFERENCE_BOTH_SUPPLIED`). Also `GET /land/frame` and `POST /land/transform-preview`; the land response includes `geometryDigest`. |

## Evidence

| Evidence | Result |
|---|---|
| `tests/captain/test_land_frame.py` | **13/13**. SLD99 origin 7°00′01.6975″N 80°46′18.1671″E → (500000, 500000) using EPSG registry parameters; golden digest `c8293eff…22a4`; CCW/start invariance; feet; outside Sri Lanka; unknown CRS; edge lineage |
| `LandFrameDigestTests` (.NET) | **3/3**: golden digest parity with Python |
| Journey phases 2,3,8,10 (process stack) | 88/88: preview; auto SLD99; unknown CRS 422; outside-Sri-Lanka 422; ambiguous 422; georeferenced land about 168 m² with digest; stored frame with provenance; generation on the transformed geometry with doors/windows; split lineage |
| Container journey run A (includes phase 10) | 132/132 |

## Defect found later

The CRS commit changed the canonical ring (start vertex, orientation), which changed generated layouts. A previously passing edit then hit a window-validation rejection. This was found by container verification and fixed in `27e327d`; see DOOR-WINDOW-GENERATION-REPORT.

## Limitations

- No survey-plan (lengths/bearings) → coordinates inference. By rule, coordinates are never inferred from survey lengths.
- No datum-grid files (NTv2 etc.) are used. Transformations rely on PROJ's built-in definitions, and the accuracy class is recorded as provenance, not certified.
- Edge roles and setbacks per edge await reviewed regulations (RAG / STEP 8B).

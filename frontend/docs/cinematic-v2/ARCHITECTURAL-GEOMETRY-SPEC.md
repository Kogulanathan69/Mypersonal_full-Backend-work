# BuildMind Cinematic V2 — Authoritative Residence Geometry

## Authority and status

`BM-CV2-RES-001` is the Phase 04 authoritative marketing residence. Its machine-readable source is `asset-production/cinematic-v2/residence.geometry.json`, transcribed from the existing live procedural source `src/app/features/public/home/cinematic/house-model-data.ts`. Phase 04 does not alter that live source.

This is illustrative marketing geometry, not customer output, construction documentation, structural engineering, or regulatory certification.

## Coordinate system

- Units: metres.
- Right-handed coordinates: east `+x`, up `+y`, south `+z`.
- Ground datum: `y=0`.
- Floor-to-floor height: 3.2 m; nominal wall height: 3.0 m.
- Authoritative footprint: `x=-8…8`, `z=-4.5…4.5` (16.0 × 9.0 m).
- Irregular six-sided parcel and buildable polygon remain exactly aligned with the live model.

## Architectural continuity

The residence is an asymmetric two-storey tropical-modern composition. A grounded lower volume opens from the living room to a south-facing verandah; a lighter upper volume extends from `x=-3…9.5`, `z=-3.5…4`. Deep slab overhangs, upper timber screening, proportionate glazing, exposed concrete, neutral stone, and warm interior light define the visual language.

Seven ground-floor zones remain unchanged: living; kitchen and dining; two bedrooms; bath; entrance and stair; verandah. All 14 wall runs, their thicknesses, levels, openings, sills, heads, slabs, louvres, pool, trees, and path positions are recorded in the JSON source.

The stair is currently an authoritative reserved volume inside “Entrance & stair,” not detailed flight geometry. Final risers, landings, headroom, and guarding require architectural approval before high-quality modelling. This limitation must not be hidden in production renders.

## Registration rules

1. Survey, blueprint, wireframe, material, exterior, and interior assets must cite `BM-CV2-RES-001`.
2. Window and door openings must be generated from wall-run distances, never redrawn by eye.
3. `SHOT-04` is the locked wireframe-to-material registration transform.
4. Interior shots must remain inside the living-zone bounds and look through the recorded verandah glazing line.
5. Geometry changes require a new version, validation, updated hashes, and explicit approval before integration.

## Validation

`node asset-production/cinematic-v2/scripts/validate-assets.mjs` checks geometry/camera version agreement, expected room/wall/shot counts, opening bounds, and technical preview hashes. The generated SVGs are technical evidence only and are not photorealistic deliverables.

# Blender Production Register

## Phase 04U execution

- Residence: `BM-CV2-RES-001` (illustrative marketing content only).
- Generator: `asset-production/cinematic-v2/blender/build_residence.py`, version `1.0.2`.
- Blender: `4.5.17 LTS`, Cycles CPU only, two render threads.
- Coordinate conversion: Blender X = source X; Blender Y = negative source Z; Blender Z = source Y.
- Geometry SHA-256: `516490c2fc9b04a4d2ba04349c6806a5af313b986729c3b4db3b3344f6851426`.
- Camera SHA-256: `ef6f975131b1062bd9d0eb368c82cbc1137366763d7eb359b74da10c33327dca`.
- Isolated production root: `C:\Users\kogul\Tools\BuildMind-Rendering\production\BM-CV2-RES-001`.
- Versioned model: `BM-CV2-RES-001-v1.blend`.

The generator validates schema version, geometry identifier, units, coordinate system, footprint, parcel/room/wall/camera counts, opening bounds, camera identifiers, and reserved-stair status before creating a scene. It fails closed on inconsistent input. Walls are segmented around authoritative openings without boolean modifiers. The reserved stair volume is named and retained as a non-rendering wire placeholder; no detailed flight was invented.

## Rendering ladder

R1 completed at 480×270/8 samples. An initial interior collision exposed by R1 was corrected by moving seating out of the registered camera path and treating `G-GLAZING` as architectural glazing. R2 completed for SHOT-04 through SHOT-08 at 640×360/16 samples. R3 completed for SHOT-05 and SHOT-08 at 960×540/32 samples. R4 and R5 were not started because the visual-quality gate did not pass.

No Eevee, GPU rendering, external textures, HDRIs, downloaded models, add-ons, driver changes, or system configuration changes were used.

## Phase 04V refinement

Generator 1.1.1 produced v2 without changing authoritative geometry. It adds concrete pores, directional timber/fabric detail, clearer glazing, darker metal, layered vegetation, refined seating, screen/roof/pool edge detail, and a Nishita late-afternoon sky. Two bounded iterations were used. R2 SHOT-04/05/08 and R3 SHOT-05/08 completed; R4/R5 were not run.

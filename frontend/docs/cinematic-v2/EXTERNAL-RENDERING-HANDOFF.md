# BuildMind Cinematic V2 — Portable External Rendering Handoff

## Required package

- `residence.geometry.json`, geometry version `BM-CV2-RES-001`.
- `cameras.json`, including exact SHOT-04 registration and SHOT-05–09 compositions.
- `ARCHITECTURAL-GEOMETRY-SPEC.md`.
- `MATERIAL-AND-LIGHTING-REGISTER.md`.
- `CAMERA-SHOT-REGISTER.md`.
- `ARCHITECTURAL-ASSET-BRIEF.md`.
- `PERFORMANCE-BUDGET.md`.
- `ASSET-LICENSE-REGISTER.md`.
- Phase 04R wireframe/material comparison captures as visual registration references.

## Coordinate conversion

The source is right-handed, metres, `+x` east, `+y` up, `+z` south. Blender is right-handed and Z-up. Import with `Blender X = source X`, `Blender Y = -source Z`, `Blender Z = source Y`, preserving one Blender unit as one metre. Convert camera position and target with the same mapping. Validate with parcel vertices, footprint dimensions, wall openings, roof bounds, and SHOT-04 silhouette before material work.

## Production workflow

1. Use an actively supported Blender LTS on supported hardware.
2. Generate or import geometry from the authoritative JSON; do not trace the screenshots.
3. Organize site, ground, upper, openings, screens, interior, furniture, landscape, lights, and cameras into named collections.
4. Resolve the reserved stair volume only after architectural review.
5. Build procedural PBR concrete, timber, limestone, glass, metal, and neutral interior materials; document any external texture/HDRI before use.
6. Establish late-afternoon sun, calibrated sky/environment, warm 2700–3000 K practicals, physically plausible bounce, and controlled blue-hour variation.
7. Render SHOT-04 wireframe/ID/material passes with identical camera, crop, geometry, and resolution. Difference-check silhouettes.
8. Render exterior hero/approach and interior reveal/journey from the same scene. Confirm visible openings and exterior/interior relationships.
9. Produce desktop masters at 2400×1350 or 1920×1080, mobile working masters at 900×1200, and tablet fallback masters at 1280×1024.
10. Preserve EXR/PNG masters outside Git; create AVIF/WebP/JPEG or short-video derivatives only after visual approval.
11. Meet the existing budgets: hero poster ≤250 KB, exterior ≤1.35 MB, interior ≤1.0 MB, extra poster/transition ≤150 KB, total deferred desktop ≤2.5 MB, mobile ≤1.2 MB.
12. Record creator, tool/version, source, licence, redistribution/modification rights, dimensions, bytes, SHA-256, shot, geometry/material versions, and approval.

## Environment comparison

| Option | Evidence-based assessment |
| --- | --- |
| This local workstation | Three.js technical rendering is proven; current Blender minimum is not met by the two-core CPU, so final Blender production is not recommended |
| Another team workstation | Preferred if it meets current Blender requirements and can be controlled by the team; hardware and compatibility must be verified before assignment |
| Cloud GPU workstation | Technically suitable in principle, but requires separate security, cost, region, upload, licence, and data-handling approval; none was provisioned or tested |
| Compatible older Blender | A limited feasibility option only; older releases may support older hardware, but security/support status, file compatibility, renderer differences, and production time must be tested before use |

No external service, workstation, model, licence, or upload was authorized or used during Phase 04R.

# BuildMind Cinematic V2 — Phase 04R Browser Rendering Feasibility

## Decision

**Classification B — suitable for technical scenes only; photorealistic renders require a separate production environment.**

The isolated Three.js prototype proves that `BM-CV2-RES-001` can be reconstructed consistently, rendered with registered cameras, and captured on this machine using hardware-accelerated WebGL2. It does not achieve the approved premium photorealistic exterior or interior quality.

## Scope and protection

- Starting commit: `c905aabf8ed0f883b2eb328cc452e58bb111600c` on `cinematic-v2` with a clean worktree.
- Prototype location: `asset-production/cinematic-v2/prototype/`.
- Existing Three.js 0.187.0 and Playwright packages reused; no dependency installation.
- No Angular source, live cinematic engine, backend, package manifest, system setting, or production website asset modified.

## Prototype content

The prototype reconstructs the authoritative exterior/interior walls, openings, slabs, roof overhangs, glazing, timber louvres, verandah shade, reserved stair volume, living and dining furniture, pool, trees, and parcel. Original canvas textures provide concrete, timber, and stone variation. Lighting uses hemisphere, directional sun, warm point lights, soft shadows where enabled, ACES filmic tone mapping, and sRGB output.

Quality tiers cap pixel ratio and shadows. Low quality disables shadows and antialiasing; standard uses a 1.35 DPR cap and 1024² shadow map; high uses a 1.75 DPR cap and 2048² shadow map.

## Measured renderer

- Browser: system Chrome, automated through existing Playwright.
- API: WebGL2.
- Renderer: `ANGLE (Intel, Intel(R) HD Graphics 520 (0x00001916) Direct3D11 vs_5_0 ps_5_0, D3D11)`.
- Vendor: `Google Inc. (Intel)`.
- Hardware/software distinction: ANGLE reports Intel HD Graphics 520 through Direct3D 11; this is hardware rendering, not SwiftShader/software rasterization.
- Context-loss extension: supported; loss and restoration events both observed.
- Prototype texture count: three procedural 256² maps plus one shadow map when enabled.
- Estimated active texture allocation: about 13.3 MB at high quality, 4.98 MB standard, and 0.75 MB without shadows; excludes driver/framebuffer overhead.

## Warm five-second measurements

| Capture | Resolution/quality | FPS | Draw calls | Triangles | PNG bytes |
| --- | --- | ---: | ---: | ---: | ---: |
| Blueprint registration | 1280×720 standard | 49.4 | 259 | 2,958 | 145,801 |
| Architectural wireframe | 1280×720 standard | 55.0 | 346 | 30 rasterized material triangles* | 151,698 |
| SHOT-04 wireframe | 1280×720 standard | 53.7 | 346 | 30* | 138,662 |
| SHOT-04 material | 1280×720 standard | 30.6 | 451 | 9,810 | 133,239 |
| Exterior hero | 1920×1080 high | 38.0 | 451 | 9,810 | 192,782 |
| Exterior hero | 1280×720 standard | 49.4 | 451 | 9,810 | 128,168 |
| Exterior approach | 1280×720 standard | 53.9 | 303 | 6,058 | 170,830 |
| Interior reveal | 1280×720 standard | 52.1 | 300 | 5,486 | 236,828 |
| Interior journey | 1280×720 standard | 49.1 | 354 | 6,718 | 265,422 |
| Exterior mobile | 390×844 low | 26.9 | 250 | 4,262 | 57,185 |
| Static-quality exterior | 1280×720 low | 53.3 | 304 | 6,702 | 100,784 |

`*` Three.js triangle statistics do not include line-segment workload, so the wireframe triangle count alone is not a complexity comparison.

All final captures completed with zero console or page errors. The 1920×1080 experiment was stable but below the 55–60 FPS desktop motion target. Mobile low quality was below the 30 FPS target. These are static-scene feasibility measurements, not complete scroll-page performance results.

## Registration and continuity

Blueprint, wireframe, material, exterior, and interior views use the same JSON geometry. SHOT-04 wireframe and material captures use exactly the same registered position, target, lens calculation, resolution, and geometry version. Their silhouette/camera alignment is therefore deterministic at the prototype level. No unrelated residence was substituted.

## Visual-quality gate

| Criterion | Assessment |
| --- | --- |
| Architectural proportions | Coherent and consistent with the authoritative massing |
| Geometry continuity | Strong for technical storytelling |
| Material realism | Insufficient; procedural surfaces lack high-frequency detail and calibrated PBR response |
| Lighting realism | Insufficient; direct lights and fog cannot replace a production environment/light transport workflow |
| Shadow quality | Useful but visibly real-time and resolution-limited |
| Exterior composition | Clear and cinematic enough for a technical study, not premium architectural marketing |
| Interior composition | Camera continuity is proven, but surfaces, glazing, furniture, bounce light, and exterior visibility are too crude |
| Mobile suitability | Composition remains legible; measured 26.9 FPS misses the 30 FPS target |
| Cinematic potential | Good as a registered technical-to-material bridge |
| Photorealistic suitability | Fails the approved quality bar |

Survey Nocturne is supported by the cyan technical linework and restrained dark UI. Tropical Material Journal is only suggested through timber, concrete, stone, pool, planting, and warm light; it is not rendered with the material nuance required for final production.

## Browser export support

System Chrome is probed for canvas PNG, JPEG, and WebP output. Exact MIME results and approximate encoded sizes are stored in `prototype/captures/format-probe.json`. Browser support does not replace final image-quality and compression review.

## Conclusion

Retain the existing real-time Three.js path for Scenes 01–04 and static/mobile technical fallbacks. Do not use these prototype material or interior captures as final Scenes 05–06 media. Produce those shots in a supported external rendering environment, then integrate only after separate Phase 05 authorization.

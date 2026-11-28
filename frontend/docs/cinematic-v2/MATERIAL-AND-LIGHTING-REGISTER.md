# BuildMind Cinematic V2 — Material and Lighting Register

## Material targets

| ID | Material | Base/appearance | Physical target | Provenance |
| --- | --- | --- | --- | --- |
| MAT-CONCRETE-01 | Smooth architectural concrete | neutral warm grey | roughness 0.72; subtle 0.15–0.3 mm bump; non-metal | Original procedural nodes |
| MAT-TIMBER-01 | Vertical timber screen/soffit | medium honey-brown | roughness 0.48; restrained grain; non-metal | Original procedural nodes; generic species |
| MAT-STONE-01 | Limestone/plinth | pale neutral aggregate | roughness 0.8; low relief | Original procedural nodes |
| MAT-GLASS-01 | Clear architectural glass | neutral clear | IOR 1.45; roughness 0.08; thin tint only | Original shader |
| MAT-METAL-01 | Frames and details | charcoal | metallic 0.8; roughness 0.32 | Original shader |
| MAT-INTERIOR-01 | Plaster and neutral finishes | warm off-white | roughness 0.65 | Original procedural nodes |

No downloaded texture or HDRI is approved. If third-party maps or environments are proposed later, commercial use, modification, rendered-output use, and redistribution terms must be entered before use.

## Lighting targets

- Sun: camera-left/rear, late afternoon, warm but neutral enough to preserve material color.
- Sky: controlled late-afternoon to blue-hour balance; no saturated teal/orange grade.
- Interior: 2700–3000 K practical illumination, used to reveal depth rather than create an orange glow.
- Shadows: plausible penumbra and contact; no floating vegetation or furniture.
- Glass: retain interior visibility and sky reflection; avoid mirror-black glazing.
- Exposure: one matched exposure for the `SHOT-04` technical/material pair; shot-specific exposure only after registration approval.

At the end of Phase 02, numeric energy, color-management, bounce, sampling, and denoising settings remained pending because Blender had not yet been verified.

## Phase 04U implementation

Blender 4.5.17 LTS is now available through the verified portable executable. Generator 1.0.2 creates twelve original procedural materials: the six registered architectural targets plus interior flooring, upholstery, pool water, landscape ground, foliage, and a light-diffuser support material. All use Blender procedural nodes or constant physical parameters; no textures, HDRIs, models, or add-ons were downloaded.

Cycles CPU uses adaptive sampling, CPU denoising, four maximum bounces, two diffuse/glossy bounces, and four transmission/transparent bounces. Lighting consists of one warm late-afternoon sun, a procedural world, and four restrained warm area practicals. R3 shows stable shadows and spatial depth, but the material/lighting system remains illustrative: concrete and timber lack production-level microvariation, glass is too visually dominant in interior views, and the procedural world lacks photographic atmospheric depth. These are visual-gate blockers, not hidden passes.

## Phase 04V refinements

Concrete adds separate broad variation and fine 115-scale pore relief. Timber adds object-coordinate directional grain; upholstery adds weave; glass uses neutral full transmission at 0.025 roughness; metal is darker. Lighting uses a Nishita sky, softer 1.55-energy sun and AgX exposure `-0.55`. R3 shows better depth and glass clarity, but timber is too graphic and the landscape remains illustrative.

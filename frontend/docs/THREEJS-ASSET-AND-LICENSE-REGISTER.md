# Three.js asset and licence register

| Item | Version | Licence | Use | Shipped to |
| --- | --- | --- | --- | --- |
| three | 0.187.0 | MIT | WebGL renderer, geometry, materials | Home Page lazy chunk only |
| three/examples/jsm/lines (Line2, LineMaterial, LineSegments2) | 0.187.0 | MIT (part of three) | Wide, progressively drawn lines | Same chunk |
| gsap + ScrollTrigger | 3.15.1 | GSAP Standard "no charge" licence (https://gsap.com/standard-license) — free, **not open source** | Scroll-linked progress | Home Page lazy chunks only |
| @types/three | 0.187.0 | MIT | Types (dev only) | Not shipped |
| House, parcel, landscaping model | — | Original procedural geometry written for BuildMind (`house-model-data.ts`) | 3D scene and SVG fallback | Home Page |
| Textures, HDR maps, GLB/GLTF models, DRACO decoder | — | **None used** | — | — |
| Fonts in canvas labels | Poppins via @fontsource/poppins 5.3.0 | SIL OFL 1.1 | Dimension and room labels | Already self-hosted |

GLTFLoader and DRACOLoader were not needed: the model is procedural, so there are no external model files to load, compress or license.

**Action for the Team Captain:** confirm that the GSAP Standard licence terms are acceptable for BuildMind. If not, GSAP can be replaced by a small native scroll-progress observer without changing the scene.

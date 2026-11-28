# BuildMind Cinematic V2 — Media Optimization Report

## Existing technical inventory

| Asset | Format | Bytes | Status |
| --- | --- | ---: | --- |
| Technical contact sheet | SVG | 3,270 | Review-only; not a website asset |
| Plan registration | SVG | 2,247 | Review-only; not a website asset |
| Massing registration | SVG | 2,101 | Review-only; not a website asset |
| Contact-sheet visual QA copy | PNG | 45,359 | Review-only; not photorealistic |

Run `node asset-production/cinematic-v2/scripts/validate-assets.mjs` for current technical-preview hashes.

## Phase 04U result

No production WebP, AVIF, JPEG, mobile crop, reduced-motion poster, or public website asset was created. The visual-quality gate stopped production after R3 because the original procedural scene remains illustrative rather than premium-photorealistic.

The largest successful evaluation files are:

| File | Dimensions | PNG bytes | SHA-256 |
| --- | ---: | ---: | --- |
| `R3-SHOT-05-960x540.png` | 960×540 | 579,253 | `7e789a351759e14f9d3d63276fc6099595db6e9e6fd2cb3879f786b0da93a194` |
| `R3-SHOT-08-960x540.png` | 960×540 | 589,868 | `5963c29c2eb84ba13c3962d2e41e0365bbc01e0c2f5c2ce830484485842acbdb` |

These are review masters outside the Angular repository, not integration candidates. Consequently, the hero, deferred desktop, interior, transition, and mobile media budgets remain unverified rather than passed.

## Production targets retained for the next approved render pass

- Initial hero poster: ≤250 KB.
- Deferred exterior: ≤1.35 MB.
- Deferred interior: ≤1.0 MB.
- Additional transition/poster: ≤150 KB each.
- Deferred desktop total: ≤2.5 MB.
- Mobile cinematic total: ≤1.2 MB.
- Optional compressed GLB: ≤3 MB.

Prefer optimized still transitions over video unless a short camera move adds clear spatial information. Export AVIF first with WebP fallback only after approved rendered masters exist and browser-visible quality is compared. Use a still for mobile and reduced motion; do not generate dense image sequences.

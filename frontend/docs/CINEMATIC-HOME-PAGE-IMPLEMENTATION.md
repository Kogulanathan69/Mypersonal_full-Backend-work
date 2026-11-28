# Cinematic Home Page — implementation

Owner: **A — Customer Portal** (C01). Status: **COMPLETE_AND_VERIFIED** in Chrome 153 (software WebGL) on 2026-11-25.

## What it is

A scroll-driven 3D story of an **illustrative** tropical-modern house: land → blueprint → structure → materials → finished home. The model is pre-designed marketing content; it is not generated from any customer plan, and the page says so in the scene and in the FAQ.

## Files

| File | Purpose |
| --- | --- |
| `src/app/features/public/home/home-page.ts` | Page: story + 13 content sections |
| `.../cinematic/house-model-data.ts` | Plain-data parcel, rooms, walls, openings, slabs, landscaping (metres) |
| `.../cinematic/timeline.ts` | Pure scene ranges, sub-stages, easing, camera path (unit-tested) |
| `.../cinematic/capability.ts` | WebGL / reduced-motion / data-saver / memory decision and saved preference |
| `.../cinematic/cinematic-story.ts` | Hero, sticky canvas, captions, chapter navigation, fallbacks, cleanup |
| `.../cinematic/engine/cinematic-engine.ts` | **Lazy chunk**: Three.js scene, GSAP ScrollTrigger binding, disposal |
| `.../cinematic/static-house-illustration.ts` | SVG isometric drawing of the same model for every stage (fallback) |
| `.../sections/*.ts` | How it works, survey, land/CRS, regulation evidence, generation, alternatives, editor, AI editing, architect review, benefits, FAQ, CTA |

## Sequence (scroll progress → scene)

| Progress | Scene | What happens |
| --- | --- | --- |
| 0–6% | Hero | Grid and glowing parcel draw in on load (2.4 s intro), hero text and CTAs |
| 0–20% | 01 The land | Corner points, offset dimension lines with edge lengths, buildable-area dashed line |
| 20–40% | 02 Intelligent blueprint | Room outlines draw progressively, room names, overall dimensions, door swings and window marks |
| 40–62% | 03 Blueprint to structure | Ground walls then upper floor rise with cyan edges; frames, columns, roof slabs form |
| 62–82% | 04 Transformation | Cyan ghost → concrete, timber, glass; light shifts from technical cyan to warm; landscaping grows |
| 82–100% | 05 Your vision | Interior glow, slow orbit, dusk sky; final call to action follows the story |

Scroll drives a GSAP tween with `scrub: 0.7`; there is no pinning, snapping or wheel interception, so native scrolling is never hijacked. Scrolling up reverses every step because the scene is a pure function of progress.

## Performance

- Three.js (622 kB raw / 131 kB transfer) and GSAP (72 + 44 kB raw) load only on the Home Page and only in WebGL mode. Initial bundle: 372 kB raw / 95.5 kB transfer.
- Device pixel ratio capped at 1.75 (1.25 on phones/low power), lowered automatically to 0.75 when frames average over 24 ms.
- Shadows only on capable desktops. Rendering stops when the story is off screen or the tab is hidden.
- All geometries, materials, textures and the WebGL context are disposed when leaving the page (verified: `activeEngines` returns to 0, no canvas remains).

## Accessibility and fallbacks

- Static SVG story (same model, five stages) for: reduced motion, no WebGL, data saver, ≤2 GB memory, engine load failure, WebGL context loss, or the visitor's own "Turn animation off" choice (remembered).
- A visitor who prefers reduced motion can still opt in with "Turn 3D animation on".
- Canvas is `aria-hidden`; chapter captions are real headings in an ordered list; chapter navigation buttons have names and `aria-current`.
- Hero controls become `inert` when faded out, so hidden buttons cannot be focused.

## Verification (2026-11-25)

`checks/home-check.mjs` — 27/27 pass: canvas start, forward progress through all scenes, final caption, reverse scroll, hero return, disposal on navigation, no console errors, recording captured, six widths (1920/1440/1024/768/390/360) without overflow or errors, reduced motion, no Three.js download in reduced motion, opt-in, opt-out + persistence, no-WebGL fallback, engine-load failure fallback, CTA routing, section anchor, skip link.
`checks/axe-home.mjs` — 0 serious/critical violations in 3D and static modes at 1280 and 390 px.
Evidence: `evidence/phase02/` (stage screenshots, contact sheet, 44 s MP4 recording).

Limitation: verification used Chrome's software renderer (SwiftShader), so captures are at reduced resolution and frame rate. Test on real GPUs (desktop, laptop, phone) before release.

# BuildMind Cinematic V2 — Technical Architecture

## Decision

Use a hybrid of real-time Three.js for technical stages, pre-rendered responsive media for photoreal exterior/interior stages, and semantic Angular HTML/SVG for the editorial product experience.

## Preserve

- Existing procedural parcel and building data
- One WebGL renderer/context
- Lazy dynamic imports
- Capability policy and saved preference
- DPR cap and adaptive quality
- intersection/visibility pausing
- context-loss fallback
- deterministic disposal
- static SVG story
- native document scrolling

## Proposed Phase 03 boundaries

- `CinematicStory`: orchestration and accessible narrative; no rendering internals.
- `CinematicTimelineService`: authoritative normalized progress and scene mapping.
- `CinematicQualityService`: capability, media-query and observed-frame policy.
- `TechnicalSceneEngine`: existing Three.js engine, narrowed to Scenes 01–04.
- `RenderedScene`: responsive image/video stage for Scenes 05–06.
- `EditorialTransition`: HTML/SVG Scene 07.
- Product section components: Scene 08 and normal flow.

No service may create a second WebGL context. Media stages must not be uploaded as giant canvas textures when native `<picture>` or `<video>` is sufficient.

## Loading order

1. HTML, CSS, hero copy and Scene 01 SVG poster.
2. Capability check after first render.
3. Lazy-load Three.js engine only for eligible devices.
4. Preload the first exterior poster when progress approaches 0.35 or during idle time on adequate networks.
5. Load motion media only when its scene is near and data saver is off.
6. Dispose/stop the real-time engine after Scene 07 when returning is unlikely; retain a deterministic recreation path for reverse navigation.

## GSAP 3.15.1

The current GSAP Standard “No Charge” licence, effective 30 April 2025 and last modified 30 May 2025, permits commercial use on websites and web applications. It prohibits use in products that provide no-code visual animation building capabilities competing with Webflow, reverse engineering for such products, and removal of proprietary notices. BuildMind is a home-planning product, not an animation builder, so the intended scroll-timeline use is a permitted use based on the published terms. GSAP remains proprietary and legal/product owners should record acceptance.

### Retain GSAP

- Existing integration is proven and compact when deferred.
- ScrollTrigger provides reversible scrub, refresh and cleanup.
- Lower Phase 03 regression risk.
- Ongoing proprietary-licence review remains necessary.

### Native scroll progress

- Removes proprietary motion dependency and roughly 42 KB estimated deferred transfer from GSAP/ScrollTrigger chunks.
- Requires custom measurement, resize, route, browser and accessibility testing.
- Risks recreating scrub smoothing and lifecycle behavior incorrectly.

**Recommendation:** retain pinned GSAP 3.15.1 for Phase 03, record the licence, use only GSAP core and ScrollTrigger already installed, and reconsider native replacement only after the hybrid timeline is stable. Do not upgrade or add plugins.

## Accessibility

- Canvas remains `aria-hidden`.
- Every scene has a real heading and concise text in DOM order.
- Chapter controls expose names and current state.
- Hidden hero controls become inert before opacity reaches zero.
- Reduced-motion content is visible without animation and follows normal flow.
- User preference cannot make essential content canvas-only.
- Product demonstrations retain keyboard and touch alternatives.

## Security and product truth

- No external model runtime, production API, analytics or backend connection is introduced.
- Media contains no secrets or customer data.
- All visual assets require provenance before inclusion.
- Maintain the existing “preliminary 2D plan” and professional-review statements.
- CTA must not imply functioning registration while `AUTH_API` remains unavailable.


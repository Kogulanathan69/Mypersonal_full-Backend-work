# BuildMind Cinematic V2 — Eight-Scene Storyboard

The ranges below describe one authoritative normalized timeline. Exact scroll distance is tuned during browser QA; progress is never derived from arbitrary per-component observers.

## 01 — Land Intelligence · 0.00–0.14

- **Composition:** parcel occupies the right two-thirds; category, premise and two truthful actions align left.
- **Architecture:** existing parcel boundary, corner points, setbacks, bearings and dimensions.
- **Camera:** high oblique survey view, 35–40° downward pitch; slow 3% dolly-in only.
- **Timing:** 1.2-second load introduction, then scroll-controlled.
- **Transition:** boundary becomes the registration plane for the blueprint.
- **Text:** “Preliminary home planning begins with your land.” Supporting line explains survey extraction and review.
- **Assets:** existing procedural data and SVG fallback.
- **Desktop:** real-time Three.js with labels in semantic HTML where possible.
- **Mobile:** static SVG plus a short boundary-draw animation when motion is allowed.
- **Reduced motion:** complete parcel still and normal-flow text.
- **Performance:** no textures; cap DPR and pause when hidden.

## 02 — Blueprint Formation · 0.14–0.29

- **Composition:** camera settles orthographic-like above the parcel; room and circulation lines form within the same boundary.
- **Architecture:** same room plan, openings, door swings and overall dimensions as the existing data model.
- **Camera:** near-plan view; no orbit during drawing.
- **Timing:** major perimeter first, internal rooms second, openings and labels last.
- **Transition:** selected lines gain depth and become structural axes.
- **Text:** “A plan takes shape inside the constraints.”
- **Assets:** existing line geometry; approved drafting labels.
- **Desktop:** real-time progressive lines.
- **Mobile:** staged SVG illustrations.
- **Reduced motion:** completed blueprint with a short textual sequence.
- **Performance:** avoid per-frame geometry allocation; update draw ranges/material uniforms.

## 03 — Architectural Wireframe · 0.29–0.44

- **Composition:** the plan remains registered while ground walls, upper elements and roof rise.
- **Architecture:** one contemporary tropical residence; openings and slab edges align with Scene 02.
- **Camera:** lower to a three-quarter eye-level view while geometry extrudes; movement finishes before detail appears.
- **Timing:** ground walls, frames, upper volume, roof, then openings.
- **Transition:** cyan edges remain as an alignment overlay while solid neutral surfaces enter.
- **Text:** “From drawing to spatial proportion.”
- **Assets:** existing procedural mesh initially; Phase 03 alignment model if approved.
- **Desktop:** real-time Three.js.
- **Mobile:** three still stages or low-cost transform animation.
- **Reduced motion:** one complete wireframe poster.
- **Performance:** reuse materials and meshes; one context; shadows off.

## 04 — Material Transformation · 0.44–0.57

- **Composition:** technical model occupies the frame; UI recedes.
- **Architecture:** concrete planes, timber screens, glass openings and stone base map exactly to the wireframe.
- **Camera:** locked registration camera for the handoff.
- **Timing:** 0.8–1.4 seconds perceived transition; controlled by progress and reversible.
- **Transition:** real-time technical render crossfades through a matched matte into pre-rendered media. A/B frames must share camera, focal length and silhouette.
- **Text:** one short statement only: “Material makes the spatial idea legible.”
- **Assets:** matched wireframe and rendered frames; material ID pass; fallback poster.
- **Desktop:** real-time-to-pre-render crossfade.
- **Mobile:** direct crossfade between optimized stills.
- **Reduced motion:** final material still.
- **Performance:** preload the first render near 35% progress; do not upload full-resolution media to WebGL.

## 05 — Photorealistic Exterior · 0.57–0.70

- **Composition:** dusk exterior, verandah and living spaces oriented to landscape; house remains the same silhouette.
- **Architecture:** two-storey tropical-modern residence, shaded glazing, deep eaves and screened upper volume.
- **Camera:** 28–35mm equivalent at human eye height; subtle lateral move, not an orbit.
- **Timing:** 3–5 second media movement across its scroll range.
- **Transition:** warm aperture light becomes the visual bridge into the interior.
- **Text:** explicit label: “Illustrative architectural study.” No claim that BuildMind generated this customer home.
- **Assets:** one hero render/video, desktop and mobile crops, AVIF/WebP poster.
- **Desktop:** short high-quality pre-render or image sequence.
- **Mobile:** poster or two-frame dissolve by default.
- **Reduced motion:** single exterior poster.
- **Performance:** deferred desktop media ≤1.35 MB allocation; poster ≤250 KB.

## 06 — Interior Camera Journey · 0.70–0.80

- **Composition:** living area looking through shaded glazing to the same exterior landscape.
- **Architecture:** interior wall/opening positions correspond to the plan and exterior.
- **Camera:** 24–28mm equivalent, 1.5m eye height; one slow forward move no longer than 4 seconds.
- **Timing:** brief journey; no free-look controls.
- **Transition:** a plan line aligns with a join in the ivory editorial page.
- **Text:** “An illustrative view of the spatial intent.”
- **Assets:** matched interior render, mobile crop, still poster.
- **Desktop:** optimized pre-rendered clip or 3–5 frame sequence.
- **Mobile:** one still, optionally a subtle crop shift.
- **Reduced motion:** still with all content available below.
- **Performance:** interior allocation ≤1.0 MB; no autoplay audio.

## 07 — Dark-to-Ivory Editorial Transition · 0.80–0.87

- **Composition:** a sectional line or plan-paper edge travels through the frame; dark mineral gives way to editorial paper.
- **Architecture:** the line is derived from the house section, not decorative geometry.
- **Camera:** scene stops; transition occurs in page space.
- **Timing:** 400–700ms equivalent, interruptible.
- **Transition:** WebGL stops and may be disposed once the editorial region is safely entered.
- **Text:** “The cinematic study is illustrative. BuildMind’s supported experience is preliminary 2D planning.”
- **Assets:** HTML/SVG section cut.
- **Desktop/mobile:** semantic CSS/SVG, no additional canvas.
- **Reduced motion:** immediate color boundary.
- **Performance:** transform/clip/opacity only; reserve layout height.

## 08 — Interactive Product Experience · 0.87–1.00 and normal page flow

- **Composition:** warm editorial layouts show the real survey, evidence, alternatives, editor and consent concepts.
- **Architecture:** existing product demonstrations and claims only.
- **Camera:** none; standard document interaction.
- **Timing:** interaction-driven states only.
- **Transition:** closes with an honest invitation to explore, not a fake registration promise.
- **Text:** retain existing preliminary-plan and professional-review limitations.
- **Assets:** existing HTML/SVG demonstrations; no photoreal building required.
- **Desktop:** varied full-width product evidence and split editorial sections.
- **Mobile:** content-first single column with full-width controls.
- **Reduced motion:** identical content.
- **Performance:** defer below-fold demonstrations; no persistent render loop.


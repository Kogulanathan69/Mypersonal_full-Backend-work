# BuildMind Cinematic V2 — Design Direction

Status: locked for Phase 03 planning. Source application files remain unchanged.

## Idea

BuildMind begins as **Survey Nocturne**: measured land, coordinates and drafting lines emerge from darkness. It resolves into **Tropical Material Journal**: the same residence gains material, light and inhabitation before the page opens onto a warm editorial product experience.

The signature is not a generic building render. It is one continuous parcel-to-plan-to-place transformation whose geometry remains recognisably the same in every scene.

## Principles

1. The land is the origin of every visual decision.
2. Technical marks communicate real dimensions or state; they are never decoration.
3. One cinematic transformation carries the opening. Product evidence carries the rest.
4. Photoreal media is explicitly illustrative and never presented as customer-specific output.
5. Dark technical scenes end decisively; product content lives on editorial paper.
6. Avoid repeated rounded cards, glass panels, generic gradients and ambient glow.
7. Preserve all authentication, API and authenticated-portal design boundaries.

## Palette

| Role            | Value     | Use                                           |
| --------------- | --------- | --------------------------------------------- |
| Survey black    | `#071015` | Cinematic canvas and deepest shadow           |
| Deep mineral    | `#10232A` | Technical surfaces and atmosphere             |
| Blueprint cyan  | `#65D9DF` | Selected survey and drafting information only |
| Limestone       | `#D9D2C5` | Architectural material and quiet neutral      |
| Warm aperture   | `#F3B56A` | Interior light and single warm signal         |
| Editorial paper | `#F4F0E7` | Product-story canvas                          |

Proposed editorial ink: `#172124`; proposed secondary ink: `#55605F`. These are Phase 03 candidates and must pass the existing contrast checker before adoption. Authenticated application tokens are out of scope.

## Typography decision

Poppins remains installed and active until Phase 03 explicitly approves a font change.

Candidates:

- **Manrope** for display and navigation. Its open counters, tabular figures and restrained geometry suit measurements without reading like a drafting cliché. Variable weights 400–700 are sufficient.
- **Source Sans 3** for product prose, controls and longer explanations. It is designed for UI environments and remains highly legible at compact sizes.

Both are licensed under SIL Open Font License 1.1 and may be embedded and redistributed with software provided the copyright and licence notices accompany the font files. Do not rename a modified font with a Reserved Font Name without permission. Phase 03 should self-host only approved WOFF2 subsets and retain full licence texts.

Recommended pairing: Manrope 500/600 for display; Source Sans 3 400/600 for body and UI. Use one tabular-number feature rather than a decorative monospace face. If loading and visual tests do not show a material improvement, retain Poppins.

## Type scale

- Hero: `clamp(3rem, 7vw, 7.5rem)`, 0.92–0.98 line height
- Section statement: `clamp(2.25rem, 4.5vw, 5rem)`
- Product heading: `clamp(1.75rem, 3vw, 3.25rem)`
- Lead: `clamp(1.0625rem, 1.4vw, 1.375rem)`
- Body: 1rem minimum, 1.55–1.7 line height
- Measurement/metadata: 0.8125–0.875rem, tabular figures

## Composition

- Dark sequence: full-viewport stage, left-aligned narrative, geometry occupying the opposite two-thirds.
- Material reveal: visual takes the complete frame; text withdraws rather than floating over every shot.
- Editorial transition: an architectural section cut becomes the edge between dark mineral and paper.
- Product sections: large demonstrations, alternating measures and varied compositions; never a grid of identical feature cards.
- Container: one shared maximum width and aligned left/right edges.

## Motion language

- Technical motion draws, measures, extrudes and resolves.
- Material motion changes light, roughness and atmosphere; it does not scatter particles.
- Editorial motion is quiet: one paper reveal, then interaction-led transitions only.
- Native scrolling is authoritative. No wheel capture, snapping or scroll-jacking.
- Reduced motion shows complete still stages in normal document flow.

## Reference use

- SpaceX: scale and single-subject screens.
- Tesla: restraint and viewport composition.
- Runway: visual storytelling and interface retreat.
- Sanity: technical/editorial hierarchy.
- WIRED: information rhythm and long-form clarity.
- Linear: precision and product evidence.

No reference branding, assets, code, fonts, copy or signature trade dress may be copied.

## CTA language

Until authentication is integrated, avoid “Start designing” and “Create your project.” Recommended primary CTA: **Explore the planning experience** linking to the supported product explanation on the same page. Recommended secondary CTA: **See how BuildMind works**. Account links may remain in navigation but must present the existing truthful unavailable state.

# Approved Warm Minimal Architectural Direction (2026-11-27)

The public website now uses the Team Captain's approved warm minimal direction as its primary visual
system: warm ivory (`#F3EEE7`), charcoal (`#222321`), soft gold (`#E8B66A`), blueprint blue
(`#83C5E5`) and natural timber (`#A38B72`). The hero is a stable editorial split with concise content
on the left and a bounded cinematic architectural stage on the right. Dark surfaces are reserved for
navigation, footer and deliberate contrast; the normal document flow is warm, spacious and daylight-led.

The exact hero message is “Plan your home with confidence.” Architectural animation remains supporting
evidence rather than a background that competes with the product promise. Mobile defaults to the semantic,
lightweight illustrated sequence. The residence remains explicitly illustrative and is not presented as a
customer-specific or photorealistic generated design.

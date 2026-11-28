# Warm Minimal Redesign Verification

Verified on 27 November 2026 against the production build on branch `cinematic-v2`.

## Implemented direction

- Replaced the oversized dark hero with a warm ivory editorial split composition.
- Applied the approved charcoal, ivory, gold, blueprint-blue and timber palette.
- Kept the headline, calls to action, navigation, disclosure and animation control readable throughout the cinematic sequence.
- Preserved the eight-scene Three.js and GSAP journey while reducing grid dominance and framing the visualization as a bounded architectural study.
- Added a lightweight static architectural sequence on narrow screens and retained reduced-motion, animation-off, missing-media and no-WebGL fallbacks.
- Extended the warm minimal surface treatment through the public page while preserving routes, FAQ, calls to action and footer content.

## Automated evidence

- `npm run verify`: passed.
- TypeScript compilation: passed.
- Angular lint: passed.
- Design token scan: 0 raw colour violations across 162 files.
- WCAG contrast check: 56/56 pairings passed.
- Unit tests: 222/222 passed across 29 files.
- Production build: passed; initial bundle 390.38 kB raw / 99.49 kB estimated transfer.
- Lazy cinematic engine: 624.13 kB raw / 131.17 kB estimated transfer.
- Mock build: passed.
- Playwright cinematic suite: 4/4 passed serially against the production build.

## Visual evidence

The `evidence` directory contains the regenerated production captures for:

- all eight cinematic scenes;
- 1024, 1280, 1440 and 1920 pixel desktop layouts;
- 360, 390 and 768 pixel responsive layouts;
- mobile navigation;
- reduced motion;
- the cinematic contact sheet.

Manual review confirmed that the headline and model no longer overlap, the navigation clears the introductory content, scroll guidance remains visible, blueprint lines remain legible on ivory, and the mobile flow remains readable without relying on WebGL animation.

## Known visual limitation

The residence remains an illustrative, procedurally rendered architectural model rather than a photoreal commissioned property visualization. Its disclosure remains visible. The implementation uses the existing approved assets and dependency set; no new runtime package was added.

## Local preview

Run `npm start`, or run the production preview command used by Playwright, and open `http://127.0.0.1:4300`.

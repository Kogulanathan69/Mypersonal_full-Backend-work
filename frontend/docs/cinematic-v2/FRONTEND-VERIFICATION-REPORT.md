# Frontend Verification Report

Date: 2026-11-27. Branch: `cinematic-v2`.

## Implemented surface

- Eight deterministic scenes: land, blueprint, wireframe, material threshold, exterior, interior,
  ivory transition and product experience.
- One lazy Three.js context with cleanup, visibility pausing, context-loss fallback, reduced-motion,
  low-power and explicit animation preferences.
- Same-residence responsive media with honest illustrative labelling.
- Existing survey, boundary, regulation, alternatives, editor, natural-language, architect-review,
  benefits, FAQ, CTA, footer, public pages and authentication entry routes retained.

## Automated browser evidence

Existing Playwright 1.65.1 used installed system Chrome in an isolated context. No browser download,
profile attachment or personal tab access occurred. Checks passed at 360, 390, 768, 1024, 1280,
1440 and 1920 pixels: eight chapter jumps, native scrolling, no horizontal overflow, navigation,
public/auth routes, mobile drawer, keyboard skip link, reduced motion, animation-off, no-WebGL and
missing-media behavior. No console errors, uncaught exceptions, failed requests or route failures
were observed in the passing run.

Evidence is under `docs/cinematic-v2/evidence/`: seven full-page responsive captures, eight scene
captures, mobile navigation and reduced-motion captures.

## Camera calibration

`node tools/calibrate-shot04.mjs` independently projects 12 distributed landmarks through the
source/Three.js and source-to-Blender coordinate systems at 640×360. Vertical FOV is
32.2688021711° for the 35 mm horizontal-sensor camera. Mathematical RMS deviation is
3.8701213228781695e-14 px; maximum is 1.1457157353758233e-13 px; normalized RMS is
5.270473548007362e-17. These pass the numeric matrix target. This is not image-derived silhouette
evidence, so rendered pixel alignment remains unclaimed and Scene 04 uses a coherent transition.

## Performance evidence

Production build estimate: initial 386.60 kB raw / 99.04 kB transferred; lazy cinematic engine
622.91 kB raw / 130.69 kB transferred. Active desktop cinematic media totals 1,338,372 bytes;
the mobile exterior plus reduced-motion interior totals 511,223 bytes. These meet the stated media
budgets. Core Web Vitals remain laboratory targets; no field-RUM claim is made.

## Conditions

The original v2 residence is visually coherent but illustrative, not photographically accepted.
No sustained high-load render was launched. Backend contracts, deployment configuration and field
performance require their separate environments.

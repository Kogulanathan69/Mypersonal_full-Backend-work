# BuildMind Cinematic V2 — Phase 03 Implementation Roadmap

Every step requires a reviewable commit and must leave the production build, typecheck, lint and unit suite passing. Do not change backend contracts or authenticated-portal styling.

## 0 — Clear baseline condition

- Correct the Windows-only path construction in `tools/check-tokens.mjs`.
- Add a cross-platform regression test or deterministic path fixture.
- Acceptance: token check passes without weakening its rules.

## 1 — Marketing design tokens and typography proof

- Add only scoped cinematic/editorial marketing roles.
- Produce Poppins, Manrope/Source Sans 3 comparison screens without changing portal tokens.
- Include OFL notices before shipping font files.
- Acceptance: design approval, contrast pass, no authenticated-route screenshot changes.

## 2 — Timeline and orchestration seam

- Introduce one eight-scene timeline abstraction with pure tests.
- Preserve current five-scene output until parity is established.
- Acceptance: forward/reverse progress, chapter mapping and cleanup tests pass.

## 3 — Technical renderer refinement

- Narrow existing engine to land, blueprint, wireframe and handoff.
- Preserve one context, DPR policy, pause/resume, context-loss and disposal.
- Acceptance: visual parity at existing stages plus approved registration frame.

## 4 — Hybrid media shell

- Add responsive poster/media component with error fallback and preload policy.
- Use temporary locally authored neutral test plates only if separately approved; do not commit unlicensed assets.
- Acceptance: no layout shift, data-saver/reduced-motion fallbacks, keyboard-neutral behavior.

## 5 — Approved residence assets

- Integrate only assets listed as approved in `ASSET-LICENSE-REGISTER.md`.
- Verify exact camera alignment between wireframe and render.
- Acceptance: exterior/interior continuity review, hashes and licences recorded, media budgets pass.

## 6 — Editorial transition and product sections

- Implement dark-to-paper transition in HTML/SVG.
- Recompose existing survey, regulation, alternatives, editor and architect content without new capabilities.
- Replace operational CTA claims with approved truthful wording.
- Acceptance: content/product review and unchanged API/auth boundaries.

## 7 — Responsive and accessibility completion

- Build intentional mobile, landscape and tablet flows.
- Test keyboard, focus, 200/400% zoom, reduced motion, forced colors and screen readers.
- Acceptance: no serious/critical Axe findings; all essential content without WebGL; ≥44px intended touch targets.

## 8 — Performance optimization

- Tune preload thresholds, media formats, quality tiers and lifecycle.
- Measure real devices and browsers.
- Acceptance: agreed LCP/INP/CLS, media, JS, frame-time and context budgets.

## 9 — Playwright visual QA

- Add project tests at 360, 390, 768, 1024, 1440 and 1920.
- Cover static, WebGL-capable, reduced-motion, save-data, media-error and context-loss states where browser support permits.
- Use existing Playwright 1.65.1 and installed browsers only.
- Acceptance: reviewed snapshots and interaction suite pass without upgrades/downloads.

## 10 — Acceptance

- Product truth, legal/licensing, design, accessibility, performance and security sign-off.
- Tag a local release candidate only after approval; no remote or publication without separate authorization.


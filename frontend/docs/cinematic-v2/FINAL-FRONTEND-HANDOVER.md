# BuildMind Cinematic Website V2 — Frontend Handover

The final public visual layer follows the approved Warm Minimal Architectural direction. The cinematic
engine is scoped to the right-side hero stage on desktop, while the headline, actions and navigation remain
stable and readable. Mobile uses the verified lightweight static sequence by default; users are not forced
into desktop-style WebGL rendering.

## Run and build

- Development/mock preview: `npm.cmd run start:mock -- --host 127.0.0.1 --port 4300`
- Verified production-directory preview: build first, then `node tools/serve-dist.mjs`
- Local URL: `http://127.0.0.1:4300/`
- Production build: `npm.cmd run build`
- Output: `dist/buildmind-frontend/browser`

## Architecture

Angular retains public, authentication and protected-portal route boundaries. The home route lazy-loads
Three.js/GSAP only after capability checks. A pure eight-scene contract drives captions and media, while
the existing procedural engine serves the first four technical scenes. Scenes 05–06 use bounded original
BM-CV2-RES-001 derivatives; Scenes 07–08 transition to semantic document content. Static content remains
the authoritative fallback.

## Rollback and protection

Starting checkpoint is local commit `3402d61`; Phase 04U and 04V remain `90b117e` and `3402d61`.
No remote was configured or pushed. `package.json`, `package-lock.json`, backend source, guards,
interceptors, runtime configuration and security settings were not changed. The protected original
manifest hash remains `4884637e9f101dcd2a46688dce815241e543452f229e9b0883b91c2d140e7c4a`
with 25,680 manifest entries and 25,680 current files.

## Deployment prerequisites and limitations

Serve the Angular SPA with history fallback, correct immutable-cache headers for hashed assets and the
environment-specific runtime configuration. Validate real backend contracts in the target environment.
The residence media are original architectural illustrations, not customer-specific output and not
photorealistic acceptance assets. No claim of field Core Web Vitals or organizational GSAP licence
acceptance is made.

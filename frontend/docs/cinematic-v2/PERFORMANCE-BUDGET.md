# BuildMind Cinematic V2 — Performance Budget

## Baseline measured in Phase 02

Production build succeeded on 2026-11-25.

| Artifact | Raw | Angular estimated transfer |
| --- | ---: | ---: |
| Initial JavaScript | 328.11 KB | 89.49 KB |
| Global CSS | 56.17 KB | 9.04 KB |
| Initial total | 384.28 KB | 98.53 KB |
| Three.js cinematic engine chunk | 622.58 KB | 130.74 KB |
| GSAP core/index chunk | 72.03 KB | 25.34 KB |
| ScrollTrigger chunk | 44.39 KB | 16.54 KB |
| Home-page chunk | 48.74 KB | 13.51 KB |

The three named cinematic/motion chunks total approximately 172.62 KB estimated transfer. Exact browser gzip/Brotli transfer and cache behavior were not measured. LCP, INP, CLS, FPS, GPU memory and mobile media do not yet have runtime measurements.

## Acceptance targets

| Metric | Target | Status |
| --- | --- | --- |
| LCP p75 | ≤2.5 s | Unmeasured |
| INP p75 | ≤200 ms | Unmeasured |
| CLS p75 | ≤0.05 | Unmeasured |
| Desktop animation | 55–60 FPS; no sustained >24ms frames | Unmeasured |
| Mobile animation | ≥30 FPS when enabled | Unmeasured |
| Initial JS transfer | ≤250 KB gzip/estimated transfer | Baseline estimate 89.49 KB; passes provisionally |
| Deferred cinematic JS | ≤250 KB gzip/estimated transfer | Named baseline estimate 172.62 KB; passes provisionally |
| Mobile cinematic media | ≤1.2 MB | No assets yet |
| Deferred desktop media | ≤2.5 MB | No assets yet |
| Active WebGL contexts | 1 | Existing architecture designed for one; runtime recheck required |

No target requires revision yet. The desktop media target must be allocated before production: exterior ≤1.35 MB, interior ≤1.0 MB, remaining posters/transition ≤150 KB beyond the initial hero poster.

## Quality tiers

- **Static:** reduced motion, save-data, no WebGL, ≤2 GB memory or context loss.
- **Low:** phone/low-power; DPR ≤1.25, no shadows, posters by default.
- **Standard:** laptop/tablet; DPR ≤1.5, limited shadows, pre-rendered photoreal stages.
- **High:** capable desktop; DPR ≤1.75, soft shadows only in technical scene, full desktop media.

## Validation gates

1. Production build budgets and chunk diff.
2. Lighthouse/Web Vitals under mobile throttling.
3. Performance trace through complete forward and reverse timeline.
4. Frame-time sampling on representative integrated GPU and mid-range phone.
5. GPU/context count and route-away disposal.
6. Data-saver, reduced-motion, context-loss and media-error fallbacks.
7. Visual stability at 360, 390, 768, 1024, 1440 and 1920.


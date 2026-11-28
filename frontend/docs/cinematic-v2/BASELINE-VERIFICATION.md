# BuildMind Cinematic V2 — Baseline Verification

Date: 2026-11-25. Working copy only; no dependency changes.

## Protection

- Original: `C:\Users\kogul\Downloads\buildmind-frontend-checkpoint-2026-11-25\frontend`
- Original files: 25,680
- Original bytes: 388,191,231
- Original/backup manifest SHA-256: `4884637e9f101dcd2a46688dce815241e543452f229e9b0883b91c2d140e7c4a`
- Backup: `C:\Users\kogul\Downloads\buildmind-frontend-checkpoint-2026-11-25\_backups\frontend-2026-11-25-cinematic-v2-baseline`
- Backup verification: 25,680 entries, zero manifest differences
- Working copy: `C:\Users\kogul\Downloads\buildmind-frontend-checkpoint-2026-11-25\frontend-cinematic-v2`
- Baseline commit: `b7c8187893896ef3f8fe3f00f685ac3e215e0e74`
- Development branch: `cinematic-v2`
- Git remote: none configured

## Commands and results

| Command | Exit | Result |
| --- | ---: | --- |
| `npm.cmd run build` | 0 | Production build succeeded in 30.035 s |
| `npm.cmd run typecheck` | 0 | App and spec TypeScript configurations passed |
| `npm.cmd run lint` | 0 | All files passed Angular ESLint |
| `npm.cmd run check:tokens` | 1 | Existing Windows path defect: attempted `C:\C:\...\src\`; no source scan occurred |
| `npm.cmd run check:contrast` | 0 | 56/56 defined pairings passed WCAG 2.2 AA thresholds |
| `npm.cmd run test:ci` | 0 | 27/27 files and 202/202 tests passed |

Coverage: statements 78.50%, branches 84.50%, functions 69.69%, lines 75.14%.

Vitest emitted jsdom warnings that `HTMLCanvasElement.getContext()` is not implemented without an optional canvas package. Tests intentionally selected static fallback and still passed. No package was installed.

## Condition

Fix and separately verify the Windows path construction in `tools/check-tokens.mjs` before Angular source implementation. Phase 02 does not authorize that source/tool modification.


# BuildMind frontend — development progress

Owner: F — Foundation. Updated at every checkpoint so work can resume after any interruption.

## Workspace

- Location (cloud workspace, no git): `/home/claude/buildmind-work/frontend`
- Evidence: `/home/claude/buildmind-work/evidence/` (logs, screenshots)
- Ad-hoc browser checks (not shipped): `/home/claude/buildmind-work/checks/`
- Node 22.23.5 in this workspace (inside Angular 22's range); team target Node 26 LTS
- Git: **none**. No commits, branches, pushes, PRs or deployments have been made.

## Phase status

| Phase | Status | Notes |
| --- | --- | --- |
| 01 Foundation | COMPLETE_AND_VERIFIED (2026-11-25) | See checkpoint below |
| 02 Cinematic Home Page | COMPLETE_AND_VERIFIED (2026-11-25) | 27/27 browser checks, 0 axe serious/critical, 161 unit tests; see CINEMATIC-HOME-PAGE-IMPLEMENTATION.md |
| 03 Public website and authentication | Public pages COMPLETE_AND_VERIFIED; auth screens IMPLEMENTED_AWAITING_BACKEND | 202 unit tests; 53/53 browser checks (13 pages × 4 widths, axe, sign-in flow) |
| 04–16 | BLOCKED_EXTERNAL | Backend contracts at `C:\Users\kogul\...\captain-full-backend-completion` are on the Captain's Windows PC and not reachable from this cloud workspace. Needs OpenAPI export + docs uploaded, or the desktop app folder link. |

## Phase 01 checkpoint (2026-11-25, Node 22.23.5)

| Check | Command | Result |
| --- | --- | --- |
| TypeScript | `npm run typecheck` | exit 0 |
| ESLint | `npm run lint` | All files pass |
| Prettier | `npm run format:check` | All files formatted |
| Tokens | `npm run check:tokens` | 0 raw colours outside tokens.css (120 files) |
| Contrast | `npm run check:contrast` | 43/43 WCAG 2.2 AA pairings pass |
| Unit/component tests | `npm run test:ci` | 20 files, 135/135 pass; lines 95.9%, statements 94.4% |
| Production build | `npm run build` | pass; initial 350.62 kB raw / 92.23 kB transfer (budget warn 500 kB) |
| Mock build | `npm run build:mock` | pass |
| Audit | `npm audit` | 0 vulnerabilities |
| Dev-route exclusion | grep of production dist | no dev route code; only placeholder link text (guarded, not rendered) |
| Browser a11y (axe, Chrome 153) | `checks/a11y-check.mjs` | 0 serious/critical on 9 routes × 2 widths |
| Keyboard | same script | skip link, dialog focus trap/restore, form errors verified |
| Responsive | `tools/capture-screens.mjs` | 7 routes × 360/768/1024/1280: no overflow, no console errors |

### Defects found and fixed during close-out

1. Customer header overflowed by 64 px at 360 px (button `inline-flex` beat `hidden`) → `max-sm:hidden`.
2. Admin fixed sidebar covered the demo-data banner → sticky sidebar in flex layout.
3. Account-menu button had no accessible name below 640 px → explicit `aria-label`.
4. Inline links relied on colour only → underlined by default in base.css.
5. Interceptor order meant errors lost the correlation id → correlation-id interceptor moved outside the error interceptor.
6. Redaction removed text after a secret query parameter → value pattern stops at `&`.
7. Global error handler checked the wrapper instead of the unwrapped error → fixed.
8. Initial bundle grew to 411 kB → session-expired dialog lazy-loaded and CDK LiveAnnouncer replaced by own live regions → 350.6 kB.

### Known differences from the earlier "293 kB" figure

293 kB was measured before the global shell (toasts, banners, progress bar, title strategy, icons) existed. The remaining initial code is Angular core/router (~280 kB) plus that shell, which every page needs.

## Exact next action

Obtain the Captain backend OpenAPI export and integration guides, fill FRONTEND-BACKEND-CONTRACT-MATRIX.md, replace `UnavailableAuthApi` with the real implementation, then build Phase 04 (customer dashboard and projects).

## External dependencies

- Backend OpenAPI + integration guides + permission matrix + lifecycle docs (Captain).
- A backend URL reachable from the test machine for live E2E.
- Confirmation of brand font (Poppins provisional).

# BuildMind frontend final handover

Date: 2026-11-28  
Branch: `cinematic-v2`  
Backend contract HEAD: `25309cd82417fb1e821a09f4e9fbc6084dfc0b2f`

## Delivered application

- Protected static architectural public site and responsive product demonstrations.
- Registration, sign-in, recovery, verification, session rotation, logout and profile/security UI.
- Customer projects, survey intake/evidence, land interpretation, requirements, generation, layout selection, plan editing, history, confirmation, reports, AI instruction proposals and professional-review tracking.
- Consent-scoped architect portal with exact-report access, response, feedback and completion.
- Module-aware staff dashboard/tasks/proposals and server-authorized admin dashboard/accounts/tasks/monitoring/audit.
- Truthful regulation-evidence unavailable state when no approved corpus exists.

## Route inventory

| Area | Routes |
| --- | --- |
| Public | `/`, `/about`, `/contact`, `/privacy`, `/terms` |
| Authentication | `/auth/sign-in`, `/auth/register`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/verify` |
| Customer | `/app/dashboard`, `/app/projects`, `/app/start`, `/app/projects/:id`, `/edit`, `/survey`, `/survey/:surveyId`, `/land`, `/requirements`, `/generation`, `/plans/:planId`, `/reports`, `/review`, `/regulations`, `/app/profile` |
| Architect | `/architect`, `/architect/requests`, `/architect/requests/:requestId` |
| Staff | `/staff`, `/staff/tasks`, `/staff/proposals` |
| Admin | `/admin`, `/admin/users`, `/admin/tasks`, `/admin/system`, `/admin/settings`, plus project/survey/land/requirements/job/plan/validation/confirmation/report monitoring routes |
| System | `/forbidden`, `/session-ended`, wildcard 404 |

All application branches require the authenticated session guard. Privileged roles and module grants are enforced by the backend on every operation; the client does not infer absent JWT claims.

## Verification evidence

- `npm run verify`: typecheck and lint passed; token enforcement passed with 0 raw colours across 205 files; 56/56 WCAG contrast pairs passed; 245/245 unit tests passed with coverage; production and mock builds passed.
- Unit coverage: 67.27% statements, 77.03% branches, 43.72% functions, 70.40% lines.
- Public Playwright: 6/6 selected desktop/mobile journeys passed, including seven responsive widths, navigation, no overflow, reduced motion and absence of cinematic runtime.
- Accessibility Playwright/axe: 10/10 public/auth checks passed at 360 px and 1280 px after correcting description-list semantics and warm-theme contrast.
- Live customer Playwright: 2/2 sequential journeys passed against disposable local accounts/data: register, sign in, dashboard, create/update/search/lifecycle, profile/session revocation and PDF/JPEG/PNG survey intake.
- Portable backend: startup marker `BUILDMIND_PORTABLE_BACKEND_READY`; 148 routes registered; API, readiness, Swagger and Mailpit returned HTTP 200. RAG remained intentionally unavailable without an approved corpus.

Key evidence:

- `docs/cinematic-v2/evidence/static-home-contact-sheet.png`
- `docs/cinematic-v2/evidence/static-hero-desktop.png`
- `docs/cinematic-v2/evidence/static-mobile-navigation.png`
- `docs/cinematic-v2/evidence/static-reduced-motion.png`
- `docs/application-completion/evidence/customer-dashboard-live.png`
- `docs/application-completion/evidence/customer-project-live.png`
- `docs/application-completion/evidence/customer-security-live.png`
- `docs/application-completion/evidence/customer-survey-live.png`

## Local startup

From the backend repository root:

```powershell
wsl.exe -d Ubuntu-24.04 --exec bash tools/portable/start.sh
```

From this frontend root, build and use the loopback-only same-origin preview proxy without committing an operator-specific origin:

```powershell
npm.cmd run build
# In ignored dist/buildmind-frontend/browser/config.json set api.mainBaseUrl to "/".
$env:BUILDMIND_API_PROXY='http://localhost:5182'
node tools/serve-dist.mjs
```

Preview: `http://127.0.0.1:4300/`  
Swagger: `http://localhost:5182/swagger/index.html`  
Mailpit: `http://localhost:8027`

## Evidence-based limitations

- No regulation corpus is approved; legal passages and compliance conclusions remain unavailable by design. STEP 8B stays on hold.
- A new customer architect request cannot safely be created until the backend exposes the authoritative current policy version. Existing requests remain visible.
- Architect, staff and admin live browser journeys require separately provisioned privileged identities. Customer credentials cannot and must not be elevated by the frontend.
- Staff policy/monitoring surfaces and admin catalogue/architect/staff administration are only partially surfaced; their backend contracts remain authoritative and unchanged.
- First-time requirements creation and richer nested spaces need a typed catalogue/bootstrap contract.
- Survey extraction review needs a synthetic document the local OCR service can successfully extract; invalid minimal files only verify intake/failure-safe behavior.
- Generation, plan editing, AI refinement, confirmation and report creation need the complete confirmed-land/requirements workflow before end-to-end live evidence can be produced.
- External SMTP, production hosting, approved regulation data and independent-PC acceptance remain external decisions. Local Mailpit is not real email delivery.

No backend source, dependency manifest, security setting, original checkpoint or remote branch was modified. No push was performed.

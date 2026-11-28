# Frontend/backend contract matrix

Evidence sources:

- Backend Docker-verified `openapi.enriched.json` and `buildmind-api.d.ts`.
- `FRONTEND-INTEGRATION-GUIDE.md`, customer, architect and admin integration guides.
- Backend HEAD `25309cd82417fb1e821a09f4e9fbc6084dfc0b2f`.

The exact TypeScript request/response type for every operation is available as
`ApiOperations["METHOD /api/v1/…"]` in the generated declaration. That generated declaration is the
source of truth where an OpenAPI body is inline rather than a named DTO. No contract below is inferred
from marketing content.

| Feature | Frontend route | Backend operation(s) | Request / response contract | Auth / policy | Success / expected errors | Runtime / integration |
| --- | --- | --- | --- | --- | --- | --- |
| Register | `/auth/register` | `POST /api/v1/auth/register` | `RegisterRequest` → auth/user response | anonymous; customer role only | 201; 400/429 possible | live browser verified with disposable accounts |
| Login | `/auth/sign-in` | `POST /api/v1/auth/login` | `LoginRequest` → access/refresh session response | anonymous | 200; 401 invalid credentials | live browser verified |
| Refresh/logout | application-wide | `POST /auth/refresh`, `/logout`, `/logout-all` | `RefreshTokenRequest`; rotated tokens / 204 | refresh anonymous; logout bearer | 200/204; 401 reused/invalid | live smoke passed; frontend single-flight rotation tested |
| Password recovery | `/auth/forgot-password`, `/auth/reset-password` | `POST /auth/forgot-password`, `/reset-password` | `ForgotPasswordRequest`, `ResetPasswordRequest` → 202/204 | anonymous, rate limited | 202/204; 400/429 | stopped / UI only |
| Current profile | `/app/profile` | `GET/PUT /profile`, `GET/PUT /users/me` | `UpdateProfileRequest` ↔ `ProfileResponse` | bearer, current account | 200; 400/401 | profile/security/session inventory live browser verified |
| Account security | `/app/profile` | `/users/me/password`, `/security`, `/security-events`, `/sessions` | generated `ApiOperations` contracts | bearer; current account | 200/204; 400/401/404/422/429 | status, password, sessions, revocation and safe event list UI implemented |
| Dashboard | `/app/dashboard` | `GET /dashboard/summary`, notification endpoints | generated dashboard and notification DTOs | bearer, owner scope | 200; 401 | guarded customer route registered; notification count integrated |
| Projects | `/app/projects`, `/app/projects/:id`, `/app/projects/:id/edit` | `GET/POST /projects`, `GET/PATCH /projects/{id}` | `CreateProjectRequest`, `UpdateProjectRequest`, generated project DTOs | bearer, owner scope | 200/201; 401/404/409 | searchable list/create/detail/edit UI; revision conflicts require reload; live create/list smoke passed |
| Lifecycle | `/app/projects/:id` | `GET /projects/{id}/lifecycle` | generated lifecycle DTO (`stage`, milestones, stale, nextActions) | bearer, owner scope | 200; 404 | lifecycle milestones, next actions and stale counts implemented; live browser evidence pending |
| Survey upload | `/app/projects/:id/survey` | `GET/POST /projects/{id}/surveys` | multipart `file`, PDF/JPEG/PNG ≤6 MiB → 202 survey id; `SurveyListDto` | bearer, owner scope | 200/202; 404/413/415 | list/upload UI implemented with 6 MiB client gate and server revalidation; live browser evidence pending |
| Survey extraction | `/app/projects/:id/survey/:surveyId` | survey status/private file/extraction/review operations | generated extraction/review contracts | bearer, owner scope | 200/201; 401/404/409/422 | authenticated blob preview with revoked object URL, extracted evidence inspection and versioned confirm/correct/reject submission implemented; live successful OCR review pending |
| Land | `/app/projects/:id/land` | land GET/PUT/confirm/validate/frame/edges/revisions | generated land and geometry contracts; optimistic revision id | bearer, owner scope | 200/201/204; 400/404/409/422 | authoritative SVG boundary, points, coordinates, bearings/lengths, validation and revision-aware confirmation implemented; no statutory constraints shown without contract data |
| Requirements | `/app/projects/:id/requirements` | requirements snapshot and full revision write; spaces/relationships/summary/revisions | generated requirement contracts; `expectedRevisionId` | bearer, owner scope | 200; 401/404/409 | existing catalogue-backed room counts and full revision persistence implemented; untyped nested records preserved, not fabricated; first-revision catalogue bootstrap remains a contract gap |
| Regulation evidence | `/app/projects/:id/regulations` | `POST /regulations/retrieve` | query/maxResults/jurisdiction/documentIds → evidence status/passages/disclaimer | bearer | 200; 401; service/corpus conditions | stopped; corpus not approved/configured |
| Generate layouts | `/app/projects/:id/generation` | generation job create/status/alternatives | generated contracts; land+requirements revisions; idempotency key | bearer, owner scope | 200/201; 404/409/422/503 | prerequisites, idempotent request, actual candidate geometry and diagnostics implemented |
| Compare/select | `/app/projects/:id/generation` | alternatives and select | backend geometry/metadata → saved plan/revision | bearer, owner scope | 200/201; 404/409 | side-by-side architectural candidates and blocking-warning-aware selection implemented |
| Plan editor | `/app/projects/:id/plans/:planId` | plan GET, candidates, validate, apply/reject, versions | command/candidate/version contracts; expected base revision | bearer, owner scope | 200/201; 401/404/409/422 | persisted geometry plus supported `resizeRoom` preview, before/after validation and explicit apply/reject implemented; unsupported arbitrary mutations remain disabled |
| Instruction refinement | `/app/projects/:id/plans/:planId` | `POST …/ai-edit/proposals`, candidate apply/reject | instruction + expected base revision → unapplied proposal and source | bearer, owner scope | 201; 404/409/422/503 | integrated in plan workspace; source and explicit apply/reject shown |
| Confirmation | project plan route | plan confirmation/confirm/revert | exact revision + geometry hash | bearer, owner scope | 200/201; 400/404/409/422 | revision-aware confirmation/history/revert implemented |
| Reports | `/app/projects/:id/reports` | report list/create/detail/download | generated report DTO / authenticated PDF blob | bearer, owner scope | 200/201; 404 | report inventory and secure ready-only PDF download implemented; creation requires real confirmation ID |
| Customer architect review | `/app/projects/:id/review` | architect directory and project architect-request operations | generated request/conversation DTOs; explicit consent | bearer, owner scope | 200/201; 404/409/422 | verified directory and existing consented request tracking implemented; creation blocked because no current policy-version discovery operation exists |
| Architect portal | `/architect/*` | architect dashboard/request/respond/feedback/report/complete | generated architect portal contracts | bearer + active ARCHITECT + directory profile | 200/201; 403/404/409 | routes and consent-scoped UI implemented; role workflow awaits provisioned identity |
| Staff portal | `/staff/*` | staff dashboard/tasks/proposals/policies/monitoring | generated staff contracts | bearer + active staff module grants | 200/201; 401/403/422 | dashboard/tasks/proposals implemented; policy/monitoring UI remains partial |
| Admin portal | `/admin/*` | admin dashboard/accounts/staff/tasks/audit/catalogues/etc. | generated admin contracts with expected versions on writes | bearer + active ADMIN, re-read per request | 200/201; 400/401/403/409/422 | dashboard/accounts/tasks/monitoring/audit implemented; catalogue/profile administration remains partial |
| Health | operational only | `GET /health` | health response | anonymous | 200 | live 200; readiness live 200 |

## Transport and security rules

- Access tokens stay in memory; refresh tokens rotate. Never log either token.
- Authenticated requests use bearer authorization. Existing frontend cookie support must not be
  mistaken for the backend's token contract.
- Refresh once after 401, never loop. Versioned writes must surface 409 and reload newer state.
- Owner-scoped 404 deliberately hides whether a resource exists; UI must not describe it as 403.
- Private responses are `Cache-Control: no-store`.
- The backend guide recommends a same-origin Angular proxy for local development. The current API
  runtime is stopped and no committed operator-specific CORS origin is authorized.

# API endpoint inventory

All product routes are under `/api/v1`, use RFC 9457 errors, and require JWT plus the active-session check unless noted. "Gate" is the configuration that registers the route. Production activation is **not approved** for any gated feature.

| Route | Method | Feature | Gate | Ownership enforcement |
|---|---|---|---|---|
| `/health` | GET | Liveness | always | n/a (anonymous) |
| `/health/ready` | GET | Readiness (MySQL mandatory; Python/RAG degraded-only) | always | n/a (anonymous) |
| `/api/v1/auth/register`, `/auth/login` | POST | M1 auth | `Authentication:Enabled` (Development only) | anonymous; auth-strict rate limit |
| `/api/v1/auth/refresh`, `/auth/logout`, `/auth/logout-all` | POST | Sessions | `Authentication:RefreshTokens:Enabled` | token family / token version |
| `/api/v1/auth/forgot-password`, `/auth/reset-password` | POST | Provisional recovery (non-delivering sender) | `Authentication:PasswordRecovery:Enabled` (Development only) | anonymous; auth-strict |
| `/api/v1/profile` | GET/PUT | M1 profile | `Authentication:Enabled` | current user |
| `/api/v1/projects` (GET list, POST), `/projects/{id:guid}` (GET, PATCH) | GET/POST/PATCH | M1 projects | `Authentication:Enabled` | owner; PATCH = atomic owner/version/revision CAS. No DELETE route exists. |
| `/api/v1/dashboard/summary` | GET | M5 dashboard | `Authentication:Enabled` | owner-scoped rows |
| `/api/v1/regulations/retrieve` | POST | M7 retrieval | `Regulations:Retrieval:Enabled` + RAG | authenticated; expensive limiter |
| `/api/v1/projects/{id}/requirements` | GET/PUT | M5 requirements | **`CaptainIntegration:Enabled`** (Development only) | read: M5 owner check; write: protected A1–A8 path (project row `FOR UPDATE` + owner + authorization version) |
| `/api/v1/projects/{id}/requirements/spaces`, `/relationships`, `/summary`; `/projects/{id}/design-method` | GET/PUT | M5 requirement sections | **`CaptainIntegration:Enabled`** | as above |
| `/api/v1/projects/{projectId}/plans/{planId}/versions`, `/versions/{revisionId}` | GET | M3 revision history | **`CaptainIntegration:Enabled`** | fresh owner authorization read before and after the history read |

| `/api/v1/projects/{id}/surveys` | POST (multipart `file`) | M2 survey upload | **`CaptainIntegration:Enabled`** + `CaptainIntegration:SurveyStorageDirectory` | owner gate, then protected insert (project row lock + owner + version) |
| `/api/v1/projects/{id}/surveys/{surveyId}` | GET | M2 survey status | **`CaptainIntegration:Enabled`** | owner gate + project-scoped read |

| `/api/v1/projects/{projectId}/land` | GET / PUT | Land boundary (user-confirmed, local metric frame) | **Captain profile** | fresh owner read; PUT = Python validation + protected append with head CAS (`expectedCurrentLandRevisionId`) |
| `/api/v1/projects/{projectId}/generation-jobs` | POST | Generation (synthetic or truthful failure) | **Captain profile** + `Services:Python` + `CaptainIntegration:GenerationPolicyFile`; expensive limiter | `idempotencyKey` per project+actor; expected land/requirement revisions; protected job writes |
| `/api/v1/projects/{projectId}/generation-jobs/{jobId}` | GET | Job + candidates | **Captain profile** | owner |
| `/api/v1/projects/{projectId}/generation-jobs/{jobId}/candidates/{candidateId}/save` | POST | Save candidate as plan revision 1 | **Captain profile** | server re-validation + server-issued evidence + protected creation |

| `/api/v1/projects/{projectId}/plans` | GET | Plan list (head + confirmation) | **Captain profile** | fresh owner read |
| `/api/v1/projects/{projectId}/plans/{planId}` | GET | Current plan revision (geometry, evidence) | **Captain profile** | fresh owner read |
| `/api/v1/projects/{projectId}/plans/{planId}/candidates` | POST | M3 edit candidate (structured command → Python preview → server validation evidence) | **Captain profile** | M3 service + protected candidate insert |
| `/api/v1/projects/{projectId}/plans/{planId}/candidates/{candidateId}/apply`, `/reject` | POST | M3 apply/reject | **Captain profile** | protected transaction: candidate CAS + head CAS + immutable append |
| `/api/v1/projects/{projectId}/plans/{planId}/confirmation` | POST | Final confirmation of the exact current revision | **Captain profile** | M3 service + protected write (head check, one per plan) |

Internal (python-services, loopback, `X-BuildMind-Internal-Token` required): `POST /internal/v1/land/validate`, `/internal/v1/generation/run`, `/internal/v1/plans/validate`, `/internal/v1/plans/edit-preview`; `GET /health` open.

Phase updates append rows below.

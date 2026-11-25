# Extended Captain API endpoint register (generated from the live Development OpenAPI export)

Source: `openapi.captain-dev.json` exported from the running API (commit after `cb48556`). **125 operations / 106 paths.** `GET /health/ready` is mapped but not listed in Swagger (operational, anonymous).

Counts by class: Admin 33, Architect 6, Authentication 7, Customer 67, Operational 1, Staff 11.

Every route is IMPLEMENTED in the Captain Development profile unless the original V3 register (MASTER-API-ENDPOINT-MATRIX) says otherwise; authorization per class: Customer = owner-scoped (404 for missing/foreign); Admin = active ADMIN role read fresh per request (403); Staff = active STAFF role + module scope (403); Architect = ARCHITECT role + own directory profile (403). Verification: LIVE = exercised by `smoke_journey.py` (188 checks) or `smoke_admin.py` (65 checks) on the real stack.

| Class | Method | Path | Tag | Authentication |
|---|---|---|---|---|
| Admin | GET | `/api/v1/admin/accounts` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/accounts/{userId}` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/accounts/{userId}/activate` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/accounts/{userId}/invitation` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/accounts/{userId}/roles` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/accounts/{userId}/suspend` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/architects` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/architects` | Admin | JWT + active session |
| Admin | PUT | `/api/v1/admin/architects/{recipientId}/listing` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/architects/{recipientId}/verification` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/audit` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/audit/verify` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/catalogues` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/catalogues` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/catalogues/{version}` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/catalogues/{version}/publish` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/catalogues/{version}/retire` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/customers` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/dashboard` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/modules` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/monitoring/{kind}` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/policies` | Admin | JWT + active session |
| Admin | PUT | `/api/v1/admin/policies/{key}` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/proposals` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/proposals/{proposalId}/approve` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/proposals/{proposalId}/reject` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/staff` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/staff` | Admin | JWT + active session |
| Admin | PUT | `/api/v1/admin/staff/{userId}/modules` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/tasks` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/tasks` | Admin | JWT + active session |
| Admin | GET | `/api/v1/admin/tasks/{taskId}` | Admin | JWT + active session |
| Admin | POST | `/api/v1/admin/tasks/{taskId}/status` | Admin | JWT + active session |
| Architect | GET | `/api/v1/architect/dashboard` | Architect portal | JWT + active session |
| Architect | GET | `/api/v1/architect/requests` | Architect portal | JWT + active session |
| Architect | GET | `/api/v1/architect/requests/{requestId}` | Architect portal | JWT + active session |
| Architect | POST | `/api/v1/architect/requests/{requestId}/feedback` | Architect portal | JWT + active session |
| Architect | GET | `/api/v1/architect/requests/{requestId}/report` | Architect portal | JWT + active session |
| Architect | POST | `/api/v1/architect/requests/{requestId}/respond` | Architect portal | JWT + active session |
| Customer | GET | `/api/v1/architects` | Architect directory | JWT + active session |
| Authentication | POST | `/api/v1/auth/forgot-password` | ProvisionalPasswordRecoveryEndpointModule | anonymous |
| Authentication | POST | `/api/v1/auth/login` | Authentication | anonymous |
| Authentication | POST | `/api/v1/auth/logout` | Authentication | JWT + active session |
| Authentication | POST | `/api/v1/auth/logout-all` | Authentication | JWT + active session |
| Authentication | POST | `/api/v1/auth/refresh` | Authentication | JWT + active session |
| Authentication | POST | `/api/v1/auth/register` | Authentication | anonymous |
| Authentication | POST | `/api/v1/auth/reset-password` | ProvisionalPasswordRecoveryEndpointModule | anonymous |
| Customer | GET | `/api/v1/dashboard/summary` | Dashboard | JWT + active session |
| Customer | GET | `/api/v1/notifications` | Notifications | JWT + active session |
| Customer | POST | `/api/v1/notifications/read-all` | Notifications | JWT + active session |
| Customer | GET | `/api/v1/notifications/unread-count` | Notifications | JWT + active session |
| Customer | POST | `/api/v1/notifications/{notificationId}/read` | Notifications | JWT + active session |
| Customer | GET | `/api/v1/profile` | Profile | JWT + active session |
| Customer | PUT | `/api/v1/profile` | Profile | JWT + active session |
| Customer | GET | `/api/v1/projects` | Projects | JWT + active session |
| Customer | POST | `/api/v1/projects` | Projects | JWT + active session |
| Customer | GET | `/api/v1/projects/{id}` | Projects | JWT + active session |
| Customer | PATCH | `/api/v1/projects/{id}` | Projects | JWT + active session |
| Customer | PUT | `/api/v1/projects/{id}/design-method` | Requirements | JWT + active session |
| Customer | GET | `/api/v1/projects/{id}/requirements` | Requirements | JWT + active session |
| Customer | PUT | `/api/v1/projects/{id}/requirements` | Requirements | JWT + active session |
| Customer | GET | `/api/v1/projects/{id}/requirements/relationships` | Requirements | JWT + active session |
| Customer | PUT | `/api/v1/projects/{id}/requirements/relationships` | Requirements | JWT + active session |
| Customer | GET | `/api/v1/projects/{id}/requirements/spaces` | Requirements | JWT + active session |
| Customer | PUT | `/api/v1/projects/{id}/requirements/spaces` | Requirements | JWT + active session |
| Customer | GET | `/api/v1/projects/{id}/requirements/summary` | Requirements | JWT + active session |
| Customer | POST | `/api/v1/projects/{id}/surveys` | Survey | JWT + active session |
| Customer | GET | `/api/v1/projects/{id}/surveys/{surveyId}` | Survey | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/architect-requests` | Reports and Architect Requests | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/architect-requests` | Reports and Architect Requests | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/architect-requests/{requestId}` | Reports and Architect Requests | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/architect-requests/{requestId}/conversation` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/architect-requests/{requestId}/withdraw` | Reports and Architect Requests | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/generation-jobs` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/generation-jobs/{jobId}` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/generation-jobs/{jobId}/alternatives` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/generation-jobs/{jobId}/candidates/{candidateId}/save` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/generation-jobs/{jobId}/select` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/land` | Planning | JWT + active session |
| Customer | PUT | `/api/v1/projects/{projectId}/land` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/land/confirm` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/land/validate` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/lifecycle` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/plans` | Plan Lifecycle | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/plans/{planId}` | Plan Lifecycle | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/ai-edit/proposals` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/candidates` | Plan Editor | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/candidates/{candidateId}/apply` | Plan Editor | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/candidates/{candidateId}/reject` | Plan Editor | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/confirm` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/confirmation` | Plan Lifecycle | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/edits/validate` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/plans/{planId}/versions` | Plan Editor | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/plans/{planId}/versions` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/plans/{planId}/versions/{revisionId}` | Plan Editor | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/reports` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/reports` | Reports and Architect Requests | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/reports/{reportId}` | Reports and Architect Requests | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/reports/{reportId}/download` | Reports and Architect Requests | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/surveys/{surveyId}/extraction` | Planning | JWT + active session |
| Customer | GET | `/api/v1/projects/{projectId}/surveys/{surveyId}/extraction/reviews` | Planning | JWT + active session |
| Customer | POST | `/api/v1/projects/{projectId}/surveys/{surveyId}/extraction/reviews` | Planning | JWT + active session |
| Customer | POST | `/api/v1/regulations/retrieve` | Regulations | JWT + active session |
| Staff | GET | `/api/v1/staff/dashboard` | Staff | JWT + active session |
| Staff | GET | `/api/v1/staff/monitoring/{kind}` | Staff | JWT + active session |
| Staff | GET | `/api/v1/staff/policies` | Staff | JWT + active session |
| Staff | GET | `/api/v1/staff/proposals` | Staff | JWT + active session |
| Staff | POST | `/api/v1/staff/proposals` | Staff | JWT + active session |
| Staff | POST | `/api/v1/staff/proposals/{proposalId}/withdraw` | Staff | JWT + active session |
| Staff | GET | `/api/v1/staff/tasks` | Staff | JWT + active session |
| Staff | GET | `/api/v1/staff/tasks/{taskId}` | Staff | JWT + active session |
| Staff | POST | `/api/v1/staff/tasks/{taskId}/notes` | Staff | JWT + active session |
| Staff | POST | `/api/v1/staff/tasks/{taskId}/project-review` | Staff | JWT + active session |
| Staff | POST | `/api/v1/staff/tasks/{taskId}/status` | Staff | JWT + active session |
| Customer | GET | `/api/v1/users/me` | Profile | JWT + active session |
| Customer | PUT | `/api/v1/users/me` | Profile | JWT + active session |
| Customer | POST | `/api/v1/users/me/email-verification` | Account security | JWT + active session |
| Customer | POST | `/api/v1/users/me/email-verification/confirm` | Account security | JWT + active session |
| Customer | GET | `/api/v1/users/me/notification-preferences` | Notifications | JWT + active session |
| Customer | PUT | `/api/v1/users/me/notification-preferences` | Notifications | JWT + active session |
| Customer | POST | `/api/v1/users/me/password` | Account security | JWT + active session |
| Customer | GET | `/api/v1/users/me/security` | Account security | JWT + active session |
| Customer | GET | `/api/v1/users/me/security-events` | Account security | JWT + active session |
| Operational | GET | `/health` | BuildMind.API | anonymous |

# Admin frontend integration

All routes are `/api/v1/admin/*` and need JWT plus an **active ADMIN role, read fresh per request**. A non-admin gets 403 `PERMISSION_DENIED`.

- **Lists:** `page` (1-based), `pageSize` (≤ 100), `search`, `status`, `sort`; responses are `{items, page, pageSize, total}`.
- **Writes:** use optimistic versions (`expectedScopeVersion`, `expectedRowVersion`, `expectedVersion`). Handle 409 by reloading.
- **Matrix:** ADMIN-STAFF-PERMISSION-MATRIX.md.

| Screen | Calls |
|---|---|
| First admin | Operator bootstrap only (`CaptainIntegration:AdminBootstrap:Email` on an existing account). There is no UI. |
| Dashboard | `GET /dashboard` → counts by role/status, pending proposals, open tasks; `GET /modules` |
| Customers / accounts | `GET /customers`, `GET /accounts` (`status=ACTIVE\|SUSPENDED`, `sort=email\|-email\|created`); `GET /accounts/{id}`; `POST /accounts/{id}/suspend\|activate {reason}` (all sessions end); `POST /accounts/{id}/roles {role: ADMIN\|ARCHITECT, granted}` |
| Staff | `GET /staff`; `POST /staff {email, fullName?, modules[]}` → 201 `{userId, invitationSent}` (an email code; no password is shown); `PUT /staff/{id}/modules {modules[], expectedScopeVersion}`; `POST /accounts/{id}/invitation` (resend) |
| Proposal queue | `GET /proposals?status=PENDING`; `POST /proposals/{id}/approve\|reject {expectedRowVersion, note?}`. 409 `STALE_PROPOSAL`: the live record changed since the proposal; it is marked STALE and nothing changes. |
| Tasks | `GET /tasks`, `POST /tasks {assigneeId, title, description?, module, projectId?}`, `GET /tasks/{id}` (with notes), `POST /tasks/{id}/status {status, expectedVersion, note?}` (admin may CANCEL) |
| Policies | `GET /policies`; `PUT /policies/{key} {value(JSON), expectedVersion?}` |
| Requirement catalogue | `GET /catalogues`, `GET /catalogues/{version}`, `POST /catalogues {version, provenance, document}` (DRAFT), `POST /catalogues/{v}/publish {expectedRowVersion, effectiveFromUtc?}`, `POST …/retire`. Values are product preferences, **not** regulations. |
| Architect directory | `GET /architects`, `POST /architects {userId, displayName, registrationNumber?}`, `POST /architects/{rid}/verification {status, reference, expectedRowVersion}` (VERIFIED requires a reference), `PUT /architects/{rid}/listing {isListed, acceptingRequests, expectedRowVersion}` |
| Monitoring | `GET /monitoring/{projects\|surveys\|generation-jobs\|reports\|architect-requests}` (metadata only) |
| Audit | `GET /audit?search=ACTION`, `GET /audit/verify` → `{intact, eventsChecked, firstBrokenSequence}` |

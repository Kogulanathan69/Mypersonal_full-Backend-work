# Staff frontend integration

## Access rules

- **Routes:** `/api/v1/staff/*`. They need JWT plus an active STAFF role; module scope is read fresh on every request.
- **Losing access:** when an admin changes modules or suspends the account, the staff member's session ends (401). Show "please sign in again"; inaccessible modules then return 403.
- **Activation:** through the invitation email code, using `POST /auth/reset-password`. There is no temporary password.

## Calls

| Screen | Calls |
|---|---|
| Dashboard | `GET /dashboard` → `{modules[], tasks{open, inProgress, escalated}, pendingProposals}` |
| Module views | `GET /monitoring/{kind}` for assigned modules only; `GET /policies` (SYSTEM_POLICIES) |
| Propose a change | `POST /proposals {module, targetType: SYSTEM_POLICY\|REQUIREMENT_CATALOGUE_DRAFT, targetKey, changeType: CREATE\|UPDATE\|DELETE, baseVersion?, payload}` → 201 PENDING. **Nothing changes until an admin approves.** `GET /proposals`, `POST /proposals/{id}/withdraw {expectedRowVersion}` |
| Tasks | `GET /tasks`, `GET /tasks/{id}`, `POST /tasks/{id}/status {status: IN_PROGRESS\|ESCALATED\|DONE, expectedVersion, note}` (ESCALATED requires a note; CANCELLED is admin-only), `POST /tasks/{id}/notes {body}` |
| Project review | `POST /tasks/{id}/project-review {purpose}` (10–500 chars, **audited**) → metadata only. It works only for an active task assigned to you that references a project. Staff can never open customer projects directly (404). |

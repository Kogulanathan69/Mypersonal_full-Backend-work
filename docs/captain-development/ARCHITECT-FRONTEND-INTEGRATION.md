# Architect frontend integration

## Access rules

- **Routes:** `/api/v1/architect/*`. They need JWT, the ARCHITECT role and an admin-created directory profile; without a profile the API returns 403 `ARCHITECT_PROFILE_MISSING`.
- **Contract:** ARCHITECT-PORTAL-CONTRACT.md.

## Calls

| Screen | Calls |
|---|---|
| Dashboard | `GET /dashboard` → `{profile, pending, accepted}` |
| Requests | `GET /requests`, `GET /requests/{id}`. Only requests addressed to you that are not withdrawn; a withdrawn request disappears (404). |
| Report | `GET /requests/{id}/report` → `application/pdf`. This is the exact consented preliminary report, and every access is audited. Show its preliminary/non-approval nature. |
| Respond | `POST /requests/{id}/respond {decision: ACCEPTED\|DECLINED, note?}`, once only (409 after) |
| Feedback | `POST /requests/{id}/feedback {kind: FEEDBACK\|REVISION_REQUEST, body ≤ 4000}`, only after ACCEPTED (409 otherwise) |
| Notifications | Same `/notifications` routes as customers (for example `ARCHITECT_REQUEST_RECEIVED`) |

Architects have no access to projects, plans, surveys or other documents.

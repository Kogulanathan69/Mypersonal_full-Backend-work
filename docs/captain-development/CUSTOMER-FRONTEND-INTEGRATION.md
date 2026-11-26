# Customer frontend integration

## Base document and conventions

- **Base document:** FRONTEND-INTEGRATION-GUIDE.md. It covers transport, CORS, auth/refresh, the error model, optimistic concurrency and the core journey; everything there still applies.
- **Contract:** `openapi.captain-dev.json` (125 operations).
- **Route list:** EXTENDED-API-ENDPOINT-REGISTER.md.

## Additions in the extended execution

| Area | Calls | UI notes |
|---|---|---|
| Account security | `POST /users/me/password {currentPassword, newPassword, confirmPassword}` → 204. Every session is revoked: send the user to login. `GET /users/me/security`; `POST /users/me/email-verification` (202, code by email); `POST …/confirm {code}`; `GET /users/me/security-events` (paged) | Passwords are 15–128 chars. 422 `CURRENT_PASSWORD_INCORRECT`. Verification is not yet required for login (policy pending). |
| Password recovery | `POST /auth/forgot-password {email}` (always 202, generic); `POST /auth/reset-password {email, token, newPassword, confirmPassword}` → 204 | Never reveal whether an account exists |
| Survey review | `POST /projects/{id}/surveys/{sid}/extraction/reviews {extractionId, expectedVersion, decisions:[{measurementIndex, action: CONFIRM\|CORRECT\|REJECT, correctedValue?, correctedUnit: m\|ft}], note?}`; `GET …/reviews` | Show "your evidence, not a confirmed boundary". 409 = someone reviewed in parallel → reload |
| Lifecycle | `GET /projects/{id}/lifecycle` → `stage`, `milestones[]`, `stale{…}`, `nextActions[]` | Drive the progress UI from this; never from client state. Show stale counts after land/requirement changes |
| AI-assisted edits | `POST /plans/{planId}/ai-edit/proposals {expectedBaseRevisionId, instruction}` → 201 `{candidate, proposalSource: "RULE_BASED_LOCAL", applied: false}`; then the existing apply/reject | **Label it "instruction assistant (rule-based)", not AI.** Supported phrasing: "make the kitchen larger by 10%", "make the living room wider by 0.5 m", "move the bathroom east by 1 m". 422 `AI_PROPOSAL_INVALID` = not understood or ambiguous (use the room name). 503 = feature disabled |
| Architect directory | `GET /architects` | Show the verification notice text from the response |
| Architect conversation | `GET /projects/{id}/architect-requests/{rid}/conversation` → `{request: {decision, decisionNote}, feedback[]}` | Withdrawal (existing) ends the architect's access |
| Notifications | `GET /notifications?unreadOnly=`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`, `POST /notifications/read-all`; `GET/PUT /users/me/notification-preferences {emailEnabled}` | Poll the unread count (for example every 30–60 s); `link` is an app-relative route hint |

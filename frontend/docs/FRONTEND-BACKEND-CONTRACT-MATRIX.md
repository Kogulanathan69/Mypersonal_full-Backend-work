# Frontend–backend contract matrix

Source of truth in code: `src/app/core/api/contract-status.ts` registry (+ `core/auth/auth.contracts.ts`).
No endpoint, DTO field, status value or permission name has been invented. **0 operations are VERIFIED.**

The Captain backend (`C:\Users\kogul\Documents\BuildMind-Worktrees\captain-full-backend-completion`, reported 125+ OpenAPI operations) is on the Captain's Windows PC and has not been made available to this workspace.

| Contract id | Screen | Route | Role | Permission | Method / path | Request / response | Loading | Empty | Error | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| auth.signIn | C02 | /auth/sign-in | Visitor | — | unknown | unknown | button spinner, aria-busy | — | form alert (credentials, suspended, rate limit, not connected) | BLOCKED | auth-screens.spec, phase03 browser flow |
| auth.register | C03 | /auth/register | Visitor | — | unknown | unknown | button spinner | — | form alert + server field errors | BLOCKED | auth-screens.spec |
| auth.verifyEmail | Verify | /auth/verify | Visitor | — | unknown | unknown | status spinner | — | invalid/expired link state | BLOCKED | auth-screens.spec |
| auth.resendVerification | Verify | /auth/verify | Visitor | — | unknown | unknown | button spinner | — | form alert | BLOCKED | auth-screens.spec |
| auth.forgotPassword | C04 | /auth/forgot-password | Visitor | — | unknown | unknown | button spinner | — | neutral confirmation / form alert | BLOCKED | auth-screens.spec |
| auth.resetPassword | C05 | /auth/reset-password | Visitor | — | unknown | unknown | button spinner | — | invalid/expired link state | BLOCKED | auth-screens.spec |
| auth.refresh | Shell | — | Any | — | unknown | unknown | — | — | session-expired dialog | BLOCKED | — |
| auth.signOut | Shell | account menu | Any | — | unknown | unknown | — | — | toast | BLOCKED | — |
| auth.currentUser | Shell | — | Any | — | unknown | unknown | — | — | session cleared | BLOCKED | — |

All remaining capabilities (projects, surveys, land/CRS, requirements catalogue, regulation evidence, generation, plans, structured editing, AI proposals, reports, architect requests, notifications, admin, staff, architect portal, health) are **not yet declared** because no contract information is available. They will be added row by row from the OpenAPI export.

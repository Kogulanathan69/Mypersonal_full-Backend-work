# Frontend route register

Routes that exist in the code today (2026-11-25). Owner of `app.routes.ts`: F — Foundation.

| Route | Screen | Layout | Guard | Lazy chunk | Status |
| --- | --- | --- | --- | --- | --- |
| `/` | C01 Home (cinematic) | Public | — | home-page (+ cinematic-engine, gsap when WebGL) | COMPLETE_AND_VERIFIED |
| `/about` | About | Public | — | about-page | COMPLETE_AND_VERIFIED |
| `/contact` | Help and contact | Public | — | contact-page | COMPLETE_AND_VERIFIED (no support channel published yet) |
| `/privacy` | Privacy information (draft) | Public | — | policy-pages | Draft pending legal review |
| `/terms` | Terms of use (draft) | Public | — | policy-pages | Draft pending legal review |
| `/auth/sign-in` | C02 Sign in | Auth | guestGuard | sign-in | IMPLEMENTED_AWAITING_BACKEND |
| `/auth/register` | C03 Register | Auth | guestGuard | register | IMPLEMENTED_AWAITING_BACKEND |
| `/auth/verify` | Email verification (+ invalid/expired link) | Auth | — | verify-email | IMPLEMENTED_AWAITING_BACKEND |
| `/auth/forgot-password` | C04 Forgot password | Auth | — | forgot-password | IMPLEMENTED_AWAITING_BACKEND |
| `/auth/reset-password` | C05 Reset password (+ invalid/expired link) | Auth | — | reset-password | IMPLEMENTED_AWAITING_BACKEND |
| `/forbidden` | Unauthorised access (403) | none | — | status-pages | COMPLETE_AND_VERIFIED |
| `/session-ended` | Session ended | none | — | status-pages | COMPLETE_AND_VERIFIED |
| `**` | Not found (404) | none | — | not-found | COMPLETE_AND_VERIFIED |
| `/dev/**` | UI gallery, layout previews | various | — | dev | Dev/mock builds only; removed from production by file replacement |

Not yet registered (blocked on backend contracts): Customer `/app/**`, Admin `/admin/**`, Staff and Architect portals. Their layouts exist (customer, wizard, admin); Staff and Architect layouts will be added with their routes.

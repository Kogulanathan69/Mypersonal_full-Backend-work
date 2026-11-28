# Frontend integration gap register

| # | Gap | Affects | Needed from | Frontend status |
| --- | --- | --- | --- | --- |
| 1 | Latest OpenAPI export of the Captain backend | Every data-driven screen (Phases 03–16) | Captain | Integration boundary in place (ApiClient + zod + contract registry); 0 operations verified |
| 2 | Customer / Admin / Staff / Architect frontend integration guides | Portals, journeys | Captain | Not started (screens would require invented contracts) |
| 3 | Permission matrix with exact permission names | Guards, `*bmHasPermission`, admin roles | Captain | Guards and checker implemented; no permission names hard-coded |
| 4 | Project lifecycle state machine | Dashboard, workspace, next-action logic | Captain | Not started |
| 5 | Auth model: cookie session vs bearer token, refresh, CSRF, error codes (suspended, unverified, locked) | C02–C05, session expiry | Captain | Screens complete; `AUTH_API` returns truthful "not connected"; 401-on-login suppression ready |
| 6 | Correlation-id header name and ProblemDetails shape | Error reporting | Backend | Assumed `X-Correlation-ID` and RFC 9457; marked ASSUMPTION in code |
| 7 | Reachable backend URL for live E2E | Phase 16 real-backend tests | Captain | Workspace cannot reach a localhost backend on the Captain's PC; tests must run on a team machine |
| 8 | Support contact channel | /contact | BuildMind team | Page states it is not yet published |
| 9 | Final privacy policy and terms text | /privacy, /terms | Legal / Captain | Marked "Draft — pending legal review" |
| 10 | Brand font confirmation | All | Design owner | Poppins provisional |
| 11 | GSAP Standard licence acceptance | Home Page | Captain | Recorded in THREEJS-ASSET-AND-LICENSE-REGISTER.md |

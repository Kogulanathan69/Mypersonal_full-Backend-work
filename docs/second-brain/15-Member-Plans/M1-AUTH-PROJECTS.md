# M1 — backend detailed task plan

Source: BuildMind_M1_Backend_7_Day_Task_Plan.pdf, revised proposal v2.0. Extracted page text preserves detailed daily checklists, API and ownership proposals. Current V3 decisions and approved actual contracts override conflicts. All tasks **planned/unverified**; never overwrite existing code. Read [ownership](../14-Current-Backend/10-OWNERSHIP.md) first.

## Source page 1

```text
MEMBER 1 / BACKEND DEVELOPMENT / REVISED PROPOSAL



 Authentication, User Management
 & Projects
 ASP.NET Core 10 | C# | MySQL 8.4

 Own the server-side identity and project lifecycle behind the customer login, profile and project
 screens. Implement real backend code, not only Angular API calls.

 Module ownership
 01. Authentication and account recovery

 02. Authenticated customer profile

 03. Owner-scoped project creation, listing, read and update

 04. Project lifecycle rules and access checks

 Ownership boundary
 M4 owns shared infrastructure, DI, JWT configuration and migrations. M5 owns dashboard
 aggregate and requirements; M3 owns plan confirmation. Do not implement their endpoints
 again.

 Development branch
 feature/m1-auth-projects


 Seven-day target
 Deliver tested vertical slices, agreed contracts and real integration evidence. Existing frontend
 assignments are retained; confirm daily capacity with the team lead. Unsupported or unfinished
 work remains Partial or Blocked. No 3D or cost estimation is included in this MVP plan.

 Working environment
 Use the existing BuildMind backend repository and develop branch. C# targets net10.0; Python 3.13 where
 needed; MySQL 8.4 is the team target. Verify package compatibility locally and use approved pinned dependency
 files.
```

## Source page 2

```text
02 / IMPLEMENTATION MAP


 Folder & data ownership
 These paths and entities are proposed feature locations within the existing starter repository, not
 a claim that the files already exist. Coordinate shared edits with M4.

 Repository paths
 src/BuildMind.API/Endpoints/Auth/ and Projects/ (proposed)
 src/BuildMind.Application/Features/Auth/, Users/, Projects/
 src/BuildMind.Domain/Entities/ (User and Project only)
 src/BuildMind.Infrastructure/Identity/ and Persistence/Projects/
 tests/BuildMind.Auth.Tests/ and BuildMind.Projects.Tests/ (proposed)


 Data model / persistence

   Entity / area                        Required data and constraints

   Users                                Id, normalized email (unique), password hash, status, created time. Never
                                        return hash.


   PasswordResetTokens                  UserId, token hash, expiry, used time; single-use, short-lived.


   RefreshTokens (if approved)          Hashed token, user, expiry, revocation; omit if no refresh flow.


   Projects                             Id, owner UserId, name, type, location reference, status,
                                        revision/concurrency token, timestamps.


 Database rule
 Feature owners define their entities and mapping proposals. M4 coordinates shared DbContext and migrations;
 use a disposable development/test database. Do not commit credentials, real survey documents or customer
 data.
```

## Source page 3

```text
03 / INTERFACE CONTRACTS


 API implementation checklist
 Proposed endpoint names and payload intent. Freeze actual routes, DTO fields, prefixes and error
 codes in the API contract register before coding. Public Angular requests go through C#; Python
 endpoints are internal.

   Method / route or interface                                 Expected behavior

   POST /auth/register                                         Email + password + allowed profile fields -> safe
                                                               user result; duplicate/invalid rejected.


   POST /auth/login                                            Credentials -> access token/session per agreed
                                                               policy; invalid credentials rejected.


   POST /auth/forgot-password                                  Email -> generic acknowledgement; never leak
                                                               existence.


   POST /auth/reset-password                                   Reset token + new password -> success,
                                                               expired/used token rejected.


   GET /users/me; PUT /users/me                                Current user only; allowlisted profile changes; no
                                                               privilege edits.


   POST /auth/logout                                           Revoke refresh/session if used; client removes
                                                               access token; JWT expiry policy documented.


   POST /projects; GET /projects                               Create and list for authenticated owner; pagination if
                                                               needed.


   GET /projects/{id}; PATCH /projects/{id}                    Owner-only read/update; validation, concurrency and
                                                               status constraints.


 All project routes are authenticated and owner-scoped; use the authenticated identity, not an arbitrary owner ID
 from the request. Return real errors rather than mock success.
```

## Source page 4

```text
DAY 1 OF 7 / TASK ID: BE-M1-D01


 Identity and project foundation
 Review the existing develop branch; implement only your feature files and
 agree shared contracts.

 Implementation checklist
 01. Read the existing API, Application, Domain and Infrastructure projects; do not recreate the
      solution.

 02. Define User and extend the existing Project entity with owner ID and allowed lifecycle states.
      Agree names with M4.

 03. Create register/login/profile and project request/response DTOs, validation contracts and
      repository interfaces.

 04. Agree password hashing service, current-user abstraction, JWT claims, generic auth errors and
      API base route with M4.

 05. Publish sample request/response JSON and create invalid-input/owner-isolation test fixtures.

 Delivery and QA gate

   Deliverable                  Compiling feature foundation; agreed identity/project contract and migration request.


   Acceptance tests             Build all four C# projects; verify no API or Infrastructure references from Domain;
                                DTO validation rejects blank required fields.


   Dependencies                 M4 must publish DI, DB and auth infrastructure contracts on Day 1; M7 confirms API
                                conventions.


   Evidence to submit           Feature commit, build log, DTO examples and migration request.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 5

```text
DAY 2 OF 7 / TASK ID: BE-M1-D02


 Register, login and JWT
 Deliver the core authentication APIs so the login/register frontend can
 integrate.

 Implementation checklist
 01. Implement register handler with normalized unique email, validation and safe password hashing
      through an approved library.

 02. Persist the new account through repository abstraction; handle unique constraint races safely.

 03. Implement login handler and secure credential verification. Use M4 token/config infrastructure;
      no hardcoded signing keys.

 04. Issue short-lived JWT and safe response; enforce authenticated routes and rate-limit policy in
      coordination with M4.

 05. Publish OpenAPI examples and integrate with the login/register UI using real responses.

 Delivery and QA gate

   Deliverable                  Register and login work with MySQL; tokens accepted by protected routes.


   Acceptance tests             Valid register/login succeeds; duplicate email, wrong password and invalid input fail
                                safely; missing/expired JWT returns 401.


   Dependencies                 M4 identity schema migration and auth middleware; M7 test accounts. If DB not
                                ready, unit-test with fakes only and mark integration blocked.


   Evidence to submit           API request/response logs with token values redacted; unit + integration test report;
                                PR link.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 6

```text
DAY 3 OF 7 / TASK ID: BE-M1-D03


 Project CRUD and owner isolation
 Allow an authenticated customer to create and reopen their own project.

 Implementation checklist
 01. Implement POST /projects with owner ID from validated claims, never from the request body.

 02. Implement GET /projects and GET /projects/{id}; support pagination and stable project status
      fields.

 03. Implement PATCH /projects/{id} with allowlisted updates, valid location/type fields and version
      checks where applicable.

 04. Protect records from cross-user access; handle duplicate submissions with a documented
      idempotency or uniqueness policy.

 05. Share persisted project IDs and query DTOs with M2, M3 and M5; connect frontend project
      wizard.

 Delivery and QA gate

   Deliverable                  Owner-scoped create/list/read/update endpoints and persisted draft project.


   Acceptance tests             Two-account test: user B cannot read/change A project; malformed name/type
                                rejected; same retry does not create unintended duplicate.


   Dependencies                 M4 project schema and MySQL migration; M5 consumes project query interface.


   Evidence to submit           CRUD tests, database evidence using synthetic records, OpenAPI and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 7

```text
DAY 4 OF 7 / TASK ID: BE-M1-D04


 Password recovery
 Deliver secure recovery APIs; agree email delivery configuration rather
 than assume it exists.

 Implementation checklist
 01. Implement forgot-password handler with same response for existing and unknown accounts.

 02. Generate cryptographically random reset tokens; persist only hashes, expiry and use state.

 03. Connect approved email delivery abstraction; do not log tokens or claim email sent when
      provider unavailable.

 04. Implement reset-password handler: validate token/hash/expiry, hash new password and consume
      token atomically.

 05. Coordinate M2 recovery UI contract; test rate limits and invalid/expired recovery requests.

 Delivery and QA gate

   Deliverable                  Recovery endpoints; email delivery verified only with configured provider.


   Acceptance tests             Single-use token cannot be replayed; expired token rejected; response does not
                                enumerate accounts. Unconfigured delivery reported as blocked.


   Dependencies                 M4 mail/config and transaction support. Recovery UI planned earlier may use
                                labeled fixtures until real API is available.


   Evidence to submit           Reset security tests, redacted delivery evidence (if available), contract and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 8

```text
DAY 5 OF 7 / TASK ID: BE-M1-D05


 Profile and session lifecycle
 Expose authenticated self-service profile and reliable session behavior.

 Implementation checklist
 01. Implement GET and PUT /users/me with explicitly editable profile fields only.

 02. Reject role, owner ID, status or password-hash changes through profile update.

 03. Implement agreed logout policy: revoke refresh/session token if applicable; otherwise document
      short-lived JWT and client logout behavior.

 04. Review disabled-account behavior, token expiry and sensitive logging with M4.

 05. Provide profile examples and current-user test helpers to M5/M7.

 Delivery and QA gate

   Deliverable                  Profile read/update and documented session/logout behavior.


   Acceptance tests             Unauthorized and overposting requests rejected; user cannot elevate privileges;
                                revocation/expiry behaves as agreed.


   Dependencies                 M4 current-user service and auth policy; M5 dashboard uses the same identity.


   Evidence to submit           Profile and session tests, redacted API samples, PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 9

```text
DAY 6 OF 7 / TASK ID: BE-M1-D06


 Project lifecycle and integration
 Make project ownership reusable and prevent invalid state changes.

 Implementation checklist
 01. Implement only approved project status transitions; prohibit client-driven final confirmation.

 02. Expose a reusable owner-check/query service for survey, requirements, generation, plan and
      report modules.

 03. Coordinate with M3 for confirmation-driven project status changes; no duplicate confirmation
      endpoint.

 04. Add concurrency tests and transactional handling for project updates.

 05. Run authenticated frontend-to-backend project creation and resume flow with partner modules.

 Delivery and QA gate

   Deliverable                  Stable project lifecycle and reusable owner guard.


   Acceptance tests             User cannot jump to confirmed state; stale update fails without partial changes;
                                owner checks consistent across modules.


   Dependencies                 M3 confirmation operation, M4 transaction handling, M2/M5 module integration.


   Evidence to submit           State-transition/owner-access tests, E2E log and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 10

```text
DAY 7 OF 7 / TASK ID: BE-M1-D07


 Security regression and handoff
 Finish the identity/project module and deliver evidence, not unverified
 claims.

 Implementation checklist
 01. Run all auth, reset, profile and project tests against a clean test database.

 02. Retest invalid JWT, expired session, enumeration, cross-account access and overposting.

 03. Run frontend login -> project create -> reopen -> logout with real API if available.

 04. Fix critical defects, document blocked provider or feature limitations and update OpenAPI
      examples.

 05. Submit final PR to develop; ask M7/independent reviewer to verify acceptance criteria.

 Delivery and QA gate

   Deliverable                  Reviewed auth/project module candidate with known limitations and test report.


   Acceptance tests             dotnet build/test pass; critical security failures resolved before Done; QA and code
                                approval recorded.


   Dependencies                 M4 infrastructure, M7 integration and independent QA; no self-approval.


   Evidence to submit           Final PR, test summary, issue list, contract changes and daily tracker update.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 11

```text
11 / TEAM HANDOFF


 Integration and Git workflow
 Cross-member dependencies

   Partner                  Handoff / contract

   M4                       Day 1: DB/provider, JWT middleware, DI/error envelope and migrations; agree entity
                            mapping.


   M5                       Expose owner-scoped project query interface for dashboard counts; no second project
                            list endpoint.


   M2/M3                    Provide owner authorization policy/project identifiers for their survey and plan APIs.


   M7                       OpenAPI examples, test accounts without real credentials, auth/security regression
                            evidence.


 Daily Git workflow
 If you already cloned the repository, do not clone it again. Start from updated develop. If the
 feature branch already exists, switch to it instead of recreating it.
 git clone -b develop https://github.com/Kogulanathan69/BuildMind-Platform.git
 cd BuildMind-Platform
 git switch develop
 git pull --ff-only origin develop
 git switch -c feature/m1-auth-projects
 git status --short
 Stage only your reviewed feature files. Commit with the daily task ID, push your branch, and open a PR into
 develop. Update your branch with develop according to team policy; resolve conflicts with file owners. Never
 force-push shared branches.
 Commit example: git commit -m "BE-M1-D01: implement feature foundation"
 git push -u origin feature/m1-auth-projects
```

## Source page 12

```text
12 / QUALITY & DAILY REPORT


 Definition of Done
 Every member is responsible for implementation, unit tests, integration tests where
 dependencies are ready, documentation and fixing defects. M7 coordinates integration / QA /
 review. A feature is not complete just because the PR exists.

 Before submitting a pull request
 01. Run dotnet build on src/BuildMind.API/BuildMind.API.csproj; run relevant C# test projects after
      they are created.

 02. For Python changes, use the project virtual environment, install approved dependencies and run
      relevant pytest tests from python-services.

 03. Verify real positive, negative and authorization cases; use synthetic or de-identified fixtures.
      Keep mock-only work labeled.

 04. Run git diff --check and inspect staged files; remove secrets, private data, generated build output
      and virtual environments.

 05. Link the task ID, acceptance tests, PR, screenshots/logs and known blockers in the delivery
      tracker.

 06. Request an independent code review; complete QA and CI checks. Team lead merges to develop
      after required gates; do not auto-mark Done from a commit.

 Daily handoff template

   Field                            What to report

   Task ID                          BE-M1-D01 ... BE-M1-D07 (proposed backend tracking IDs)


   Status                           Not Started / In Progress / Blocked / Review / Done


   Evidence                         PR URL, commit SHA/link, test log, API payload example, screenshot if useful


   Blocker / next step              Missing dependency, responsible owner, next action


   Acceptance / QA                  Pass, fail or pending; reviewer name; no invented results


 Planning notes
 This revised backend plan replaces overlapping backend ownership in the older M1-M7 7-day task plans. It does
 not change the frontend plans, repository or tracker automatically. Daily sequencing is a proposal and must be
 matched to actual capacity and API readiness. M7 RAG / integration is outside these six assignments. Admin
 features not assigned by the selected six modules require a separate scope decision.
```

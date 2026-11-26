# M4 — backend detailed task plan

Source: BuildMind_M4_Backend_7_Day_Task_Plan.pdf, revised proposal v2.0. Extracted page text preserves detailed daily checklists, API and ownership proposals. Current V3 decisions and approved actual contracts override conflicts. All tasks **planned/unverified**; never overwrite existing code. Read [ownership](../14-Current-Backend/10-OWNERSHIP.md) first.

## Source page 1

```text
MEMBER 4 / BACKEND DEVELOPMENT / REVISED PROPOSAL



 Clean Architecture, MySQL,
 Security & Infrastructure
 ASP.NET Core 10 | C# | EF Core with compatible MySQL provider | MySQL
 8.4

 Own the shared backend foundation that enables all other members to implement their modules
 without duplicate solutions, conflicting migrations or insecure integration.

 Module ownership
 01. Shared C# host, solution and Clean Architecture wiring

 02. MySQL provider, DbContext, migrations and shared transaction policy

 03. Environment configuration, authentication middleware, CORS, errors and logging

 04. C# to Python internal service infrastructure and private storage abstraction

 Ownership boundary
 M1 owns auth/profile/project endpoints and feature logic; M2 owns survey/land endpoints; M3
 owns editor/version/confirm; M5 owns requirements/report; M6 owns generation/validation. M4
 owns shared setup/security and schema migration coordination, not duplicate feature APIs.

 Development branch
 feature/m4-infrastructure


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
 src/BuildMind.API/Program.cs, middleware, configuration and shared endpoint registration
 src/BuildMind.Application/Abstractions/ (shared only; coordinate feature-specific contracts)
 src/BuildMind.Infrastructure/Persistence/ (DbContext, migration files, shared configuration)
 src/BuildMind.Infrastructure/Services/ (HTTP, storage, security adapters)
 src/BuildMind.Domain/ (shared primitives only; feature entities owned by members)
 tests/BuildMind.Infrastructure.Tests/ (proposed)


 Data model / persistence

   Entity / area                        Required data and constraints

   Schema / migrations                  M4 single migration coordinator. Feature members own entities/mapping
                                        proposals; review constraints and build migrations in order.


   User and Project / feature           Implement shared DbContext and FK conventions, not other members
   tables                               business definitions.


   Audit/operation records (if          Minimal security and operational events without secrets or sensitive
   agreed)                              payloads; retention policy agreed.


   Concurrency and consistency          Version tokens, transactions and unique keys; run clean database migration
                                        and rollback tests.


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

   GET /health                                                 Process health; do not claim DB/Python ready from
                                                               basic health alone.


   GET /health/ready (proposed)                                Dependency readiness with no internal connection
                                                               strings or detailed secrets exposed.


   OpenAPI development endpoint                                Version/response/error conventions, auth scheme
                                                               and feature routes; restrict in production.


   Shared ICurrentUser / token service                         Authenticated subject and JWT issuance/config; M1
                                                               invokes it.


   Shared repository/transaction abstractions                  DbContext and unit-of-work/transaction policy; no
                                                               generic duplicate feature routes.


   Internal HTTP clients                                       Typed C# adapters with configured FastAPI base
                                                               URL, timeouts, correlation IDs and safe errors.


   Private file storage abstraction                            Controlled local/dev or approved storage with safe
                                                               keys; M2 owns upload logic.


 All project routes are authenticated and owner-scoped; use the authenticated identity, not an arbitrary owner ID
 from the request. Return real errors rather than mock success.
```

## Source page 4

```text
DAY 1 OF 7 / TASK ID: BE-M4-D01


 Extend existing foundation and unblock
 the team
 Use the merged develop starter; publish shared contracts early.

 Implementation checklist
 01. Clone/pull develop and inspect already existing API/Application/Domain/Infrastructure projects,
      /health and FastAPI skeleton. Do not regenerate them.

 02. Verify dependency direction; add solution file only if agreed, and configure shared DI/feature
      registration approach.

 03. Publish environment configuration, common error shape, OpenAPI and CORS contract; set
      development API base URL.

 04. Agree current-user, password/token services, transaction abstraction and internal client
      interfaces with feature owners.

 05. Start MySQL provider compatibility check for net10.0 and publish expected connection/config
      interface for Day 2.

 Delivery and QA gate

   Deliverable                  Shared infrastructure contract and compiling C# foundation; feature owners can
                                start without conflicting Program.cs edits.


   Acceptance tests             dotnet build passes; /health contract stable; no secrets committed; dependency
                                direction verified.


   Dependencies                 All module owners supply entity/route proposals; M7 API contract sign-off.


   Evidence to submit           Build log, shared conventions, compatibility decision and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 5

```text
DAY 2 OF 7 / TASK ID: BE-M4-D02


 MySQL and identity/security foundation
 Enable real database integration for the first feature modules.

 Implementation checklist
 01. Select an EF Core MySQL provider compatible with project target; record exact package versions
      only after local restore and test.

 02. Configure DbContext and connection from environment/user secrets; create dev/test databases
      with least privilege.

 03. Integrate M1 User/Project mappings and agreed constraints; generate first controlled migration.

 04. Configure JWT validation/middleware and token service contract with M1; signing key outside
      source, expiry/issuer/audience checked.

 05. Apply migration on clean test database; document reset/rollback procedure and release
      connection details without passwords.

 Delivery and QA gate

   Deliverable                  Working MySQL dev/test connection and auth/project infrastructure.


   Acceptance tests             Restore/build pass; migration applies to empty DB; FK/unique constraints tested;
                                invalid/expired token rejected; no real secrets in files.


   Dependencies                 M1 entity mappings; M7 environment test; if provider incompatible, flag blocker and
                                agree alternative, do not claim done.


   Evidence to submit           Migration log, integration test, package/version register, redacted config and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 6

```text
DAY 3 OF 7 / TASK ID: BE-M4-D03


 Feature schema and migrations
 Coordinate schema changes so six developers can work safely in parallel.

 Implementation checklist
 01. Review survey, land, requirements, plan and generation entity proposals with M2/M3/M5/M6.

 02. Agree keys, owner relations, nullable fields, timestamps, geometry JSON and revision data
      representation; avoid storing binary PDFs in relational tables.

 03. Register feature mappings in shared DbContext using agreed extension pattern; one migration
      coordinator prevents conflicts.

 04. Apply migrations sequentially on disposable test DB; test FK constraints, rollback and data
      preservation with synthetic data.

 05. Publish schema/migration handoff and repository extension conventions to all owners.

 Delivery and QA gate

   Deliverable                  Versioned database schema and migration sequence.


   Acceptance tests             Fresh migration succeeds; referenced records valid; invalid FK rejected; rollback
                                tested on disposable DB only.


   Dependencies                 Feature owners deliver stable entity proposals; changes after contract freeze
                                reviewed by M4/M7.


   Evidence to submit           Migration files, schema diagram, test log and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 7

```text
DAY 4 OF 7 / TASK ID: BE-M4-D04


 Global error handling and shared security
 Harden API infrastructure consistently across module endpoints.

 Implementation checklist
 01. Implement shared exception/error mapping to agreed response shape with correlation ID and
      safe status code.

 02. Configure explicit dev CORS allowlist for Angular; do not use unrestricted origin with credentials.

 03. Add structured logging that redacts credentials, tokens, private files and sensitive payloads.

 04. Set shared auth/authorization policies, request limits and safe configuration defaults; feature
      owners enforce resource ownership.

 05. Add readiness check and validate middleware ordering; review development-only OpenAPI
      exposure.

 Delivery and QA gate

   Deliverable                  Shared security/error/logging pipeline integrated into API host.


   Acceptance tests             Invalid requests produce expected 4xx; unexpected exceptions return safe 5xx;
                                CORS and authorization tests pass; secrets absent from logs.


   Dependencies                 M1 auth, M2 upload limits, M7 error/QA conventions.


   Evidence to submit           Middleware tests, sanitized logs, CORS evidence, PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 8

```text
DAY 5 OF 7 / TASK ID: BE-M4-D05


 C# to Python service integration
 Enable authorized and reliable internal requests without exposing Python
 directly to customers.

 Implementation checklist
 01. Configure typed HTTP client and internal base URL per environment; coordinate shared DTO error
      mapping and version with M2/M3/M6.

 02. Add configurable timeouts, cancellation, correlation IDs and bounded retry only for
      safe/idempotent operations.

 03. Configure approved internal authentication/network restrictions; keep internal credentials in
      environment/secret storage.

 04. Provide private storage adapter for M2 and validate file reference handoff without public path
      leakage.

 05. Run health/readiness and service failure tests; ensure errors do not report fake success.

 Delivery and QA gate

   Deliverable                  Shared C#-FastAPI client and storage infrastructure.


   Acceptance tests             Unavailable Python yields truthful failure; timeout/cancel handled; internal
                                auth/config not exposed; safe retry does not duplicate jobs.


   Dependencies                 M2/M3/M6 Python endpoints; M7 integration; earlier days may use agreed adapter
                                interface/fixtures until actual service.


   Evidence to submit           Integration tests, failure examples, configured route documentation, PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 9

```text
DAY 6 OF 7 / TASK ID: BE-M4-D06


 Concurrency, transactions and database
 security
 Support version-safe persistence for editor, land and generation modules.

 Implementation checklist
 01. Publish a shared transaction/unit-of-work convention and optimistic concurrency token handling
      for MySQL.

 02. Coordinate unique keys for version/job/owner-scoped entities with module owners; keep writes
      atomic.

 03. Test concurrent edit/update and failure rollback using disposable test database; map conflicts
      consistently.

 04. Review minimum database permissions, connection strings, migration privileges and
      configuration secrets.

 05. Run full schema migration from empty database and check build/test CI with feature branches as
      available.

 Delivery and QA gate

   Deliverable                  Reliable shared transaction/concurrency and database integrity baseline.


   Acceptance tests             Failed transaction rolls back; stale token returns agreed conflict; migration test
                                passes; least-privilege config verified.


   Dependencies                 M2/M3/M6 revision/job operations; M5 report snapshot; M7 CI.


   Evidence to submit           Concurrency and rollback tests, schema log, PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 10

```text
DAY 7 OF 7 / TASK ID: BE-M4-D07


 Infrastructure regression and release
 readiness
 Verify the integrated shared backend and hand over setup instructions.

 Implementation checklist
 01. Run dotnet restore/build/test and MySQL migration on a clean isolated test environment.

 02. Start C# and FastAPI services and verify health/readiness, auth, internal service error mapping
      and CORS.

 03. Review secrets/config, dependency versions and critical vulnerabilities using available tooling;
      report unverified checks.

 04. Publish repeatable setup steps, environment variable names (no values), migration sequence and
      operational limitations.

 05. Review other members integration issues and submit final infrastructure PR for independent
      approval.

 Delivery and QA gate

   Deliverable                  Repeatable environment, validated infrastructure and release checklist.


   Acceptance tests             Clean setup works; critical security/DB failures resolved; no untested feature marked
                                ready; independent QA approval.


   Dependencies                 All feature owners and M7 QA/review.


   Evidence to submit           Final PR, build/test results, migration record and handover notes.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 11

```text
11 / TEAM HANDOFF


 Integration and Git workflow
 Cross-member dependencies

   Partner                  Handoff / contract

   M1                       Day 1 publish identity/current-user interfaces and DTO/error conventions; prioritize
                            auth/project DB schema.


   M2                       Day 1 storage interface / internal client contract; ensure survey/land migration available
                            before integration.


   M3                       Plan revision mapping, transactional save and optimistic concurrency contract.


   M5                       Requirements/report/architect tables and scoped query/DB conventions.


   M6 / M7                  Internal FastAPI contracts, secrets, service health, CI and environment documentation.


 Daily Git workflow
 If you already cloned the repository, do not clone it again. Start from updated develop. If the
 feature branch already exists, switch to it instead of recreating it.
 git clone -b develop https://github.com/Kogulanathan69/BuildMind-Platform.git
 cd BuildMind-Platform
 git switch develop
 git pull --ff-only origin develop
 git switch -c feature/m4-infrastructure
 git status --short
 Stage only your reviewed feature files. Commit with the daily task ID, push your branch, and open a PR into
 develop. Update your branch with develop according to team policy; resolve conflicts with file owners. Never
 force-push shared branches.
 Commit example: git commit -m "BE-M4-D01: implement feature foundation"
 git push -u origin feature/m4-infrastructure
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

   Task ID                          BE-M4-D01 ... BE-M4-D07 (proposed backend tracking IDs)


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

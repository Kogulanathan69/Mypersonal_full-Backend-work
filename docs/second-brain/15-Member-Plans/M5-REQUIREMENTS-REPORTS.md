# M5 — backend detailed task plan

Source: BuildMind_M5_Backend_7_Day_Task_Plan.pdf, revised proposal v2.0. Extracted page text preserves detailed daily checklists, API and ownership proposals. Current V3 decisions and approved actual contracts override conflicts. All tasks **planned/unverified**; never overwrite existing code. Read [ownership](../14-Current-Backend/10-OWNERSHIP.md) first.

## Source page 1

```text
MEMBER 5 / BACKEND DEVELOPMENT / REVISED PROPOSAL



 Home Requirements, Dashboard,
 PDF Reports & Architect Requests
 ASP.NET Core 10 | C# | MySQL | PDF generation library (approved)

 Own the business workflow from customer requirements to useful project summaries and
 preliminary reports. The report must reflect a real saved and confirmed plan, and architect
 sharing must be consent-based.

 Module ownership
 01. Customer dashboard summary and project progress views

 02. Ground-floor requirements, optional spaces, room relationships and summary

 03. Design method persistence and readiness contract

 04. Preliminary PDF reports and consent-based architect assistance requests

 Ownership boundary
 M1 owns profile and project CRUD/list; M6 owns generation jobs/results/selection. M3 owns final
 plan confirmation and immutable geometry versions. M4 owns migration files and shared
 storage/HTTP adapters. Do not duplicate these routes.

 Development branch
 feature/m5-requirements-reports


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
 src/BuildMind.API/Endpoints/Requirements/, Dashboard/, Reports/, ArchitectRequests/
 src/BuildMind.Application/Features/Requirements/, Dashboard/, Reports/, ArchitectRequests/
 src/BuildMind.Domain/Entities/ (RequirementRevision, Report, ArchitectRequest, Consent)
 src/BuildMind.Infrastructure/Persistence/Requirements/ and Reports/
 tests/BuildMind.Requirements.Tests/ and BuildMind.Reports.Tests/ (proposed)


 Data model / persistence

   Entity / area                        Required data and constraints

   RequirementRevisions                 ProjectId, revision, ground-floor counts, spaces, relationships, design
                                        method, timestamps; immutable input snapshot.


   Reports                              ProjectId, confirmed PlanVersionId, requirement revision reference, private
                                        artifact reference, generated time and status.


   ArchitectRequests                    ProjectId, recipient reference, selected report/documents, status, owner,
                                        timestamps; allowed transitions.


   ShareConsents                        RequestId, explicit consent time, selected documents/recipient and policy
                                        version; no implicit consent.


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

   GET /dashboard/summary                                      Owner-scoped project counts/status summaries;
                                                               consume M1 project query.


   GET /projects/{id}/requirements; PUT same route             Read/save validated current requirement
                                                               draft/revision, ground floor and optional fields.


   GET/PUT /projects/{id}/requirements/spaces                  Optional additional spaces; same requirement
                                                               revision lifecycle.


   GET/PUT /projects/{id}/requirements/relationships           Allowed optional adjacency preferences; validate
                                                               references.


   GET /projects/{id}/requirements/summary                     Combined persisted values, readiness and missing
                                                               items.


   PUT /projects/{id}/design-method                            Persist approved design method; do not generate
                                                               plans here.


   POST /projects/{id}/reports; GET                            Generate/read version-specific preliminary PDF with
   /projects/{id}/reports/{reportId}/download                  owner check.


   POST /projects/{id}/architect-requests; GET same            Explicit selected docs/recipient/consent; truthful
   route + /{requestId}                                        request status.


   POST /projects/{id}/architect-requests/{requestId}/         Allowed withdrawal only; authorized status transition.
   withdraw


 All project routes are authenticated and owner-scoped; use the authenticated identity, not an arbitrary owner ID
 from the request. Return real errors rather than mock success.
```

## Source page 4

```text
DAY 1 OF 7 / TASK ID: BE-M5-D01


 Requirements and business workflow
 models
 Define the server-side requirements model and snapshot contracts.

 Implementation checklist
 01. Inspect existing Application feature folders and extend only assigned modules.

 02. Define ground-floor room counts, optional additional spaces, relationship references and
      design-method DTOs; coordinate bounds with approved requirements.

 03. Define immutable requirement revision and current draft behavior, report snapshot and architect
      request models.

 04. Publish repository abstractions and request mapping/migration changes from M4.

 05. Agree readiness input/output with M6 and confirmation snapshot interface with M3.

 Delivery and QA gate

   Deliverable                  Compiling requirements/dashboard/report foundation and accepted DTO examples.


   Acceptance tests             Missing or unknown options rejected; optional fields can remain empty; no
                                unsupported counts/rules invented.


   Dependencies                 M4 shared DB conventions, M1 project query, M6 generation input, M3 revision
                                contract.


   Evidence to submit           Schema/DTO tests, migration request, contract examples and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 5

```text
DAY 2 OF 7 / TASK ID: BE-M5-D02


 Customer dashboard API
 Provide authentic owner-scoped summary data for the dashboard.

 Implementation checklist
 01. Implement GET /dashboard/summary with project counts/stages derived from actual saved
      project statuses.

 02. Use M1 project query/repository contract; do not implement duplicate GET /projects or profile
      endpoints.

 03. Handle genuine empty results; no hardcoded project or progress data.

 04. Map safe DTO fields for frontend cards and planning journey; coordinate status enum with M1/M7.

 05. Add two-account query tests and connect dashboard consumer with real API.

 Delivery and QA gate

   Deliverable                  Owner-specific dashboard summary API.


   Acceptance tests             Empty account returns zeros/empty state; account A cannot see B projects; counts
                                match database.


   Dependencies                 M1 authenticated owner and project query; M4 initial schema; frontend dashboard
                                consumer.


   Evidence to submit           Query tests, synthetic DB evidence, response sample and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 6

```text
DAY 3 OF 7 / TASK ID: BE-M5-D03


 Ground-floor requirements persistence
 Save and retrieve the actual customer room requirements.

 Implementation checklist
 01. Implement owner-scoped GET/PUT requirements endpoints with room counts, ground-floor scope
      and revision metadata.

 02. Validate request schema, allowed room types, count bounds and mandatory fields only from
      agreed rules.

 03. Store requirement inputs and new revision atomically; detect stale update and keep existing
      values on failure.

 04. Return saved DTO and error fields suitable for frontend counters and draft recovery.

 05. Test invalid/missing counts, owner isolation and refresh/reopen; publish early integration payload
      to M1/M2/M3.

 Delivery and QA gate

   Deliverable                  Persisted requirements draft/revision and real read/write API.


   Acceptance tests             Valid values survive reopen; invalid/duplicate fields rejected; stale save cannot
                                overwrite newer revision.


   Dependencies                 M1 owner check, M4 requirements migration, M6 schema.


   Evidence to submit           C# unit/integration tests, request examples, PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 7

```text
DAY 4 OF 7 / TASK ID: BE-M5-D04


 Additional spaces and room relationships
 Extend the requirement revision with optional choices and preferences.

 Implementation checklist
 01. Implement additional-spaces read/write with known options and optional other text with length
      limit.

 02. Implement relationship preferences referencing valid room types/IDs; reject duplicates and
      invalid references.

 03. Preserve core room counts when updating optional sections; use shared revision rules.

 04. Provide merged saved summary DTO; ensure optional preferences do not become mandatory
      without approved rule.

 05. Coordinate frontend multiselect and relationship screens, and test round-trip persistence.

 Delivery and QA gate

   Deliverable                  Optional spaces and relationships integrated into requirements.


   Acceptance tests             No selection allowed; invalid/duplicate options rejected; partial updates preserve
                                other sections; revisions accurate.


   Dependencies                 M1 ownership, M4 DB mapping, M6 consumer contract.


   Evidence to submit           Round-trip tests, examples, PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 8

```text
DAY 5 OF 7 / TASK ID: BE-M5-D05


 Summary, design method and readiness
 Deliver a trustworthy requirements snapshot for generation.

 Implementation checklist
 01. Build combined summary from latest persisted room counts, optional spaces and preferences;
      include source revision IDs.

 02. Implement design method update for agreed options; if manual path scope is uncertain, mark
      pending decision instead of inventing flow.

 03. Check owner, current confirmed land, required fields and allowed project state; return
      missing-item diagnostics.

 04. Publish immutable readiness/requirements query interface for M6, including exact land and
      requirement versions.

 05. Integrate summary/choice screen; ensure later requirements edits invalidate stale generation
      readiness.

 Delivery and QA gate

   Deliverable                  Summary, method and readiness APIs; M6 can consume exact input.


   Acceptance tests             Summary reflects saved values; incomplete/invalid readiness fails with specific
                                reasons; no fake plan preview or duplicate generation job.


   Dependencies                 M2 confirmed land, M6 generation job owner, M1 project status.


   Evidence to submit           Readiness tests, sample snapshot, API contract and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 9

```text
DAY 6 OF 7 / TASK ID: BE-M5-D06


 Preliminary PDF report generation
 Render actual plan/requirements evidence into a version-specific
 downloadable report.

 Implementation checklist
 01. Select approved PDF library and verify license/restore; define private output storage through M4
      abstraction.

 02. Consume M3 confirmed plan snapshot contract; include project summary, geometry-derived
      room/area data, validation notes and preliminary disclaimer.

 03. Build PDF renderer with a labeled confirmed test fixture; do not substitute unrelated static
      floor-plan images.

 04. Implement owner-scoped report generation/download, artifact status and clear
      failure/unavailable responses.

 05. Verify snapshot has matching geometry/requirements revision, output renders legibly and
      unconfirmed versions are rejected.

 Delivery and QA gate

   Deliverable                  Report renderer and protected report API candidate. Live confirmation integration
                                may complete Day 7.


   Acceptance tests             PDF opens and content matches fixture; missing artifact/unauthorized request fails;
                                no unconfirmed version labeled final.


   Dependencies                 M3 snapshot contract on Day 1, real confirmation Day 7; M4 private storage; M7 QA.


   Evidence to submit           Rendered sample PDF (test fixture), version evidence, tests and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 10

```text
DAY 7 OF 7 / TASK ID: BE-M5-D07


 Architect requests and final workflow QA
 Complete consent-based request workflow and report handoff.

 Implementation checklist
 01. Implement request creation with allowed recipient, explicit consent and selected
      document/report IDs; block absent consent or unauthorized documents.

 02. Store pending request status and actual events; implement read and allowed withdrawal; never
      fabricate acceptance/decline.

 03. Verify report generation using the real confirmed version from M3 if ready; keep draft/unavailable
      state if not.

 04. Test report access, stale revision, missing PDF, missing consent, wrong recipient and
      cross-account requests.

 05. Run requirements -> summary -> confirmation -> report -> architect request acceptance path,
      document limitations and submit PR.

 Delivery and QA gate

   Deliverable                  Preliminary report and architect workflow candidate with truthful statuses.


   Acceptance tests             Critical access/consent failures resolved; approved recipient/source required for real
                                sharing; no production/legal approval implied.


   Dependencies                 M3 confirmed snapshot, M4 storage/auth, M7 QA; external notification/directory if
                                approved.


   Evidence to submit           Final PR, PDF evidence, API/consent tests, blocker list and tracker update.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 11

```text
11 / TEAM HANDOFF


 Integration and Git workflow
 Cross-member dependencies

   Partner                  Handoff / contract

   M1                       Use project owner guard and project query interface; M1 owns profile and list.


   M2                       Use confirmed land revision and true status in readiness/summary.


   M3                       Consume exact confirmed plan snapshot; report generation never auto-confirms a plan.


   M6                       Publish requirements revision and readiness contract; M6 owns generation jobs and
                            alternatives.


   M4 / M7                  DB migrations, private report storage, authorization and QA; confirm architect
                            directory/recipient source and PDF library.


 Daily Git workflow
 If you already cloned the repository, do not clone it again. Start from updated develop. If the
 feature branch already exists, switch to it instead of recreating it.
 git clone -b develop https://github.com/Kogulanathan69/BuildMind-Platform.git
 cd BuildMind-Platform
 git switch develop
 git pull --ff-only origin develop
 git switch -c feature/m5-requirements-reports
 git status --short
 Stage only your reviewed feature files. Commit with the daily task ID, push your branch, and open a PR into
 develop. Update your branch with develop according to team policy; resolve conflicts with file owners. Never
 force-push shared branches.
 Commit example: git commit -m "BE-M5-D01: implement feature foundation"
 git push -u origin feature/m5-requirements-reports
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

   Task ID                          BE-M5-D01 ... BE-M5-D07 (proposed backend tracking IDs)


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

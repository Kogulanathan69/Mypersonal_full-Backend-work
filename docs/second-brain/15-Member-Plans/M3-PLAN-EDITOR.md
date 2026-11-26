# M3 — backend detailed task plan

Source: BuildMind_M3_Backend_7_Day_Task_Plan.pdf, revised proposal v2.0. Extracted page text preserves detailed daily checklists, API and ownership proposals. Current V3 decisions and approved actual contracts override conflicts. All tasks **planned/unverified**; never overwrite existing code. Read [ownership](../14-Current-Backend/10-OWNERSHIP.md) first.

## Source page 1

```text
MEMBER 3 / BACKEND DEVELOPMENT / REVISED PROPOSAL



 2D Editor Engine, Plan Versions &
 Final Confirmation
 ASP.NET Core 10 | C# | Python FastAPI | Shapely | MySQL

 Own the server-side editor operations and saved plan lifecycle. A room edit is a validated change
 to geometry, not a change to a disconnected picture.

 Module ownership
 01. Canonical editor plan data and edit commands

 02. Manual room/wall/opening edit operations and AI proposal workflow

 03. Validated atomic save and revision history

 04. Explicit final confirmation bound to the exact saved revision

 Ownership boundary
 M6 owns generation, feasibility and authoritative plan validation. M3 owns edit transformations
 and calls M6 validation; do not implement a second conflicting validator. M5 owns report
 generation. M4 owns shared DB migrations and HTTP client.

 Development branch
 feature/m3-plan-editor


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
 src/BuildMind.API/Endpoints/PlanEditor/ (proposed)
 src/BuildMind.Application/Features/PlanEditor/, PlanVersions/
 src/BuildMind.Domain/Entities/ (FloorPlan, PlanVersion, PlanConfirmation)
 src/BuildMind.Infrastructure/Persistence/Plans/
 python-services/app/plans/editor/ (proposed; M6 owns app/plans/generation/)
 python-services/tests/test_editor_*.py; tests/BuildMind.Editor.Tests/


 Data model / persistence

   Entity / area                        Required data and constraints

   FloorPlans                           Id, ProjectId, selected alternative reference (nullable for manual), current
                                        draft/revision.


   PlanVersions                         PlanId, version no, geometry JSON, requirements/land revision, validation
                                        reference, timestamps; immutable saved versions.


   PlanEditProposals                    PlanId, base revision, proposed operations, state, validation diagnostics;
                                        optional durable storage if needed.


   PlanConfirmations                    PlanVersionId, user ID, confirmed timestamp, acknowledgement; old
                                        confirmation does not apply to new revisions.


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

   POST /projects/{id}/plans                                   Create draft from selected alternative or allowed
                                                               manual flow; owner-scoped.


   GET /projects/{id}/plans/{planId}                           Current plan geometry and revision state.


   POST /projects/{id}/plans/{planId}/edits/validate           Base revision + commands -> candidate geometry
                                                               and diagnostics; no persisted change.


   POST /projects/{id}/plans/{planId}/ai-edit/proposals        User instruction -> proposed commands, explicit
                                                               Apply/Reject; unavailable if AI not configured.


   POST /projects/{id}/plans/{planId}/versions                 Expected base revision + validated candidate ->
                                                               saved immutable version or conflict.


   GET /projects/{id}/plans/{planId}/versions                  List versions; GET same route + /{versionId} returns
                                                               exact version.


   POST /projects/{id}/plans/{planId}/confirm                  Exact saved version + explicit acknowledgement;
                                                               server rechecks current validation.


   Internal Python: POST /plans/editor/apply                   Geometry + commands -> candidate geometry; M6
                                                               validates authoritative result.


 All project routes are authenticated and owner-scoped; use the authenticated identity, not an arbitrary owner ID
 from the request. Return real errors rather than mock success.
```

## Source page 4

```text
DAY 1 OF 7 / TASK ID: BE-M3-D01


 Plan schema and editor backend
 foundation
 Agree canonical geometry model before building transformations.

 Implementation checklist
 01. Inspect existing C# projects and Python plans/validation packages; create only editor-owned
      subfolders.

 02. Define plan, room, wall, door/window and opening DTOs with stable unique IDs, units in metres
      and coordinate origin.

 03. Define edit commands (add/remove/move/resize) and base revision/version token, plus
      structured error response.

 04. Agree generator output and authoritative validator interface with M6; define manual draft
      creation from land/requirements.

 05. Create fixture JSON and serialization tests; request M4 plan/version schema migration.

 Delivery and QA gate

   Deliverable                  Compiling editor feature and agreed plan JSON contract.


   Acceptance tests             Round-trip JSON preserves IDs and geometry; duplicate IDs/invalid dimensions
                                rejected; shared fixture consumed by M6 and renderer.


   Dependencies                 M6 schema/validator contract, M2 land convention, M4 migrations.


   Evidence to submit           Schema sample, test report, PR and change-contract note.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 5

```text
DAY 2 OF 7 / TASK ID: BE-M3-D02


 Room add, remove and move operations
 Implement deterministic backend command application on a copy of base
 geometry.

 Implementation checklist
 01. Implement Python edit dispatcher and room add/remove/move handlers; require explicit IDs and
      valid numeric inputs.

 02. Maintain stable relationships when rooms are removed or moved; reject dangling references.

 03. Return candidate geometry and changed IDs; do not mutate the source object or save an
      unvalidated candidate.

 04. Call agreed validation interface (use clearly labeled test fake until M6 implementation ready).

 05. Add C# edit validation request DTO and service abstraction.

 Delivery and QA gate

   Deliverable                  Testable room command engine returning candidate geometry.


   Acceptance tests             Valid add/move/remove works on fixtures; unknown ID and invalid coordinates
                                rejected; original input unchanged.


   Dependencies                 M6 validator contract; M4 HTTP abstraction. Full validation blocked until actual M6
                                validator.


   Evidence to submit           pytest transformation tests, C# contract test and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 6

```text
DAY 3 OF 7 / TASK ID: BE-M3-D03


 Room resize and wall topology
 Implement geometry edits with safe, deterministic neighbor handling.

 Implementation checklist
 01. Implement resize with finite positive dimensions and explicit anchor; update related wall
      coordinates and properties.

 02. Implement wall move/update with reference preservation; reject unsupported topology rather
      than silently corrupt shape.

 03. If a neighbor adjustment is supported, compute it on the candidate copy and show changed IDs;
      require whole-plan validation.

 04. Send candidate to M6 validator; block overlaps/out-of-bound and critical errors, retain original
      plan on failure.

 05. Add tests for irregular lots, shared walls, zero-size elements and rollback behavior.

 Delivery and QA gate

   Deliverable                  Room/wall edit operations and validation adapter integration candidate.


   Acceptance tests             Successful resize consistent with dimensions; invalid overlap causes no persisted or
                                in-memory source mutation; no silent neighbor changes beyond command result.


   Dependencies                 M6 authoritative geometry checks and M2 confirmed land fixture.


   Evidence to submit           Operation fixtures, before/after JSON, test logs and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 7

```text
DAY 4 OF 7 / TASK ID: BE-M3-D04


 Doors, windows and opening operations
 Support editor element placement and backend validation.

 Implementation checklist
 01. Implement add/move/remove opening commands with unique IDs, parent wall ID, offset and
      width.

 02. Check local command preconditions such as existing wall and valid finite sizes; preserve
      geometry references.

 03. Delegate full intersection, wall fit and connectivity checks to M6; reject critical invalid results.

 04. Implement C# edit validation endpoint returning candidate, changed IDs and severity-tagged
      diagnostics.

 05. Publish response examples for property panel, error state and supported operations.

 Delivery and QA gate

   Deliverable                  Door/window and opening edit candidate API.


   Acceptance tests             Valid fixture attaches opening; unknown wall/invalid size rejected; overlapping or
                                out-of-wall opening fails authoritative check.


   Dependencies                 M6 opening validation and M4 C# adapter/DI; frontend editor integration.


   Evidence to submit           pytest and C# tests, DTO example, PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 8

```text
DAY 5 OF 7 / TASK ID: BE-M3-D05


 AI edit proposals and Apply/Reject
 Keep AI changes user-controlled, validated and revision-aware.

 Implementation checklist
 01. Define proposal request with project/plan/base version and natural-language instruction.

 02. Use approved AI adapter coordinated with M7 to translate into allowed edit commands; validate
      command schema and permissions.

 03. Return proposed changes and M6 diagnostic result without updating saved geometry.

 04. Apply only after explicit user action and revalidation against current base revision; Reject leaves
      geometry/version unchanged.

 05. If AI service is unavailable, return honest unavailable response; manually test deterministic
      proposal fixtures marked demo.

 Delivery and QA gate

   Deliverable                  AI edit proposal orchestration; explicit user control contract.


   Acceptance tests             Reject and stale proposal leave state unchanged; invalid proposed edit blocked; no
                                fabricated AI success.


   Dependencies                 M7 AI/RAG adapter; M6 validator; M4 transaction support.


   Evidence to submit           Proposal examples, Apply/Reject tests and PR; provider limitation if any.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 9

```text
DAY 6 OF 7 / TASK ID: BE-M3-D06


 Save plan and version history
 Persist only valid or explicitly permitted draft states with atomic version
 handling.

 Implementation checklist
 01. Implement create/read plan from M6 selected alternative or approved manual start; enforce
      owner and source revision.

 02. Implement save version with expected base token, immutable revision JSON and validation
      reference; use M4 transaction/concurrency infrastructure.

 03. Revalidate candidate on server before accepting changes; reject critical invalid edits and stale
      base revisions atomically.

 04. Implement list/get version endpoints and ensure prior versions cannot be silently overwritten.

 05. Return exact revision ID, diagnostics and conflict status to UI; integrate save flow.

 Delivery and QA gate

   Deliverable                  Saved plan, immutable version history, validated edit rollback.


   Acceptance tests             Concurrent saves yield one accepted revision and conflict as appropriate; failed save
                                changes no data; reopened version matches geometry.


   Dependencies                 M6 alternative selection/validator, M4 schema/transactions, M2 confirmed land.


   Evidence to submit           MySQL revision tests, before/after state, OpenAPI and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 10

```text
DAY 7 OF 7 / TASK ID: BE-M3-D07


 Final confirmation and editor regression
 Confirm the exact saved revision and pass it to the report owner.

 Implementation checklist
 01. Implement final confirm endpoint with owner, explicit acknowledgement, exact saved revision
      and fresh validation check.

 02. Block critical errors, outdated requirements/land, missing evidence or stale revision; apply
      warning policy agreed with M7.

 03. Persist confirmation atomically and call M1 approved project lifecycle transition; later editing
      invalidates confirmation for the new revision.

 04. Expose confirmed snapshot interface to M5; test manual/AI edit, save, history and final review
      flow.

 05. Fix critical editor defects and request independent QA before final PR merge.

 Delivery and QA gate

   Deliverable                  Revision-specific confirmation and reliable editor lifecycle candidate.


   Acceptance tests             Invalid/stale/unconfirmed revisions cannot become confirmed; Reject keeps plan
                                unchanged; PDF receives matching confirmed geometry only.


   Dependencies                 M1 project access/status, M5 report, M6 validation, M7 QA.


   Evidence to submit           Full regression, confirmation evidence, final PR and open limitations.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 11

```text
11 / TEAM HANDOFF


 Integration and Git workflow
 Cross-member dependencies

   Partner                  Handoff / contract

   M6                       Day 1 shared plan schema, stable IDs, metric coordinate system; M6 provides
                            validator/alternative reference.


   M2                       Confirmed land revision and boundary for edit validation.


   M4                       Plan tables, DB transaction/concurrency, internal HTTP adapters and migrations.


   M1 / M5                  Owner policy and confirmation status; M5 consumes exact confirmed version for PDF.


   M7                       RAG/AI edit adapter and acceptance QA; no silent AI changes.


 Daily Git workflow
 If you already cloned the repository, do not clone it again. Start from updated develop. If the
 feature branch already exists, switch to it instead of recreating it.
 git clone -b develop https://github.com/Kogulanathan69/BuildMind-Platform.git
 cd BuildMind-Platform
 git switch develop
 git pull --ff-only origin develop
 git switch -c feature/m3-plan-editor
 git status --short
 Stage only your reviewed feature files. Commit with the daily task ID, push your branch, and open a PR into
 develop. Update your branch with develop according to team policy; resolve conflicts with file owners. Never
 force-push shared branches.
 Commit example: git commit -m "BE-M3-D01: implement feature foundation"
 git push -u origin feature/m3-plan-editor
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

   Task ID                          BE-M3-D01 ... BE-M3-D07 (proposed backend tracking IDs)


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

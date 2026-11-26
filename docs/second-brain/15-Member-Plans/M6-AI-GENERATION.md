# M6 — backend detailed task plan

Source: BuildMind_M6_Backend_7_Day_Task_Plan.pdf, revised proposal v2.0. Extracted page text preserves detailed daily checklists, API and ownership proposals. Current V3 decisions and approved actual contracts override conflicts. All tasks **planned/unverified**; never overwrite existing code. Read [ownership](../14-Current-Backend/10-OWNERSHIP.md) first.

## Source page 1

```text
MEMBER 6 / BACKEND DEVELOPMENT / REVISED PROPOSAL



 AI Plan Generation, Feasibility &
 Geometry Validation
 Python 3.13 | FastAPI | Shapely | Pydantic | C# orchestration | MySQL

 Own the generation and authoritative validation backend. Generate actual structured 2D
 geometry from confirmed land and saved requirement revisions; return three distinct feasible
 alternatives only when supported by valid input.

 Module ownership
 01. Buildable footprint and geometry-based feasibility

 02. Room placement, adjacency and distinct alternative generation

 03. Authoritative topology, collision, footprint and supported rule checks

 04. C# generation orchestration, results, comparison and selection APIs

 Ownership boundary
 M2 owns survey extraction and land polygon validation; M3 owns edit commands,
 save/version/confirmation. M6 provides authoritative floor-plan validator to M3 but does not save
 editor revisions. M7 owns regulatory retrieval and AI/RAG infrastructure. M4 owns shared
 DB/HTTP foundation.

 Development branch
 feature/m6-ai-generation


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
 python-services/app/plans/generation/ (proposed)
 python-services/app/geometry/footprint/ and app/validation/
 python-services/tests/test_generation_*.py and test_validation_*.py
 src/BuildMind.API/Endpoints/Generation/ (proposed)
 src/BuildMind.Application/Features/Generation/
 src/BuildMind.Domain/Entities/ (GenerationJob, PlanAlternative);
 Infrastructure/Persistence/Generation/


 Data model / persistence

   Entity / area                        Required data and constraints

   GenerationJobs                       ProjectId, exact land/requirement revisions, state, correlation ID, error,
                                        timestamps, idempotency key.


   PlanAlternatives                     JobId, stable alternative ID, canonical geometry JSON, computed measures,
                                        supported validation results and provenance.


   SelectedAlternatives                 Project/job/alternative reference, owner and timestamp; M3 consumes
                                        selection to create draft plan.


   ValidationResults                    Geometry/input version reference, severity diagnostics, rule
                                        source/provenance and engine version; coordinate storage with M3.


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

   Internal Python: POST /geometry/footprint/feasibility       Confirmed metric polygon + known constraints ->
                                                               footprint/feasibility/diagnostics.


   Internal Python: POST /plans/generate                       Exact input snapshot -> 3 distinct feasible
                                                               alternatives or honest failed/infeasible result.


   Internal Python: POST /plans/validate                       Candidate geometry + land/requirements/rules ->
                                                               structured critical errors/warnings, metrics.


   POST /projects/{id}/generation-jobs                         Owner-scoped generation request; persist exact
                                                               input revisions and truthful job state.


   GET /projects/{id}/generation-jobs/{jobId}                  Real status/results and alternative IDs; no fabricated
                                                               percentage.


   GET /projects/{id}/generation-jobs/{jobId}/alternativ       Computed comparison data grounded in geometry.
   es


   POST /projects/{id}/generation-jobs/{jobId}/select          Select exact valid alternative, reject stale input/job;
                                                               M3 creates plan draft.


 All project routes are authenticated and owner-scoped; use the authenticated identity, not an arbitrary owner ID
 from the request. Return real errors rather than mock success.
```

## Source page 4

```text
DAY 1 OF 7 / TASK ID: BE-M6-D01


 Generation and validation contracts
 Agree exact schemas and supported fixture scope before writing
 algorithms.

 Implementation checklist
 01. Inspect existing FastAPI app; extend plans/geometry/validation modules without creating a
      second app.

 02. Define Pydantic generation input (land revision, requirements revision, rooms, constraints,
      units/CRS) and canonical geometry response with stable IDs.

 03. Agree room/wall/door/window schema, edit validation interface and severity/error codes with M3;
      agree metric land model with M2.

 04. Define job state and idempotency/persistence contracts in C# feature layer; request M4
      migrations and publish API examples.

 05. Set deterministic seed/fixture conventions, unsupported-case policy and pytest smoke/schema
      tests.

 Delivery and QA gate

   Deliverable                  Shared geometry schema, generation/validation contracts and compiling skeleton.


   Acceptance tests             Schemas reject malformed geometry/IDs; not-implemented stub clearly reports
                                unavailable; no fake plans.


   Dependencies                 M2 and M5 contracts, M3 editor schema, M4 internal client, M7 rule provenance.


   Evidence to submit           Schema JSON, test log, C#/Python PR and migration request.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 5

```text
DAY 2 OF 7 / TASK ID: BE-M6-D02


 Buildable footprint and feasibility
 Determine if supported known constraints permit a preliminary building
 footprint.

 Implementation checklist
 01. Accept M2 metric boundary and exact source revision; validate input and unit conventions.

 02. Compute candidate buildable region using known verified setbacks/constraints with explicit
      assumptions and Shapely; never invent local legal limits.

 03. Calculate usable area and compare with required room/space area estimates where agreed.

 04. Return feasible/infeasible/needs-review states and structured reasons; missing regulatory inputs
      remain unverified.

 05. Test rectangle, irregular polygon, narrow lot, missing rule and insufficient buildable area fixtures.

 Delivery and QA gate

   Deliverable                  Feasibility endpoint and reproducible geometry tests.


   Acceptance tests             No candidate outside valid buildable area; missing rules flagged; empty footprint
                                produces honest infeasible result.


   Dependencies                 M2 metric boundary fixtures; M5 requirement area assumptions; M7 sourced rules or
                                explicit unknowns.


   Evidence to submit           pytest numeric/geometry fixtures, response examples and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 6

```text
DAY 3 OF 7 / TASK ID: BE-M6-D03


 Room placement and adjacency algorithms
 Build deterministic, inspectable baseline layouts for supported cases.

 Implementation checklist
 01. Implement room program normalization from saved requirement counts/types with stable IDs.

 02. Design a bounded room placement algorithm for supported land/footprint forms; assign
      dimensions from approved constraints, not invented legal standards.

 03. Represent room polygons, walls and shared boundaries; compute adjacency preference scores
      and circulation checks where implemented.

 04. Reject overlapping/out-of-footprint candidates; keep diagnostics and bounded search runtime.

 05. Create fixture tests of required room counts, adjacency preference and no placement solution.

 Delivery and QA gate

   Deliverable                  Working candidate layout generator for a documented supported fixture scope.


   Acceptance tests             Geometry valid on known fixture; every requested supported room accounted for;
                                impossible input returns failure.


   Dependencies                 M5 normalized requirements; M2 metrics, M3 shared plan JSON; M7 rule constraints.


   Evidence to submit           Candidate JSON, algorithm summary, pytest fixtures and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 7

```text
DAY 4 OF 7 / TASK ID: BE-M6-D04


 Layout diversity and backend heuristics
 Generate meaningfully different alternatives and authoritative core
 validation.

 Implementation checklist
 01. Implement bounded search/variation with deterministic reproducibility; compare layout
      topology/room placement, not just IDs or labels.

 02. Implement geometry validation for positive area, overlaps, footprint containment, topology and
      supported openings/connectivity checks.

 03. Apply backend-only Vastu preference only as an approved soft ranking heuristic; do not expose
      labels or block user edits for this reason.

 04. Integrate known sourced regulatory constraints from M7; return unverified warning/critical
      according to agreed policy if missing.

 05. Publish validator contract to M3 and tests for malformed/invalid and acceptable candidates.

 Delivery and QA gate

   Deliverable                  Distinct candidate search and callable authoritative validator.


   Acceptance tests             Overlap/outside footprint invalid; duplicate layouts rejected; Vastu does not act as
                                mandatory edit gate; unknown rules not declared compliant.


   Dependencies                 M3 editor validation integration; M7 verified rules/heuristic; M2 land geometry.


   Evidence to submit           Validation fixtures, diversity tests, diagnostic samples and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 8

```text
DAY 5 OF 7 / TASK ID: BE-M6-D05


 Three alternatives and generation job
 orchestration
 Deliver C# public generation lifecycle backed by actual Python output.

 Implementation checklist
 01. Implement generation endpoint in C# using M1 owner guard and M5 readiness snapshot; persist
      exact input revisions and idempotent job key.

 02. Call Python generation through M4 typed adapter; implement agreed actual job lifecycle
      (queued/running/succeeded/failed) with durable state.

 03. Validate each candidate and ensure exactly three distinct feasible alternatives for successful
      fixture; if fewer valid, fail/return explicit insufficient result without fake options.

 04. Persist alternative IDs and geometry/metadata atomically; expose real status and results.

 05. Test unavailable Python, timeout, malformed input, duplicate request and stale
      requirements/land.

 Delivery and QA gate

   Deliverable                  Generation job and three valid test-fixture alternatives, or truthful failure.


   Acceptance tests             Successful job has exactly 3 distinct validated alternatives; no partial success
                                mislabeled; retry does not create unintended duplicate job.


   Dependencies                 M4 DB/adapter early contract; M5 readiness, M2 confirmed land, M3 schema; M7
                                rule retrieval.


   Evidence to submit           Job/alternative DB evidence, integration tests, geometry JSON and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 9

```text
DAY 6 OF 7 / TASK ID: BE-M6-D06


 Comparison, selection and editor
 validation support
 Enable safe alternative selection and authoritative validation for edited
 candidates.

 Implementation checklist
 01. Compute comparison area, room counts and supported metrics directly from plan geometry; do
      not invent scores.

 02. Implement owner-scoped alternative read/select, validate selected ID belongs to current job and
      exact source revisions.

 03. Persist selected alternative reference; hand off to M3 draft creation service without duplicating
      plan save/version logic.

 04. Expand validator for M3 edit candidates, wall/opening bounds, required rooms and supported rule
      checks; return critical/warning codes.

 05. Test duplicate IDs, opening collisions, overlaps, missing constraints, stale selection and validator
      failure.

 Delivery and QA gate

   Deliverable                  Comparison/selection APIs and editor-usable deterministic validation endpoint.


   Acceptance tests             Selected reference matches returned geometry; invalid candidate blocked; stale job
                                cannot be selected; validator failures not treated as pass.


   Dependencies                 M3 plan draft/save, M4 transactions, M7 regulation provenance.


   Evidence to submit           Selection/validation tests, response examples and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 10

```text
DAY 7 OF 7 / TASK ID: BE-M6-D07


 Generation and validation regression
 Verify supported planning flow and report realistic limitations.

 Implementation checklist
 01. Run confirmed land -> requirements -> feasibility -> generate -> compare -> select ->
      edit-validation path with real service integrations.

 02. Run deterministic and failure fixtures for irregular lands, narrow footprints, impossible counts,
      malformed commands and unavailable rules.

 03. Retest exactly-three distinctness, stable IDs, measured geometry and input revision consistency;
      compare with M3 and M5 report data.

 04. Document supported house/shape constraints, algorithm limitations and cases requiring
      professional review; do not claim nationwide regulatory verification.

 05. Submit final PR with test evidence and ask M7/independent QA to verify the end-to-end gate.

 Delivery and QA gate

   Deliverable                  Validated generation candidate for demonstrated inputs and documented blockers.


   Acceptance tests             pytest/dotnet tests pass; critical geometry/ownership failures resolved; unsupported
                                cases clearly fail or request review.


   Dependencies                 All module owners; M7 QA and RAG checks.


   Evidence to submit           Final PR, test summary, fixture output and limitation report.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 11

```text
11 / TEAM HANDOFF


 Integration and Git workflow
 Cross-member dependencies

   Partner                  Handoff / contract

   M2                       Confirmed metric boundary, CRS/origin and geometry revision. No duplicate survey
                            pipeline.


   M5                       Approved requirement snapshot, readiness and design method; no duplicate
                            requirement APIs.


   M3                       Shared geometry schema, operation candidate validation, selected alternative
                            reference; M3 owns final save/confirm.


   M4                       Typed client, transaction and migration convention; coordinate C# generation
                            registration.


   M7                       Verified rule retrieval/source metadata, backend-only soft Vastu heuristic if approved, AI
                            provider/config and QA.


 Daily Git workflow
 If you already cloned the repository, do not clone it again. Start from updated develop. If the
 feature branch already exists, switch to it instead of recreating it.
 git clone -b develop https://github.com/Kogulanathan69/BuildMind-Platform.git
 cd BuildMind-Platform
 git switch develop
 git pull --ff-only origin develop
 git switch -c feature/m6-ai-generation
 git status --short
 Stage only your reviewed feature files. Commit with the daily task ID, push your branch, and open a PR into
 develop. Update your branch with develop according to team policy; resolve conflicts with file owners. Never
 force-push shared branches.
 Commit example: git commit -m "BE-M6-D01: implement feature foundation"
 git push -u origin feature/m6-ai-generation
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

   Task ID                          BE-M6-D01 ... BE-M6-D07 (proposed backend tracking IDs)


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

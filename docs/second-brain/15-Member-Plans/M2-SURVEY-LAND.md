# M2 — backend detailed task plan

Source: BuildMind_M2_Backend_7_Day_Task_Plan.pdf, revised proposal v2.0. Extracted page text preserves detailed daily checklists, API and ownership proposals. Current V3 decisions and approved actual contracts override conflicts. All tasks **planned/unverified**; never overwrite existing code. Read [ownership](../14-Current-Backend/10-OWNERSHIP.md) first.

## Source page 1

```text
MEMBER 2 / BACKEND DEVELOPMENT / REVISED PROPOSAL



 Survey Processing, Location &
 Land Geometry
 ASP.NET Core 10 | Python 3.13 | FastAPI | Shapely / PyProj | MySQL

 Own the secure survey file intake, extraction workflow and land boundary backend. Convert
 documented survey information into reviewable geometry, not an invented legal survey.

 Module ownership
 01. Secure PDF/JPG/PNG survey upload and metadata

 02. Python extraction and manual-review workflow

 03. Location and boundary normalization, area and perimeter

 04. Boundary update, validation and explicit land confirmation

 Ownership boundary
 M6 owns building footprint and floor-plan validation, not land ingestion. M3 owns editor geometry
 operations after a plan exists. M4 owns shared storage abstraction, HTTP adapter foundation and
 migrations. Do not create a second FastAPI application.

 Development branch
 feature/m2-survey-land


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
 src/BuildMind.API/Endpoints/Survey/ and Land/ (proposed)
 src/BuildMind.Application/Features/Survey/, Land/
 src/BuildMind.Domain/Entities/ (SurveyPlan, LandBoundary)
 src/BuildMind.Infrastructure/Survey/ and Persistence/Land/
 python-services/app/survey/ and app/geometry/land/ (proposed)
 python-services/tests/test_survey_*.py; tests/BuildMind.Survey.Tests/


 Data model / persistence

   Entity / area                        Required data and constraints

   SurveyPlans                          Id, ProjectId, private storage reference, content hash, MIME, size, status,
                                        timestamps.


   SurveyExtractionResults              SurveyId, structured fields, source/page, confidence/uncertainty, errors,
                                        extraction revision.


   LandBoundaries                       ProjectId, geometry version, CRS/unit metadata, source, measured
                                        area/perimeter, review/confirmation state.


   LandLocations                        ProjectId, longitude/latitude, selected locality IDs, source, updated time; no
                                        automatic legal verification.


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

   POST /projects/{id}/surveys                                 Multipart file -> survey ID/status; validate owner,
                                                               limits, signatures.


   GET /projects/{id}/surveys/{surveyId}                       Processing status, structured extraction result,
                                                               missing fields and diagnostics.


   GET /projects/{id}/land; PUT /projects/{id}/land            Location and boundary draft; version-aware edit;
                                                               explicit CRS.


   POST /projects/{id}/land/validate                           Candidate polygon -> normalized geometry,
                                                               area/perimeter and errors.


   POST /projects/{id}/land/confirm                            Exact validated boundary revision + explicit
                                                               confirmation -> confirmed state.


   Internal Python: POST /survey/extract                       Secure file handoff -> structured
                                                               known/missing/uncertain fields.


   Internal Python: POST /geometry/land/validate               Polygon with CRS/units -> validity, metrics and
                                                               diagnostics.


 All project routes are authenticated and owner-scoped; use the authenticated identity, not an arbitrary owner ID
 from the request. Return real errors rather than mock success.
```

## Source page 4

```text
DAY 1 OF 7 / TASK ID: BE-M2-D01


 Survey contracts and land models
 Prepare backend data contracts and safe storage before implementing
 extraction.

 Implementation checklist
 01. Inspect existing survey/geometry FastAPI packages and C# feature layers; extend rather than
      duplicate.

 02. Define survey metadata, extraction states and boundary revision models; distinguish unknown
      from zero.

 03. Define Pydantic and C# DTOs with file ID, document fields, confidence and source reference.

 04. Specify WGS84 longitude/latitude for map display and explicit projected/local metric coordinates
      for calculations; document origin and units.

 05. Publish valid/empty/invalid sample payloads and request M4 schema migration.

 Delivery and QA gate

   Deliverable                  Reviewed file/extraction/land contracts and compile-ready feature structure.


   Acceptance tests             DTO and schema tests reject missing IDs/invalid units; sample data labeled; no
                                extraction success claimed.


   Dependencies                 M4 storage and DB contracts; M1 project identity; M6 coordinate convention.


   Evidence to submit           Schema examples, tests, branch commit and ownership map.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 5

```text
DAY 2 OF 7 / TASK ID: BE-M2-D02


 Secure survey upload API
 Accept supported survey documents and persist private storage metadata
 safely.

 Implementation checklist
 01. Implement authenticated multipart upload for PDF/JPG/PNG with configurable size limits and file
      signature checks.

 02. Generate server-side storage keys; reject unsafe filenames, oversized and unsupported files;
      avoid public raw file URLs.

 03. Persist metadata/hash and actual status; coordinate duplicate handling and cleanup with M4.

 04. Implement status read endpoint and Python processing handoff interface; use queued/failed
      states truthfully.

 05. Add test samples and share upload/error contract with the frontend.

 Delivery and QA gate

   Deliverable                  Real upload and owner-scoped status API; private storage reference persists.


   Acceptance tests             Valid files accepted; wrong MIME/signature, unsafe path, cross-owner and oversize
                                rejected; no storage secret logged.


   Dependencies                 M1 project authorization, M4 storage and DB migration. Use a local controlled test
                                store if approved.


   Evidence to submit           Upload/security tests, status example, redacted logs and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 6

```text
DAY 3 OF 7 / TASK ID: BE-M2-D03


 Survey extraction pipeline
 Read supported files into structured fields with uncertainty handling.

 Implementation checklist
 01. Implement Python PDF text extraction for readable documents; use approved OCR adapter for
      image-only documents only if available.

 02. Extract recognized values such as survey/lot reference, boundary dimensions and reported area
      when present; keep source page and raw evidence.

 03. Return null/missing/uncertain fields rather than guessing; never infer a georeferenced polygon
      from incomplete measurements.

 04. Connect C# processing status/results to Python service; handle timeout, corrupt file, unavailable
      OCR and retry limits.

 05. Enable a manual-review path for unrecognized or incomplete survey documents.

 Delivery and QA gate

   Deliverable                  Structured extraction endpoint and stored result for supported test fixture.


   Acceptance tests             Readable, scanned/unconfigured OCR, corrupt and incomplete examples produce
                                truthful outcomes; manual review available.


   Dependencies                 M4 internal HTTP client setup; synthetic/de-identified survey fixtures; M7 QA.


   Evidence to submit           pytest, C# adapter tests, extraction result samples with sources and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 7

```text
DAY 4 OF 7 / TASK ID: BE-M2-D04


 Boundary geometry and coordinate
 normalization
 Produce valid candidate polygon geometry for land confirmation.

 Implementation checklist
 01. Accept manual polygon points or extraction-derived geometry only when sufficient data is
      available.

 02. Validate coordinate ranges, coordinate order, ring closure, distinct vertices, nonzero area and
      self-intersections using Shapely.

 03. Normalize boundary orientation and preserve coordinate reference system; use PyProj for
      appropriate metric projection.

 04. Return map-display GeoJSON (lon/lat when georeferenced) and metric geometry for calculation;
      avoid fabricating location.

 05. Expose structured diagnostic codes and test irregular/inverted/invalid polygons.

 Delivery and QA gate

   Deliverable                  Python land validation endpoint and reviewable candidate boundary.


   Acceptance tests             Known rectangle and irregular fixture valid; self-intersections and degenerate
                                geometry rejected; CRS round-trip within agreed tolerance.


   Dependencies                 M6 metric convention and M4 adapter; frontend geometry contract from M2/M3
                                plans.


   Evidence to submit           Geometry fixture JSON, numeric tests, map contract and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 8

```text
DAY 5 OF 7 / TASK ID: BE-M2-D05


 Location, area and perimeter
 Calculate land metrics from the confirmed coordinate model and save
 location information.

 Implementation checklist
 01. Implement location DTO with selected locality/source and optional longitude/latitude; validate
      input ranges.

 02. Integrate approved geocoding service only if keys/coverage available; distinguish user-selected
      from verified locality.

 03. Compute polygon area (m2), perimeter (m) and optional display conversions using projected
      units.

 04. Preserve reported survey area separately from calculated area; flag discrepancy for manual
      review.

 05. Expose saved land/location read and draft update endpoints with version metadata.

 Delivery and QA gate

   Deliverable                  Persisted location and measured area/perimeter with clear provenance.


   Acceptance tests             Known 10 m x 20 m rectangle gives 200 m2 and 60 m; degree coordinates never
                                treated as metres; missing location yields explicit state.


   Dependencies                 M4 database/config; M7 jurisdiction/regulation interface; M6 consumes metric
                                geometry.


   Evidence to submit           Numeric tests, API examples, discrepancy sample and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 9

```text
DAY 6 OF 7 / TASK ID: BE-M2-D06


 Boundary edits and explicit confirmation
 Validate edited boundary atomically and confirm only the accepted
 revision.

 Implementation checklist
 01. Implement draft boundary update with owner check and expected revision/concurrency token.

 02. Call Python geometry validation for candidate edits; reject invalid candidate without overwriting
      last valid boundary.

 03. Persist source, measurements, geometry version and diagnostic result; invalidate stale
      confirmation on accepted edits.

 04. Implement explicit land confirm endpoint for exact valid revision; require user review when
      extraction uncertain.

 05. Publish confirmed land contract for M6 footprint and M5 requirement readiness.

 Delivery and QA gate

   Deliverable                  Revision-aware validated boundary and confirmation API.


   Acceptance tests             Invalid edit leaves prior state unchanged; stale edit conflict; uncertain survey cannot
                                be represented as legally verified; owner check enforced.


   Dependencies                 M1 owner checks, M4 transaction support, M6 footprint input.


   Evidence to submit           DB transaction tests, confirm/invalid edit tests, contract and PR.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 10

```text
DAY 7 OF 7 / TASK ID: BE-M2-D07


 Survey/land end-to-end QA
 Verify secure ingestion and hand off valid land to the planning engine.

 Implementation checklist
 01. Run upload -> extraction -> review -> boundary edit -> confirm using real C# and Python
      services.

 02. Retest malformed and large files, missing page text, ambiguous measurements and unavailable
      geocoder/OCR.

 03. Test cross-user file/result access, stale confirmation, invalid CRS and geometry.

 04. Send confirmed boundary fixture plus source revision to M6; verify feasible input can be
      consumed.

 05. Document supported formats/accuracy limitations and close only verified issues.

 Delivery and QA gate

   Deliverable                  Integrated survey and land backend candidate; confirmed geometry fixture.


   Acceptance tests             pytest and dotnet tests pass; critical ownership/file issues resolved; no unsupported
                                extraction claimed complete.


   Dependencies                 M6 input acceptance, M4 services, M7 QA and PR review.


   Evidence to submit           Final PR, fixture files, redacted tests, limitations and tracker status.


 Daily status: Not Started / In Progress / Blocked / Review / Done. Done requires verified acceptance checks,
 evidence and review.
```

## Source page 11

```text
11 / TEAM HANDOFF


 Integration and Git workflow
 Cross-member dependencies

   Partner                  Handoff / contract

   M1                       Use project ownership and stable project IDs; do not allow cross-account file access.


   M4                       Agree private storage/config, internal HTTP contract, DB mappings and migration order.


   M6                       Supply confirmed metric boundary + CRS/origin and source version; M6 computes
                            buildable footprint.


   M5 / M3                  Share confirmed land state for requirements and plan/editor consumers; publish stable
                            GeoJSON display contract.


   M7                       Provide extraction source evidence and unverified-rule diagnostics for QA.


 Daily Git workflow
 If you already cloned the repository, do not clone it again. Start from updated develop. If the
 feature branch already exists, switch to it instead of recreating it.
 git clone -b develop https://github.com/Kogulanathan69/BuildMind-Platform.git
 cd BuildMind-Platform
 git switch develop
 git pull --ff-only origin develop
 git switch -c feature/m2-survey-land
 git status --short
 Stage only your reviewed feature files. Commit with the daily task ID, push your branch, and open a PR into
 develop. Update your branch with develop according to team policy; resolve conflicts with file owners. Never
 force-push shared branches.
 Commit example: git commit -m "BE-M2-D01: implement feature foundation"
 git push -u origin feature/m2-survey-land
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

   Task ID                          BE-M2-D01 ... BE-M2-D07 (proposed backend tracking IDs)


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

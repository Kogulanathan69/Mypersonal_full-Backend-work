# M5 Day 04 Step 02: isolated section updates

Task: BE-M5-D04. Owner: M5. Date: 2026-11-19.

## Authorization and provisional decisions recorded before implementation

The M5 developer authorized bounded isolated implementation. This does not establish independent
cross-member agreement, an atomic ownership protocol or production authorization.

- GET `/api/v1/projects/{id}/requirements/spaces` and `/relationships`: provisionally return the
  existing complete `RequirementRevisionDto` (200), including the merged current snapshot and provenance.
- Owned project without a saved revision: provisionally 204 for either GET.
- Section PUT requires an existing revision. Missing/null/empty expected revision ID is structural
  400. A supplied nonempty expected ID against no saved base is provisionally 409 REVISION_CONFLICT;
  no default core requirements or initial revision are invented. Use Day 03 full PUT for initial creation.
- Each PUT replaces only its addressed collection. A required explicit empty array clears it subject
  to the existing catalogue validator. Omitted/null collections are rejected. Other sections are preserved.
- Success returns the complete confirmed saved DTO. The Day 03 full PUT remains complete replacement.
- All other identity, policy, failure and cancellation behavior reuses Day 03. No automatic retry.
  Core content is revalidated with the composed snapshot; invalid preserved content is not silently pruned.
- Relationship references retain the existing requirement room-type semantics (including existing
  zero-count handling), direction, duplicate and self-reference rules. No stricter or production policy is added.
- Registration remains Testing-only and absent from the production composition root.

## Verified integration commit

Local merge commit: `c5c927af06a77616699c0ed1e71a4c96c942336c`.
Parents: `9a669d4f975edbf49aae575584cdb55c3ca8bba2` and
`d5db6f81a1a0440fa0a4d1ffad2e5c1e175de73c`.
Committed tree: `c4eb5ae753ec2a2b9630f1bc73679c5d5b8e3e9c`, exactly matching pinned develop.
External Step 01 backup and staged tree were reverified before committing. No push occurred.
Day 04 source/test/document work is separate and must remain uncommitted.

## Production boundaries

Sequential Day 03 ownership checks do not establish atomic ownership during persistence. Real
authentication/customer policy, project persistence, an approved catalogue provider, the atomic
ownership protocol and real two-account/disposable-MySQL evidence remain pending.
M4 R-1/R-3 provider risks, historical credential clearance, RPT-01/RPT-02, M2/L1 and earlier deferrals remain open.

## Implemented isolated workflow

`RequirementsSectionApplicationService` depends only on the existing `IRequirementsApplicationService`.
It reads the owner-scoped current DTO, verifies the project and exact expected revision, composes a
complete immutable save request with only the addressed collection replaced, and delegates to the
existing full service. The full service rechecks ownership and resolves the authoritative catalogue;
the existing validator, writer and repository retain their responsibilities. No validation catalogue,
transaction, repository, authentication or authorization implementation was duplicated.

The original expected ID survives the read/save interval. An intervening update conflicts even when
the two callers changed different sections. There is no reload/rebase/retry. Missing base maps to the
provisional conflict above. Null/corrupt results and mismatched project identities fail internally.
Cancellation is checked before/after reads and before saving, without reclassifying confirmed commits.
Opaque failures, including unknown transaction outcomes, propagate without guessing a rollback.

New input contracts defensively copy collections. HTTP binding requires explicit expected ID,
catalogue version and target collection, using inherited JsonRequired metadata. Existing nested
transport types share structural checks with the Day 03 full DTO; catalogue rules still live solely
in the existing validator. Unknown client core/owner fields cannot change preserved snapshot fields.

The full Day 03 PUT remains complete snapshot replacement. The section module reuses its feature-local
HTTP mapper and errors through internal methods; no shared error contract was changed. All four new
routes require authorization and no-store responses. Their opt-in registration is added only to the
existing Testing-only helper; the production composition root is unchanged.

Each saved revision retains its own schema/catalogue provenance. An authoritative policy change
revalidates the complete composed snapshot: invalid preserved content rejects the operation rather
than being pruned; a valid new revision carries newly validated provenance without rewriting history.

## Files created

- `src/BuildMind.Application/Features/Requirements/Contracts/SaveAdditionalSpacesRequest.cs`
- `src/BuildMind.Application/Features/Requirements/Contracts/SaveRoomRelationshipsRequest.cs`
- `src/BuildMind.Application/Features/Requirements/Abstractions/IRequirementsSectionApplicationService.cs`
- `src/BuildMind.Application/Features/Requirements/Services/RequirementsSectionApplicationService.cs`
- `src/BuildMind.API/Endpoints/Requirements/PutRequirementsSectionHttpRequests.cs`
- `src/BuildMind.API/Endpoints/Requirements/RequirementsSectionsEndpointModule.cs`
- `tests/BuildMind.Requirements.Tests/Contracts/RequirementsSectionContractTests.cs`
- `tests/BuildMind.Requirements.Tests/Services/RequirementsSectionApplicationServiceTests.cs`
- `tests/BuildMind.Infrastructure.Tests/RequirementsSectionsEndpointTests.cs`
- This M5 handover.

## Existing M5 files modified

- `src/BuildMind.API/Endpoints/Requirements/PutRequirementsHttpRequest.cs`: reusable internal nested
  structural checks, preserving existing full-snapshot behavior and required JSON fields.
- `src/BuildMind.API/Endpoints/Requirements/RequirementsEndpointModule.cs`: internal feature-mapper
  reuse and section service/module registration within the unchanged Testing-only environment guard.
- `tests/BuildMind.Requirements.Tests/Services/RequirementsApplicationServiceTests.cs`: existing fixture
  reuse within the same test assembly; its synthetic store now compares expected heads under a lock.
- `tests/BuildMind.Infrastructure.Tests/RequirementsEndpointTests.cs`: existing isolated host/fixture reuse,
  injectable synthetic catalogue and a locked synthetic store for concurrency scenarios.

No InternalsVisibleTo, reflection, new test projects, dependencies or shared-source changes were introduced.
No Domain, validator, persistence, production Program/DI, solution, CI, schema or migration files changed.

## Actual verification

- Release solution compilation: PASS, zero errors, two existing cached NU1900 warnings.
- New focused tests: **83 passed, zero failed, zero skipped**:
  contract 12; section application 34; section HTTP 37.
- Complete affected suites: Requirements **398/398**, Infrastructure **528/528**;
  **926 passed, zero failed, zero skipped**. Focused executions are subsets, not additional unique tests.
- Day 03 full-snapshot tests ran unchanged within those suites. The Step 01 full 1,478 baseline was not
  repeated unnecessarily; no new full-suite aggregate is claimed for this step.
- Coverage: empty/populated selections, explicit clearing, text limits, duplicate/unknown options,
  invalid/directed/self/zero-count relationships under existing rules, core/other-section preservation,
  immutable history, policy change/provenance, stale and coordinated concurrent updates, missing base,
  required HTTP fields/nulls, untrusted core/owner input, auth/owner rejection, cancellation and failures.
- Tests use synthetic catalogues and in-memory repositories. The integrated HTTP host uses test
  authentication plus real M5 services and HttpContextCurrentUser. No production JWT, MySQL transaction,
  real account isolation or atomic ownership guarantee is established. No live database tests ran.
- No fresh dependency vulnerability audit was needed or executed: dependency versions are unchanged.
  Previous audits remain previous evidence; build NU1900 warnings are not suppressed or called a fresh pass.
- Git may report LF-to-CRLF notices for future checkouts. No Git configuration or authentication was changed.

Evidence directory outside the repository:
`C:\Users\tharm\BuildMind-M5-Backups\Day04-Step01-20261119-115306-489-05af0238\reports\step02`.
It holds affected-suite TRX and the final source/credential verification report. Day 04 changes remain
unstaged and uncommitted. The only new commit is the explicitly authorized integration commit above.

## Remaining review and activation requirements

The GET full-snapshot/204 and missing-base PUT 409 behaviors remain explicitly provisional decisions.
Independent M1/M3/M4/M6/M7 review and production activation authorization have not been inferred.
Production still requires the atomic ownership protocol, actual auth/customer policy, project persistence,
approved catalogue implementation and real two-account/disposable-MySQL evidence. Existing M4 provider
risks and previous quality/security deferrals are not resolved by these isolated tests.

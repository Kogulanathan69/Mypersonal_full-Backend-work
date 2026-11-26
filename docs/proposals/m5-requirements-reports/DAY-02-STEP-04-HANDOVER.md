# M5 Day 02 Step 04 - Dashboard Application Foundation

Task: BE-M5-D02. Owner: M5. Status: PARTIAL - independent application foundation implemented;
production identity/project adapter, shared wiring and real integration verification remain pending.

## Repository and approvals

- Local branch: `feature/m5-requirements-reports`, implementation base `4d4ac53f933f9237a255ac79689d3b56de23f9ad`.
- Initial working tree was clean. Day 01 code and tests were preserved.
- M5 confirmed M1/M4/M7 approval in this session; this does not freeze missing interface or stage-policy details.
- `git fetch origin --no-prune` completed (with per-command low-speed timeout settings, no persistent config change).
  The restricted sandbox first denied writing `.git/FETCH_HEAD`; the authorized fetch completed outside it.
- No branch switch, merge, rebase, commit or push. Remote references were inspected through `git show`/`git ls-tree`.

## Verified remote contracts (not merged into the local checkout)

| Ref inspected | Commit | Relevant evidence |
|---|---|---|
| origin/develop | fef5814 | M1 owner-filtered repository, M4 current-user contract, Project.Status |
| origin/feature/m1-auth-projects | 4252d5c | GetByOwnerIdAsync / GetByIdAndOwnerAsync; Project.Status |
| origin/feature/m1-ci-integration | 0a47296 | Same owner-query and project status contracts |
| origin/feature/m4-shared-infrastructure | 2d2a474 | ICurrentUser and HttpContextCurrentUser; validated identity boundary, JWT integration pending |
| origin/feature/m4-database-infrastructure | d99d09f | Shared persistence registration and owner-query interface; not proof of live repository integration |
| origin/feature/m4-shared-contracts | 5f52f52 | Shared conventions proposal; approved and pending portions must remain distinguished |
| origin/feature/m3-cross-member-integration | 85f8a7e | Updated confirmation outcomes/publication contract; no Dashboard implementation imported |
| origin/feature/m3-plan-integration | c77185b | Available integration reference; no changes imported |
| origin/feature/m5-requirements-reports | 63324b9 | Remote M5 branch is ahead with shared integration changes; local branch intentionally unchanged |

M1 `IProjectRepository.GetByOwnerIdAsync(Guid, CancellationToken)` and M4 `ICurrentUser`
are verified remote contracts but absent from the local base. Local `IProjectRepository` still
only has GetByIdAsync/AddAsync, and local Project has no Status. Direct integration therefore
cannot compile on this base without separately approved integration of other owners' files.

M1 Project.Status currently defaults to `Created`. No complete approved Dashboard stage catalogue
was established. The new service defines no production stage strings or stage mapping.

## Implemented scope

- Parameterless DashboardSummaryQuery: no client owner identifier.
- Query handler delegates to the Dashboard application service and forwards cancellation.
- IDashboardProjectSource is an M5 consumer port, not a replacement auth contract/M1 repository.
  Its future adapter must resolve M4 ICurrentUser, reject unauthenticated callers before querying,
  use M1 GetByOwnerIdAsync, and preserve authoritative stage values in the agreed projection.
- Internal source outcomes distinguish Loaded (including empty), Unauthenticated and Unavailable.
  Loaded requires a nonempty owner binding and defensively copied rows.
- Service rejects any row belonging to a different owner and rejects duplicate project IDs through
  the existing DashboardSummaryDto. It returns no partial or successful-empty summary on failure.
- Explicit application outcomes distinguish unavailable dependency, timeout and invalid response.
  No raw exception messages, identities or source data are included in failures. HTTP mapping is deferred.
- Asynchronous execution, cancellation forwarding, cancellation after a dependency completes,
  and differentiation from dependency-owned cancellation are implemented.
- Day 01 aggregation remains the sole summary/count implementation. A source must return the complete
  owner-scoped set, not a single page; failed/unavailable queries must never be mapped to empty success.

## Executed verification

- Workflow project Release build, `--no-restore --maxcpucount:1`: PASS, 0 warnings / 0 errors.
- Workflow tests, Release `--no-build --no-restore`: 110 passed / 0 failed / 0 skipped.
  Includes 70 existing Workflow cases plus 40 new Dashboard application/source/result cases.
- Local TRX evidence: `tests/BuildMind.Workflow.Tests/obj/Verification/dashboard-step04.trx` (ignored build artifact).
- Existing solution Release build, `--no-restore --maxcpucount:1`: PASS, 0 warnings / 0 errors.
- Unit tests use synthetic stages and an in-memory test double. They verify owner-binding rejection,
  not production authentication, owner-filtered SQL, HTTP authorization or real account isolation.
- The historical 443-test baseline was not rerun in full during this step.
- NU1900 vulnerability verification remains incomplete; a no-restore build does not establish a successful audit.

## Next separately approved integration

1. Integrate the exact M1/M4 contracts into the M5 base through the agreed team Git workflow.
2. Confirm authoritative stage semantics and completeness/pagination expectations with M1/M7.
3. Implement the real current-user/project-source adapter using the existing contracts; no fake authentication or persistence.
4. Coordinate shared DI/endpoint registration and safe HTTP error mapping with M4.
5. Verify unauthenticated access, two-account isolation, unavailable database and correct persisted counts through real integration tests.

RPT-01 and RPT-02: M3 approval reported by M5; implementation pending, separate from this Dashboard slice.
Requirements M2/L1, direct internal-constructor tests, solution/CI integration and other previously
deferred production integrations remain pending. No Day 03 work was started.

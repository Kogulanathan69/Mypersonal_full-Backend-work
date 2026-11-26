# Isolated backend integration evidence

Branch `feature/backend-final-integration`, starting SHA `cbcbe93aaafb22975d061bb25b41005c4d06cdd8`. The starting worktree was clean and matched the cached `origin/develop` ref. Production composition is NO-GO. M7 STEP 8B remains on hold.

## Implemented slices

- **M1/M3/M4 history.** The isolated HTTP host maps only the existing two GET handlers. The normal Plan Editor module still maps its five routes when explicitly used by its existing contract hosts. No normal-host registration was added. The M4 reader rejects use within a database or ambient transaction so a stale snapshot cannot supply post-read authorization. Current owner and project scope, positive version evidence, dependency classification and cancellation continue to use the existing contract. The existing M1 owner port and version store were left untouched.
- **M4 transaction primitive.** `ProjectMutationAuthorization` requires an active EF transaction, takes a parameterized project-first locking read scoped to project and trusted owner, and compares the persisted positive version. It is an isolated building block. No business writer calls it. The live test exercises the lock read, owner/version denial, guard against stale history reads, and rollback of a version increment. No cross-feature lock ordering or live blocking behavior is certified.
- **M5-compatible fingerprint.** `RequirementMutationFingerprint` frames a Requirements-specific draft V1 SHA-256 digest over project, actor, operation ID, positive authorization version, optional expected revision and caller-supplied server-canonical bytes. It does not define the canonical M5 payload and does not accept a caller-supplied digest. No durable ledger, CAS integration or reconciliation service was added. Existing M5 source and interfaces were left unchanged.
- **M2 private storage.** `PrivateSurveyFileStorage` implements the existing storage port using opaque GUID keys, bounded staged writes and a same-directory publication step. It rejects oversized, empty and cancelled uploads and keeps the physical path out of the returned identifier. The root must be an exclusive, operator-provisioned private directory with appropriate parent permissions. No storage registration, parser, metadata schema or public endpoint was added.

## Executed verification

Release build and focused execution used Ubuntu 24.04 WSL, .NET SDK 10.0.115 and the existing local package cache. All source, generated build artifacts, test artifacts and Survey test temporary files stayed under the isolated checkout. Commands below were run from `/mnt/c/BuildMind-M4-MySQL-Isolated`.

```bash
export DOTNET_CLI_HOME="$PWD/src/BuildMind.API/obj/fast-track"
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$DOTNET_CLI_HOME/packages" VSTEST_ARTIFACTS_DIRECTORY="$DOTNET_CLI_HOME"
dotnet build BuildMind.sln -c Release --no-restore
dotnet build tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj -c Release --no-restore
TMPDIR="$DOTNET_CLI_HOME" dotnet vstest tests/BuildMind.Infrastructure.Tests/bin/Release/net10.0/BuildMind.Infrastructure.Tests.dll \
  '--TestCaseFilter:FullyQualifiedName~RevisionHistoryIntegrationHostTests|FullyQualifiedName~PlanEditorEndpointContractTests|FullyQualifiedName~ProjectMutationAuthorizationTests|FullyQualifiedName~ProjectOwnerAuthorizationPortTests|FullyQualifiedName~RequirementMutationFingerprintTests|FullyQualifiedName~PrivateSurveyFileStorageTests|FullyQualifiedName~TransactionRunnerTests' \
  '--Logger:trx;LogFileName=autonomous-backend-focused.trx' --ResultsDirectory:"$DOTNET_CLI_HOME"
```

| Check | Exit | Evidence |
| --- | ---: | --- |
| Linux Release solution build | 0 | Zero warnings and errors |
| Linux disposable MySQL tests Release build | 0 | Zero warnings and errors |
| Focused infrastructure, HTTP and security tests | 0 | 99 passed, 0 failed, 0 skipped. `src/BuildMind.API/obj/fast-track/autonomous-backend-focused.trx` |
| Initial Windows Release build without restore | 1 | `NETSDK1064` because existing assets referenced the Linux package cache. The Linux build succeeded using those assets. |
| Disposable MySQL tests | Not run | Both test environment variables absent on Windows and WSL. No Docker or Podman runtime available. Windows MySQL84 was left untouched. |

The compiled real-MySQL HTTP fixture maps only Revision History GETs, uses the existing JWT validator, account session middleware, trusted current user, and actual EF authorization and history adapters. It covers anonymous, owner, other owner and revoked account session behavior. Existing separate live cases cover project authorization version changes, corrupt lineage, pagination and dependency failures. No migration was added or applied in this batch.

## Gates remaining

| Owner | Exact blocker |
| --- | --- |
| M1/M3/M4 | Select the canonical history authorization port and status policy. Approve lifecycle/revocation writers, atomic version advancement and project-lock ordering. Run the JWT/MySQL host, locking query, and existing migration/history tests on the pinned disposable MySQL 8.4 fixture. |
| M5 Requirements/M1/M4 | Approve canonical payload and categories, durable operation key scope/retention, duplicate and reconciliation disclosure rules, and the same-transaction owner/version, immutable append, head CAS and outcome write. |
| M2/M1/M4/M6 | Approve owner-bound Survey receipts, physical storage policy, orphan and uncertain-commit recovery, durable processing metadata, parser isolation and authoritative Land revision identity. The existing compensation can delete a blob after an ambiguous metadata commit. |
| M3/M6/M4 | Agree the candidate mutation authority, validation service and freshness issuer, private transport, and job persistence. The shared Python host exposes health only. No authoritative validation result or generation publication was introduced. |
| M5 Reports/M3/M1/M4 | Approve authoritative confirmation, durable publication, private artifact identity and retention, and current access/consent before status, download or sharing. Existing report renderer and private artifact verifier remain reusable. |
| M7/M6/M4 | Confirm private hosting and authoritative evidence consumption. Existing authentication and retrieval activation guards were inspected only. STEP 8B remains on hold. |

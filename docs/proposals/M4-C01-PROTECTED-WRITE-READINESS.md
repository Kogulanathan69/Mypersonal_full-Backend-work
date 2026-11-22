# M4 C-01 and protected-write release decision

**Production NO-GO. M7 STEP 8B remains strictly on hold.**

## Provenance

The clean starting branch was `feature/backend-final-integration` at local C-01 commit `e0fc0ca7d54617710f9345200b9aba25cf25542d`. Its parent tree is the previously verified PR #88 merge tree `09a98af0da25b6ff845bb9920451c27a6d784c70`. GitHub resolves remote `develop` reference `c9b5e0226cc9d9af002c3ea3925a012836928915` and the M3 RH-09 follow-up `2484ea2a986ffe283ac5fd5a637af03a3d417321`. Neither remote commit object was in the local checkout. The M3 commit was not integrated and its CI recovery stayed outside this work. The Captain directive records M1/M3 ratification of A1–A8.

## Protected-write protocol and coverage

- **Project metadata.** The existing `ProjectRepository.UpdateAsync` makes one conditional `UPDATE` that acquires the project row lock and matches project, current owner, the server-read project authorization version, and expected metadata revision. The existing service maps an unsuccessful CAS to its existing conflict outcome. New project creation still binds owner through the existing actor path.
- **Isolated Requirements save.** `TrySaveAuthorizedAtomicallyAsync` takes trusted actor and positive expected version. `TransactionRunner` starts the transaction. `ProjectMutationAuthorization.LockAndCheckAsync` locks the authoritative project row and checks owner and version before any feature-head access. In that same context and transaction the existing append and head CAS execute. Denial rolls back and returns the existing no-payload `ProjectNotFound` shape. Known conflicts, cancellation and pre-commit failures roll back. Commit uncertainty remains a distinct failure. The protected path avoids an unscoped head lookup after rollback and lock release.
- **Uncovered paths.** The M5 application port supplies no actor or authorization version and retains its existing behavior. The isolated Requirements entry point is unwired. M3 candidate and confirmation mutations lack a connected durable adapter. No application lifecycle writer was found for project transfer or access revocation. The version store is a CAS primitive. A future authoritative lifecycle writer must lock the same project row, validate current authority, and commit access and version changes together. These gaps block claims of full enforcement.
- Keep `users.token_version`, `projects.authorization_version` and `projects.revision` separate. No session, lifecycle writer, normal-host registration or schema changed.

## Security and evidence matrix

| Coverage | Evidence and limit |
| --- | --- |
| A1, A2, A8 | Shared C-01 read binds the trusted actor to the exact owner and project. Missing and wrong-owner cases deny without a payload. A nonpositive version on the matching row faults; the existing M3 error mapping sanitizes it. The updated live fixture is compiled and remains unexecuted. |
| A3, A4, A5, A7 | Focused adapter tests use synthetic provider failures. The existing classifier keeps native 1205 separate from command timeout. Cancellation wins before invalid-ID handling, and the existing HTTP contract checks preserve safe failure outcomes. |
| A6 | Focused tests cover the ambient read guard and transaction-required write helper. Authenticated metadata writes now use an atomic owner/version/revision predicate. The isolated Requirements path contains the project lock before head access. Live lock evidence remains pending. |
| Transfer, stale version and denial | Test fixtures arrange a committed synthetic transfer and stale version, assert no revision or head is inserted on denial, and verify fresh owner success. This is a test arrangement, not an approved lifecycle writer. |
| Conflict, rollback and order | Competing saves target one head. Synthetic failure and cancellation are injected after SQL insertion and before the head change. A second transaction probes the project row with `FOR UPDATE NOWAIT` before feature insertion. These disposable cases require live execution. |

Second-pass source review found no new normal-host registration, API contract, credential output, migration or protected M7 change. The old Requirements route contract and future lifecycle bypass remain **high release blockers**. Post-startup TLS validation consistency remains a separate M4 finding. The test fixture guards generated names and rejects root, but server/credentials/TLS must be verified before using it because it creates the schema before its final connection-string validation. No independent reviewer sign-off was obtained. **Security gate remains pending.**

## Recorded commands and results

Commands ran under Ubuntu 24.04 WSL with .NET SDK `10.0.115`, from `/mnt/c/BuildMind-M4-MySQL-Isolated`. The CLI home, package cache and test output were scoped to the existing `src/BuildMind.API/obj/fast-track` directory. The focused run exited 0 with **98 passed, 0 failed, 0 skipped**. VSTest reported 19 seconds. The disposable regression assembly compiled without warnings or errors. The disposable connection variables and Docker/Podman were absent in Windows and WSL. No MySQL tests were executed and no database/container cleanup evidence exists for this patch. A previously reported MySQL 8.4.13 result applies to the earlier PR #88 tree only.

```bash
dotnet test tests/BuildMind.Infrastructure.Tests/BuildMind.Infrastructure.Tests.csproj -c Release --no-restore \
  --filter 'FullyQualifiedName~ProjectOwnerAuthorizationPortTests|FullyQualifiedName~ProjectMutationAuthorizationTests|FullyQualifiedName~ProjectAuthorizationCompositionTests|FullyQualifiedName~RevisionHistoryIntegrationHostTests|FullyQualifiedName~ProjectHttpIntegrationTests|FullyQualifiedName~TransactionRunnerTests|FullyQualifiedName~PersistenceErrorClassifierTests|FullyQualifiedName~RequirementSave' \
  --logger 'trx;LogFileName=protected-write-focused.trx' --results-directory "$PWD/src/BuildMind.API/obj/fast-track"
dotnet build tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj -c Release --no-restore --no-dependencies
git diff --check
```

The focused TRX is `src/BuildMind.API/obj/fast-track/protected-write-focused.trx`. For a later approved live run, use the verified fixture and capture the following filtered suite as a separate TRX.

```bash
dotnet test tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ProjectAuthorizationLiveTests|FullyQualifiedName~RequirementRevisionRepositoryTests|FullyQualifiedName~PlanRevisionHistoryMySqlTests' \
  --logger 'trx;LogFileName=protected-write-mysql.trx' --results-directory TestResults/protected-write
```

## Release gates and operations plan

1. On the approved digest-pinned MySQL 8.4 fixture, capture actual server/SDK versions, verified TLS and scoped grants, focused TRX, migration discovery/parity, and zero leftover databases. Execute `ProjectAuthorizationLiveTests`, `RequirementRevisionRepositoryTests`, and the M4-owned `PlanRevisionHistoryMySqlTests`. Keep M3 RH-09 recovery separate. Do not use Windows MySQL84 or a shared server.
2. Obtain M1's lifecycle writer and lock-order decision, plus M5/M3 acceptance of their protected mutation boundaries. Verify two-connection transfer and revocation ordering before declaring full write enforcement. Obtain independent review of runtime results and disclosure tests.
3. Preserve the production activation gate. Before a future release, review feature registration and credentials, verify backup/restore and a known-good build, and require the Captain's release authorization. Track aggregate denial, version conflict, lock/deadlock, dependency timeout, unknown commit, and readiness rates without actor identifiers, SQL or credentials.
4. This patch has no schema delta. For an approved future rollout, keep the previous approved build available for binary rollback and disable the affected feature by its approved rollout mechanism. Keep production data intact. Handle uncertain commits by reconciliation before a retry. Any later schema migration needs its own reviewed ranged SQL, backup and restore validation, and explicit DBA/Captain approval.

No production database, shared migration, production DI, CI workflow or M7 modification is needed to review this package.

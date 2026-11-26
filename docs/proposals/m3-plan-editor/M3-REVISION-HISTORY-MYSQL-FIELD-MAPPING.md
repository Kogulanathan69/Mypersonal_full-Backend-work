# M3 Revision History MySQL field mapping

M3-RH-SCHEMA-V1.0 · M4 STEP 22 / STEP 1 · Isolated schema preparation.

The `plans` identity is `(project_id, plan_id)`. Nullable `head_revision_id` references a revision of that same project and plan. The head version comes from the referenced row. A null head represents an empty history. Revision identity is `(project_id, plan_id, revision_id)` and `(project_id, plan_id, version_number)` is unique.

| M3 field | `plan_revisions` column | SQL type and nullability |
| --- | --- | --- |
| `ProjectId` | `project_id` | `char(36)` required |
| `PlanId` | `plan_id` | `char(36)` required |
| `RevisionId` | `revision_id` | `char(36)` required |
| `ParentRevisionId` | `parent_revision_id` | `char(36)` nullable, scoped self-FK |
| `VersionNumber` | `version_number` | `bigint` required, 1 through `long.MaxValue - 1` |
| `GeometrySchemaVersion` | `geometry_schema_version` | `varchar(64)` required |
| `GeometryJson` | `geometry_json` | `mediumtext` with `utf8mb4`, required, 1–262,144 bytes provisionally |
| `GeometryHash` | `geometry_hash` | `varbinary(32)` required, exactly 32 bytes |
| `LandRevisionId` | `land_revision_id` | `char(36)` required |
| `RequirementsRevisionId` | `requirements_revision_id` | `char(36)` required |
| `EditCommandId` | `edit_command_id` | `char(36)` nullable |
| `CreatedBy` | `created_by` | `char(36)` required |
| `CreatedAtUtc` | `created_at_utc` | `datetime(6)` required, UTC |
| `ChangeSummary` | `change_summary` | `varchar(1024)` nullable |
| `Validation.ValidationId` | `validation_id` | `char(36)` required |
| `Validation.ValidatorVersion` | `validator_version` | `varchar(64)` required |
| `Validation.GeometryHash` | `validation_geometry_hash` | `varbinary(32)` required, exactly 32 bytes |
| `Validation.GeometryValid` | `geometry_valid` | `tinyint(1)` required |
| `Validation.RequirementsMatch` | `requirements_match` | `tinyint(1)` nullable, preserves true, false and unknown |
| `Validation.RegulatoryEvidenceStatus` | `regulatory_evidence_status` | `varchar(64)` required |
| `Validation.HasBlockingIssues` | `has_blocking_issues` | `tinyint(1)` required |
| `Validation.ValidatedAtUtc` | `validated_at_utc` | `datetime(6)` required, UTC |

Geometry text is stored verbatim. The EF converter accepts the canonical `sha256:` string with 64 lowercase hexadecimal digits and restores that same format on materialization. The two digest columns preserve the snapshot and validation evidence separately. M3 continues to own strict JSON, hash binding and proof of contiguous parent versions. Timestamp precision is microseconds under the existing UTC convention.

Review remains open for the provisional geometry and aggregate page budgets, cross-module reference and retention constraints, and command uniqueness. M1 authorization and version advancement are outside this schema. The records have no revision write API. Database-enforced append-only grants and mutation behavior belong to a later slice.

## STEP 1 verification and handover

The EF Core 10.0.12 scaffold is `20261122080431_AddPlanRevisionHistory`. Static review of its operations found two new tables only. Named indexes precede the scoped head and parent foreign keys. The rollback removes the plan back-reference before dropping the new tables. The parent foreign key uses InnoDB's implicit restrictive action to keep the parent shape check compatible with MySQL. Both SHA-256 values use `varbinary(32)` with exact length checks to avoid short values being padded by the database.

During STEP 1, the API and disposable MySQL test project compiled. Windows Application Control blocked offline SQL generation and the metadata test assembly at load time (`0x800711C7`). The fast-track checks below supersede that offline blocker.

## Fast-track adapter handover

`PlanRevisionMapper.ToRecord` and `ToSnapshot` map both directions using the field table above. There is no revision mutation endpoint or adapter. `MySqlPlanRevisionHistoryQuery` reads the authoritative head and page under one InnoDB repeatable-read transaction, then disposes that view before the application's post-read authorization. Payload reads use a scoped version index and take up to 51 rows. The cursor proof verifies the original head, anchor and expected parent. It counts exact predecessor links in SQL from the captured current head to the requested lower boundary without materializing intervening payloads. The proof's database work grows with the version interval. Capture real `EXPLAIN` and latency for deep cursors and historical exact lookups before accepting large histories.

`ProjectAuthorizationReader` projects only the current positive version for an exact owner and project. No lifecycle predicate or version-changing writer was added. The new disposable suite injects a version change between two real reads to check payload suppression. M1 still owns approval of revocation events, overwrite or reassignment behavior and atomic version advancement. `PlanHistoryDatabaseRead` uses the shared provider classifier to surface recognized timeout and unavailability outcomes without connection details.

The generated EF forward and rollback SQL was reviewed offline. It adds only `plans` and `plan_revisions`, creates supporting indexes before the scoped head and parent foreign keys, and drops the plan back-reference before the new tables on rollback. The geometry column explicitly uses `utf8mb4`. The SQL assumes the established InnoDB and database collation defaults for the scoped GUID keys. MySQL DDL performs implicit commits. A partially applied script therefore needs state inspection before any recovery attempt. [MySQL implicit commits](https://dev.mysql.com/doc/refman/8.4/en/implicit-commit.html)

### Geometry benchmark

`python3 -B tools/database/mysql/measure_revision_geometry.py` measured UTF-8 encoding and SHA-256 for 300 iterations on synthetic M3-shaped fixtures. These are CPU measurements without database I/O or HTTP serialization.

| Fixture | UTF-8 bytes | Mean µs | Within provisional limit |
| --- | ---: | ---: | --- |
| 8 rooms with walls and openings | 7,548 | 28.92 | Yes |
| 24 rooms with walls and openings | 21,786 | 73.52 | Yes |
| 512-room stress fixture | 465,518 | 2,078.87 | No |
| Exact boundary, padded JSON | 262,144 | 1,205.30 | Yes |
| Boundary plus one byte | 262,145 | 1,316.21 | No |

The disposable schema test checks the exact limit, text/hash round-trip and rejection of an oversized multibyte value. A worst-case page query can materialize 12.75 MiB of geometry before metadata and application allocations. Keep 256 KiB provisional. Establish a real aggregate budget and decide whether the history page should fetch all geometry before production composition.

### Verification commands

Final Release runs used Ubuntu 24.04 WSL from `/mnt/c/BuildMind-M4-MySQL-Isolated` and Linux .NET SDK 10.0.115. `artifacts` below is `/mnt/c/BuildMind-M4-MySQL-Isolated/src/BuildMind.API/obj/fast-track`. The Linux CLI and NuGet working paths were placed below that directory. The initial cross-platform run of Windows-built infrastructure assemblies exited 1 with 844 passed, 262 failed and 0 skipped. The Windows-generated ASP.NET content paths could not resolve under WSL. The suite was rebuilt natively in the same isolated workspace for verification. The WSL SDK first-run output also reported generating its development certificate. No trust command or system security setting change was made.

| Command inside the isolated checkout | Exit | Result |
| --- | ---: | --- |
| `dotnet build BuildMind.sln -c Release --no-restore` | 0 | Zero warnings or errors |
| `dotnet build tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj -c Release --no-dependencies --source /home/buildmind/.nuget/packages --source /mnt/c/Users/kogul/.nuget/packages -p:NuGetAudit=false` | 0 | Zero warnings or errors |
| `dotnet tests/BuildMind.Application.ContractTests/bin/Release/net10.0/BuildMind.Application.ContractTests.dll` | 0 | 480 passed, 0 failed |
| `dotnet vstest tests/BuildMind.Infrastructure.Tests/bin/Release/net10.0/BuildMind.Infrastructure.Tests.dll '--TestCaseFilter:FullyQualifiedName~PlanEditorEndpointContractTests\|FullyQualifiedName~SchemaModelTests\|FullyQualifiedName~DesignTimeFactoryTests' --ResultsDirectory:$artifacts` | 0 | 94 passed, 0 failed, 0 skipped |
| `dotnet vstest tests/BuildMind.Infrastructure.Tests/bin/Release/net10.0/BuildMind.Infrastructure.Tests.dll --TestCaseFilter:FullyQualifiedName~PersistenceRegistrationTests --ResultsDirectory:$artifacts` | 0 | 11 passed, 0 failed, 0 skipped |
| `dotnet vstest tests/BuildMind.Infrastructure.Tests/bin/Release/net10.0/BuildMind.Infrastructure.Tests.dll --ResultsDirectory:$artifacts` | 1 | Aborted at 14 minutes on the mounted workspace. VSTest reported 843 passed, 0 failed, 0 skipped before termination. The full suite has no completed verdict. |
| `python3 -B tools/database/mysql/measure_revision_geometry.py` | 0 | Five fixtures measured |
| `git diff --check` | 0 | No whitespace errors in the tracked patch |

EF Core 10.0.12 ran through its `ef.dll` design-time entry point with `BuildMindDbContext`, the isolated Release assemblies and a dummy loopback port-1 connection. `migrations script 20261120202215_AddSessionSecurity 20261122080431_AddPlanRevisionHistory --output $artifacts/revision-history-up.sql`, the same command with migration IDs reversed and `--output $artifacts/revision-history-down.sql`, and `migrations has-pending-model-changes` each exited 0. These checks rendered and reviewed SQL without connecting. The ignored SQL files are available in the workspace for review.

The exact EF invocation can be reproduced from the Linux shell with these paths and flags.

```bash
root=/mnt/c/BuildMind-M4-MySQL-Isolated
api=$root/src/BuildMind.API/bin/Release/net10.0
artifacts=$root/src/BuildMind.API/obj/fast-track
cache=/mnt/c/Users/kogul/.nuget/packages
export BUILDMIND_MIGRATIONS_CONNECTION='Server=127.0.0.1;Port=1;Database=offline_schema;User ID=bm_migrator;Password=offline-placeholder;SslMode=Required'
ef_offline() {
  dotnet exec --depsfile "$api/BuildMind.API.deps.json" \
    --runtimeconfig "$api/BuildMind.API.runtimeconfig.json" --additionalprobingpath "$cache" \
    "$cache/dotnet-ef/10.0.12/tools/net8.0/any/tools/net8.0/any/ef.dll" "$@" \
    --context BuildMindDbContext --assembly "$api/BuildMind.Infrastructure.dll" \
    --startup-assembly "$api/BuildMind.API.dll" --project-dir "$root/src/BuildMind.Infrastructure" \
    --working-dir "$root" --language C# --framework net10.0 \
    --design-assembly "$cache/microsoft.entityframeworkcore.design/10.0.12/lib/net10.0/Microsoft.EntityFrameworkCore.Design.dll" --nullable
}
ef_offline migrations script 20261120202215_AddSessionSecurity 20261122080431_AddPlanRevisionHistory --output "$artifacts/revision-history-up.sql"
ef_offline migrations script 20261122080431_AddPlanRevisionHistory 20261120202215_AddSessionSecurity --output "$artifacts/revision-history-down.sql"
ef_offline migrations has-pending-model-changes
```

The MySQL test settings `BUILDMIND_TEST_MYSQL` and `BUILDMIND_TEST_MYSQL_RUNTIME` were absent in both environments, and neither exposed a Docker or Podman runtime for the pinned container. The running Windows MySQL service was left untouched. The live suite was not run. With the existing disposable fixture available, use `dotnet test tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj -c Release --filter 'FullyQualifiedName~BuildMindMigrationTests|FullyQualifiedName~PlanRevisionSchemaTests|FullyQualifiedName~PlanRevisionHistoryMySqlTests'` and capture the results and `EXPLAIN` output before M3 composition.

# MySQL development and test accounts (BE-M4-D02a proposal)

[`001-dev-test-accounts.sql`](001-dev-test-accounts.sql) creates `buildmind_dev` and three least-privilege accounts limited to `localhost`. **Nothing runs it automatically.** The lead reviews it and runs it manually on a local development server.

| Account | Purpose | Privileges |
|---|---|---|
| `bm_runtime` | Application at run time | `SELECT, INSERT, UPDATE, DELETE` on `buildmind_dev.*` |
| `bm_migrator` | Applying reviewed migration scripts | DDL and data privileges on `buildmind_dev.*`; no `GRANT OPTION` |
| `bm_test` | Disposable spike and test databases | `ALL` on `buildmind\_test\_%` only |

## Steps (lead)

1. Open a DBA session to the **local** server. Check the port first; it is not assumed to be 3306. Run the pre-check line first: you need `partial_revokes = 0` and a `tls_version` that lists TLSv1.2 or TLSv1.3, otherwise stop. All three accounts `REQUIRE SSL`.
2. Copy the script into the session. Replace the three placeholders with newly generated passwords, inside the session only.
3. Run it, and check that the `SHOW GRANTS` output matches the table above.
4. Store the secrets locally, never in the repository:
   ```sh
   # Runtime (API): User Secrets
   dotnet user-secrets set "ConnectionStrings:BuildMind" "Server=localhost;Port=<port>;Database=buildmind_dev;User ID=bm_runtime;SslMode=Required;Password=<...>" --project src/BuildMind.API
   # Migrations (dotnet-ef): environment variable for the session only
   #   BUILDMIND_MIGRATIONS_CONNECTION=Server=localhost;Port=<port>;Database=buildmind_dev;User ID=bm_migrator;SslMode=Required;Password=<...>
   # Spike tests: environment variables for the session only
   #   BUILDMIND_TEST_MYSQL=Server=localhost;Port=<port>;User ID=bm_test;SslMode=Required;Password=<...>
   #   BUILDMIND_TEST_MYSQL_RUNTIME=Server=localhost;Port=<port>;Database=buildmind_dev;User ID=bm_runtime;SslMode=Required;Password=<...>
   ```
5. Turn on database access in the API only when it's needed: `Database:Enabled=true`, set through User Secrets or an environment variable.

## Migration deployment (lead decision 2026-11-18: Option B, internal development scope)

**Migrations are never applied by the application or automatically.** The only supported mechanism is a **reviewed, ranged script** applied by **one authorized executor** with the `bm_migrator` identity.

**Explicit authorization:** every deployment to any shared or persistent database needs a recorded go-ahead from the M4 lead, naming the target database and the `<from>`/`<to>` migrations. Disposable CI and test databases are the only exception. **No production database has been migrated or authorized.**

**Committed migrations** (`src/BuildMind.Infrastructure/Persistence/Migrations`):

| Migration | Content | Ranged script (from the previous migration) |
|---|---|---|
| `20261118133137_InitialFeatureSchema` | `users`, `projects`, `requirement_revisions`, `project_requirement_heads` (BE-M4-D03) | `migrations script 0 20261118133137_InitialFeatureSchema …` |
| `20261119092022_AddProjectAuthorizationVersion` | `projects.authorization_version` `bigint NOT NULL DEFAULT 1` (M1). Down drops only that column | `migrations script 20261118133137_InitialFeatureSchema 20261119092022_AddProjectAuthorizationVersion …` |
| `20261120202215_AddSessionSecurity` | `users.token_version` `bigint NOT NULL DEFAULT 1`; `refresh_tokens`; `password_reset_tokens` (hashes only; FKs to `users` RESTRICT) (M4, STEP 05). Down drops only those tables and that column | `migrations script 20261119092022_AddProjectAuthorizationVersion 20261120202215_AddSessionSecurity …` |

**Empty database:** `migrations script 0 20261120202215_AddSessionSecurity`.

**Existing database at `InitialFeatureSchema`:** one ranged script `20261118133137_InitialFeatureSchema 20261120202215_AddSessionSecurity`. Existing users receive `token_version = 1` from the column default, so no data fix is needed. Tokens issued before the upgrade carry no `tv` claim and are rejected by the session check; users sign in again.

**Live coverage:** `BuildMindMigrationTests` M01–M05.

**Rollback is destructive.** `Down` drops tables and **every row in them**. Never run a down migration on a database with data you need. Recover from the backup (step 4) instead, unless the M4 lead explicitly authorizes the rollback.

1. **Single executor:** only the person assigned by M4 runs migrations against a given database, and announces it first. No two people migrate the same database at the same time.
2. **Check the history before running anything:** `SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId;`. The last row must be exactly the `<from>` migration of the script. If it isn't, **stop**: the script was generated for a different state.
3. **Generate the ranged script:** `dotnet tool run dotnet-ef migrations script <from> <to> --project src/BuildMind.Infrastructure --startup-project src/BuildMind.API --output <file>.sql`. Review it as code: DDL, data changes, and the history `INSERT` it appends.
4. **Back up first:** take a full logical backup of the target schema (for example `mysqldump --single-transaction --routines --triggers <schema>`), stored outside the repository, and confirm it can be restored.
5. **Apply once** with `bm_migrator` (the `mysql` client or a reviewed tool), then **verify**:
   - the expected `MigrationId` rows exist in `__EFMigrationsHistory`;
   - tables, columns (`information_schema.columns`), indexes and foreign keys match the reviewed script;
   - the application starts and its smoke checks pass.

**What history does and does not guarantee.** The history rows stop a ranged script from being applied again **by a process that follows step 2**. They do not make a raw ranged script idempotent: running the same script twice by hand fails or duplicates work. They also don't make the deployment atomic.

**MySQL DDL commits implicitly.** Every `CREATE`/`ALTER`/`DROP` commits immediately, even inside `START TRANSACTION`. So a script that fails partway leaves the earlier DDL applied and **no history row** for that migration.

**Recovering a partial failure:**
1. Stop; never rerun the whole script.
2. Compare the actual schema with the script to see which statements were applied.
3. Either complete the remaining statements by hand and insert the history row, or restore the backup from step 4.
4. Record what happened, then repeat step 5's verification.

**`--idempotent` is not supported.** `MySql.EntityFrameworkCore` 10.0.x generates SQL Server-style `IF NOT EXISTS(...) BEGIN … END;` blocks that MySQL rejects (error 1064; CI T06). This is a **documented provider limitation**, pinned by T06. Reassess it on any provider upgrade: T06 fails if the provider starts emitting executable SQL.

**Production** is out of scope here. It needs its own identities, TLS with certificate validation (`SslMode=VerifyFull`) and a secret store, and the G03 licence review must be accepted first.

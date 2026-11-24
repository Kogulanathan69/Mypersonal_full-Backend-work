# Captain local database setup

**Local development only.** This setup touches no production database, no shared server and no other container, and the Windows `MySQL84` service is not used.

## Server

| Item | Value |
|---|---|
| Host | WSL Ubuntu-24.04 Docker 29.1.3 |
| Container | `bm-captain-mysql`, label `buildmind.owner=captain` |
| Image | `mysql:8.4@sha256:a81d0439…0902`, the same digest as `backend-ci.yml`; server **8.4.13** |
| Network | `127.0.0.1:3417` only |
| Volume | `bm-captain-mysql-data` (persistent development data) |
| Transport | `--require-secure-transport=ON`; plaintext login is refused (verified) |
| TLS | Clients use `SslMode=VerifyCA` with the server CA (`~/bm-captain/secrets/ca.pem`, exported from the container). The session negotiates TLSv1.3. VerifyCA checks the certificate chain; the MySQL auto-generated certificate has no host-name SAN, so VerifyFull is not used. The repository validator accepts VerifyCA on loopback in Development and requires VerifyFull elsewhere. |
| `partial_revokes` | OFF (checked by the setup script) |

## Accounts (least privilege, all `REQUIRE SSL`)

| Account | Grants | Used by |
|---|---|---|
| `bm_runtime` | SELECT, INSERT, UPDATE, DELETE on `buildmind_dev.*` | API runtime (`ConnectionStrings__BuildMind`) |
| `bm_migrator` | DML + CREATE, ALTER, DROP, INDEX, REFERENCES on `buildmind_dev.*` | Ranged migration scripts only |
| `bm_test` | ALL on `` `buildmind\_test\_%`.* `` | Live integration tests (disposable databases) |
| `root` | — | Setup script only, through `MYSQL_PWD` inside `docker exec`; never used by the application |

## Secrets

Secrets live in `~/bm-captain/secrets` (directory mode 0700, files 0600), one random 48-hex value per account, plus the JWT signing key and internal service tokens. Scripts read them into environment variables and never print them. Nothing is committed.

## Scripts (`tools/captain/`)

| Script | Purpose |
|---|---|
| `mysql-setup.sh` | Idempotent: create/start the container, databases, accounts and grants; export the CA; verify TLS and plaintext refusal |
| `env.sh` | Source to export the runtime, migrator and test connection strings |
| `migrate.sh` | Option B deployment: `has-pending-model-changes`, then a ranged script from the last applied migration, executed by `bm_migrator`; `__EFMigrationsHistory` prevents reapplication |
| `add-migration.sh <Name>` | Generate a migration in WSL (G17) and copy it into the worktree |
| `persistence-tests.sh` | Live MySQL suite plus the leftover `buildmind_test_*` check |
| `run-api.sh` | API in Development with the Captain profile on loopback |

## Migration inventory (`buildmind_dev`)

| Migration | Owner | State |
|---|---|---|
| `20261118133137_InitialFeatureSchema` | M4 | applied |
| `20261119092022_AddProjectAuthorizationVersion` | M1 | applied |
| `20261120202215_AddSessionSecurity` | M4 | applied |
| `20261122080431_AddPlanRevisionHistory` | M4 | applied |
| `20261124095515_AddSurveys` | **Captain** | applied. Additive only: table `surveys` with FKs to projects/users (RESTRICT), unique `storage_id`, CAS `state_version` |

Later Captain migrations are appended per phase. Each Captain migration is registered in `BuildMindMigrationTests.CaptainMigrations`. That keeps the M4 schema-pin tests exact and also proves rollback back to the M4 baseline.

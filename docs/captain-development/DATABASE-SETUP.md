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
| TLS | **`SslMode=VerifyFull`** (certificate chain **and** host name) since the review correction of 2026-11-24. See the TLS section below. |
| `partial_revokes` | OFF (checked by the setup script) |

## Accounts (least privilege, all `REQUIRE SSL`)

| Account | Grants | Used by |
|---|---|---|
| `bm_runtime` | SELECT, INSERT, UPDATE, DELETE on `buildmind_dev.*` | API runtime (`ConnectionStrings__BuildMind`) |
| `bm_migrator` | DML + CREATE, ALTER, DROP, INDEX, REFERENCES on `buildmind_dev.*` | Ranged migration scripts only |
| `bm_test` | ALL on `` `buildmind\_test\_%`.* `` | Live integration tests (disposable databases) |
| `root` | — | Setup script only, through `MYSQL_PWD` inside `docker exec`; never used by the application |

## TLS (review correction)

The first setup used `VerifyCA`. That validates certificate trust (the chain to the server CA) but **not** the host name: any certificate issued by that CA would be accepted for any host. MySQL's auto-generated certificate has no SAN, so it cannot support host-name verification.

Current setup (`tools/captain/mysql-setup.sh`):
- **Server certificate:** a Captain local CA (`~/bm-captain/secrets/tls`, directory 0700, CA key 0600) issues a server certificate with `SAN DNS:localhost, IP:127.0.0.1`. mysqld uses it via `--ssl-ca`, `--ssl-cert` and `--ssl-key`.
- **File modes:** the files are bind-mounted individually. The server key is 0644 because mysqld runs as uid 999 in the container; the 0700 host directory keeps it private on the host.
- **Client mode:** clients use `SslMode=VerifyFull;SslCa=...`.
- **Provider requirements found:**
  1. MySql.Data 26.10.0 requires `SslCert`/`SslKey` whenever VerifyFull is used with PEM files ("No file path has been provided for the connection option SslCert"). A client certificate is therefore issued by the same CA and presented. The server still requires SSL only, so this adds client authentication and weakens nothing.
  2. MySql.Data parses only traditional PKCS#1 RSA keys ("The specified file cannot be converted to a key"), so the client key is converted with `openssl rsa -traditional`.
- **Verified:**
  - live MySQL suite 94/94 under VerifyFull;
  - live API journey 32/32 under VerifyFull;
  - MySQL CLI `VERIFY_IDENTITY` accepts 127.0.0.1;
  - **negative check:** `VERIFY_IDENTITY` refuses the container bridge IP, which is not in the SAN. This is CLI-level evidence; a MySql.Data-level negative test was not run.
- **Scope:** local development only. The certificates are self-issued with a 365-day lifetime. Production certificate management is out of scope and remains G11/hosting.

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
| `20261124104612_AddCaptainPlanningWorkflow` | **Captain** | applied. Additive only: `land_revisions` (unique project+number), `generation_jobs` (unique project+actor+idempotency key, FK land), `generation_candidates` (unique job+ordinal), `plan_validation_evidence` (server-issued ledger) |

Later Captain migrations are appended per phase. Each Captain migration is registered in `BuildMindMigrationTests.CaptainMigrations`. That keeps the M4 schema-pin tests exact and also proves rollback back to the M4 baseline.

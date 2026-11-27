# BuildMind Portable Backend

## Purpose

Run the BuildMind backend locally for development,
frontend integration, demonstrations and supervisor review.

## Prerequisites

- Windows with WSL 2 enabled.
- Ubuntu-24.04 WSL distribution.
- Docker Engine or Docker Desktop with WSL integration.
- Docker Compose v2 or compatible newer version.
- Git, Python 3, OpenSSL, flock and Linux libacl. Install the `acl` command-line package as well for `getfacl`/`setfacl` diagnostics.

On Ubuntu 24.04 run:

```bash
sudo apt-get update
sudo apt-get install -y python3 openssl util-linux libacl1 acl ca-certificates
```

The shared library `libacl1` alone does **not** install `getfacl` and `setfacl`.
- Internet access for initial image and dependency downloads.
- Network access to the BuildMind source repository.

## 1. Clone the repository

Clone the approved backend repository and open PowerShell
inside the repository root.

## 2. Check Docker

    wsl -d Ubuntu-24.04 --exec docker info

Docker must be running and accessible to the WSL user.

## 3. Start the backend

    wsl -d Ubuntu-24.04 --exec bash tools/portable/start.sh

The startup script prepares credentials, TLS certificates,
Docker images, MySQL, database migrations and backend services.
It then verifies the Development Swagger document exposes **actual feature routes**,
including registration, login, projects, surveys, land, generation, Admin and Staff.
If only `/health` is registered, startup fails with
`INCOMPLETE_BACKEND_ROUTE_REGISTRATION` rather than claiming READY.

The first startup may take several minutes or longer,
depending on hardware and internet speed.

Expected completion message:

    BUILDMIND_PORTABLE_BACKEND_READY

## 4. Access the backend

Swagger:
http://localhost:5182/swagger/index.html

**Important:** If Swagger is open at `http://localhost:5080/swagger/index.html`
and only lists `GET /health`, you have started a standalone default-configured
API, **not** the configured portable integration stack. Stop that standalone
process, then launch the portable stack with the command above and use port
`5182`. Do not disable authentication to make routes appear.

To verify feature registration explicitly, from the repository root run:

    wsl -d Ubuntu-24.04 --exec python3 tools/portable/verify-api-routes.py

Expected marker: `BUILDMIND_DEVELOPMENT_ROUTES_REGISTERED`.
This checks that routes are registered, not that all protected workflows pass.
Login and business-operation tests must still be performed.

Local email testing:
http://localhost:8027

Python, RAG and MySQL remain on the internal Docker network.

## 5. Stop the backend

    wsl -d Ubuntu-24.04 --exec bash tools/portable/stop.sh

Database and artifact volumes are preserved.

## 6. Restart the backend

Run the same start command again.

Existing credentials, certificates and database data are reused.
Pending migrations are applied when required.

## 7. Troubleshooting

Check container status:

    wsl -d Ubuntu-24.04 --exec bash tools/portable/compose.sh ps -a

Check service logs:

    wsl -d Ubuntu-24.04 --exec bash tools/portable/compose.sh logs --tail 100

Do not delete database volumes to resolve startup failures.

If a migration fails, do not retry blindly.
Review the migration failure and database state first.

The migrator generates the SQL inside its container and streams it to a
private host-side file; it does not write into a host-file bind mount.
It waits up to 60 seconds for the migrator's own TLS database path.
`MIGRATOR_TLS_CONNECTION_UNAVAILABLE_AFTER_60_SECONDS` means the connection
never became ready, rather than that migration SQL was attempted.

If `PREVIOUS_MIGRATION_REQUIRES_REVIEW` appears, review the actual MySQL
migration history and `~/.local/share/buildmind-portable/migration-in-progress`
before clearing that guard. Do not bypass it without verifying database state.

## 8. Security and data storage

Local credentials and certificates are generated outside
the repository under:

    ~/.local/share/buildmind-portable

Do not commit or share generated secrets or private keys.

Database and application artifacts use Docker named volumes.
API and Mailpit ports are bound to loopback.

Mailpit is for local email capture, not production email delivery.

## 9. Current limitations

- Local development and demonstration use only.
- Approved regulation corpus is not included.
- RAG starts without corpus; retrieval is unavailable.
- STEP 8B remains ON HOLD.
- External production integrations are not approved.
- Production deployment remains NO-GO.
- Independent-PC acceptance requires the team lead to retest the corrected checkpoint; a developer-machine pass alone is not independent acceptance.
- This setup uses one portable stack per Docker engine.
- Certificate expiry and migration failures require review.

## 10. Verified evidence

- All four application images built successfully.
- All 17 database migrations applied.
- EF model consistency check passed.
- All six container startup conditions satisfied.
- Windows Swagger returned HTTP 200.
- Stop automation passed.
- Existing-installation restart automation passed.

These results do not establish production readiness or
complete portable business-operation coverage.

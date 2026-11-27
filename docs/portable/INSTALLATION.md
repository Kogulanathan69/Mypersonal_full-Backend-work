# BuildMind Portable Backend

## Purpose

Run the BuildMind backend locally for development,
frontend integration, demonstrations and supervisor review.

## Prerequisites

- Windows with WSL 2 enabled.
- Ubuntu-24.04 WSL distribution.
- Docker Engine or Docker Desktop with WSL integration.
- Docker Compose v2 or compatible newer version.
- Git, Python 3, OpenSSL, flock and Linux libacl.
- Internet access for initial image and dependency downloads.
- Access to the private BuildMind repository.

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

The first startup may take several minutes or longer,
depending on hardware and internet speed.

Expected completion message:

    BUILDMIND_PORTABLE_BACKEND_READY

## 4. Access the backend

Swagger:
http://localhost:5182/swagger/index.html

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
- Fresh installation on an independent PC remains unverified.
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

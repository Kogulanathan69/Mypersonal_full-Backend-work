#!/usr/bin/env bash
# Usage: add-migration.sh <Name>
# Generates a BuildMindDbContext migration in the WSL build copy (dotnet-ef 10.0.12, no database connection) and
# copies the new migration files plus the updated model snapshot back into the Windows Captain worktree.
set -euo pipefail
name="$1"
~/bm-captain/sync.sh
cd ~/bm-captain/src
export BUILDMIND_MIGRATIONS_CONNECTION="Server=127.0.0.1;Port=3417;Database=buildmind_dev;User ID=bm_migrator;SslMode=VerifyCA"
dotnet tool restore >/dev/null
dotnet build src/BuildMind.API -c Release 2>&1 | grep -E " error |Error\(s\)" | sort -u
dotnet tool run dotnet-ef migrations add "$name" --context BuildMindDbContext --configuration Release --no-build \
  --output-dir Persistence/Migrations --project src/BuildMind.Infrastructure --startup-project src/BuildMind.API
dest=/mnt/c/Users/kogul/Documents/BuildMind-Worktrees/captain-full-backend-completion/src/BuildMind.Infrastructure/Persistence/Migrations
for f in src/BuildMind.Infrastructure/Persistence/Migrations/*_"$name".cs src/BuildMind.Infrastructure/Persistence/Migrations/*_"$name".Designer.cs src/BuildMind.Infrastructure/Persistence/Migrations/BuildMindDbContextModelSnapshot.cs; do
  cp "$f" "$dest/"
  echo "copied $(basename "$f")"
done

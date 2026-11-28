# Start the complete local BuildMind backend on a Windows team PC.
# No shared secrets or repository-specific manual configuration are required.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $root

Write-Host 'BuildMind Team Development — full portable backend' -ForegroundColor Cyan

if (-not (Get-Command wsl.exe -ErrorAction SilentlyContinue)) {
    throw 'WSL_NOT_INSTALLED: Install WSL 2 and Ubuntu-24.04, then retry.'
}
if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) {
    Write-Warning 'Git is not in PATH; clone must already be complete.'
}

& wsl.exe -d Ubuntu-24.04 --exec bash -c 'printf "WSL_READY\n"'
if ($LASTEXITCODE -ne 0) {
    throw 'UBUNTU_2404_NOT_AVAILABLE: Run wsl --install -d Ubuntu-24.04 then retry.'
}

& wsl.exe -d Ubuntu-24.04 --exec bash -c 'set -e; for c in docker python3 openssl flock; do command -v "$c" >/dev/null || { echo "MISSING_WSL_COMMAND:$c" >&2; exit 2; }; done; test -r /usr/lib/x86_64-linux-gnu/libacl.so.1 || ldconfig -p | grep -q libacl.so.1; docker info >/dev/null; docker compose version >/dev/null; echo "DEVELOPMENT_PREREQUISITES_READY"'
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Install Ubuntu packages if missing:' -ForegroundColor Yellow
    Write-Host 'wsl -d Ubuntu-24.04 --exec sudo apt-get update'
    Write-Host 'wsl -d Ubuntu-24.04 --exec sudo apt-get install -y python3 openssl util-linux libacl1 acl ca-certificates'
    Write-Host 'Start Docker Desktop, enable Ubuntu-24.04 integration in Docker Desktop > Settings > Resources > WSL Integration.'
    throw 'DEVELOPMENT_PREREQUISITES_MISSING'
}

# The portable bootstrap generates per-PC secrets and TLS. It migrates the
# private named-volume database and verifies Swagger feature registration.
& wsl.exe -d Ubuntu-24.04 --exec bash tools/portable/start.sh
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Startup failed; inspect logs with:' -ForegroundColor Yellow
    Write-Host 'wsl -d Ubuntu-24.04 --exec bash tools/portable/compose.sh ps -a'
    Write-Host 'wsl -d Ubuntu-24.04 --exec bash tools/portable/compose.sh logs --tail 100'
    throw "PORTABLE_BACKEND_STARTUP_FAILED: exit code $LASTEXITCODE"
}

Write-Host 'FULL_BACKEND_DEVELOPMENT_READY' -ForegroundColor Green
Write-Host 'Swagger: http://localhost:5182/swagger/index.html'
Write-Host 'Mailpit: http://localhost:8027'
Write-Host 'Do not use localhost:5080 for the full portable stack.'

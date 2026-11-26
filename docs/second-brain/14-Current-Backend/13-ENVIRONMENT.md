# Environment and setup

Historical baseline .NET SDK 10.0.401, Python 3.13.16; target net10.0, MySQL 8.4 LTS, Angular 20, TypeScript, Tailwind, Konva.js, Mapbox. Verify exact versions and compatible EF Core MySQL provider before package selection. No unrequested Next.js/MSSQL/PostgreSQL. Python dependencies as actual requirements, add Shapely/PyProj/GeoPandas/OCR only where required; approve lock/pins. Never install arbitrary packages solely to hide errors.

Read-only Windows audit from real repo root:
```powershell
git status -sb
git branch --show-current
git remote -v
git log -5 --oneline
dotnet --info
py -3.13 --version
Get-Service *mysql* -ErrorAction SilentlyContinue
Get-ChildItem src -Recurse -Filter *.csproj
Get-ChildItem python-services -Force
```
Inspect current repository and `.env.example` redacting values. Local installation commands are conditional after approval; use project venv (not global packages). Existing smoke commands historically: `dotnet build src/BuildMind.API/BuildMind.API.csproj`; in python-services `\.venv\Scripts\python.exe -m pytest` (use `.\.venv\Scripts\python.exe` in PowerShell). Verify DB port/target and credentials securely; do not print secrets. Use separate disposable DB for tests. Do not claim MySQL integrated merely because server installed. C# /health liveness separate readiness. Ports/config names discover from actual appsettings and environment; proposed configuration keys tracked after audit.

## Solution commands (from BE-M4-D01a, 2026-11-17)
`dotnet restore BuildMind.sln`, `dotnet build BuildMind.sln`, `dotnet test BuildMind.sln`. CI runs the same in Release on ubuntu-latest. On the lead's Windows workstation, Smart App Control (inbox policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`) blocked the test host and a direct run of the newly built `BuildMind.API.dll`; restore/build are unaffected. See G17.

## Configuration keys and User Secrets (BE-M4-D01c, 2026-11-18)
Implemented keys (secret-free defaults in `src/BuildMind.API/appsettings.json`): `Services:Python:Enabled|BaseUrl|TimeoutSeconds` and `Services:Rag:Enabled|BaseUrl|TimeoutSeconds` (both disabled; BaseUrl empty; timeouts 10 s / 5 s; environment variables use `__`, e.g. `Services__Python__BaseUrl`). Start-up fails only when a service is enabled without a valid URL or a value is invalid. Local secrets for future keys (e.g. `ConnectionStrings:BuildMind`, `Jwt:SigningKey`) belong in .NET User Secrets for `src/BuildMind.API` (`dotnet user-secrets set <key> <value> --project src/BuildMind.API`); never commit or print them. Proposed local API URL `http://localhost:5080` (`launchSettings.json`) is not confirmed with the frontend.

Local C# test execution (2026-11-18): Smart App Control blocked different freshly built assemblies on different builds (`BuildMind.Infrastructure.dll`, `BuildMind.Domain.dll`, then the test assembly itself). Linux CI remains the execution path (G17).

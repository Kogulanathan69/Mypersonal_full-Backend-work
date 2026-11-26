# Historical baseline and verification status

These are historical conversation observations, not a current live repo audit. Source starter zip is a scaffold, not proof of business features. No source code was modified to produce this documentation pack.

| Item | Evidence available | State to use |
|---|---|---|
| Backend foundation PR #5 merged to develop | historical screenshot, merge 763a9a7 | HISTORICALLY VERIFIED |
| Four .NET projects built | local terminal build output, .NET 10.0.401 | HISTORICALLY VERIFIED |
| Python syntax | 7 files parsed | HISTORICALLY VERIFIED |
| FastAPI health test | 1 passed, 1 Starlette/httpx deprecation warning | HISTORICALLY VERIFIED |
| 81 staged foundation files | terminal count + clean whitespace check after edits | HISTORICALLY VERIFIED |
| Backend/Frontend protected develop and CODEOWNERS | historical setup; current policy not checked | HISTORICAL CONFIGURATION |
| MySQL integration/migrations | no current proof here | UNVERIFIED |
| C# API runtime startup beyond build | no direct evidence in this pack | UNVERIFIED |
| RAG engine | owner reports completed; actual source not included/audited | USER REPORTED / UNVERIFIED |
| Current M1–M6 implementation | no live audit | UNVERIFIED |
| CI workflow file | starter contains configuration; run result not audited | EXISTING / UNVERIFIED |
| Google Sheets sync | authorization blocked in reported session | BLOCKED / OPTIONAL |

Inspect current branch and commit first. Do not transfer historical passes to current task completion. Add current evidence after tests. All new daily cards are NOT STARTED for this plan only (not a statement that no team member has coded anything).

Current audit (2026-11-17): see [07-AUDIT-2026-11-17](07-AUDIT-2026-11-17.md). It re-verified the C# build, Python health test and C# `/health` runtime at `ef9cbf3`; MySQL integration, CI runs and RAG tests remain unverified.

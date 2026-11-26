> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# GitHub workflow: avoid two people writing the same file

## Repository policy
- Protect `main`: no direct pushes, require PR + passing CI + review, disable force-push.
- `develop`: optional short-lived integration branch; protect too; do not leave perpetually divergent.
- Feature branches from latest integration base: `feature/M4-BM-101-auth-login`, `feature/M5-BM-205-plan-version`, `feature/M6-BM-302-polygon-check`, `feature/M7-BM-410-rag-evidence`.
- Single task branch per narrow vertical slice. Never share one human-owned working branch among all seven members.
- Branch naming, PR titles and commits reference issue ID: `feat(plans): persist accepted revision (BM-205)`.

## Before coding checklist
1. Read ownership register and verify task ID and paths.
2. `git switch develop && git pull --ff-only` (or approved base branch).
3. `git switch -c feature/M5-BM-205-plan-version`.
4. Confirm no one else is the current exclusive writer of your target files.
5. Contract changes first; notify FE/BE peers in standup/handoff note.

## PR requirements (non-negotiable)
- PR title, issue, owner, dependent FE/BE feature, risk, changed paths.
- Validation: dotnet build/test or pytest as appropriate, lint/static checks, contract fixtures, regression tests.
- Schema change: migration approval M4; M5 supplies entity/config and migration proposal only.
- Geometry/RAG contracts: two consumer reviews M5+M6/M7 where relevant; version fixture.
- Screenshot or HTTP sample + evidence of error paths for UI/API integration.
- Required code owner reviews; M7 lead tracks but must not bypass required owner.
- At least 1 non-author reviewer; security/DB/contract changes 2 reviewers including designated owner.

## Two members need the same file: conflict-resolution priority
| Situation | Writer | Others do |
|---|---|---|
| `Program.cs`, DI registration, DbContext, migrations | **M4** | M5 opens issue/PR in own feature folder and requests integration |
| Python `main.py`, deps/config | **M6** | M7 changes only RAG submodule, sends route registration request |
| Python RAG retrieval/evidence source | **M7** | M6 changes `rag_client.py`, consumes published schema |
| Canonical plan JSON schema | **M6** | M5/M3 propose schema change; wait for approved version |
| Main HTTP OpenAPI spec | **M4** | M5 edits feature OpenAPI proposal or issue, M4 publishes canonical |
| Angular `app.routes.ts`, shared navbar/theme | **M1** | M2/M3 only add feature module and request route hookup |
| Risky emergency shared file change | **Temporary owner nominated explicitly** | all other writers freeze until handoff ends |

## Merge order for connected work
1. **Contract PR** approved/versioned (M4/M6/M7 as applicable).
2. **Producer PR** creates implementation and contract tests (M4/M5/M6/M7).
3. **Consumer PR** connects frontend/C# adapter using contract fixtures (M1/M2/M3/M5/M6).
4. **Integration PR** evidence + smoke test; merge once compatible.
5. M7 updates tracker: `Done` only after merged & validated acceptance.

## Avoid conflict in practice
- Keep independent feature folders, avoid giant `Service.cs` or central `Models.cs` edited by everyone.
- Avoid concurrent EF migrations; M4 is migration coordinator, names migration with issue and owner, serializes all migration generation.
- CI must regenerate canonical contract snapshots and fail on unapproved drift.
- Commits should be small, focused; do not check in secrets, raw personal survey plans, downloaded regulation corpora, `.env`, generated vector DB files.
- Update branch often via `git fetch`, `git rebase origin/develop` on your **unshared** feature branch; never rebase a shared branch another member is actively using.
- `git status`, `git diff`, `git diff --check`, local tests before push. Never `git push --force` main/develop.

## Rejected/blocked PR steps
1. Label `BLOCKED-CONTRACT`, `BLOCKED-OWNER`, `BLOCKED-CI`, or `BLOCKED-DATA`.
2. Comment exact file, conflict, current branch, owning member, target date.
3. M7 triages; source owner resolves; blocked member works on independent tests/mocks meanwhile.
4. Owner sends commit/merge hash; consuming member syncs; M7 closes handoff record.

## PR template
```
## Issue / member / branch
## Scoped files (ownership checked)
## API/schema and compatibility effects
## Test commands + PASS evidence links
## Sensitive data / authorization checks
## Dependencies and blocked teammates
## Reviewer (non-author) / CODEOWNER
## Acceptance demo and rollback plan
```

# Install and review the V3 documentation overlay

This package has ONLY documentation files, not replacement backend code. It contains `CLAUDE.md`, `AGENTS.md`, `docs/second-brain/` and this guide. Do not overwrite the repository wholesale or delete existing files. No live push was done.

1. Extract ZIP to a new staging directory OUTSIDE the Git clone. Check for secrets, licensing and private content before publishing to the public repo. Review scope/reconciliation and confirm roster/admin gap first.
2. In the actual backend repo, run `git status -sb`, `git branch --show-current`, `git remote -v`. Stop if dirty or unexpected repo. Fetch with approval, inspect develop. Use a dedicated documentation feature branch from current develop (e.g. `docs/second-brain-v3`), only if clean and approved. Reuse existing branch if appropriate.
3. Compare staged docs against actual `docs/second-brain` and existing root `CLAUDE.md`/`AGENTS.md`. Preserve newer changes; do not overwrite automatically. V3 intentionally adds historical notices to older files and replaces entry navigation; merge by reviewed diff. Existing repo README, CODEOWNERS, CI, source, and DB remain unchanged. If archive/legacy note duplicates are present, resolve via explicit review rather than delete.
4. Review `git diff`, `git status`, selected files and security; stage only approved documentation paths. Run `git diff --cached --check`. Confirm docs links and no unrelated files. With explicit approval, commit and push the docs branch, then PR to develop for review. Never push directly to protected branches.
5. Claude Code from actual repo root: read `CLAUDE.md`, `docs/second-brain/00-HOME.md` and start with read-only audit. The vault does not verify current code, RAG path, dependencies, or database; do that before coding. Obsidian opens `docs/second-brain` as vault. No embeddings/MCP required.

## Suggested first prompt
Read CLAUDE.md and the active V3 Second Brain. Audit the existing backend and RAG source without modifying files. My proposed ownership is M4 and M7; verify the actual roster. Separate verified completed work from pending/unverified work. Show exact files, tests and blockers, then propose the next small implementation slice and wait for approval.

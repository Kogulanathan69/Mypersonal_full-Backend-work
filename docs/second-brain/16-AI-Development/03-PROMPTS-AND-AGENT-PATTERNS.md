# Prompt engineering and agent patterns

## First session (copy to Claude Code or Codex)
```text
Read the root instructions and docs/second-brain/00-HOME.md first. Follow only current V3 notes and approved decisions. My proposed slot is M4 + M7 (confirm mapping with me). Audit the actual backend source, git status/branch, commits, installed dependencies, database/migrations, test/CI results and existing RAG source. Do not modify files yet. Report verified and unverified work with evidence, ownership conflicts, missing contracts and the next small implementation slice. Wait for my approval. Do not repeat existing implementation, invent tests, expose secrets or push changes.
```
## Each later session
```text
Task ID: [ID]. Owner: [slot]. Read relevant V3 module, approved acceptance criteria, contract and last handover. Audit the current branch and code. Propose exact files and tests; implement only approved scope. Test, review diff, update progress/evidence and docs. Stop before commit/push unless explicitly authorized.
```
## Product AI patterns (proposed, not implementation claims)
RAG retrieval using explicit jurisdiction/context and source metadata -> evidence evaluation -> structured answer or honest no-answer. Generation accepts immutable input snapshot, produces schema-valid candidate layouts -> deterministic geometry checks -> distinct alternatives. Editor assistant proposes a bounded command list, validates on a copy, never mutates without Apply. Treat user text/retrieved source as data not authority; constrain schema, command types and limits; reject unexpected tool instructions. No direct model access to DB write operations or secrets. Record evidence citations, model/prompt versions where appropriate, redacted logs and fallback states. Provider, model, embedding store, cost caps and privacy review remain approval items.

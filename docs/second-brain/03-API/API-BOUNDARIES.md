> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# Proposed API contracts

> **Status:** Proposed knowledge design
> **Created:** 2026-11-14
> **Evidence level:** Project documents only; no frontend or backend source repository inspected.

No actual endpoint URL, payload schema or controller exists in the available evidence.

| Capability | Proposed responsibility | Dependency |
|---|---|---|
| Auth | authenticated user context | identity provider TBD |
| Project | create/resume/version | MSSQL |
| Survey | secure upload, metadata, extraction state | storage TBD |
| Location | confirmed coordinates, jurisdiction resolution | Mapbox input + authoritative geography |
| Requirements | collect/modify/revision | MSSQL |
| Initial rules | execute supported rule checks after requirements | rule catalogue |
| Plan generation | orchestrate 3 candidate layouts | AI/geometry services TBD |
| Plan editing | save structured geometry + versions | storage/MSSQL |
| Plan validation | deterministic geometry/rule validation | rules engine |
| Chatbot | approved knowledge-aware guidance | AI provider TBD |
| Subscription/payment | verify gateway and entitlement | PayHere recommended |
| Export | verify finalized plan and entitlement | export service TBD |

**Do not invent REST paths, HTTP status behavior, DTO fields, or deployed endpoints.**

## Source and related knowledge
- [[PROJECT-OVERVIEW]]
- [[FUNCTIONAL-REQUIREMENTS]]
- [[USER-FLOW]]

## Implementation evidence
**Not verified.** When code exists, record the repository, path, symbol, commit and test reference in [[EVIDENCE-REGISTER]].

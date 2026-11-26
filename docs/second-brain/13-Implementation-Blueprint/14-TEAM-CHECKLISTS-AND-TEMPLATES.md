> **HISTORICAL V2 — superseded where inconsistent.** Do not execute old paths, ownership, payment or technology choices. See [current V3 home](../00-HOME.md).

# GitHub issue, PR, daily report templates

## Issue
```
BM-### | owner M# | task day # | module
Exact owned files:
API / schema contract:
Downstream FE/BE consumer:
Acceptance criteria:
Expected tests + negative cases:
Blocked shared file? primary owner and requested patch:
```

## Pull Request
```
Issue / branch / author:
Changed exact paths and owners checked:
Contract version & fixture:
CI command + PASS output + SHA:
Shared-file owner review:
Data/PII/security validation:
Dependent member acknowledged:
Rollback plan / acceptance screenshot:
```

## Daily standup
`Member | Day | Issues | Current state | Dev % | QA accepted % | PR link | Build/test evidence | FE/BE dependency | Blocker | ETA`.

**Definition of Done:** own code merged + CI passing + contract compatible + reviewer accepted + representative success/failure path tested. M7 tracks integration and RAG evidence, each developer tests own work.

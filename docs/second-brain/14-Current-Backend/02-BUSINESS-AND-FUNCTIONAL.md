# Business and functional requirements

BuildMind reduces friction between homeowner intentions and an editable preliminary residential plan. Users need understandable requirements, honest feasibility, alternatives, safe editing and a shareable report. Architects need reproducible input, geometry and evidence, not a claim of official approval. Admin needs controlled source and staff management. Proposed measurable outcomes: successful owner-scoped project reopen; no lost requirements; three distinct validated layouts for supported fixtures; rejected edit leaves state unchanged; correct revision in report; verified source provenance. Do not invent baseline KPIs.

| ID | Requirement | Acceptance gate | Owner slot |
|---|---|---|---|
| FR01 | Registration/login/profile/recovery | Duplicate account and expired recovery denied; no hash returned | M1 |
| FR02 | Create/reopen/update project | Cross-owner requests denied; idempotent create or clear duplicate policy | M1 |
| FR03 | Private survey upload and extraction | Type/signature/size checks; unreadable files flagged | M2 |
| FR04 | Confirm location/boundary | Valid polygon, explicit CRS, exact revision confirmation | M2 |
| FR05 | Requirements form and optional spaces/relationships | Save/reopen; counts validated, user intent preserved | M5 |
| FR06 | Pre-design feasibility after land+requirements | Known constraints distinguished from missing evidence | M6/M7 |
| FR07 | Generate/compare/select alternatives | Exactly 3 distinct feasible results or honest failure | M6 |
| FR08 | Manual and AI editor operations | Invalid edits rollback; Apply/Reject explicit; undo/redo | M3 |
| FR09 | Save version and confirm | Immutable saved revision; reject stale/conflicting edits | M3 |
| FR10 | Preliminary PDF and download | Correct confirmed snapshot and owner; disclaimer | M5 |
| FR11 | Architect request and status | Explicit selected recipient/documents consent; truthful status | M5 |
| FR12 | Regulatory retrieval / assistant | Cite document/page; no invented applicability | M7 |
| FR13 | Admin staff and approval workflow | Pending staff writes do not affect live state; admin rejection no change | Unassigned — approval needed |
| FR14 | Shared infrastructure/security | Builds, migrations, API errors and dependency health tested | M4 |

Non-functional: confidentiality/owner isolation, deterministic geometry validation, traceability, reproducible builds, bounded timeouts and uploads, accessible error states, no unverified compliance. Performance and availability targets require measured baseline and approval; do not invent SLAs.

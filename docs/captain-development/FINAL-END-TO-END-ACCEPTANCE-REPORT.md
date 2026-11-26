# Final end-to-end acceptance report

Real processes, real MySQL (TLS) and the real approved regulation corpus; synthetic users and projects only.

| # | Journey (order §19) | Evidence | Result |
|---|---|---|---|
| 1 | Registration → final plan → report | journey 2–6 (process 253/253; containers 216/216) | PASS |
| 2 | Survey upload → extraction → review → confirmed land | journey 2/8 | PASS (boundary roles: R-01 open) |
| 3 | Regulation RAG retrieval → evidence-grounded validation | journey 5 (24/24), rag_acceptance 33/33 (process and containers) | PASS for retrieval with verified citations; reviewed constraint values E-5 |
| 4 | Requirements → generation → alternative selection | journey 3/8/10 | PASS |
| 5 | Manual editing → final confirmation (+ undo/redo) | journey 4/11 | PASS |
| 6 | Real AI proposal → validation → application | smoke_llm 10/10 (process + container), benchmark 19/20 with 0 unsafe | PASS (local model) |
| 7 | Report generation → authorized download | journey 6 | PASS |
| 8 | Architect request → consent-bound feedback | journey 9 | PASS |
| 9 | Consent withdrawal → access rejected | journey 9 (404 after withdrawal) | PASS |
| 10 | Administrator workflows | smoke_admin 65/65; journey 11 (oversight, workers, dependencies) | PASS |
| 11 | Staff workflows | smoke_admin | PASS |
| 12 | Password recovery and session security (incl. single-session revocation) | journey 7/9/11; admin smoke | PASS |
| 13 | Database restart and recovery | WSL VM crash twice → InnoDB recovery, data intact; persistence 115/115 | PASS |
| 14 | Docker restart and persistent state recovery | compose restart, and VM-crash auto-recovery with data intact | PASS |

Excluded scope (cost, BOQ, 3D, payments) is not part of any journey. External acceptance (external LLM, SMTP provider, reviewed regulation values) is listed separately in CAPTAIN-EXTERNAL-INPUT-REQUEST.md.

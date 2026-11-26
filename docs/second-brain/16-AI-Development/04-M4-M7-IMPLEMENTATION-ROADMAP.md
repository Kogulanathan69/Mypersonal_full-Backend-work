# Lead implementation roadmap — M4 + M7 proposal

If the user selects M4 in addition to M7, use this sequence, not a promise of completion. Audit and reuse existing work. M4 PDF contains detailed seven-day tasks; M7 plan is separate. Reserve time for independent review of lead-authored changes.

| Phase | Work | Exit evidence |
|---|---|---|
| 0 | Read V3, verify branch and source, actual roster | Baseline inventory, approval |
| 1 | Confirm .NET/EF/MySQL compatibility; solution and shared DI | Clean build, approved dependency versions |
| 2 | DbContext, mappings from feature owners, migration | Disposable DB apply, owner FK/unique checks, recovery |
| 3 | Middleware, Problem Details, logging, CORS, health, shared security | API/security tests; no secrets |
| 4 | Typed internal HTTP clients, auth/timeouts/cancellation | Contract and dependency failure tests |
| 5 | Existing RAG audit, evidence schema and integration | Real source traceability and no-hit tests |
| 6 | Partner integration: M1/M2/M5/M6/M3 | Actual consistent IDs, revisions, owner checks; blocked if missing |
| 7 | CI, QA, docs and release review | Verified go/no-go with independent review |

Do not implement feature modules assigned to other developers. Do not create another RAG pipeline unless explicitly approved. Admin feature has no assigned delivery owner yet. GitHub/Sheets automation is optional and does not block coding.

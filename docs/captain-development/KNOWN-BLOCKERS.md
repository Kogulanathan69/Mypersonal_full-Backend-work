# Known blockers — Captain full-backend completion

Branch `feature/captain-full-backend-completion` (worktree `BuildMind-Worktrees/captain-full-backend-completion`), started from `fec3254`.

**Production integration and deployment: NO-GO. STEP 8B: ON HOLD.**

| ID | Blocker | State | Effect on this work |
|---|---|---|---|
| RH09 | `RH_MYSQL_HTTP_09_command_timeout_classification_maps_to_504`: MySql.Data 26.10.0 and 26.7.0 hang inside the provider's command-timeout recovery and reader cleanup (reproduced without BuildMind or EF Core; `SELECT SLEEP(5)` with a 1 s timeout hangs). Diagnostic container removed; zero leftover sessions or schemas; cleanup exit 0. | **DEFERRED / UNRESOLVED** | Not re-investigated. Any suite that contains RH09 is reported as incomplete, never as passed. Production readiness cannot be claimed. |
| STEP 8B | M7 step on hold | **ON HOLD** | No STEP 8B functionality is activated, modified or bypassed. |
| R-1 / R-3 | MySql.Data provider `KILL QUERY` gating (R-1) and unbounded TLS handshake holding the process-wide open gate (R-3), doc 27 | OPEN (production) | Accepted for local development only. |
| G03 | `MySql.EntityFrameworkCore` commercial licence review | OPEN | Internal development and testing use only. |
| G06 | Legal currency of the regulation sources; real reviewed constraint values | OPEN | No regulatory value is invented. Generation treats regulatory evidence as advisory unless a verified constraint pack exists. |
| G11 | Internal service transport/TLS/hosting. The per-service shared token (`X-BuildMind-Internal-Token`) is decided for rag-service. | PARTIAL | Loopback-only local runs. |
| G17 | Windows Smart App Control blocks freshly built .NET assemblies | OPEN (not bypassed) | All .NET build/test/run happens in WSL Ubuntu-24.04 from a one-way copy (`~/bm-captain/src`). |
| G19 | Merged PRs without independent review | OPEN | This branch is local only; no push or merge. |
| Lifecycle writer | No approved project transfer/revocation writer (C-01 doc, "Uncovered paths") | OPEN | Captain-owned writers use the ratified A1–A8 primitive (`ProjectMutationAuthorization.LockAndCheckAsync` in the same transaction). Lifecycle transfer is not implemented. |
| Python auth | python-services has no internal token | See DEVELOPMENT-PROGRESS | Tracked per phase. |

Entries are appended per phase below.
| RAG-CORPUS-WSL | Real regulation corpus cannot be served in this environment: the manifest pins Windows-separator identities (the loader refuses normalisation, correctly); the API cannot run on Windows (G17); WSL is in NAT mode | BLOCKED (environment) | Live RAG verified only on fail-closed paths; real-corpus retrieval not re-verified in this session |
| EDGE-ROLE | Mapping front/rear/side constraint kinds to parcel edges (doc 29 §11, M6) | OPEN | rag-constraints evidence is never REVIEWED; generation answers NEEDS_REVIEW |
| G06 constraint table | No reviewed real constraint table; only a synthetic fixture | OPEN | Synthetic setbacks remain an explicit development-only policy mode |
| STEP 8B scope | Not defined in the repository | ON HOLD | No M7 code or registration changed |

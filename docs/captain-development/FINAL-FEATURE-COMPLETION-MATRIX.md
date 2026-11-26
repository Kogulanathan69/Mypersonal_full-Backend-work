# Final feature completion matrix

**Snapshot:** HEAD `a69086a` plus the final docs commit, 2026-11-25. This supersedes FEATURE-COMPLETION-MATRIX.md.

**Vocabulary (order §7):**
- Status: IMPLEMENTED / PARTIAL / MISSING / BLOCKED_EXTERNAL / BLOCKED_PROTECTED_SCOPE / OUT_OF_SCOPE.
- Verification: NOT_TESTED / UNIT_VERIFIED / INTEGRATION_VERIFIED / LIVE_VERIFIED / INDEPENDENTLY_ACCEPTED.
- **No feature is INDEPENDENTLY_ACCEPTED.** Independent review of this implementation has not happened and cannot be self-claimed.
- **Live** means the final process-stack run (`~/bm-captain/results/final-live/`):

  | Run | Result |
  |---|---|
  | Journey phases 2–10 | 202/202 |
  | Admin smoke | 65/65 |
  | mTLS negatives | 12/12 |

  plus the container runs (132/132 and 110/111) and the real-LLM run (10/10).

| # | Feature | Status | Verification | Evidence | Limitation |
|---|---|---|---|---|---|
| 1 | Registration, login, refresh rotation, logout, logout-all | IMPLEMENTED | LIVE_VERIFIED | journey 2/7 | — |
| 2 | Password recovery (enumeration-safe) and email verification | IMPLEMENTED | LIVE_VERIFIED (local capture) | journey 9 via Mailpit | External delivery awaits I-2/I-3 |
| 3 | Account security (password change, security events) | IMPLEMENTED | LIVE_VERIFIED | admin smoke | — |
| 4 | Profile / current user | IMPLEMENTED | LIVE_VERIFIED | journey 8 | — |
| 5 | Projects CRUD and lifecycle | IMPLEMENTED | LIVE_VERIFIED | journey | — |
| 6 | Customer dashboard | IMPLEMENTED | LIVE_VERIFIED | journey | — |
| 7 | Requirements (snapshot, spaces, relationships, summary, design method) | IMPLEMENTED | LIVE_VERIFIED | journey; Requirements.Tests 456 | Catalogue content is synthetic until approved (I-6) |
| 8 | Requirement catalogue administration (versions, publish, retire) | IMPLEMENTED | LIVE_VERIFIED | journey 9 / admin smoke | — |
| 9 | Survey upload (private storage) | IMPLEMENTED | LIVE_VERIFIED | journey 2/8 | — |
| 10 | Survey extraction (worker, PDF text, local OCR) and customer review | IMPLEMENTED | LIVE_VERIFIED | journey 8; live DB tests 12/12 | OCR accuracy on real surveys not measured (I-5) |
| 11 | Land: validate, confirm, read; local metric frame | IMPLEMENTED | LIVE_VERIFIED | journey 3/8 | — |
| 12 | **Georeferenced land / CRS frame / digest / edges** | IMPLEMENTED | LIVE_VERIFIED | journey 10; 13 py + 3 .NET | Edge roles UNASSIGNED (regulation-dependent) |
| 13 | Generation: 3 distinct alternatives or truthful INFEASIBLE | IMPLEMENTED | LIVE_VERIFIED | journey 3/8/10 | Setbacks synthetic (G06) |
| 14 | **Doors and windows in generation; access validation** | IMPLEMENTED | LIVE_VERIFIED | journey; test_openings 13 | Design-preference sizes |
| 15 | Alternatives / select / save plan | IMPLEMENTED | LIVE_VERIFIED | journey 8 | — |
| 16 | Edit validation and structured edits (candidate create/apply/reject) | IMPLEMENTED | LIVE_VERIFIED | journey 4 | — |
| 17 | **AI natural-language edits (real LLM)** | IMPLEMENTED | LIVE_VERIFIED (local model) | smoke_llm 10/10; LlmEditIntentTests 11 | External provider I-1; latency about 120 s locally |
| 18 | AI edits (rule-based fallback) with specific rejection reasons | IMPLEMENTED | LIVE_VERIFIED | journey 8; AiProposalRejectionTests 6 | Labelled as not an AI model |
| 19 | Revision history | IMPLEMENTED | LIVE_VERIFIED | journey 3/4 | — |
| 20 | Final confirmation | IMPLEMENTED | LIVE_VERIFIED | journey 4/8 | — |
| 21 | Preliminary PDF report (generate/status/download/list), source-traceable | IMPLEMENTED | LIVE_VERIFIED | journey 6/8; PDF QA enabled (Reports.Tests 164, 0 skipped) | Not an approval document |
| 22 | Architect directory, requests, consent, withdrawal, portal (respond/feedback) | IMPLEMENTED | LIVE_VERIFIED | journey 9 | — |
| 23 | Notifications (in-app) and transactional outbox email | IMPLEMENTED | LIVE_VERIFIED | journey 9 (incl. REPORT_READY, ARCHITECT_REQUEST_WITHDRAWN) | External SMTP awaits I-2/I-3 |
| 24 | Admin: accounts, roles, staff modules, tasks, policies, monitoring, audit chain verify | IMPLEMENTED | LIVE_VERIFIED | admin smoke 65/65 | — |
| 25 | Staff workspace (tasks, notes, proposals, project review) | IMPLEMENTED | LIVE_VERIFIED | admin smoke | — |
| 26 | Regulation retrieval (real corpus) | BLOCKED_PROTECTED_SCOPE | LIVE_VERIFIED (fail-closed only) | journey 5 | STEP 8B / I-4 |
| 27 | Regulatory constraints for generation | BLOCKED_PROTECTED_SCOPE | LIVE_VERIFIED (fail-closed only) | journey 5 | Depends on 26 + G06 |
| 28 | External email delivery | BLOCKED_EXTERNAL | UNIT_VERIFIED (policy) | SmtpDeliveryPolicyTests 7 | I-2/I-3 |
| 29 | External LLM provider | BLOCKED_EXTERNAL | — (adapter LIVE_VERIFIED locally) | REAL-LLM report | I-1 |
| 30 | Health / readiness | IMPLEMENTED | LIVE_VERIFIED | journey 2/7, containers | — |
| 31 | Internal mTLS API → python-services | IMPLEMENTED | LIVE_VERIFIED | mTLS 12/12, containers | RAG link token-only (STEP 8B) |
| 32 | Containerized stack | IMPLEMENTED | LIVE_VERIFIED (local) | DOCKER-CONTAINER-VERIFICATION | Not a deployment |
| 33 | DB call containment (RH09) | PARTIAL | INTEGRATION_VERIFIED | RH09 probe | Provider defect unresolved (I-8) |
| — | Cost estimation, BOQ, 3D, payments | OUT_OF_SCOPE | — | — | Excluded by the scope correction; not counted |

**Totals (33 counted features):**

| Status | Count | Features |
|---|---|---|
| IMPLEMENTED | 28 | — |
| PARTIAL | 1 | 33 |
| BLOCKED_PROTECTED_SCOPE | 2 | 26, 27 |
| BLOCKED_EXTERNAL | 2 | 28, 29 |
| MISSING | 0 | — |

Verification for the IMPLEMENTED features: 28 LIVE_VERIFIED. Two of them are local-only: #17 (local model) and #32 (local containers).

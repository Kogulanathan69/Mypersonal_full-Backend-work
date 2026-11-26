# Final remaining blockers register

**Updated:** final remaining-development order.

**Previously open actionable items (all closed in this order):**

| # | Item | Status | Evidence |
|---|---|---|---|
| R-01 | Boundary-role review (road frontage / side / rear) | **DONE** | migration #16 `AddLandEdgeRoles`; `GET land/edges`, `PUT land/edges/{edgeId}/role`. Customer-declared only, bound to the exact land revision, protected transaction, stale 409, never carried over (lineage suggestion only). Journey phase 12. |
| R-02 | Survey original-file download | **DONE** | `GET surveys/{surveyId}/file`: owner-scoped; server-GUID path; symlink, size and SHA-256 checks; attachment + no-store + `X-Content-SHA256`. Phase 12: exact bytes and hash. |
| R-03 | Regulation citations in the PDF report | **DONE** | M5 renderer unchanged; Captain decorator appends quoted passages with source, full document SHA-256, page, offsets, passage id and status. Phase 6 extracts the downloaded PDF text and finds an approved document id + page + passage provenance. |
| R-04 | Architect completion + consent expiry | **DONE** | `POST /architect/requests/{id}/complete`: after ACCEPTED, row lock + CAS; notification + audit. 3 concurrent completions → exactly one succeeds. Consent lifetime is enforced in the single visibility query (expired → 404 everywhere, absent from lists), proven live on an instance with a 1-minute lifetime. |
| R-05 | API response contracts + live acceptance | **DONE** | 146 operations, **146 with a successful live call**, 94 with negative-path evidence, 17 with an observed 401 (FINAL-API-ACCEPTANCE-MATRIX). OpenAPI + TypeScript regenerated from the run. |

**Remaining genuine blockers (not actionable inside this workspace):**

| # | Item | Class | Detail |
|---|---|---|---|
| B-1 | RH09: MySql.Data command-timeout / cancellation | **Unresolved provider defect** | No EF Core 10 provider without MySql.Data exists (re-verified 2026-11-25). Bounded containment only (server limits, open gate); **not** a resolution. |
| B-2 | External LLM provider | External (E-1, E-2) | adapter ready; local model benchmarked |
| B-3 | External SMTP provider + approved recipient | External (E-3, E-4) | verified-TLS delivery proven against the TLS-required capture |
| B-4 | Reviewed regulation constraint values (setbacks, coverage) | External (E-5) | needs a named reviewer; no values are invented |
| B-5 | Additional regulation sources | External (E-6) | M7 approval of new pins |
| B-6 | OCR accuracy on real surveys | External (E-7) | authorised representative surveys |
| B-7 | MySQL provider licence | Decision (E-8) | — |
| B-8 | Publication / CI / container supply chain | Decision (E-9) | no push authorized |
| B-9 | Semantic (embedding) retrieval | Decision (M7) | the approved engine is lexical BM25 |
| B-10 | 180-operation target | Decision | 146 justified operations; the rest would be duplicates (FINAL-API-ENDPOINT-INVENTORY) |
| B-11 | Independent security and code review; production deployment | Review | production **NO-GO** |
| B-12 | Local model reliability | Limitation | 1.5B model: 20/20 on the benchmark with 0 unsafe, but 2 must-reject prompts still produced a command that only server validation stopped. **Not "fully reliable".** |

Excluded scope (not pending): cost estimation, BOQ, customer 3D generation, payments.

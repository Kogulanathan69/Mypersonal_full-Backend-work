# Feature completion matrix

**Categories (section 23 of the master order):**
- **A** = implemented and verified live in Development;
- **B** = implemented, verified by unit/integration tests only;
- **C** = implemented but limited by an external decision or provider (truthful degraded behaviour);
- **D** = blocked (needs an approval, provider or environment change);
- **E** = out of scope until approved.

| Feature | Category | Verified by | Limitation / next owner action |
|---|---|---|---|
| Registration, login, logout, refresh rotation, logout-all | A | Journey 2/7, live S08/S09 | — |
| Password recovery | C | Unit + live store tests | No email provider (non-delivering); needs a provider decision |
| Current user (`/profile`, `/users/me`) | A | Journey 8 | — |
| Projects CRUD (create/list/read/patch) | A (patch: B) | Journey; unit | Delete/transfer/lifecycle not approved (M1) |
| Dashboard summary | A | Journey | — |
| Requirements (snapshot + sections + summary + design method) | A (sections: B) | Journey; Requirements.Tests 456 | Catalogue provisional (synthetic fixture) |
| Survey upload (private storage) | A | Journey 2/8 | — |
| Survey extraction (worker, PDF text, local OCR, result API) | A | Journey 8; live Q01–Q04, P01–P03; Python 27 + 3 local OCR | OCR accuracy not evaluated on real surveys; values are always customer-reviewed |
| Land validate (dry run), confirm, read | A | Journey 3/8 | Real CRS/georeferencing out of scope (local metric frame) |
| Generation (3 distinct alternatives / truthful INFEASIBLE, idempotent) | A | Journey 3/8 | Constraints synthetic (G06); EDGE-ROLE open |
| Alternatives / select / POST plans | A | Journey 8 | — |
| Edit validation (no persistence) | A | Journey 8 | — |
| Structured edits (candidate create/apply/reject) | A | Journey 4 | — |
| Save server-validated version | A | Journey 8 | — |
| AI edit proposals | D | Journey 8 (503 truthful) | G20: approve an AI provider and data policy |
| Revision history | A (by-id: B) | Journey 3/4; integration | — |
| Final confirmation (+ `/confirm` alias) | A | Journey 4/8 | Project status transition not approved (lifecycle writer) |
| Preliminary PDF report (generate/status/download/list) | A | Journey 6/8; PDF QA; pypdf check | — |
| Architect requests (create/read/list/withdraw) | C | Journey 6 | G09/G12: recorded, never dispatched; synthetic directory |
| Regulation retrieval | C | Journey 5 (fail closed) | Real corpus blocked in WSL (identity pins); STEP 8B ON HOLD |
| Regulatory constraints for generation (rag-constraints mode) | C | Journey 5 | Never REVIEWED (EDGE-ROLE) |
| Health / readiness | A | Journey 2/7 | — |
| Admin / staff / RBAC / audit UI | E | — | G09: approve module, roles, contracts |
| Cost estimation, 3D | E | — | Not in the 2D MVP |
| Production deployment | D | — | NO-GO (RH09, G03, G11, G17, G19) |

**Totals:** A 17, C 4, D 2, E 2 (25 features). Features verified only by unit/integration tests appear as sub-items marked B above (project patch, requirement sections, revision-by-id).

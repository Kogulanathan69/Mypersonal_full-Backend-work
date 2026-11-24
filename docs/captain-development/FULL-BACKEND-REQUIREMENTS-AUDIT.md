# Full backend requirements audit (Phase 0 of the master execution order)

Audit date 2026-11-24. Worktree HEAD at start: `ed7945d` on `feature/captain-full-backend-completion`; the main workspace (`fec3254`, 6 pre-existing items) is untouched.

## Sources reconciled

| Source | Role |
|---|---|
| `docs/second-brain/14-Current-Backend/05-API-CONTRACTS.md` | **Authoritative V3 product API register** (current approved planning scope; supersedes V2) |
| `docs/API-CONTRACT-REGISTER.md`, `13-Implementation-Blueprint/05-API-AND-DATA-FLOWS.md`, `03-API/API-BOUNDARIES.md` | Supporting / historical; no additional product routes beyond the V3 register |
| `14-Current-Backend/09-ADMIN.md`, `15-OPEN-DECISIONS.md` (G09) | Admin/staff module **not approved**: "Admin/staff/proposal routes — approve module, permissions and contracts first" |
| Source code: `IApiEndpointModule` implementations, `Program.cs`, Captain composition, FastAPI apps | Actual registrations |

**"180+ endpoints" claim:** no document in the repository supports it. Every Markdown file together mentions 71 distinct method+route strings, including internal, historical and proposal routes. The V3 register lists **46 product method+route pairs**, plus health/readiness and an unspecified admin/staff placeholder. No endpoints were invented to reach a number.

## Classification summary (before this execution)

See MASTER-API-ENDPOINT-MATRIX for every row. At the start of this execution:
- **implemented, matching the V3 route:** 33;
- **implemented under a different route:** `/profile` vs `/users/me`; `/plans/{planId}/confirmation` vs `/confirm`; save-candidate vs `select`;
- **missing:** `land/validate`, `land/confirm`, `generation-jobs/{jobId}/alternatives`, `generation-jobs/{jobId}/select`, `POST /plans`, `edits/validate`, `POST versions`;
- **blocked:** `ai-edit/proposals` (G20, no approved AI provider);
- **out of scope until approved:** admin/staff (G09).

## Functional gap analysis

| Gap | Category | Action |
|---|---|---|
| Survey extraction (worker, internal route, persistence, OCR, result API) | Independently implementable (local Tesseract present) | **Implement** |
| `/users/me` routes | Independently implementable (alias to existing handlers) | **Implement** |
| Land validate (dry run) / confirm | Independently implementable | **Implement** |
| Generation alternatives / select | Independently implementable | **Implement** |
| `POST /plans` (create from a selected alternative) | Independently implementable | **Implement** |
| Edit validation without persistence | Independently implementable (Captain path; M3 module unchanged) | **Implement** |
| `POST /plans/{planId}/versions` (atomic save of a server-validated geometry) | Independently implementable | **Implement** |
| `POST /plans/{planId}/confirm` | Alias of the existing confirmation | **Implement** |
| AI edit proposals | Requires an approved AI provider (G20 / G11) | Provider-independent route answering truthfully "unavailable"; no fake AI |
| Report listing | Independently implementable (supporting read) | **Implement** |
| Password recovery delivery | Requires an email provider | Keep the non-delivering, Development-only provisional module |
| Admin/staff, RBAC, audit, notifications | Requires business policy (G09/G12) | OUT_OF_SCOPE; no privileged roles invented |
| Project deletion/transfer/lifecycle | Requires M1 policy | Not implemented |
| Real regulatory constraints, edge roles | Regulatory/legal (G06) | Fail closed |
| Real RAG corpus in WSL | Explicit restriction (identity pins) | Fail closed |
| OpenAPI / frontend guide | Independently implementable | **Implement** (Development Swagger export + guide) |
| CORS | Approved development origin unknown (C6 open) | Not configured; documented |
